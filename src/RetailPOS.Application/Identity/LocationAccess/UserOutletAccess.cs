using RetailPOS.Application.Authorization;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Identity.LocationAccess;

public sealed class UserOutletAccess(IUserOutletAccessStore store) : IUserOutletAccess
{
    public async Task<AuthorizedOutletsReadModel> GetAuthorizedOutletsAsync(
        UserOutletAccessContext context)
    {
        var userId = context.UserId
            ?? throw new UnauthorizedAccessException("User identity not found");

        var user = await store.FindUserAsync(userId)
            ?? throw new UnauthorizedAccessException("User not found");

        if (context.IsSuperAdmin)
        {
            // Super Admin has no home Business: location access is always scoped to an explicitly validated Business.
            var superAdminBusinessId = context.EffectiveBusinessId
                ?? throw new BusinessContextRequiredException("A validated Business scope is required.");
            var allOutlets = await store.ListOutletsAsync(superAdminBusinessId);
            var allWarehouses = await store.ListWarehousesAsync(superAdminBusinessId);
            var defaultOutlet = context.EffectiveOutletId.HasValue &&
                allOutlets.Any(outlet => outlet.Id == context.EffectiveOutletId.Value)
                ? context.EffectiveOutletId
                : null;

            return new AuthorizedOutletsReadModel(
                allOutlets,
                allWarehouses,
                allOutlets,
                allWarehouses,
                defaultOutlet,
                IsBusinessOwner: true,
                IsGlobalAccess: true,
                User.InventoryAccessAll,
                defaultOutlet,
                defaultOutlet.HasValue ? "outlet" : null);
        }

        
        var effectiveBusinessId = context.EffectiveBusinessId ?? user.BusinessId;
        var accessScope = NormalizeAccessScope(user.InventoryLocationAccessScope);

        if (context.IsBusinessOwner &&
            (context.HasWildcardPermission || string.IsNullOrWhiteSpace(user.InventoryLocationAccessScope)))
        {
            accessScope = User.InventoryAccessAll;
        }

        if (accessScope == User.InventoryAccessAll)
        {
            if (!effectiveBusinessId.HasValue)
            {
                throw new UnauthorizedAccessException("A Business context is required to access locations.");
            }

            var outlets = await store.ListOutletsAsync(effectiveBusinessId);
            var warehouses = await store.ListWarehousesAsync(effectiveBusinessId);
            var defaultOutletId = context.EffectiveOutletId ?? user.OutletId;
            if (!outlets.Any(outlet => outlet.Id == defaultOutletId))
            {
                defaultOutletId = null;
            }
            var defaultLocationId = defaultOutletId
                ?? outlets.FirstOrDefault()?.Id
                ?? warehouses.FirstOrDefault()?.Id;
            var defaultLocationType = defaultOutletId.HasValue || outlets.Count > 0
                ? "outlet"
                : warehouses.Count > 0 ? "warehouse" : null;

            return new AuthorizedOutletsReadModel(
                outlets,
                warehouses,
                outlets,
                warehouses,
                defaultOutletId,
                context.IsBusinessOwner,
                IsGlobalAccess: true,
                User.InventoryAccessAll,
                defaultLocationId,
                defaultLocationType);
        }

        if (accessScope == User.InventoryAccessSpecific)
        {
            var outletAssignments = (await store.ListActiveOutletAssignmentsAsync(userId))
                .Where(assignment => assignment.OutletName is not null &&
                    (!effectiveBusinessId.HasValue || assignment.OutletBusinessId == effectiveBusinessId))
                .OrderByDescending(assignment => assignment.IsPrimary)
                .ThenBy(assignment => assignment.OutletName)
                .ToList();
            var warehouseAssignments = (await store.ListActiveWarehouseAssignmentsAsync(userId))
                .Where(assignment => assignment.WarehouseName is not null &&
                    (!effectiveBusinessId.HasValue || assignment.WarehouseBusinessId == effectiveBusinessId))
                .OrderByDescending(assignment => assignment.IsPrimary)
                .ThenBy(assignment => assignment.WarehouseName)
                .ToList();

            var outlets = outletAssignments
                .Where(assignment => assignment.OutletName is not null &&
                    (!effectiveBusinessId.HasValue || assignment.OutletBusinessId == effectiveBusinessId))
                .Select(assignment => new AuthorizedLocationReadModel(
                    assignment.OutletId,
                    assignment.OutletName!,
                    "outlet"))
                .ToList();
            var warehouses = warehouseAssignments
                .Where(assignment => assignment.WarehouseName is not null &&
                    (!effectiveBusinessId.HasValue || assignment.WarehouseBusinessId == effectiveBusinessId))
                .Select(assignment => new AuthorizedLocationReadModel(
                    assignment.WarehouseId,
                    assignment.WarehouseName!,
                    "warehouse"))
                .ToList();
            var (destinationOutlets, destinationWarehouses) =
                await LoadDestinationLocationsAsync(effectiveBusinessId);

            await AddActingLocationsAsync(context, user, userId, effectiveBusinessId, outlets, warehouses);
            var preferredOutletId = context.IsContextSwitched
                && outlets.Any(outlet => outlet.Id == context.EffectiveOutletId)
                ? context.EffectiveOutletId
                : user.OutletId;
            // A Default Outlet only counts when the user can actually use it.
            long? defaultOutletId = outlets.Any(outlet => outlet.Id == preferredOutletId) ? preferredOutletId : null;
            var (defaultLocationId, defaultLocationType) = ResolveAssignedDefault(
                defaultOutletId,
                outletAssignments,
                warehouseAssignments);
            if (!defaultLocationId.HasValue && warehouses.Count > 0)
            {
                (defaultLocationId, defaultLocationType) = (warehouses[0].Id, "warehouse");
            }


            return new AuthorizedOutletsReadModel(
                outlets,
                warehouses,
                destinationOutlets,
                destinationWarehouses,
                defaultOutletId,
                context.IsBusinessOwner,
                IsGlobalAccess: false,
                User.InventoryAccessSpecific,
                defaultLocationId,
                defaultLocationType);
        }

        var assignedOutletId = await IsActingLocationAuthorizedAsync(
            context,
            user,
            userId,
            effectiveBusinessId,
            context.EffectiveOutletId,
            isWarehouse: false)
            ? context.EffectiveOutletId ?? user.OutletId
            : user.OutletId;
        var assignedWarehouse = (await store.ListActiveWarehouseAssignmentsAsync(userId))
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.AssignmentId)
            .FirstOrDefault();
        var outletsList = new List<AuthorizedLocationReadModel>();

        if (assignedOutletId.HasValue)
        {
            var outlet = (await store.ListOutletsAsync(effectiveBusinessId))
                .FirstOrDefault(candidate => candidate.Id == assignedOutletId.Value);
            if (outlet is not null)
            {
                outletsList.Add(outlet);
            }
        }

        var warehousesList = new List<AuthorizedLocationReadModel>();
        if (assignedWarehouse?.WarehouseName is not null &&
            (!effectiveBusinessId.HasValue || assignedWarehouse.WarehouseBusinessId == effectiveBusinessId))
        {
            warehousesList.Add(new AuthorizedLocationReadModel(
                assignedWarehouse.WarehouseId,
                assignedWarehouse.WarehouseName,
                "warehouse"));
        }

        await AddActingLocationsAsync(context, user, userId, effectiveBusinessId, outletsList, warehousesList);

        var (destinationOutletsAssigned, destinationWarehousesAssigned) =
            await LoadDestinationLocationsAsync(effectiveBusinessId);
        var defaultLocationIdAssigned = outletsList.FirstOrDefault()?.Id
            ?? warehousesList.FirstOrDefault()?.Id;
        var defaultLocationTypeAssigned = outletsList.Count > 0
            ? "outlet"
            : warehousesList.Count > 0 ? "warehouse" : null;

        return new AuthorizedOutletsReadModel(
            outletsList,
            warehousesList,
            destinationOutletsAssigned,
            destinationWarehousesAssigned,
            outletsList.Any(outlet => outlet.Id == assignedOutletId) ? assignedOutletId : null,
            context.IsBusinessOwner,
            IsGlobalAccess: false,
            User.InventoryAccessAssignedOnly,
            defaultLocationIdAssigned,
            defaultLocationTypeAssigned);
    }

    public async Task<AuthorizedOutletsReadModel> GetAuthorizedLocationsAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy)
    {
        var result = policy switch
        {
            LocationAccessPolicy.InventoryStockView => await GetAuthorizedOutletsAsync(context),
            LocationAccessPolicy.StockCountOperation
                or LocationAccessPolicy.StockTransferSource
                or LocationAccessPolicy.GrnView
                or LocationAccessPolicy.ExpenseOperation =>
                await GetAssignedLocationsAsync(context),
            LocationAccessPolicy.StockTransferDestination => await GetBusinessLocationsAsync(context),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported location access policy.")
        };

        // The acting warehouse only preselects; every write still validates against the policy's list.
        if (context.IsContextSwitched
            && context.EffectiveWarehouseId is long actingWarehouseId
            && result.Warehouses.Any(warehouse => warehouse.Id == actingWarehouseId))
        {
            result = result with { DefaultLocationId = actingWarehouseId, DefaultLocationType = "warehouse" };
        }

        return result with { Policy = policy };
    }

    // An acting location comes from a signed but possibly stale token, so a non-owner's is
    // re-checked against persisted state on every request.
    private async Task<bool> IsActingLocationAuthorizedAsync(
        UserOutletAccessContext context,
        UserLocationAccessUser user,
        long userId,
        long? businessId,
        long? locationId,
        bool isWarehouse)
    {
        if (!context.IsContextSwitched || context.IsBusinessOwner || locationId is not long id)
        {
            return true;
        }

        if (!user.ContextSwitchEnabled || !businessId.HasValue)
        {
            return false;
        }

        if (user.ContextLocationScope == User.ContextLocationAll)
        {
            var locations = isWarehouse
                ? await store.ListWarehousesAsync(businessId)
                : await store.ListOutletsAsync(businessId);
            return locations.Any(location => location.Id == id);
        }

        if (isWarehouse)
        {
            return (await store.ListActiveWarehouseAssignmentsAsync(userId))
                .Any(assignment => assignment.WarehouseId == id && assignment.WarehouseBusinessId == businessId);
        }

        return user.OutletId == id
            || (await store.ListActiveOutletAssignmentsAsync(userId))
                .Any(assignment => assignment.OutletId == id && assignment.OutletBusinessId == businessId);
    }

    private async Task AddActingLocationsAsync(
        UserOutletAccessContext context,
        UserLocationAccessUser user,
        long userId,
        long? businessId,
        List<AuthorizedLocationReadModel> outlets,
        List<AuthorizedLocationReadModel> warehouses)
    {
        if (!context.IsContextSwitched || !businessId.HasValue)
        {
            return;
        }

        if (context.EffectiveOutletId is long outletId
            && outlets.All(outlet => outlet.Id != outletId)
            && await IsActingLocationAuthorizedAsync(context, user, userId, businessId, outletId, isWarehouse: false)
            && (await store.ListOutletsAsync(businessId)).FirstOrDefault(outlet => outlet.Id == outletId) is { } actingOutlet)
        {
            outlets.Add(actingOutlet);
        }

        if (context.EffectiveWarehouseId is long warehouseId
            && warehouses.All(warehouse => warehouse.Id != warehouseId)
            && await IsActingLocationAuthorizedAsync(context, user, userId, businessId, warehouseId, isWarehouse: true)
            && (await store.ListWarehousesAsync(businessId)).FirstOrDefault(warehouse => warehouse.Id == warehouseId) is { } actingWarehouse)
        {
            warehouses.Add(actingWarehouse);
        }
    }

    // Step 5 only: the user's own active outlet/warehouse assignments inside the effective business.
    private async Task<AuthorizedOutletsReadModel> GetAssignedLocationsAsync(UserOutletAccessContext context)
    {
        var userId = context.UserId
            ?? throw new UnauthorizedAccessException("User identity not found");
        var user = await store.FindUserAsync(userId)
            ?? throw new UnauthorizedAccessException("User not found");

        if (context.IsSuperAdmin || context.IsBusinessOwner)
        {
            // Privileged roles keep business-wide access; Step 5 narrows only ordinary users.
            return await GetBusinessLocationsAsync(context);
        }

        var businessId = context.EffectiveBusinessId ?? user.BusinessId
            ?? throw new UnauthorizedAccessException("A Business context is required to access locations.");

        var outletAssignments = (await store.ListActiveOutletAssignmentsAsync(userId))
            .Where(assignment => assignment.OutletName is not null && assignment.OutletBusinessId == businessId)
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.OutletName)
            .ToList();
        var warehouseAssignments = (await store.ListActiveWarehouseAssignmentsAsync(userId))
            .Where(assignment => assignment.WarehouseName is not null && assignment.WarehouseBusinessId == businessId)
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.WarehouseName)
            .ToList();

        var outlets = outletAssignments
            .Select(assignment => new AuthorizedLocationReadModel(assignment.OutletId, assignment.OutletName!, "outlet"))
            .ToList();
        var warehouses = warehouseAssignments
            .Select(assignment => new AuthorizedLocationReadModel(
                assignment.WarehouseId,
                assignment.WarehouseName!,
                "warehouse"))
            .ToList();

        var preferredOutletId = context.EffectiveOutletId ?? user.OutletId;
        long? defaultOutletId = preferredOutletId.HasValue && outlets.Any(outlet => outlet.Id == preferredOutletId.Value)
            ? preferredOutletId
            : null;
        var (defaultLocationId, defaultLocationType) = ResolveAssignedDefault(
            defaultOutletId,
            outletAssignments,
            warehouseAssignments);
        var (destinationOutlets, destinationWarehouses) = await LoadDestinationLocationsAsync(businessId);
        return new AuthorizedOutletsReadModel(
            outlets,
            warehouses,
            destinationOutlets,
            destinationWarehouses,
            defaultOutletId,
            context.IsBusinessOwner,
            IsGlobalAccess: false,
            NormalizeAccessScope(user.InventoryLocationAccessScope),
            defaultLocationId,
            defaultLocationType);
    }

    // Every location of the current business; independent of Step 3 and Step 5.
    private async Task<AuthorizedOutletsReadModel> GetBusinessLocationsAsync(UserOutletAccessContext context)
    {
        var userId = context.UserId
            ?? throw new UnauthorizedAccessException("User identity not found");
        var user = await store.FindUserAsync(userId)
            ?? throw new UnauthorizedAccessException("User not found");

        var businessId = context.IsSuperAdmin
            ? context.EffectiveBusinessId
                ?? throw new BusinessContextRequiredException("A validated Business scope is required.")
            : context.EffectiveBusinessId ?? user.BusinessId
                ?? throw new UnauthorizedAccessException("A Business context is required to access locations.");
        var outlets = await store.ListOutletsAsync(businessId);
        var warehouses = await store.ListWarehousesAsync(businessId);
        var preferredOutletId = context.EffectiveOutletId ?? user.OutletId;
        long? defaultOutletId = preferredOutletId.HasValue && outlets.Any(outlet => outlet.Id == preferredOutletId.Value)
            ? preferredOutletId
            : null;

        return new AuthorizedOutletsReadModel(
            outlets,
            warehouses,
            outlets,
            warehouses,
            defaultOutletId,
            context.IsBusinessOwner,
            IsGlobalAccess: true,
            NormalizeAccessScope(user.InventoryLocationAccessScope),
            defaultOutletId ?? outlets.FirstOrDefault()?.Id ?? warehouses.FirstOrDefault()?.Id,
            defaultOutletId.HasValue || outlets.Count > 0 ? "outlet" : warehouses.Count > 0 ? "warehouse" : null);
    }

    private static (long? LocationId, string? LocationType) ResolveAssignedDefault(
        long? defaultOutletId,
        IReadOnlyList<UserOutletAssignmentReadModel> outletAssignments,
        IReadOnlyList<UserWarehouseAssignmentReadModel> warehouseAssignments)
    {
        if (defaultOutletId.HasValue)
        {
            return (defaultOutletId, "outlet");
        }

        // Assignments arrive ordered primary-first, so the first entry is the primary when one exists.
        if (outletAssignments.Count > 0 && (outletAssignments[0].IsPrimary || warehouseAssignments.Count == 0
            || !warehouseAssignments[0].IsPrimary))
        {
            return (outletAssignments[0].OutletId, "outlet");
        }

        return warehouseAssignments.Count > 0
            ? (warehouseAssignments[0].WarehouseId, "warehouse")
            : (null, null);
    }

    public Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        UserOutletAccessContext context,
        long? requestedLocationId,
        string? requestedLocationType) =>
        ResolveAndAuthorizeLocationAsync(
            context,
            LocationAccessPolicy.InventoryStockView,
            requestedLocationId,
            requestedLocationType);

    public async Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy,
        long? requestedLocationId,
        string? requestedLocationType)
    {
        var authorized = await GetAuthorizedLocationsAsync(context, policy);
        var normalizedRequestedType = NormalizeLocationType(requestedLocationType);

        if (!requestedLocationId.HasValue)
        {
            if (authorized.IsGlobalAccess)
            {
                return (null, normalizedRequestedType);
            }

            // Step 5 policies have no fallback: a user without assignments is simply not authorized.
            if (policy != LocationAccessPolicy.InventoryStockView
                && authorized.Outlets.Count == 0
                && authorized.Warehouses.Count == 0)
            {
                throw new UnauthorizedAccessException("You are not assigned to any location for this operation.");
            }

            if (normalizedRequestedType == "warehouse")
            {
                if (authorized.Warehouses.Count == 0)
                {
                    throw new UnauthorizedAccessException("You are not authorized to view warehouse data.");
                }

                return authorized.Warehouses.Count > 1
                    ? (null, "warehouse")
                    : (authorized.Warehouses[0].Id, "warehouse");
            }

            if (normalizedRequestedType == "outlet")
            {
                if (authorized.Outlets.Count == 0)
                {
                    throw new UnauthorizedAccessException("You are not authorized to view outlet data.");
                }

                return authorized.Outlets.Count > 1
                    ? (null, "outlet")
                    : (authorized.Outlets[0].Id, "outlet");
            }

            var defaultLocationId = authorized.DefaultLocationId
                ?? authorized.Outlets.FirstOrDefault()?.Id
                ?? authorized.Warehouses.FirstOrDefault()?.Id;
            var defaultLocationType = authorized.DefaultLocationType
                ?? (authorized.Outlets.Count > 0
                    ? "outlet"
                    : authorized.Warehouses.Count > 0 ? "warehouse" : "outlet");

            return defaultLocationId.HasValue
                ? (defaultLocationId, defaultLocationType)
                : (null, defaultLocationType);
        }

        var type = normalizedRequestedType ?? "outlet";
        var allowed = type == "warehouse"
            ? authorized.Warehouses.Any(location => location.Id == requestedLocationId.Value)
            : authorized.Outlets.Any(location => location.Id == requestedLocationId.Value);

        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for {type} #{requestedLocationId.Value}.");
        }

        return (requestedLocationId, type);
    }

    public async Task<long?> ResolveAndAuthorizeOutletFilterAsync(
        UserOutletAccessContext context,
        long? requestedOutletId)
    {
        var authorized = await GetAuthorizedOutletsAsync(context);

        if (!authorized.IsGlobalAccess)
        {
            if (requestedOutletId.HasValue)
            {
                if (!authorized.Outlets.Any(outlet => outlet.Id == requestedOutletId.Value))
                {
                    throw new UnauthorizedAccessException(
                        $"You are not authorized to view data for outlet #{requestedOutletId.Value}.");
                }

                return requestedOutletId;
            }

            if (authorized.Outlets.Count > 1)
            {
                return null;
            }

            return authorized.Outlets.Count == 1
                ? authorized.Outlets[0].Id
                : throw new UnauthorizedAccessException(
                    "Your account is not assigned to an authorized outlet.");

        }

        if (!requestedOutletId.HasValue)
        {
            return null;
        }

        if (!authorized.Outlets.Any(outlet => outlet.Id == requestedOutletId.Value))
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to view data for outlet #{requestedOutletId.Value}.");
        }

        return requestedOutletId;
    }

    public async Task<long> EnforceWriteOutletAsync(
        UserOutletAccessContext context,
        long? requestedOutletId)
    {
        // A warehouse or All Locations context is not an outlet: the transaction must name one.
        if (context.IsContextSwitched
            && !context.EffectiveOutletId.HasValue
            && (!requestedOutletId.HasValue || requestedOutletId.Value <= 0))
        {
            throw new InvalidOperationException("OutletId is required for this transaction.");
        }

        var authorized = await GetAuthorizedOutletsAsync(context);

        if (!authorized.IsGlobalAccess)
        {
            if (!requestedOutletId.HasValue || requestedOutletId.Value <= 0)
            {
                // A revoked acting outlet must not quietly move the transaction to another outlet.
                if (context.IsContextSwitched
                    && authorized.Outlets.All(outlet => outlet.Id != context.EffectiveOutletId))
                {
                    throw new UnauthorizedAccessException(
                        "Your current outlet context is no longer authorized. Switch context and try again.");
                }

                if (authorized.Outlets.Count == 0)
                {
                    throw new UnauthorizedAccessException(
                        "Your account is not assigned to an authorized outlet.");
                }

                if (authorized.DefaultOutletId is long defaultOutletId
                    && authorized.Outlets.Any(outlet => outlet.Id == defaultOutletId))
                {
                    return defaultOutletId;
                }

                // Several authorized outlets and no default: the caller must choose one.
                return authorized.Outlets.Count == 1
                    ? authorized.Outlets[0].Id
                    : throw new InvalidOperationException("OutletId is required for this transaction.");
            }

            if (!authorized.Outlets.Any(outlet => outlet.Id == requestedOutletId.Value))
            {
                throw new UnauthorizedAccessException(
                    $"You are not authorized to record this transaction for outlet #{requestedOutletId.Value}.");
            }

            return requestedOutletId.Value;
        }

        if (!requestedOutletId.HasValue || requestedOutletId.Value <= 0)
        {
            throw new InvalidOperationException("OutletId is required for this transaction.");
        }

        if (!authorized.Outlets.Any(outlet => outlet.Id == requestedOutletId.Value))
        {
            throw new UnauthorizedAccessException(
                $"Outlet #{requestedOutletId.Value} is not in your authorized set.");
        }

        return requestedOutletId.Value;
    }

    public Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        UserOutletAccessContext context,
        long requestedLocationId,
        string requestedLocationType) =>
        EnforceWriteLocationAsync(
            context,
            LocationAccessPolicy.InventoryStockView,
            requestedLocationId,
            requestedLocationType);

    public async Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy,
        long requestedLocationId,
        string requestedLocationType)
    {
        if (requestedLocationId <= 0)
        {
            throw new InvalidOperationException("locationId is required.");
        }

        var type = string.IsNullOrWhiteSpace(requestedLocationType)
            ? "outlet"
            : requestedLocationType.ToLowerInvariant();
        if (type != "outlet" && type != "warehouse")
        {
            throw new InvalidOperationException($"Invalid locationType '{requestedLocationType}'.");
        }

        var authorized = await GetAuthorizedLocationsAsync(context, policy);
        var allowed = type == "warehouse"
            ? authorized.Warehouses.Any(location => location.Id == requestedLocationId)
            : authorized.Outlets.Any(location => location.Id == requestedLocationId);
        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to record this transaction for {type} #{requestedLocationId}.");
        }

        return (requestedLocationId, type);
    }

    private async Task<(IReadOnlyList<AuthorizedLocationReadModel> Outlets,
        IReadOnlyList<AuthorizedLocationReadModel> Warehouses)> LoadDestinationLocationsAsync(long? businessId)
    {
        // A null business would make the store return every tenant's locations.
        if (!businessId.HasValue)
        {
            return ([], []);
        }

        return (await store.ListOutletsAsync(businessId), await store.ListWarehousesAsync(businessId));
    }

    private static string? NormalizeLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
        {
            return null;
        }

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
        {
            throw new InvalidOperationException($"Invalid locationType '{locationType}'.");
        }

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
}
