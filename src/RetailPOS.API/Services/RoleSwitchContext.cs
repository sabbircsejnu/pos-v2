using System.Security.Claims;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public static class RoleSwitchClaims
{
    public const string RealRoleId = AuthenticationClaimNames.RealRoleId;
    public const string RealRoleName = AuthenticationClaimNames.RealRoleName;
    public const string ActingRoleId = AuthenticationClaimNames.ActingRoleId;
    public const string ActingRoleName = AuthenticationClaimNames.ActingRoleName;
    public const string ActingOutletId = AuthenticationClaimNames.ActingOutletId;
    public const string ActingOutletName = AuthenticationClaimNames.ActingOutletName;
    public const string IsRoleSwitched = AuthenticationClaimNames.IsRoleSwitched;
    public const string BusinessOwnerRoleName = SystemRoleNames.BusinessOwner;
    public const string SuperAdminRoleName = SystemRoleNames.SuperAdmin;

    /// <summary>
    /// Roles that operate above any single outlet — they create/manage businesses
    /// and don't need a default outlet. Outlet enforcement is bypassed for these.
    /// </summary>
    public static bool IsOutletExempt(string? roleName) =>
        string.Equals(roleName, BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, SuperAdminRoleName, StringComparison.OrdinalIgnoreCase);
}

public sealed class RoleSwitchContext : IRoleSwitchContext
{
    private readonly IHttpContextAccessor _http;

    public RoleSwitchContext(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public long? RealUserId =>
        TryParseLong(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value);

    public long? RealRoleId =>
        TryParseLong(User?.FindFirst(RoleSwitchClaims.RealRoleId)?.Value)
        ?? TryParseLong(User?.FindFirst("roleId")?.Value);

    public string? RealRoleName =>
        User?.FindFirst(RoleSwitchClaims.RealRoleName)?.Value
        ?? User?.FindFirst(ClaimTypes.Role)?.Value;

    public long? ActingRoleId => TryParseLong(User?.FindFirst(RoleSwitchClaims.ActingRoleId)?.Value);
    public string? ActingRoleName => User?.FindFirst(RoleSwitchClaims.ActingRoleName)?.Value;
    public long? ActingOutletId => TryParseLong(User?.FindFirst(RoleSwitchClaims.ActingOutletId)?.Value);
    public string? ActingOutletName => User?.FindFirst(RoleSwitchClaims.ActingOutletName)?.Value;

    public bool IsRoleSwitched =>
        string.Equals(User?.FindFirst(RoleSwitchClaims.IsRoleSwitched)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

    public bool IsBusinessOwner =>
        string.Equals(RealRoleName, RoleSwitchClaims.BusinessOwnerRoleName, StringComparison.OrdinalIgnoreCase);

    public bool IsSuperAdmin =>
        string.Equals(RealRoleName, RoleSwitchClaims.SuperAdminRoleName, StringComparison.OrdinalIgnoreCase);

    public string? EffectiveRoleName => IsRoleSwitched ? ActingRoleName : RealRoleName;

    public long? EffectiveOutletId =>
        IsRoleSwitched
            ? ActingOutletId
            : TryParseLong(User?.FindFirst("outletId")?.Value);

    public long? EffectiveBusinessId =>
        _http.HttpContext?.Items[RetailPOS.API.Middleware.BusinessContextMiddleware.ItemName] is long selected
            ? selected
            : TryParseLong(User?.FindFirst("businessId")?.Value);

    private static long? TryParseLong(string? s) => long.TryParse(s, out var v) ? v : null;
}
