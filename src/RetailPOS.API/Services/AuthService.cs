using System.Text.Json;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using BCrypt.Net;

namespace RetailPOS.API.Services;

public class AuthService : IAuthService
{
    private readonly RetailPOSDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        RetailPOSDbContext context,
        ITokenService tokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _tokenService = tokenService;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        if (user.MustResetPassword)
        {
            throw new UnauthorizedAccessException(
                "Password setup is required. Complete your invitation before signing in.");
        }

        // Outlet assignment is intentionally NOT checked here.
        // Roles such as AccountsAdmin and WarehouseManager are permitted to have
        // no outlet assigned, and the business policy allows any authenticated user
        // to log in regardless of outlet assignment. Outlet enforcement happens at
        // the feature/transaction level (UserOutletAccessService) so that a clear,
        // context-specific message is shown only on pages that actually require it.

        var token = _tokenService.GenerateAccessToken(user, user.Role);
        var refreshToken = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        await PersistRefreshTokenAsync(user.Id, refreshToken);

        var permissions = new List<string>();
        if (user.Role != null)
        {
            try
            {
                permissions = JsonSerializer.Deserialize<List<string>>(user.Role.Permissions) ?? new List<string>();
            }
            catch { }
        }

        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = new UserInfoDto
            {
                Id = user.Id,
                BusinessId = user.BusinessId,
                Name = user.Name,
                Email = user.Email,
                RoleName = user.Role?.Name,
                Permissions = permissions,
                OutletId = user.OutletId,
                OutletName = user.Outlet?.Name,
                RealRoleName = user.Role?.Name,
                IsBusinessOwner = string.Equals(user.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase),
                MustResetPassword = user.MustResetPassword
            }
        };
    }

    public async Task<LoginResponseDto> CompleteInvitationAsync(CompleteInvitationRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new InvalidOperationException("Invitation token is required.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters.");

        var tokenHash = HashToken(request.Token.Trim());

        var invitation = await _context.UserInvitations
            .Include(i => i.User)
                .ThenInclude(u => u.Role)
            .Include(i => i.User)
                .ThenInclude(u => u.Outlet)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invitation == null || invitation.ConsumedAt.HasValue || invitation.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invitation token is invalid or expired.");

        invitation.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 11);
        invitation.User.MustResetPassword = false;
        invitation.User.IsActive = true;
        invitation.User.UpdatedAt = DateTime.UtcNow;
        invitation.ConsumedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var token = _tokenService.GenerateAccessToken(invitation.User, invitation.User.Role);
        var refreshToken = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        await PersistRefreshTokenAsync(invitation.User.Id, refreshToken);

        var permissions = new List<string>();
        if (invitation.User.Role != null)
        {
            try
            {
                permissions = JsonSerializer.Deserialize<List<string>>(invitation.User.Role.Permissions) ?? new List<string>();
            }
            catch { }
        }

        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = new UserInfoDto
            {
                Id = invitation.User.Id,
                BusinessId = invitation.User.BusinessId,
                Name = invitation.User.Name,
                Email = invitation.User.Email,
                RoleName = invitation.User.Role?.Name,
                Permissions = permissions,
                OutletId = invitation.User.OutletId,
                OutletName = invitation.User.Outlet?.Name,
                RealRoleName = invitation.User.Role?.Name,
                IsBusinessOwner = string.Equals(invitation.User.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase),
                MustResetPassword = invitation.User.MustResetPassword
            }
        };
    }

    public async Task<LoginResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        // Check if email already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            throw new InvalidOperationException("Email already registered");
        }

        // Registration endpoint is intentionally constrained to non-privileged roles only.
        var defaultRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "Cashier")
            ?? await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");

        if (defaultRole == null)
        {
            throw new InvalidOperationException("Default registration role is not configured.");
        }

        if (string.Equals(defaultRole.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(defaultRole.Name, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Registration role configuration is invalid.");
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = defaultRole?.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Reload user with role
        user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstAsync(u => u.Id == user.Id);

        return await LoginAsync(new LoginRequestDto
        {
            Email = request.Email,
            Password = request.Password
        });
    }

    public async Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request)
    {
        var principal = GetPrincipalFromToken(request.Token, validateLifetime: false);
        if (principal == null)
        {
            throw new UnauthorizedAccessException("Invalid token");
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid token");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("User not found or inactive");
        }

        var incomingTokenHash = HashToken(request.RefreshToken.Trim());
        var storedRefreshToken = await _context.UserRefreshTokens
            .FirstOrDefaultAsync(rt => rt.UserId == userId && rt.TokenHash == incomingTokenHash);

        if (storedRefreshToken == null)
        {
            throw new UnauthorizedAccessException("Invalid refresh token");
        }

        if (storedRefreshToken.RevokedAt.HasValue)
        {
            throw new UnauthorizedAccessException("Refresh token reuse detected: token has already been revoked");
        }

        var now = DateTime.UtcNow;
        if (storedRefreshToken.ExpiresAt <= now)
        {
            storedRefreshToken.RevokedAt = now;
            storedRefreshToken.RevokedReason = "Expired";
            await _context.SaveChangesAsync();
            throw new UnauthorizedAccessException("Refresh token has expired");
        }

        var newAccessToken = _tokenService.GenerateAccessToken(user, user.Role);
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = HashToken(newRefreshToken);

        storedRefreshToken.RevokedAt = now;
        storedRefreshToken.RevokedReason = "Rotated";
        storedRefreshToken.ReplacedByTokenHash = newRefreshTokenHash;

        _context.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefreshTokenHash,
            ExpiresAt = now.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        var permissions = new List<string>();
        if (user.Role != null)
        {
            try
            {
                permissions = JsonSerializer.Deserialize<List<string>>(user.Role.Permissions) ?? new List<string>();
            }
            catch { }
        }

        return new LoginResponseDto
        {
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = now.AddMinutes(_jwtSettings.ExpirationMinutes),
            User = new UserInfoDto
            {
                Id = user.Id,
                BusinessId = user.BusinessId,
                Name = user.Name,
                Email = user.Email,
                RoleName = user.Role?.Name,
                Permissions = permissions,
                OutletId = user.OutletId,
                OutletName = user.Outlet?.Name,
                RealRoleName = user.Role?.Name,
                IsBusinessOwner = string.Equals(user.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase),
                MustResetPassword = user.MustResetPassword
            }
        };
    }

    public async Task<bool> LogoutAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var activeTokens = await _context.UserRefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = "Logout";
        }

        if (activeTokens.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return true;
    }

    public async Task<UserInfoDto?> GetCurrentUserAsync(long userId)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return null;
        }

        var permissions = new List<string>();
        if (user.Role != null)
        {
            try
            {
                permissions = JsonSerializer.Deserialize<List<string>>(user.Role.Permissions) ?? new List<string>();
            }
            catch { }
        }

        return new UserInfoDto
        {
            Id = user.Id,
            BusinessId = user.BusinessId,
            Name = user.Name,
            Email = user.Email,
            RoleName = user.Role?.Name,
            Permissions = permissions,
            OutletId = user.OutletId,
            OutletName = user.Outlet?.Name,
            RealRoleName = user.Role?.Name,
            IsBusinessOwner = string.Equals(user.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase),
            MustResetPassword = user.MustResetPassword
        };
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private async Task PersistRefreshTokenAsync(long userId, string refreshToken)
    {
        _context.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
