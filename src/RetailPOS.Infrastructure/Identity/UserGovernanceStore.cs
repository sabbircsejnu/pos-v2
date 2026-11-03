using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Identity;

public sealed class UserGovernanceStore(
    RetailPOSDbContext dbContext,
    IUserReadStore userReadStore,
    IAuditService auditService) : IUserGovernanceStore
{
    public async Task<TResult> ExecuteSerializableAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<UserGovernanceActor?> FindActorAsync(long userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserGovernanceActor(
                user.Id,
                user.BusinessId,
                user.Role == null ? null : user.Role.Name,
                user.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<UserGovernanceTarget?> FindTargetAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.AsNoTracking().AsQueryable();
        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        return query
            .Where(user => user.Id == userId)
            .Select(user => new UserGovernanceTarget(
                user.Id,
                user.Email,
                user.BusinessId,
                user.Role == null ? null : user.Role.Name,
                user.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<UserGovernanceRole?> FindRoleAsync(long roleId, CancellationToken cancellationToken) =>
        dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Id == roleId)
            .Select(role => new UserGovernanceRole(role.Id, role.Name))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> EmailExistsAsync(
        string email,
        long? exceptUserId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.AsQueryable();
        if (exceptUserId.HasValue)
        {
            query = query.Where(user => user.Id != exceptUserId.Value);
        }

        return query.AnyAsync(user => user.Email == email, cancellationToken);
    }

    public Task<long> CountActiveSuperAdminsAsync(CancellationToken cancellationToken) =>
        dbContext.Users.LongCountAsync(
            user => user.IsActive
                && user.Role != null
                && user.Role.Name.ToLower() == SystemRoleNames.SuperAdmin.ToLower(),
            cancellationToken);

    public async Task<UserReadModel> CreateAsync(
        UserGovernanceProfile profile,
        string password,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles
            .FirstOrDefaultAsync(candidate => candidate.Id == profile.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role with ID {profile.RoleId} not found");
        var accessScope = NormalizeAccessScope(profile.InventoryLocationAccessScope);
        if (businessId.HasValue
            && !await dbContext.Businesses.AnyAsync(business => business.Id == businessId.Value, cancellationToken))
        {
            throw new KeyNotFoundException($"Business with ID {businessId.Value} not found");
        }

        if (profile.OutletId.HasValue)
        {
            var outlet = await dbContext.Outlets
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == profile.OutletId.Value
                    && (!businessId.HasValue || candidate.BusinessId == businessId.Value),
                    cancellationToken);
            if (outlet is null)
            {
                throw new UnauthorizedAccessException(
                    $"Outlet #{profile.OutletId.Value} is not in your authorized business scope.");
            }

            businessId ??= outlet.BusinessId;
        }

        var outletIds = NormalizeIds(profile.OutletIds);
        if (outletIds.Count > 0)
        {
            var count = await dbContext.Outlets.LongCountAsync(
                outlet => outletIds.Contains(outlet.Id) && outlet.BusinessId == businessId,
                cancellationToken);
            if (count != outletIds.Count)
            {
                throw new UnauthorizedAccessException(
                    "One or more outlets are not in your authorized business scope.");
            }
        }

        var warehouseIds = NormalizeIds(profile.WarehouseIds);
        if (warehouseIds.Count > 0)
        {
            var count = await dbContext.Warehouses.LongCountAsync(
                warehouse => warehouseIds.Contains(warehouse.Id) && warehouse.BusinessId == businessId,
                cancellationToken);
            if (count != warehouseIds.Count)
            {
                throw new UnauthorizedAccessException(
                    "One or more warehouses are not in your authorized business scope.");
            }
        }

        var (defaultLocationType, defaultLocationId) = ResolveDefaultLocation(
            profile.DefaultLocationType,
            profile.DefaultLocationId,
            profile.OutletId,
            outletIds,
            warehouseIds);
        var defaultOutletId = defaultLocationType == "outlet" ? defaultLocationId : null;
        AddDefaultLocationAssignment(accessScope, defaultLocationType, defaultLocationId, outletIds, warehouseIds);
        await ValidateDefaultLocationScopeAsync(
            businessId,
            defaultLocationType,
            defaultLocationId,
            cancellationToken);
        ValidateRoleAssignments(role.Name, defaultOutletId, warehouseIds);

        if (businessId.HasValue)
        {
            var maxUsers = await dbContext.Businesses
                .Where(business => business.Id == businessId.Value)
                .Select(business => business.MaxUsers)
                .FirstOrDefaultAsync(cancellationToken);
            if (maxUsers.HasValue)
            {
                var activeUsers = await dbContext.Users.LongCountAsync(
                    user => user.BusinessId == businessId.Value && user.IsActive,
                    cancellationToken);
                if (activeUsers >= maxUsers.Value)
                {
                    throw new InvalidOperationException(
                        $"User limit reached for this business ({maxUsers.Value}). Upgrade subscription or increase user limit.");
                }
            }
        }

        ValidateInventoryAccessConfiguration(accessScope, defaultOutletId, outletIds, warehouseIds);
        var user = new User
        {
            BusinessId = businessId,
            Name = profile.Name ?? throw new InvalidOperationException("Name is required"),
            Email = profile.Email ?? throw new InvalidOperationException("Email is required"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11),
            RoleId = profile.RoleId,
            OutletId = defaultOutletId,
            PermissionOverrides = JsonSerializer.Serialize(ResolveValidatedUserPermissions(profile.UserPermissions)),
            InventoryLocationAccessScope = accessScope,
            IsActive = profile.IsActive
        };
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = user.CreatedAt;
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAssignments(user, outletIds, warehouseIds);
        await dbContext.SaveChangesAsync(cancellationToken);

        await WriteUserAuditAsync(
            AuditActionType.Create,
            $"User created: {user.Id}",
            user,
            actorUserId,
            AuditOperationType.Insert,
            [
                ("BusinessId", null, user.BusinessId),
                ("RoleId", null, user.RoleId),
                ("RoleName", null, role.Name),
                ("OutletId", null, user.OutletId),
                ("OutletAssignments", null, outletIds.ToArray()),
                ("WarehouseAssignments", null, warehouseIds.ToArray()),
                ("PermissionOverrides", null, user.PermissionOverrides),
                ("InventoryLocationAccessScope", null, user.InventoryLocationAccessScope),
                ("IsActive", null, user.IsActive)
            ],
            cancellationToken);

        return await FindReadModelAsync(user.Id, user.BusinessId, cancellationToken);
    }

    public async Task<UserReadModel> UpdateAsync(
        long userId,
        UserGovernanceProfile profile,
        bool actorIsSuperAdmin,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(candidate => candidate.Role)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {userId} not found");
        var oldBusinessId = user.BusinessId;
        var oldRoleId = user.RoleId;
        var oldRoleName = user.Role?.Name;
        var oldOutletId = user.OutletId;
        var oldPermissionOverrides = user.PermissionOverrides;
        var oldAccessScope = user.InventoryLocationAccessScope;
        var oldIsActive = user.IsActive;
        var oldOutletAssignmentIds = await dbContext.UserOutletAssignments
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Id)
            .Select(assignment => assignment.OutletId)
            .ToArrayAsync(cancellationToken);
        var oldWarehouseAssignmentIds = await dbContext.UserWarehouseAssignments
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Id)
            .Select(assignment => assignment.WarehouseId)
            .ToArrayAsync(cancellationToken);

        var role = await dbContext.Roles
            .FirstOrDefaultAsync(candidate => candidate.Id == profile.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Role with ID {profile.RoleId} not found");
        var accessScope = NormalizeAccessScope(profile.InventoryLocationAccessScope);
        // Omitted (null) collections keep the existing assignments; an explicit empty list removes them.
        var outletIds = NormalizeIds(profile.OutletIds ?? oldOutletAssignmentIds);
        var warehouseIds = NormalizeIds(profile.WarehouseIds ?? oldWarehouseAssignmentIds);
        var (defaultLocationType, defaultLocationId) = ResolveDefaultLocation(
            profile.DefaultLocationType,
            profile.DefaultLocationId,
            profile.OutletId,
            outletIds,
            warehouseIds);
        var targetBusinessId = await ResolveUpdateBusinessIdAsync(
            user,
            profile,
            role.Name,
            defaultLocationType,
            defaultLocationId,
            outletIds,
            warehouseIds,
            actorIsSuperAdmin,
            cancellationToken);

        if (profile.OutletId.HasValue
            && !await dbContext.Outlets.AnyAsync(outlet =>
                outlet.Id == profile.OutletId.Value
                && (!targetBusinessId.HasValue || outlet.BusinessId == targetBusinessId.Value),
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                $"Outlet #{profile.OutletId.Value} is not in the user's business scope.");
        }

        if (outletIds.Count > 0)
        {
            var count = await dbContext.Outlets.LongCountAsync(
                outlet => outletIds.Contains(outlet.Id) && outlet.BusinessId == targetBusinessId,
                cancellationToken);
            if (count != outletIds.Count)
            {
                throw new UnauthorizedAccessException("One or more outlets are not in the user's business scope.");
            }
        }

        if (warehouseIds.Count > 0)
        {
            var count = await dbContext.Warehouses.LongCountAsync(
                warehouse => warehouseIds.Contains(warehouse.Id) && warehouse.BusinessId == targetBusinessId,
                cancellationToken);
            if (count != warehouseIds.Count)
            {
                throw new UnauthorizedAccessException("One or more warehouses are not in the user's business scope.");
            }
        }

        var defaultOutletId = defaultLocationType == "outlet" ? defaultLocationId : null;
        AddDefaultLocationAssignment(accessScope, defaultLocationType, defaultLocationId, outletIds, warehouseIds);
        await ValidateDefaultLocationScopeAsync(
            targetBusinessId,
            defaultLocationType,
            defaultLocationId,
            cancellationToken);
        ValidateRoleAssignments(role.Name, defaultOutletId, warehouseIds);
        ValidateInventoryAccessConfiguration(accessScope, defaultOutletId, outletIds, warehouseIds);

        user.BusinessId = targetBusinessId;
        user.Name = profile.Name ?? throw new InvalidOperationException("Name is required");
        user.Email = profile.Email ?? throw new InvalidOperationException("Email is required");
        user.RoleId = profile.RoleId;
        user.OutletId = defaultOutletId;
        user.PermissionOverrides = JsonSerializer.Serialize(ResolveValidatedUserPermissions(profile.UserPermissions));
        user.InventoryLocationAccessScope = accessScope;
        user.IsActive = profile.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var currentOutletAssignments = await dbContext.UserOutletAssignments
            .Where(assignment => assignment.UserId == userId)
            .ToListAsync(cancellationToken);
        dbContext.UserOutletAssignments.RemoveRange(currentOutletAssignments);
        var currentWarehouseAssignments = await dbContext.UserWarehouseAssignments
            .Where(assignment => assignment.UserId == userId)
            .ToListAsync(cancellationToken);
        dbContext.UserWarehouseAssignments.RemoveRange(currentWarehouseAssignments);
        AddAssignments(user, outletIds, warehouseIds);
        await dbContext.SaveChangesAsync(cancellationToken);

        await WriteUserAuditAsync(
            AuditActionType.PermissionChange,
            $"User access updated: {user.Id}",
            user,
            actorUserId,
            AuditOperationType.Update,
            [
                ("BusinessId", oldBusinessId, user.BusinessId),
                ("RoleId", oldRoleId, user.RoleId),
                ("RoleName", oldRoleName, role.Name),
                ("OutletId", oldOutletId, user.OutletId),
                ("OutletAssignments", oldOutletAssignmentIds, outletIds.ToArray()),
                ("WarehouseAssignments", oldWarehouseAssignmentIds, warehouseIds.ToArray()),
                ("PermissionOverrides", oldPermissionOverrides, user.PermissionOverrides),
                ("InventoryLocationAccessScope", oldAccessScope, user.InventoryLocationAccessScope),
                ("IsActive", oldIsActive, user.IsActive)
            ],
            cancellationToken);

        return await FindReadModelAsync(user.Id, user.BusinessId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        long userId,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await FindScopedUserAsync(userId, businessId, cancellationToken);

        if (user.IsActive)
        {
            var outletAssignments = await dbContext.UserOutletAssignments
                .Where(assignment => assignment.UserId == userId)
                .OrderBy(assignment => assignment.Id)
                .Select(assignment => assignment.OutletId)
                .ToArrayAsync(cancellationToken);
            var warehouseAssignments = await dbContext.UserWarehouseAssignments
                .Where(assignment => assignment.UserId == userId)
                .OrderBy(assignment => assignment.Id)
                .Select(assignment => assignment.WarehouseId)
                .ToArrayAsync(cancellationToken);
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await WriteUserAuditAsync(
                AuditActionType.Delete,
                $"User deactivated by delete: {user.Id}",
                user,
                actorUserId,
                AuditOperationType.Delete,
                [
                    ("BusinessId", user.BusinessId, user.BusinessId),
                    ("RoleId", user.RoleId, user.RoleId),
                    ("RoleName", user.Role?.Name, user.Role?.Name),
                    ("OutletId", user.OutletId, user.OutletId),
                    ("OutletAssignments", outletAssignments, outletAssignments),
                    ("WarehouseAssignments", warehouseAssignments, warehouseAssignments),
                    ("IsActive", true, false)
                ],
                cancellationToken);
        }

        return true;
    }

    public async Task<bool> SetActiveAsync(
        long userId,
        bool isActive,
        long? businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await FindScopedUserAsync(userId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {userId} not found");
        if (user.IsActive == isActive)
        {
            throw new InvalidOperationException(isActive ? "User is already active" : "User is already inactive");
        }

        var outletAssignments = await dbContext.UserOutletAssignments
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Id)
            .Select(assignment => assignment.OutletId)
            .ToArrayAsync(cancellationToken);
        var warehouseAssignments = await dbContext.UserWarehouseAssignments
            .Where(assignment => assignment.UserId == userId)
            .OrderBy(assignment => assignment.Id)
            .Select(assignment => assignment.WarehouseId)
            .ToArrayAsync(cancellationToken);
        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteUserAuditAsync(
            AuditActionType.Update,
            isActive ? $"User activated: {user.Id}" : $"User deactivated: {user.Id}",
            user,
            actorUserId,
            AuditOperationType.Update,
            [
                ("BusinessId", user.BusinessId, user.BusinessId),
                ("RoleId", user.RoleId, user.RoleId),
                ("RoleName", user.Role?.Name, user.Role?.Name),
                ("OutletId", user.OutletId, user.OutletId),
                ("OutletAssignments", outletAssignments, outletAssignments),
                ("WarehouseAssignments", warehouseAssignments, warehouseAssignments),
                ("IsActive", !isActive, isActive)
            ],
            cancellationToken);
        return true;
    }

    private async Task<User> FindScopedUserAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Users.Include(user => user.Role).AsQueryable();
        if (businessId.HasValue)
        {
            query = query.Where(user => user.BusinessId == businessId.Value);
        }

        return await query.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User with ID {userId} not found");
    }

    private async Task<UserReadModel> FindReadModelAsync(
        long userId,
        long? businessId,
        CancellationToken cancellationToken) =>
        await userReadStore.FindByIdAsync(userId, businessId, cancellationToken)
            ?? throw new InvalidOperationException($"The persisted user {userId} could not be read after mutation.");

    private async Task WriteUserAuditAsync(
        string actionType,
        string summary,
        User user,
        long actorUserId,
        string operationType,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields,
        CancellationToken cancellationToken) =>
        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Admin,
            Summary = summary,
            PrimaryEntity = ("User", user.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["targetUserId"] = user.Id,
                ["actorUserId"] = actorUserId,
                ["businessId"] = user.BusinessId
            },
            Entities =
            [
                new AuditEntityInput("User", user.Id.ToString(), operationType, fields)
            ]
        }, cancellationToken);

    private void AddAssignments(User user, IReadOnlyList<long> outletIds, IReadOnlyList<long> warehouseIds)
    {
        if (outletIds.Count > 0 && !user.BusinessId.HasValue)
        {
            throw new InvalidOperationException("BusinessId is required before saving outlet assignments.");
        }

        for (var index = 0; index < outletIds.Count; index++)
        {
            dbContext.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = user.Id,
                OutletId = outletIds[index],
                BusinessId = user.BusinessId!.Value,
                IsPrimary = index == 0,
                IsActive = true
            });
        }

        if (warehouseIds.Count > 0 && !user.BusinessId.HasValue)
        {
            throw new InvalidOperationException("BusinessId is required before saving warehouse assignments.");
        }

        for (var index = 0; index < warehouseIds.Count; index++)
        {
            dbContext.UserWarehouseAssignments.Add(new UserWarehouseAssignment
            {
                UserId = user.Id,
                WarehouseId = warehouseIds[index],
                BusinessId = user.BusinessId!.Value,
                IsPrimary = index == 0,
                IsActive = true
            });
        }
    }

    private async Task<long?> ResolveUpdateBusinessIdAsync(
        User user,
        UserGovernanceProfile profile,
        string roleName,
        string? defaultLocationType,
        long? defaultLocationId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds,
        bool actorIsSuperAdmin,
        CancellationToken cancellationToken)
    {
        if (!actorIsSuperAdmin)
        {
            return user.BusinessId;
        }

        var businessId = profile.BusinessId ?? user.BusinessId;
        if (!businessId.HasValue)
        {
            var candidateOutletIds = outletIds.ToHashSet();
            if (profile.OutletId.HasValue)
            {
                candidateOutletIds.Add(profile.OutletId.Value);
            }

            if (defaultLocationType == "outlet" && defaultLocationId.HasValue)
            {
                candidateOutletIds.Add(defaultLocationId.Value);
            }

            var inferredBusinessIds = new HashSet<long>();
            if (candidateOutletIds.Count > 0)
            {
                var outletBusinessIds = await dbContext.Outlets
                    .Where(outlet => candidateOutletIds.Contains(outlet.Id))
                    .Select(outlet => outlet.BusinessId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var outletBusinessId in outletBusinessIds)
                {
                    if (outletBusinessId.HasValue)
                    {
                        inferredBusinessIds.Add(outletBusinessId.Value);
                    }
                }
            }

            var candidateWarehouseIds = warehouseIds.ToHashSet();
            if (defaultLocationType == "warehouse" && defaultLocationId.HasValue)
            {
                candidateWarehouseIds.Add(defaultLocationId.Value);
            }

            if (candidateWarehouseIds.Count > 0)
            {
                var warehouseBusinessIds = await dbContext.Warehouses
                    .Where(warehouse => candidateWarehouseIds.Contains(warehouse.Id))
                    .Select(warehouse => warehouse.BusinessId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var warehouseBusinessId in warehouseBusinessIds)
                {
                    if (warehouseBusinessId.HasValue)
                    {
                        inferredBusinessIds.Add(warehouseBusinessId.Value);
                    }
                }
            }

            if (inferredBusinessIds.Count > 1)
            {
                throw new InvalidOperationException("Selected outlets/warehouses must belong to the same business.");
            }

            businessId = inferredBusinessIds.FirstOrDefault();
            if (businessId == 0)
            {
                businessId = null;
            }
        }

        if (!IsSuperAdmin(roleName) && !businessId.HasValue)
        {
            throw new InvalidOperationException(
                "BusinessId is required for SuperAdmin user updates. Provide BusinessId or select a default location inside the target business.");
        }

        if (businessId.HasValue
            && !await dbContext.Businesses.AnyAsync(
                business => business.Id == businessId.Value,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Business with ID {businessId.Value} not found");
        }

        return businessId;
    }

    private async Task ValidateDefaultLocationScopeAsync(
        long? businessId,
        string? locationType,
        long? locationId,
        CancellationToken cancellationToken)
    {
        if (!businessId.HasValue || !locationId.HasValue || string.IsNullOrWhiteSpace(locationType))
        {
            return;
        }

        var exists = locationType == "outlet"
            ? await dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == locationId.Value && outlet.BusinessId == businessId.Value,
                cancellationToken)
            : await dbContext.Warehouses.AnyAsync(
                warehouse => warehouse.Id == locationId.Value && warehouse.BusinessId == businessId.Value,
                cancellationToken);
        if (!exists)
        {
            var type = locationType == "outlet" ? "outlet" : "warehouse";
            throw new UnauthorizedAccessException(
                $"Default {type} #{locationId.Value} is not in the user's business scope.");
        }
    }

    private static (string? LocationType, long? LocationId) ResolveDefaultLocation(
        string? defaultLocationType,
        long? defaultLocationId,
        long? legacyDefaultOutletId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        var normalizedType = NormalizeDefaultLocationType(defaultLocationType);
        if (defaultLocationId.HasValue)
        {
            if (normalizedType is null)
            {
                throw new InvalidOperationException(
                    "DefaultLocationType is required when DefaultLocationId is provided.");
            }

            return (normalizedType, defaultLocationId.Value);
        }

        if (legacyDefaultOutletId.HasValue)
        {
            return ("outlet", legacyDefaultOutletId.Value);
        }

        if (outletIds.Count > 0)
        {
            return ("outlet", outletIds.First());
        }

        return warehouseIds.Count > 0 ? ("warehouse", warehouseIds.First()) : (null, null);
    }

    private static string? NormalizeDefaultLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
        {
            return null;
        }

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized is not ("outlet" or "warehouse"))
        {
            throw new InvalidOperationException("DefaultLocationType must be either outlet or warehouse.");
        }

        return normalized;
    }

    private static string NormalizeAccessScope(string? scope) =>
        (scope ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            User.InventoryAccessAssignedOnly => User.InventoryAccessAssignedOnly,
            User.InventoryAccessSpecific => User.InventoryAccessSpecific,
            User.InventoryAccessAll => User.InventoryAccessAll,
            _ => User.InventoryAccessAssignedOnly
        };

    private static List<long> NormalizeIds(IEnumerable<long>? values) =>
        values?.Where(id => id > 0).Distinct().ToList() ?? [];

    private static IReadOnlyList<string> ResolveValidatedUserPermissions(
        IEnumerable<string?>? requested)
    {
        if (requested is null)
        {
            return [];
        }

        var allowed = new HashSet<string>(PermissionCatalog.All, StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in requested)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            var normalized = permission.Trim();
            if (string.Equals(normalized, "pos.sale.backdate", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "sales.backdate";
            }

            if (allowed.Contains(normalized))
            {
                selected.Add(normalized);
            }
        }

        return selected.ToArray();
    }

    private static void AddDefaultLocationAssignment(
        string accessScope,
        string? locationType,
        long? locationId,
        List<long> outletIds,
        List<long> warehouseIds)
    {
        if (accessScope == User.InventoryAccessAll || !locationId.HasValue)
        {
            return;
        }

        if (locationType == "outlet" && !outletIds.Contains(locationId.Value))
        {
            outletIds.Insert(0, locationId.Value);
        }

        if (locationType == "warehouse" && !warehouseIds.Contains(locationId.Value))
        {
            warehouseIds.Insert(0, locationId.Value);
        }
    }

    private static void ValidateInventoryAccessConfiguration(
        string accessScope,
        long? defaultOutletId,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (accessScope == User.InventoryAccessAssignedOnly)
        {
            if (!defaultOutletId.HasValue && warehouseIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Assigned Location Only requires a default outlet or at least one assigned warehouse.");
            }

            return;
        }

        if (accessScope == User.InventoryAccessSpecific)
        {
            if (outletIds.Count == 0 && warehouseIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Specific Outlets/Warehouses requires at least one assigned outlet or warehouse.");
            }

            return;
        }

        if (accessScope != User.InventoryAccessAll)
        {
            throw new InvalidOperationException($"Unsupported inventory access scope '{accessScope}'.");
        }
    }

    private static void ValidateRoleAssignments(
        string? roleName,
        long? outletId,
        IEnumerable<long> warehouseIds)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new InvalidOperationException("Role name is required");
        }

        if (IsOutletRequired(roleName) && !outletId.HasValue)
        {
            throw new InvalidOperationException($"OutletId is required for {roleName}. Assign a default outlet.");
        }

        if (IsWarehouseRequired(roleName) && !warehouseIds.Any())
        {
            throw new InvalidOperationException("WarehouseManager must be assigned to at least one warehouse.");
        }
    }

    private static bool IsOutletRequired(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName)
        && !new[]
        {
            "Super Admin", "BusinessOwner", "BusinessAdmin", "AccountsAdmin", "WarehouseManager"
        }.Contains(roleName, StringComparer.OrdinalIgnoreCase);

    private static bool IsWarehouseRequired(string? roleName) =>
        string.Equals(roleName, "WarehouseManager", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuperAdmin(string? roleName) =>
        string.Equals(roleName, "Super Admin", StringComparison.OrdinalIgnoreCase);
}
