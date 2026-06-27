using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

public class UserOutletAccessService : IUserOutletAccessService
{
    private readonly RetailPOSDbContext _context;
    private readonly IRoleSwitchContext _roleSwitch;

    public UserOutletAccessService(RetailPOSDbContext context, IRoleSwitchContext roleSwitch)
    {
        _context = context;
        _roleSwitch = roleSwitch;
    }

    public async Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync()
    {
        var userId = _roleSwitch.RealUserId
            ?? throw new UnauthorizedAccessException("User identity not found");

        if (_roleSwitch.IsSuperAdmin)
        {
            var allOutlets = await _context.Outlets.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new AuthorizedLocationDto { Id = o.Id, Name = o.Name, Type = "outlet" })
                .ToListAsync();

            var allWarehouses = await _context.Warehouses.AsNoTracking()
                .OrderBy(w => w.Name)
                .Select(w => new AuthorizedLocationDto { Id = w.Id, Name = w.Name, Type = "warehouse" })
                .ToListAsync();

            var defaultOutlet = _roleSwitch.EffectiveOutletId;
            return new AuthorizedOutletsDto
            {
                Outlets = allOutlets,
                Warehouses = allWarehouses,
                DestinationOutlets = allOutlets,
                DestinationWarehouses = allWarehouses,
                DefaultOutletId = defaultOutlet,
                IsBusinessOwner = true,
                IsGlobalAccess = true,
                AccessScope = User.InventoryAccessAll,
                DefaultLocationId = defaultOutlet,
                DefaultLocationType = defaultOutlet.HasValue ? "outlet" : null
            };
        }

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new UnauthorizedAccessException("User not found");

        var effectiveBusinessId = _roleSwitch.EffectiveBusinessId ?? user.BusinessId;
        var accessScope = NormalizeAccessScope(user.InventoryLocationAccessScope);

        // Backward compatibility for old BusinessOwner users.
        if (string.IsNullOrWhiteSpace(user.InventoryLocationAccessScope) && _roleSwitch.IsBusinessOwner)
        {
            accessScope = User.InventoryAccessAll;
        }

        if (accessScope == User.InventoryAccessAll)
        {
            IQueryable<Outlet> outletQuery = _context.Outlets.AsNoTracking();
            IQueryable<Warehouse> warehouseQuery = _context.Warehouses.AsNoTracking();

            if (effectiveBusinessId.HasValue)
            {
                outletQuery = outletQuery.Where(o => o.BusinessId == effectiveBusinessId);
                warehouseQuery = warehouseQuery.Where(w => w.BusinessId == effectiveBusinessId);
            }

            var outlets = await outletQuery
                .OrderBy(o => o.Name)
                .Select(o => new AuthorizedLocationDto { Id = o.Id, Name = o.Name, Type = "outlet" })
                .ToListAsync();

            var warehouses = await warehouseQuery
                .OrderBy(w => w.Name)
                .Select(w => new AuthorizedLocationDto { Id = w.Id, Name = w.Name, Type = "warehouse" })
                .ToListAsync();

            var defaultOutletId = _roleSwitch.EffectiveOutletId ?? user.OutletId;
            var defaultLocationId = defaultOutletId
                ?? outlets.FirstOrDefault()?.Id
                ?? warehouses.FirstOrDefault()?.Id;
            var defaultLocationType = defaultOutletId.HasValue || outlets.Any()
                ? "outlet"
                : warehouses.Any() ? "warehouse" : null;

            return new AuthorizedOutletsDto
            {
                Outlets = outlets,
                Warehouses = warehouses,
                DestinationOutlets = outlets,
                DestinationWarehouses = warehouses,
                DefaultOutletId = defaultOutletId,
                IsBusinessOwner = _roleSwitch.IsBusinessOwner,
                IsGlobalAccess = true,
                AccessScope = User.InventoryAccessAll,
                DefaultLocationId = defaultLocationId,
                DefaultLocationType = defaultLocationType
            };
        }

        if (accessScope == User.InventoryAccessSpecific)
        {
            var outletAssignments = await _context.UserOutletAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive)
                .Include(a => a.Outlet)
                .OrderByDescending(a => a.IsPrimary)
                .ThenBy(a => a.Outlet!.Name)
                .ToListAsync();

            var warehouseAssignments = await _context.UserWarehouseAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive)
                .Include(a => a.Warehouse)
                .OrderByDescending(a => a.IsPrimary)
                .ThenBy(a => a.Warehouse!.Name)
                .ToListAsync();

            var outlets = outletAssignments
                .Where(a => a.Outlet != null && (!effectiveBusinessId.HasValue || a.Outlet.BusinessId == effectiveBusinessId))
                .Select(a => new AuthorizedLocationDto { Id = a.OutletId, Name = a.Outlet!.Name, Type = "outlet" })
                .ToList();

            var warehouses = warehouseAssignments
                .Where(a => a.Warehouse != null && (!effectiveBusinessId.HasValue || a.Warehouse.BusinessId == effectiveBusinessId))
                .Select(a => new AuthorizedLocationDto { Id = a.WarehouseId, Name = a.Warehouse!.Name, Type = "warehouse" })
                .ToList();

            var (destinationOutlets, destinationWarehouses) = await LoadDestinationLocationsAsync(effectiveBusinessId);

            var defaultOutletId = user.OutletId;
            var defaultLocationId = defaultOutletId
                ?? outletAssignments.FirstOrDefault(a => a.IsPrimary)?.OutletId
                ?? warehouseAssignments.FirstOrDefault(a => a.IsPrimary)?.WarehouseId
                ?? outlets.FirstOrDefault()?.Id
                ?? warehouses.FirstOrDefault()?.Id;
            var defaultLocationType = defaultOutletId.HasValue || outlets.Any()
                ? "outlet"
                : warehouses.Any() ? "warehouse" : null;

            return new AuthorizedOutletsDto
            {
                Outlets = outlets,
                Warehouses = warehouses,
                DestinationOutlets = destinationOutlets,
                DestinationWarehouses = destinationWarehouses,
                DefaultOutletId = defaultOutletId,
                IsBusinessOwner = _roleSwitch.IsBusinessOwner,
                IsGlobalAccess = false,
                AccessScope = User.InventoryAccessSpecific,
                DefaultLocationId = defaultLocationId,
                DefaultLocationType = defaultLocationType
            };
        }

        // assigned_only (or fallback): only assigned outlet and/or primary warehouse.
        var assignedOutletId = _roleSwitch.EffectiveOutletId ?? user.OutletId;

        var assignedWarehouse = await _context.UserWarehouseAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderByDescending(a => a.IsPrimary)
            .ThenBy(a => a.Id)
            .Include(a => a.Warehouse)
            .FirstOrDefaultAsync();

        var outletsList = new List<AuthorizedLocationDto>();
        if (assignedOutletId.HasValue)
        {
            var outlet = await _context.Outlets.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == assignedOutletId.Value
                    && (!effectiveBusinessId.HasValue || o.BusinessId == effectiveBusinessId));
            if (outlet != null)
            {
                outletsList.Add(new AuthorizedLocationDto { Id = outlet.Id, Name = outlet.Name, Type = "outlet" });
            }
        }

        var warehousesList = new List<AuthorizedLocationDto>();
        if (assignedWarehouse?.Warehouse != null
            && (!effectiveBusinessId.HasValue || assignedWarehouse.Warehouse.BusinessId == effectiveBusinessId))
        {
            warehousesList.Add(new AuthorizedLocationDto
            {
                Id = assignedWarehouse.WarehouseId,
                Name = assignedWarehouse.Warehouse.Name,
                Type = "warehouse"
            });
        }

        var (destinationOutletsAssigned, destinationWarehousesAssigned) = await LoadDestinationLocationsAsync(effectiveBusinessId);

        var defaultLocationIdAssigned = outletsList.FirstOrDefault()?.Id
            ?? warehousesList.FirstOrDefault()?.Id;
        var defaultLocationTypeAssigned = outletsList.Any() ? "outlet" : warehousesList.Any() ? "warehouse" : null;

        return new AuthorizedOutletsDto
        {
            Outlets = outletsList,
            Warehouses = warehousesList,
            DestinationOutlets = destinationOutletsAssigned,
            DestinationWarehouses = destinationWarehousesAssigned,
            DefaultOutletId = assignedOutletId,
            IsBusinessOwner = _roleSwitch.IsBusinessOwner,
            IsGlobalAccess = false,
            AccessScope = User.InventoryAccessAssignedOnly,
            DefaultLocationId = defaultLocationIdAssigned,
            DefaultLocationType = defaultLocationTypeAssigned
        };
    }

    public async Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        long? requestedLocationId,
        string? requestedLocationType)
    {
        var auth = await GetAuthorizedOutletsAsync();
        var normalizedRequestedType = NormalizeLocationType(requestedLocationType);

        if (!requestedLocationId.HasValue)
        {
            if (auth.IsGlobalAccess)
                return (null, normalizedRequestedType);

            if (normalizedRequestedType == "warehouse")
            {
                if (!auth.Warehouses.Any())
                    throw new UnauthorizedAccessException("You are not authorized to view warehouse data.");

                return auth.Warehouses.Count > 1
                    ? (null, "warehouse")
                    : (auth.Warehouses.First().Id, "warehouse");
            }

            if (normalizedRequestedType == "outlet")
            {
                if (!auth.Outlets.Any())
                    throw new UnauthorizedAccessException("You are not authorized to view outlet data.");

                return auth.Outlets.Count > 1
                    ? (null, "outlet")
                    : (auth.Outlets.First().Id, "outlet");
            }

            var defaultLocationId = auth.DefaultLocationId
                ?? auth.Outlets.FirstOrDefault()?.Id
                ?? auth.Warehouses.FirstOrDefault()?.Id;
            var defaultLocationType = auth.DefaultLocationType
                ?? (auth.Outlets.Any() ? "outlet" : auth.Warehouses.Any() ? "warehouse" : "outlet");

            return defaultLocationId.HasValue
                ? (defaultLocationId, defaultLocationType)
                : (null, defaultLocationType);
        }

        var type = normalizedRequestedType ?? "outlet";

        var allowed = type == "warehouse"
            ? auth.Warehouses.Any(w => w.Id == requestedLocationId.Value)
            : auth.Outlets.Any(o => o.Id == requestedLocationId.Value);

        if (!allowed)
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for {type} #{requestedLocationId.Value}.");

        return (requestedLocationId, type);
    }

    public async Task<long?> ResolveAndAuthorizeOutletFilterAsync(long? requestedOutletId)
    {
        var auth = await GetAuthorizedOutletsAsync();

        if (!auth.IsGlobalAccess)
        {
            if (requestedOutletId.HasValue)
            {
                var allowed = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
                if (!allowed)
                {
                    throw new UnauthorizedAccessException(
                        $"You are not authorized to view data for outlet #{requestedOutletId.Value}.");
                }

                return requestedOutletId;
            }

            if (auth.Outlets.Count > 1)
                return null;

            return auth.Outlets.FirstOrDefault()?.Id
                ?? auth.DefaultOutletId
                ?? throw new UnauthorizedAccessException(
                    "Your account is not assigned to an authorized outlet.");
        }

        if (!requestedOutletId.HasValue)
            return null;

        var ok = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
        if (!ok)
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for outlet #{requestedOutletId.Value}.");

        return requestedOutletId;
    }

    public async Task<long> EnforceWriteOutletAsync(long? requestedOutletId)
    {
        var auth = await GetAuthorizedOutletsAsync();

        if (!auth.IsGlobalAccess)
        {
            if (!requestedOutletId.HasValue || requestedOutletId.Value <= 0)
            {
                var fallback = auth.Outlets.FirstOrDefault()?.Id ?? auth.DefaultOutletId;
                if (!fallback.HasValue)
                {
                    throw new UnauthorizedAccessException(
                        "Your account is not assigned to an authorized outlet.");
                }

                return fallback.Value;
            }

            var allowed = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
            if (!allowed)
                throw new UnauthorizedAccessException(
                    $"You are not authorized to record this transaction for outlet #{requestedOutletId.Value}.");

            return requestedOutletId.Value;
        }

        if (!requestedOutletId.HasValue || requestedOutletId.Value <= 0)
            throw new InvalidOperationException("OutletId is required for this transaction.");

        var ok = auth.Outlets.Any(o => o.Id == requestedOutletId.Value);
        if (!ok)
            throw new UnauthorizedAccessException(
                $"Outlet #{requestedOutletId.Value} is not in your authorized set.");

        return requestedOutletId.Value;
    }

    public async Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        long requestedLocationId,
        string requestedLocationType)
    {
        if (requestedLocationId <= 0)
            throw new InvalidOperationException("locationId is required.");

        var type = string.IsNullOrWhiteSpace(requestedLocationType)
            ? "outlet"
            : requestedLocationType.ToLowerInvariant();

        if (type != "outlet" && type != "warehouse")
            throw new InvalidOperationException($"Invalid locationType '{requestedLocationType}'.");

        var auth = await GetAuthorizedOutletsAsync();

        var allowed = type == "warehouse"
            ? auth.Warehouses.Any(w => w.Id == requestedLocationId)
            : auth.Outlets.Any(o => o.Id == requestedLocationId);

        if (!allowed)
            throw new UnauthorizedAccessException(
                $"You are not authorized to record this transaction for {type} #{requestedLocationId}.");

        return (requestedLocationId, type);
    }

    private static string? NormalizeLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            return null;

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid locationType '{locationType}'.");

        return normalized;
    }

    private static string NormalizeAccessScope(string? scope)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            User.InventoryAccessAssignedOnly => User.InventoryAccessAssignedOnly,
            User.InventoryAccessSpecific => User.InventoryAccessSpecific,
            User.InventoryAccessAll => User.InventoryAccessAll,
            _ => User.InventoryAccessAssignedOnly
        };
    }

    private async Task<(List<AuthorizedLocationDto> Outlets, List<AuthorizedLocationDto> Warehouses)> LoadDestinationLocationsAsync(long? businessId)
    {
        IQueryable<Outlet> outletQuery = _context.Outlets.AsNoTracking();
        IQueryable<Warehouse> warehouseQuery = _context.Warehouses.AsNoTracking();

        if (businessId.HasValue)
        {
            outletQuery = outletQuery.Where(o => o.BusinessId == businessId.Value);
            warehouseQuery = warehouseQuery.Where(w => w.BusinessId == businessId.Value);
        }

        var outlets = await outletQuery
            .OrderBy(o => o.Name)
            .Select(o => new AuthorizedLocationDto { Id = o.Id, Name = o.Name, Type = "outlet" })
            .ToListAsync();

        var warehouses = await warehouseQuery
            .OrderBy(w => w.Name)
            .Select(w => new AuthorizedLocationDto { Id = w.Id, Name = w.Name, Type = "warehouse" })
            .ToListAsync();

        return (outlets, warehouses);
    }
}
