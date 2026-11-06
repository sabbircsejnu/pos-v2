using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Identity;

public sealed class IdentityReadStore(RetailPOSDbContext dbContext) : IRoleReadStore, IUserReadStore
{
    public async Task<UserReadModel?> FindByIdAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.AsNoTracking().AsQueryable();
        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        var user = await query
            .Where(user => user.Id == userId)
            .Select(user => new UserDetailRow(
                user.Id,
                user.Name,
                user.Email,
                user.RoleId,
                user.Role == null ? null : user.Role.Name,
                user.OutletId,
                user.Outlet == null ? null : user.Outlet.Name,
                user.BusinessId,
                user.Business == null ? null : user.Business.Name,
                user.PermissionOverrides,
                user.InventoryLocationAccessScope,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var contextAccess = await dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new UserContextAccessReadModel(
                candidate.ContextSwitchEnabled,
                candidate.ContextLocationScope,
                candidate.ContextRoleScope,
                candidate.ContextRoles.OrderBy(mapping => mapping.RoleId).Select(mapping => mapping.RoleId).ToArray()))
            .FirstAsync(cancellationToken);

        return (await MapDetailRowsAsync([user], cancellationToken))[0] with { ContextAccess = contextAccess };
    }

    public Task<IReadOnlyList<UserReadModel>> ListByOutletAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken) =>
        ListActiveUsersAsync(
            dbContext.Users.AsNoTracking().Where(user => user.OutletId == outletId),
            businessId,
            cancellationToken);

    public Task<IReadOnlyList<UserReadModel>> ListByRoleAsync(
        long roleId,
        long? businessId,
        CancellationToken cancellationToken) =>
        ListActiveUsersAsync(
            dbContext.Users.AsNoTracking().Where(user => user.RoleId == roleId),
            businessId,
            cancellationToken);

    public async Task<RoleReadModel?> FindByIdAsync(
        long roleId,
        bool isSuperAdmin,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .AsNoTracking()
            .Where(candidate => candidate.Id == roleId)
            .Select(candidate => new RoleRow(
                candidate.Id,
                candidate.Name,
                candidate.Permissions,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                candidate.Users.Count(user => user.IsActive)))
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null || (!isSuperAdmin && string.Equals(
                role.Name,
                "Super Admin",
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new RoleReadModel(
            role.Id,
            role.Name,
            ParseRolePermissions(role.Permissions),
            role.ActiveUserCount,
            role.CreatedAt,
            role.UpdatedAt);
    }

    public async Task<IReadOnlyList<RoleReadModel>> ListAsync(
        bool isSuperAdmin,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new RoleRow(
                role.Id,
                role.Name,
                role.Permissions,
                role.CreatedAt,
                role.UpdatedAt,
                role.Users.Count(user =>
                    user.IsActive
                    && (!businessId.HasValue || user.BusinessId == businessId.Value))))
            .ToListAsync(cancellationToken);

        return rows
            .Where(role => isSuperAdmin || !string.Equals(
                role.Name,
                "Super Admin",
                StringComparison.OrdinalIgnoreCase))
            .Select(role => new RoleReadModel(
                role.Id,
                role.Name,
                ParseRolePermissions(role.Permissions),
                role.ActiveUserCount,
                role.CreatedAt,
                role.UpdatedAt))
            .ToArray();
    }

    public async Task<UserListReadModel> SearchAsync(
        int pageNumber,
        int pageSize,
        string? search,
        long? roleId,
        long? outletId,
        bool? isActive,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.AsNoTracking().AsQueryable();

        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.Name.Contains(search)
                || user.Email.Contains(search));
        }

        if (roleId.HasValue)
        {
            query = query.Where(user => user.RoleId == roleId.Value);
        }

        if (outletId.HasValue)
        {
            query = outletId.Value == 0
                ? query.Where(user => user.OutletId == null)
                : query.Where(user => user.OutletId == outletId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(user => user.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserRow(
                user.Id,
                user.Name,
                user.Email,
                user.RoleId,
                user.Role == null ? null : user.Role.Name,
                user.OutletId,
                user.Outlet == null ? null : user.Outlet.Name,
                user.BusinessId,
                user.PermissionOverrides,
                user.InventoryLocationAccessScope,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new UserListReadModel([], totalCount, pageNumber, pageSize);
        }

        var userIds = rows.Select(user => user.Id).ToList();
        var outletAssignments = await dbContext.UserOutletAssignments
            .AsNoTracking()
            .Where(assignment => userIds.Contains(assignment.UserId) && assignment.IsActive)
            .Select(assignment => new OutletAssignmentRow(
                assignment.UserId,
                new UserOutletAssignmentReadModel(
                    assignment.Id,
                    assignment.OutletId,
                    assignment.Outlet == null ? null : assignment.Outlet.Name,
                    assignment.IsPrimary,
                    assignment.IsActive)))
            .ToListAsync(cancellationToken);
        var warehouseAssignments = await dbContext.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => userIds.Contains(assignment.UserId) && assignment.IsActive)
            .Select(assignment => new WarehouseAssignmentRow(
                assignment.UserId,
                new UserWarehouseAssignmentReadModel(
                    assignment.Id,
                    assignment.WarehouseId,
                    assignment.Warehouse == null ? null : assignment.Warehouse.Name,
                    assignment.IsPrimary,
                    assignment.IsActive)))
            .ToListAsync(cancellationToken);

        var outletsByUser = outletAssignments.ToLookup(row => row.UserId, row => row.Assignment);
        var warehousesByUser = warehouseAssignments.ToLookup(row => row.UserId, row => row.Assignment);

        var users = rows.Select(user =>
        {
            var userOutlets = outletsByUser[user.Id].ToArray();
            var userWarehouses = warehousesByUser[user.Id].ToArray();
            var primaryWarehouse = userWarehouses.FirstOrDefault(assignment => assignment.IsPrimary)
                ?? userWarehouses.FirstOrDefault();

            return new UserReadModel(
                user.Id,
                user.Name,
                user.Email,
                user.RoleId,
                user.RoleName,
                user.OutletId,
                user.OutletName,
                user.BusinessId,
                BusinessName: null,
                string.IsNullOrWhiteSpace(user.InventoryLocationAccessScope)
                    ? User.InventoryAccessAssignedOnly
                    : user.InventoryLocationAccessScope,
                user.OutletId.HasValue
                    ? "outlet"
                    : primaryWarehouse is not null
                        ? "warehouse"
                        : null,
                user.OutletId ?? primaryWarehouse?.WarehouseId,
                ParseUserPermissions(user.PermissionOverrides),
                userOutlets,
                userWarehouses,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt);
        }).ToArray();

        return new UserListReadModel(users, totalCount, pageNumber, pageSize);
    }

    private async Task<IReadOnlyList<UserReadModel>> ListActiveUsersAsync(
        IQueryable<User> query,
        long? businessId,
        CancellationToken cancellationToken)
    {
        query = query.Where(user => user.IsActive);
        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        var rows = await query
            .OrderBy(user => user.Name)
            .Select(user => new UserDetailRow(
                user.Id,
                user.Name,
                user.Email,
                user.RoleId,
                user.Role == null ? null : user.Role.Name,
                user.OutletId,
                user.Outlet == null ? null : user.Outlet.Name,
                user.BusinessId,
                user.Business == null ? null : user.Business.Name,
                user.PermissionOverrides,
                user.InventoryLocationAccessScope,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt))
            .ToListAsync(cancellationToken);

        return await MapDetailRowsAsync(rows, cancellationToken);
    }

    private async Task<IReadOnlyList<UserReadModel>> MapDetailRowsAsync(
        IReadOnlyList<UserDetailRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var userIds = rows.Select(user => user.Id).ToList();
        var outlets = await dbContext.UserOutletAssignments
            .AsNoTracking()
            .Where(assignment => userIds.Contains(assignment.UserId) && assignment.IsActive)
            .Select(assignment => new OutletAssignmentRow(
                assignment.UserId,
                new UserOutletAssignmentReadModel(
                    assignment.Id,
                    assignment.OutletId,
                    assignment.Outlet == null ? null : assignment.Outlet.Name,
                    assignment.IsPrimary,
                    assignment.IsActive)))
            .ToListAsync(cancellationToken);
        var warehouses = await dbContext.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => userIds.Contains(assignment.UserId) && assignment.IsActive)
            .Select(assignment => new WarehouseAssignmentRow(
                assignment.UserId,
                new UserWarehouseAssignmentReadModel(
                    assignment.Id,
                    assignment.WarehouseId,
                    assignment.Warehouse == null ? null : assignment.Warehouse.Name,
                    assignment.IsPrimary,
                    assignment.IsActive)))
            .ToListAsync(cancellationToken);
        var outletsByUser = outlets.ToLookup(row => row.UserId, row => row.Assignment);
        var warehousesByUser = warehouses.ToLookup(row => row.UserId, row => row.Assignment);

        return rows.Select(user =>
        {
            var userOutlets = outletsByUser[user.Id].ToArray();
            var userWarehouses = warehousesByUser[user.Id].ToArray();
            var primaryWarehouse = userWarehouses.FirstOrDefault(assignment => assignment.IsPrimary)
                ?? userWarehouses.FirstOrDefault();

            return new UserReadModel(
                user.Id,
                user.Name,
                user.Email,
                user.RoleId,
                user.RoleName,
                user.OutletId,
                user.OutletName,
                user.BusinessId,
                user.BusinessName,
                string.IsNullOrWhiteSpace(user.InventoryLocationAccessScope)
                    ? User.InventoryAccessAssignedOnly
                    : user.InventoryLocationAccessScope,
                user.OutletId.HasValue ? "outlet" : primaryWarehouse is not null ? "warehouse" : null,
                user.OutletId ?? primaryWarehouse?.WarehouseId,
                ParseUserPermissions(user.PermissionOverrides),
                userOutlets,
                userWarehouses,
                user.IsActive,
                user.CreatedAt,
                user.UpdatedAt);
        }).ToArray();
    }

    private static IReadOnlyList<string> ParseRolePermissions(string? json)
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

    private static IReadOnlyList<string> ParseUserPermissions(string? json)
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

    private sealed record RoleRow(
        long Id,
        string Name,
        string? Permissions,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int ActiveUserCount);

    private sealed record UserRow(
        long Id,
        string Name,
        string Email,
        long? RoleId,
        string? RoleName,
        long? OutletId,
        string? OutletName,
        long? BusinessId,
        string? PermissionOverrides,
        string? InventoryLocationAccessScope,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record UserDetailRow(
        long Id,
        string Name,
        string Email,
        long? RoleId,
        string? RoleName,
        long? OutletId,
        string? OutletName,
        long? BusinessId,
        string? BusinessName,
        string? PermissionOverrides,
        string? InventoryLocationAccessScope,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record OutletAssignmentRow(
        long UserId,
        UserOutletAssignmentReadModel Assignment);

    private sealed record WarehouseAssignmentRow(
        long UserId,
        UserWarehouseAssignmentReadModel Assignment);
}
