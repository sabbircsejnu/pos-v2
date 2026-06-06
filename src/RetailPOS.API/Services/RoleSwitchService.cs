using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class RoleSwitchService : IRoleSwitchService
{
    // Roles a BusinessOwner is allowed to act as
    private static readonly HashSet<string> AllowedActingRoles =
        new(StringComparer.OrdinalIgnoreCase) { "OutletManager", "Cashier", "Salesman", "Stock Manager", "StockManager" };

    private readonly RetailPOSDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings _jwt;

    public RoleSwitchService(RetailPOSDbContext db, ITokenService tokenService, IOptions<JwtSettings> jwt)
    {
        _db = db;
        _tokenService = tokenService;
        _jwt = jwt.Value;
    }

    public async Task<LoginResponseDto> SwitchRoleAsync(long realUserId, RoleSwitchRequestDto request)
    {
        var user = await LoadUserAsync(realUserId);
        EnsureBusinessOwner(user);

        if (string.IsNullOrWhiteSpace(request.ActingRole))
            throw new InvalidOperationException("ActingRole is required.");

        if (!AllowedActingRoles.Contains(request.ActingRole))
            throw new UnauthorizedAccessException($"Role '{request.ActingRole}' is not switchable.");

        var actingRole = await _db.Roles.FirstOrDefaultAsync(r =>
            r.Name.ToLower() == request.ActingRole.ToLower())
            ?? throw new InvalidOperationException($"Role '{request.ActingRole}' not found.");

        string? actingOutletName = null;
        if (request.ActingOutletId.HasValue)
        {
            var outlet = await _db.Outlets.FirstOrDefaultAsync(o => o.Id == request.ActingOutletId.Value)
                ?? throw new InvalidOperationException($"Outlet {request.ActingOutletId} not found.");
            actingOutletName = outlet.Name;
        }

        var actingPermissions = TryParsePermissions(actingRole.Permissions);

        var acting = new ActingRoleClaims(
            ActingRoleId: actingRole.Id,
            ActingRoleName: actingRole.Name,
            ActingPermissions: actingPermissions,
            ActingOutletId: request.ActingOutletId,
            ActingOutletName: actingOutletName);

        var token = _tokenService.GenerateAccessToken(user, user.Role, acting);
        var refresh = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes);

        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refresh,
            ExpiresAt = expiresAt,
            User = BuildUserInfo(user, actingRole.Name, actingPermissions, request.ActingOutletId, actingOutletName, isSwitched: true)
        };
    }

    public async Task<LoginResponseDto> ReturnOwnerAsync(long realUserId)
    {
        var user = await LoadUserAsync(realUserId);
        EnsureBusinessOwner(user);

        var token = _tokenService.GenerateAccessToken(user, user.Role);
        var refresh = _tokenService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes);

        var ownerPerms = TryParsePermissions(user.Role?.Permissions);
        return new LoginResponseDto
        {
            Token = token,
            RefreshToken = refresh,
            ExpiresAt = expiresAt,
            User = BuildUserInfo(user, actingRoleName: null, actingPermissions: ownerPerms,
                actingOutletId: null, actingOutletName: null, isSwitched: false)
        };
    }

    private async Task<RetailPOS.Core.Entities.User> LoadUserAsync(long userId)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        return user ?? throw new UnauthorizedAccessException("User not found or inactive.");
    }

    private static void EnsureBusinessOwner(RetailPOS.Core.Entities.User user)
    {
        if (!string.Equals(user.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Only BusinessOwner can switch roles.");
    }

    private static List<string> TryParsePermissions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static UserInfoDto BuildUserInfo(RetailPOS.Core.Entities.User user, string? actingRoleName,
        List<string> actingPermissions, long? actingOutletId, string? actingOutletName, bool isSwitched)
    {
        return new UserInfoDto
        {
            Id = user.Id,
            BusinessId = user.BusinessId,
            Name = user.Name,
            Email = user.Email,
            RoleName = isSwitched ? actingRoleName : user.Role?.Name,
            Permissions = isSwitched ? actingPermissions : TryParsePermissions(user.Role?.Permissions),
            OutletId = isSwitched ? actingOutletId : user.OutletId,
            OutletName = isSwitched ? actingOutletName : user.Outlet?.Name,
            RealRoleName = user.Role?.Name,
            ActingRoleName = isSwitched ? actingRoleName : null,
            ActingOutletId = isSwitched ? actingOutletId : null,
            ActingOutletName = isSwitched ? actingOutletName : null,
            IsRoleSwitched = isSwitched,
            IsBusinessOwner = string.Equals(user.Role?.Name, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase),
            MustResetPassword = user.MustResetPassword
        };
    }
}
