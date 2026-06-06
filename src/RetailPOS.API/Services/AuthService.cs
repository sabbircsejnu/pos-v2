using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

        // Outlet-bound roles must have a default outlet — every transactional API
        // enforces it server-side. SuperAdmin and BusinessOwner operate above outlets
        // (SuperAdmin manages businesses; BusinessOwner can act across all their outlets),
        // so the requirement is skipped for them.
        if (!RoleSwitchClaims.IsOutletExempt(user.Role?.Name) && !user.OutletId.HasValue)
        {
            throw new UnauthorizedAccessException(
                "Your account is not assigned to an outlet. Please contact an administrator.");
        }

        var token = _tokenService.GenerateAccessToken(user, user.Role);
        var refreshToken = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        // Here you would typically store the refresh token in database
        // For now, we'll just return it

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

        // Get default role (you might want to create a "User" role first)
        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");

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
        // In a real application, you would validate the refresh token from database
        // For now, we'll just validate the access token structure
        if (!_tokenService.ValidateToken(request.Token))
        {
            throw new UnauthorizedAccessException("Invalid token");
        }

        // Extract user ID from token and regenerate tokens
        // This is simplified - in production, validate refresh token from DB
        throw new NotImplementedException("Refresh token logic to be implemented with token storage");
    }

    public async Task<bool> LogoutAsync(long userId)
    {
        // In a real application, you would invalidate the refresh token in database
        // For JWT, we can't invalidate the access token until it expires
        // You could maintain a blacklist of tokens if needed
        return await Task.FromResult(true);
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
}
