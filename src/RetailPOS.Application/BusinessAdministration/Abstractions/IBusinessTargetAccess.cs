namespace RetailPOS.Application.BusinessAdministration.Abstractions;

/// <summary>
/// Authorizes administrative operations that act on an explicitly named target Business
/// (for example changing the features of a Business). Unlike Business-scoped operations these do
/// not depend on the caller's selected Business context.
/// </summary>
public interface IBusinessTargetAccess
{
    /// <summary>
    /// Returns the validated target Business identifier. Throws <see cref="ArgumentException"/> for a
    /// non-positive identifier and <see cref="UnauthorizedAccessException"/> when the caller may not
    /// administer that Business.
    /// </summary>
    long RequireTargetBusinessId(long targetBusinessId);

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> unless the caller really is a Platform Super Admin acting as
    /// one. A role-switched caller is refused, whichever role it switched to or from.
    /// </summary>
    void RequirePlatformSuperAdmin();
}
