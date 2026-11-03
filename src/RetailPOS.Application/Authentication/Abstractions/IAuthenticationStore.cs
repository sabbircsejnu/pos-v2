namespace RetailPOS.Application.Authentication.Abstractions;

public interface IAuthenticationStore : IAuthenticationProfileStore
{
    Task<TResult> ExecuteAtomicallyAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    Task<AuthenticationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<AuthenticationRole?> FindRegistrationRoleAsync(CancellationToken cancellationToken);
    Task<AuthenticationRoleSwitchUser?> FindActiveUserForRoleSwitchAsync(
        long userId,
        CancellationToken cancellationToken);
    Task<AuthenticationRole?> FindRoleForSwitchAsync(
        string roleName,
        CancellationToken cancellationToken);
    Task<AuthenticationOutlet?> FindOutletForSwitchAsync(
        long outletId,
        CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<AuthenticationUser> CreateRegisteredUserAsync(
        string name,
        string email,
        string passwordHash,
        long roleId,
        CancellationToken cancellationToken);

    Task<AuthenticationInvitation?> FindInvitationAsync(
        string token,
        CancellationToken cancellationToken);

    Task<AuthenticationUser?> CompleteInvitationAsync(
        long invitationId,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken);

    Task<AuthenticationUser?> FindUserForPasswordChangeAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken);

    Task UpdatePasswordAsync(
        long userId,
        string passwordHash,
        DateTime updatedAt,
        CancellationToken cancellationToken);

    Task PersistRefreshTokenAsync(
        long userId,
        string refreshToken,
        DateTime expiresAt,
        CancellationToken cancellationToken);
}

public sealed class AuthenticationUser
{
    public AuthenticationUser(
        long id,
        long? businessId,
        string name,
        string email,
        string passwordHash,
        long? roleId,
        string? roleName,
        string? rolePermissions,
        long? outletId,
        string? outletName,
        string? permissionOverrides,
        bool isActive,
        bool mustResetPassword)
    {
        Id = id;
        BusinessId = businessId;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        RoleId = roleId;
        RoleName = roleName;
        RolePermissions = rolePermissions;
        OutletId = outletId;
        OutletName = outletName;
        PermissionOverrides = permissionOverrides;
        IsActive = isActive;
        MustResetPassword = mustResetPassword;
    }

    public long Id { get; }
    public long? BusinessId { get; }
    public string Name { get; }
    public string Email { get; }
    public string PasswordHash { get; }
    public long? RoleId { get; }
    public string? RoleName { get; }
    public string? RolePermissions { get; }
    public long? OutletId { get; }
    public string? OutletName { get; }
    public string? PermissionOverrides { get; }
    public bool IsActive { get; }
    public bool MustResetPassword { get; }
}

public sealed record AuthenticationRole(long Id, string Name, string Permissions);

public sealed record AuthenticationRoleSwitchUser(
    long Id,
    long? BusinessId,
    string Name,
    string Email,
    long? RoleId,
    string? RoleName,
    string? RolePermissions,
    long? OutletId,
    string? OutletName,
    bool IsActive,
    bool MustResetPassword);

public sealed record AuthenticationOutlet(long Id, long? BusinessId, string Name);

public sealed record AuthenticationInvitation(
    long Id,
    string Purpose,
    DateTime ExpiresAt,
    DateTime? ConsumedAt,
    AuthenticationUser User);
