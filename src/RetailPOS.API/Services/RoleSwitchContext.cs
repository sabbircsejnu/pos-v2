using System.Security.Claims;

namespace RetailPOS.API.Services;

public static class RoleSwitchClaims
{
    public const string RealRoleId = "real_role_id";
    public const string RealRoleName = "real_role";
    public const string ActingRoleId = "acting_role_id";
    public const string ActingRoleName = "acting_role";
    public const string ActingOutletId = "acting_outlet_id";
    public const string ActingOutletName = "acting_outlet_name";
    public const string IsRoleSwitched = "is_role_switched";
    public const string BusinessOwnerRoleName = "BusinessOwner";
    public const string SuperAdminRoleName = "Super Admin";

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

    public long? EffectiveBusinessId => TryParseLong(User?.FindFirst("businessId")?.Value);

    private static long? TryParseLong(string? s) => long.TryParse(s, out var v) ? v : null;
}
