using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Organisation;

public sealed class HrOrganisationStore(RetailPOSDbContext db, TimeProvider? clock = null) :
    IHrOrganisationReadStore, IHrOrgUnitWriteStore,
    IOperationalLocationProjectionWriteStore, IOperationalLocationSourceReadStore
{
    public async Task<bool> IsLocationAccessibleAsync(
        long businessId,
        long projectionId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == projectionId);
        if (scope.AllowedSources is not null)
        {
            var allowedOutlets = scope.AllowedSources.Where(source => source.EntityType == "Outlet")
                .Select(source => source.EntityId).ToArray();
            var allowedWarehouses = scope.AllowedSources.Where(source => source.EntityType == "Warehouse")
                .Select(source => source.EntityId).ToArray();
            query = query.Where(row =>
                row.SourceEntityType == "Outlet" && Enumerable.Contains(allowedOutlets, row.SourceEntityId)
                || row.SourceEntityType == "Warehouse" && Enumerable.Contains(allowedWarehouses, row.SourceEntityId));
        }
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HrOrgUnitReadModel>> GetOrgUnitsAsync(
        long businessId,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = db.HrOrgUnits.AsNoTracking().Where(row => row.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(row => row.Code.ToLower().Contains(term)
                || row.Name.ToLower().Contains(term) || row.UnitType.ToLower().Contains(term));
        }

        if (scope.AllowedSources is not null)
        {
            var allowedOutlets = scope.AllowedSources.Where(source => source.EntityType == "Outlet")
                .Select(source => source.EntityId).ToArray();
            var allowedWarehouses = scope.AllowedSources.Where(source => source.EntityType == "Warehouse")
                .Select(source => source.EntityId).ToArray();
            var allowedProjectionIds = await db.OperationalLocationProjections.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                  && (row.SourceEntityType == "Outlet" && Enumerable.Contains(allowedOutlets, row.SourceEntityId)
                      || row.SourceEntityType == "Warehouse" && Enumerable.Contains(allowedWarehouses, row.SourceEntityId)))
                .Select(row => row.Id).ToArrayAsync(cancellationToken);
            var visibleOrgUnitIds = await db.HrOrgUnits.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && (row.OperationalLocationProjectionId == null
                        || Enumerable.Contains(allowedProjectionIds, row.OperationalLocationProjectionId.Value)))
                .Select(row => row.Id).ToArrayAsync(cancellationToken);
            query = query.Where(row => row.OperationalLocationProjectionId == null
                || Enumerable.Contains(allowedProjectionIds, row.OperationalLocationProjectionId.Value))
                .Where(row => row.ParentOrgUnitId == null
                    || Enumerable.Contains(visibleOrgUnitIds, row.ParentOrgUnitId.Value));
        }

        var rows = await query.OrderBy(row => row.Code).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<OperationalLocationReadModel>> GetOperationalLocationsAsync(
        long businessId,
        string? sourceEntityType,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sourceEntityType)
            && sourceEntityType is not ("Outlet" or "Warehouse"))
            throw new ArgumentException("Only Outlet and Warehouse operational sources are supported.");
        var query = db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(sourceEntityType))
            query = query.Where(row => row.SourceEntityType == sourceEntityType);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(row => row.Name.ToLower().Contains(term)
                || row.Address.ToLower().Contains(term) || row.SourceEntityId.Contains(term));
        }
        if (scope.AllowedSources is not null)
        {
            var allowedOutlets = scope.AllowedSources.Where(source => source.EntityType == "Outlet")
                .Select(source => source.EntityId).ToArray();
            var allowedWarehouses = scope.AllowedSources.Where(source => source.EntityType == "Warehouse")
                .Select(source => source.EntityId).ToArray();
            query = query.Where(row =>
                row.SourceEntityType == "Outlet" && Enumerable.Contains(allowedOutlets, row.SourceEntityId)
                || row.SourceEntityType == "Warehouse" && Enumerable.Contains(allowedWarehouses, row.SourceEntityId));
        }
        var rows = await query.OrderBy(row => row.SourceEntityType).ThenBy(row => row.Name).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public async Task<OperationalLocationReadModel?> GetOperationalLocationAsync(
        long businessId,
        long projectionId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        var row = await db.OperationalLocationProjections.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.BusinessId == businessId
                && candidate.Id == projectionId, cancellationToken);
        if (row is null)
            return null;
        if (scope.AllowedSources is not null)
        {
            var visible = scope.AllowedSources.Any(source =>
                source.EntityType == row.SourceEntityType && source.EntityId == row.SourceEntityId);
            if (!visible)
                return null;
        }
        return Map(row);
    }

    public async Task<HrOrgUnitReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        HrOrgUnitWriteData data,
        CancellationToken cancellationToken)
    {
        await ValidateReferencesAsync(businessId, data, null, cancellationToken);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var effectiveFrom = data.EffectiveFrom ?? DateOnly.FromDateTime(now);
        ValidateDates(effectiveFrom, data.EffectiveTo);
        var row = new HrOrgUnit
        {
            BusinessId = businessId,
            Code = data.Code.Trim(),
            Name = data.Name.Trim(),
            UnitType = data.UnitType.Trim(),
            ParentOrgUnitId = data.ParentOrgUnitId,
            OperationalLocationProjectionId = data.OperationalLocationProjectionId,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = data.EffectiveTo,
            IsActive = data.IsActive,
            Version = 1,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            UpdatedAtUtc = now,
            UpdatedByUserId = actorUserId
        };
        db.HrOrgUnits.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public Task<long?> GetLocationProjectionIdAsync(
        long businessId,
        long orgUnitId,
        CancellationToken cancellationToken) =>
        db.HrOrgUnits.AsNoTracking()
            .Where(unit => unit.BusinessId == businessId && unit.Id == orgUnitId)
            .Select(unit => unit.OperationalLocationProjectionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HrOrgUnitReadModel?> UpdateAsync(
        long businessId,
        long actorUserId,
        long orgUnitId,
        long expectedVersion,
        HrOrgUnitWriteData data,
        CancellationToken cancellationToken)
    {
        var row = await db.HrOrgUnits.SingleOrDefaultAsync(
            unit => unit.BusinessId == businessId && unit.Id == orgUnitId, cancellationToken);
        if (row is null)
            return null;
        if (row.Version != expectedVersion)
            throw new HrOrganisationConcurrencyException("Organisation unit changed since it was read.");
        await ValidateReferencesAsync(businessId, data, row.Id, cancellationToken);
        var effectiveFrom = data.EffectiveFrom ?? row.EffectiveFrom;
        ValidateDates(effectiveFrom, data.EffectiveTo);

        row.Code = data.Code.Trim();
        row.Name = data.Name.Trim();
        row.UnitType = data.UnitType.Trim();
        row.ParentOrgUnitId = data.ParentOrgUnitId;
        row.OperationalLocationProjectionId = data.OperationalLocationProjectionId;
        row.EffectiveFrom = effectiveFrom;
        row.EffectiveTo = data.EffectiveTo;
        row.IsActive = data.IsActive;
        row.Version = checked(row.Version + 1);
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = actorUserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrOrganisationConcurrencyException(
                "Organisation unit was changed by another request.", exception);
        }
        return Map(row);
    }

    public async Task<ProjectionUpsertResult> UpsertFromSourceAsync(
        OperationalLocationSourceEvent source,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Projection changes require the caller's inbox or reconciliation transaction.");

        var identity = $"{source.BusinessId}:{source.SourceSystem}:{source.SourceEntityType}:{source.SourceEntityId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", cancellationToken);
        var hash = IntegrationEnvelope.Hash(System.Text.Json.JsonSerializer.Serialize(source));
        var row = await db.OperationalLocationProjections.SingleOrDefaultAsync(candidate =>
            candidate.BusinessId == source.BusinessId && candidate.SourceSystem == source.SourceSystem
            && candidate.SourceEntityType == source.SourceEntityType
            && candidate.SourceEntityId == source.SourceEntityId, cancellationToken);
        if (row is null)
        {
            db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                BusinessId = source.BusinessId,
                SourceSystem = source.SourceSystem,
                SourceEntityType = source.SourceEntityType,
                SourceEntityId = source.SourceEntityId,
                Name = source.Name,
                Address = source.Address,
                ContactNumber = source.ContactNumber,
                ManagerExternalUserId = source.ManagerExternalUserId,
                SourceUpdatedAtUtc = source.SourceUpdatedAtUtc,
                SourceVersion = source.SourceVersion,
                SourcePayloadHash = hash,
                LastSyncedAtUtc = DateTime.UtcNow,
                IsSourceActive = !source.IsDeleted,
                IsSourceDeleted = source.IsDeleted,
                SyncStatus = "Current"
            });
            return ProjectionUpsertResult.Inserted;
        }
        if (source.SourceVersion < row.SourceVersion)
            return ProjectionUpsertResult.Stale;
        if (source.SourceVersion == row.SourceVersion)
        {
            if (row.SourcePayloadHash != hash)
                throw new IntegrationConflictException(
                    "A source version was reused with different operational location content.");
            return ProjectionUpsertResult.Unchanged;
        }

        row.Name = source.Name;
        row.Address = source.Address;
        row.ContactNumber = source.ContactNumber;
        row.ManagerExternalUserId = source.ManagerExternalUserId;
        row.SourceUpdatedAtUtc = source.SourceUpdatedAtUtc;
        row.SourceVersion = source.SourceVersion;
        row.SourcePayloadHash = hash;
        row.LastSyncedAtUtc = DateTime.UtcNow;
        row.IsSourceActive = !source.IsDeleted;
        row.IsSourceDeleted = source.IsDeleted;
        row.SyncStatus = "Current";
        return ProjectionUpsertResult.Updated;
    }

    public async Task<IReadOnlyList<OperationalLocationSourceEvent>> GetPageAsync(
        long businessId,
        string sourceEntityType,
        long afterId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || afterId < 0 || pageSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Invalid Business-scoped source page.");

        if (sourceEntityType == "Outlet")
        {
            var rows = await db.Outlets.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id > afterId)
                .OrderBy(row => row.Id).Take(pageSize).ToListAsync(cancellationToken);
            return rows.Select(row => new OperationalLocationSourceEvent(
                businessId, "RetailPOS", "Outlet", row.Id.ToString(), row.SourceVersion, false,
                row.Name, row.Address, row.ContactNumber, row.ManagerId?.ToString(), AsUtc(row.UpdatedAt))).ToArray();
        }
        if (sourceEntityType == "Warehouse")
        {
            var rows = await db.Warehouses.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id > afterId)
                .OrderBy(row => row.Id).Take(pageSize).ToListAsync(cancellationToken);
            return rows.Select(row => new OperationalLocationSourceEvent(
                businessId, "RetailPOS", "Warehouse", row.Id.ToString(), row.SourceVersion, false,
                row.Name, row.Address, null, row.ManagerId?.ToString(), AsUtc(row.UpdatedAt))).ToArray();
        }
        throw new InvalidDataException("Only Outlet and Warehouse source snapshots are supported.");
    }

    private async Task ValidateReferencesAsync(
        long businessId,
        HrOrgUnitWriteData data,
        long? currentId,
        CancellationToken cancellationToken)
    {
        var duplicateCode = await db.HrOrgUnits.AnyAsync(unit =>
            unit.BusinessId == businessId && unit.Code == data.Code.Trim() && unit.Id != currentId,
            cancellationToken);
        if (duplicateCode)
            throw new InvalidOperationException("Organisation unit code already exists in this Business.");

        if (data.ParentOrgUnitId.HasValue)
        {
            if (data.ParentOrgUnitId == currentId)
                throw new InvalidOperationException("An organisation unit cannot be its own parent.");
            var parent = await db.HrOrgUnits.AsNoTracking().SingleOrDefaultAsync(
                unit => unit.BusinessId == businessId && unit.Id == data.ParentOrgUnitId.Value,
                cancellationToken);
            if (parent is null || !parent.IsActive)
                throw new InvalidOperationException("Parent organisation unit must be active in the same Business.");
            var visited = new HashSet<long>();
            var nextId = data.ParentOrgUnitId;
            while (nextId.HasValue)
            {
                if (!visited.Add(nextId.Value) || nextId == currentId)
                    throw new InvalidOperationException("Organisation unit hierarchy cannot contain a cycle.");
                nextId = await db.HrOrgUnits.AsNoTracking()
                    .Where(unit => unit.BusinessId == businessId && unit.Id == nextId.Value)
                    .Select(unit => unit.ParentOrgUnitId).SingleOrDefaultAsync(cancellationToken);
            }
        }
        if (data.OperationalLocationProjectionId.HasValue)
        {
            var validLocation = await db.OperationalLocationProjections.AsNoTracking().AnyAsync(location =>
                location.BusinessId == businessId && location.Id == data.OperationalLocationProjectionId.Value
                && location.IsSourceActive && !location.IsSourceDeleted, cancellationToken);
            if (!validLocation)
                throw new InvalidOperationException("Organisation unit location must be an active projection in the same Business.");
        }
    }

    private static void ValidateSource(OperationalLocationSourceEvent source)
    {
        if (source.BusinessId <= 0 || source.SourceSystem != "RetailPOS"
            || source.SourceEntityType is not ("Outlet" or "Warehouse")
            || !long.TryParse(source.SourceEntityId, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0
            || source.SourceVersion <= 0 || string.IsNullOrWhiteSpace(source.Name)
            || source.Name.Length > 150 || source.Address is null || source.Address.Length > 500
            || source.ContactNumber?.Length > 100 || source.ManagerExternalUserId?.Length > 80
            || source.SourceUpdatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("Operational location source snapshot is invalid.");
    }

    private static void ValidateDates(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
            throw new ArgumentException("Organisation unit end date cannot precede its effective start date.");
    }

    private static HrOrgUnitReadModel Map(HrOrgUnit row) => new(
        row.Id, row.BusinessId, row.Code, row.Name, row.UnitType, row.ParentOrgUnitId,
        row.OperationalLocationProjectionId, row.EffectiveFrom, row.EffectiveTo, row.IsActive,
        row.Version, row.CreatedAtUtc, row.UpdatedAtUtc);

    private static OperationalLocationReadModel Map(OperationalLocationProjection row) => new(
        row.Id, row.BusinessId, row.SourceSystem, row.SourceEntityType, row.SourceEntityId, row.Name,
        row.Address, row.ContactNumber, row.ManagerExternalUserId, row.SourceVersion, row.SourceUpdatedAtUtc,
        row.LastSyncedAtUtc, row.IsSourceActive, row.IsSourceDeleted, row.SyncStatus);

    private static DateTime AsUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
