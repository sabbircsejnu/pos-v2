using System.Text.Json;
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
                Name = user.Name,
                Email = user.Email,
                RoleName = user.Role?.Name,
                Permissions = permissions,
                OutletId = user.OutletId,
                OutletName = user.Outlet?.Name
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
            Name = user.Name,
            Email = user.Email,
            RoleName = user.Role?.Name,
            Permissions = permissions,
            OutletId = user.OutletId,
            OutletName = user.Outlet?.Name
        };
    }
}
