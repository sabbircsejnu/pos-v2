namespace RetailPOS.API.Services;

/// <summary>
/// Resolves the tenant scope of the current caller. Operations fall into three categories:
/// <list type="bullet">
/// <item><description><b>Platform-scoped</b> (Super Admin only, e.g. managing Businesses): call
/// <see cref="EnsurePlatformAdministrator"/>; no Business context is involved.</description></item>
/// <item><description><b>Business-scoped</b> (the normal case): call <see cref="RequireBusinessId"/>,
/// which is strict and never substitutes a default Business.</description></item>
/// <item><description><b>Target-Business administrative</b> (a Super Admin acting on an explicitly named
/// Business, e.g. changing its features): call <see cref="RequireTargetBusinessId"/>.</description></item>
/// </list>
/// </summary>
public interface ITenantAccessService
{
    bool IsSuperAdmin { get; }
    long? EffectiveBusinessId { get; }

    /// <summary>Returns the effective Business or throws <c>BusinessContextRequiredException</c>.</summary>
    long RequireBusinessId();

    void EnsureBusinessMatch(long? entityBusinessId, string resourceName);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> unless the caller is a real Super Admin.</summary>
    void EnsurePlatformAdministrator();

    /// <summary>
    /// Validates an explicit target Business identifier. A Super Admin may target any Business without
    /// selecting it as context; every other caller may only target their own effective Business.
    /// </summary>
    long RequireTargetBusinessId(long targetBusinessId);
}
