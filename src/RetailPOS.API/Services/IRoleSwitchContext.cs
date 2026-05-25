namespace RetailPOS.API.Services;

/// <summary>
/// Session-scoped role-switch context. Reflects whether the current request is
/// running under the user's real role or under a temporary acting role granted
/// by a BusinessOwner. Backed entirely by JWT claims — no DB state.
/// </summary>
public interface IRoleSwitchContext
{
    long? RealUserId { get; }
    long? RealRoleId { get; }
    string? RealRoleName { get; }

    long? ActingRoleId { get; }
    string? ActingRoleName { get; }
    long? ActingOutletId { get; }
    string? ActingOutletName { get; }

    bool IsRoleSwitched { get; }
    bool IsBusinessOwner { get; }

    /// <summary>Effective role name for permission checks (acting if switched, otherwise real).</summary>
    string? EffectiveRoleName { get; }

    /// <summary>Effective outlet for permission checks (acting if switched, otherwise user's home outlet).</summary>
    long? EffectiveOutletId { get; }
}
