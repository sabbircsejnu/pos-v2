using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Abstractions;

public interface IUserGovernanceStore
{
    Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    Task<UserGovernanceActor?> FindActorAsync(long userId, CancellationToken cancellationToken);
    Task<UserGovernanceTarget?> FindTargetAsync(long userId, long? businessId, CancellationToken cancellationToken);
    Task<UserGovernanceRole?> FindRoleAsync(long roleId, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string email, long? exceptUserId, CancellationToken cancellationToken);
    Task<long> CountActiveSuperAdminsAsync(CancellationToken cancellationToken);

    Task<UserReadModel> CreateAsync(
        UserGovernanceProfile profile,
        string password,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<UserReadModel> UpdateAsync(
        long userId,
        UserGovernanceProfile profile,
        bool actorIsSuperAdmin,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long userId,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(
        long userId,
        bool isActive,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken);
}

public sealed record UserGovernanceActor(
    long Id,
    long? BusinessId,
    string? RoleName,
    bool IsActive);

public sealed record UserGovernanceTarget(
    long Id,
    string Email,
    long? BusinessId,
    string? RoleName,
    bool IsActive);

public sealed record UserGovernanceRole(long Id, string Name);

public sealed record UserGovernanceProfile(
    string? Name,
    string? Email,
    long RoleId,
    long? OutletId,
    IReadOnlyList<long>? WarehouseIds,
    IReadOnlyList<long>? OutletIds,
    string? DefaultLocationType,
    long? DefaultLocationId,
    string? InventoryLocationAccessScope,
    long? BusinessId,
    IReadOnlyList<string?>? UserPermissions,
    bool IsActive);
