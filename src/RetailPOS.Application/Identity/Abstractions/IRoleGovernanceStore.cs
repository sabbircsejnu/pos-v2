using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Identity.Abstractions;

public interface IRoleGovernanceStore
{
    Task<Role?> FindByIdAsync(long id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);
    Task<bool> HasActiveAssignmentsAsync(long roleId, CancellationToken cancellationToken);
    Task<RoleReadModel> CreateAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken);
    Task<RoleReadModel> UpdateAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken);
}

public enum RoleGovernanceAction
{
    Create,
    Update,
    Delete
}

public sealed record RoleGovernanceAudit(
    RoleGovernanceAction Action,
    string? OldName,
    string? OldPermissions,
    string? NewName,
    string? NewPermissions);
