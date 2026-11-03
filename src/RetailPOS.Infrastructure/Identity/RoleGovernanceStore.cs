using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Identity;

public sealed class RoleGovernanceStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService) : IRoleGovernanceStore
{
    public Task<Role?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
        dbContext.Roles
            .Include(role => role.Users.Where(user => user.IsActive))
            .FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Roles.AnyAsync(role => role.Name == name, cancellationToken);

    public Task<bool> HasActiveAssignmentsAsync(long roleId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(
            user => user.RoleId == roleId && user.IsActive,
            cancellationToken);

    public async Task<RoleReadModel> CreateAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        role.CreatedAt = DateTime.UtcNow;
        role.UpdatedAt = role.CreatedAt;
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(role, audit, cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return ToReadModel(role);
    }

    public async Task<RoleReadModel> UpdateAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        role.UpdatedAt = DateTime.UtcNow;
        dbContext.Roles.Update(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(role, audit, cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return ToReadModel(role);
    }

    public async Task<bool> DeleteAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(role, audit, cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return true;
    }

    private Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(
        CancellationToken cancellationToken) =>
        await dbContext.Database.BeginTransactionAsync(cancellationToken);

    private Task WriteAuditAsync(
        Role role,
        RoleGovernanceAudit audit,
        CancellationToken cancellationToken)
    {
        var (actionType, summary, operationType) = audit.Action switch
        {
            RoleGovernanceAction.Create =>
                (AuditActionType.Create, $"Role created: {role.Name}", AuditOperationType.Insert),
            RoleGovernanceAction.Update =>
                (AuditActionType.PermissionChange, $"Role permissions updated: {role.Name}", AuditOperationType.Update),
            RoleGovernanceAction.Delete =>
                (AuditActionType.Delete, $"Role deleted: {role.Name}", AuditOperationType.Delete),
            _ => throw new ArgumentOutOfRangeException(nameof(audit), audit.Action, "Unknown Role governance action")
        };

        var fields = new List<(string FieldName, object? OldValue, object? NewValue)>
        {
            ("Name", audit.OldName, audit.NewName),
            ("Permissions", audit.OldPermissions, audit.NewPermissions)
        };

        return auditService.RecordAsync(new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Admin,
            Summary = summary,
            PrimaryEntity = ("Role", role.Id.ToString()),
            Entities =
            [
                new AuditEntityInput("Role", role.Id.ToString(), operationType, fields)
            ]
        }, cancellationToken);
    }

    private static RoleReadModel ToReadModel(Role role) =>
        new(
            role.Id,
            role.Name,
            ParsePermissions(role.Permissions),
            role.Users.Count(user => user.IsActive),
            role.CreatedAt,
            role.UpdatedAt);

    private static IReadOnlyList<string> ParsePermissions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
