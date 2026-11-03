using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Organisation;

public sealed class HrPositionStore(RetailPOSDbContext db) : IHrPositionStore
{
    public async Task<IReadOnlyList<HrPositionReadModel>> GetPositionsAsync(
        long businessId,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = db.HrPositions.AsNoTracking()
            .Where(row => row.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(row => row.Code.ToLower().Contains(term)
                || row.Name.ToLower().Contains(term));
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
            query = query.Where(row =>
                (row.OrgUnit.OperationalLocationProjectionId == null
                    || Enumerable.Contains(allowedProjectionIds, row.OrgUnit.OperationalLocationProjectionId.Value)));
        }

        var rows = await query.OrderBy(row => row.Code).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public Task<long?> GetPositionLocationProjectionIdAsync(
        long businessId,
        long positionId,
        CancellationToken cancellationToken) =>
        // Only Department/Team visibility applies; legacy Position scope is historical data.
        db.HrPositions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == positionId)
            .Select(row => row.OrgUnit.OperationalLocationProjectionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HrPositionReadModel> CreateAsync(
        long businessId,
        HrPositionWriteData data,
        CancellationToken cancellationToken)
    {
        await LockReportingHierarchyAsync(businessId, cancellationToken);
        await ValidateReferencesAsync(businessId, data, null, cancellationToken);
        var row = new HrPosition
        {
            BusinessId = businessId,
            Code = data.Code.Trim(),
            Name = data.Name.Trim(),
            OrgUnitId = data.OrgUnitId,
            ReportsToPositionId = data.ReportsToPositionId,
            Status = ParseStatus(data.Status),
            Version = 1
        };
        db.HrPositions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<HrPositionReadModel?> UpdateAsync(
        long businessId,
        long positionId,
        long expectedVersion,
        HrPositionWriteData data,
        CancellationToken cancellationToken)
    {
        await LockReportingHierarchyAsync(businessId, cancellationToken);
        var row = await db.HrPositions.SingleOrDefaultAsync(
            candidate => candidate.BusinessId == businessId && candidate.Id == positionId,
            cancellationToken);
        if (row is null)
            return null;
        if (row.Version != expectedVersion)
            throw new HrOrganisationConcurrencyException("Position changed since it was read.");
        if (row.Status == HrPositionStatus.Inactive)
            throw new InvalidOperationException("An inactive position cannot be updated.");
        await ValidateReferencesAsync(businessId, data, row.Id, cancellationToken);

        row.Code = data.Code.Trim();
        row.Name = data.Name.Trim();
        row.OrgUnitId = data.OrgUnitId;
        row.ReportsToPositionId = data.ReportsToPositionId;
        row.Status = ParseStatus(data.Status);
        row.Version = checked(row.Version + 1);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrOrganisationConcurrencyException("Position was changed by another request.", exception);
        }
        return Map(row);
    }

    public async Task<HrPositionReadModel?> DeactivateAsync(
        long businessId,
        long positionId,
        long expectedVersion,
        DateOnly effectiveTo,
        CancellationToken cancellationToken)
    {
        var row = await db.HrPositions.SingleOrDefaultAsync(
            candidate => candidate.BusinessId == businessId && candidate.Id == positionId,
            cancellationToken);
        if (row is null)
            return null;
        if (row.Version != expectedVersion)
            throw new HrOrganisationConcurrencyException("Position changed since it was read.");
        if (row.EffectiveFrom.HasValue && effectiveTo < row.EffectiveFrom.Value)
            throw new ArgumentException("Position end date cannot precede its effective start date.");
        if (row.Status == HrPositionStatus.Inactive)
            throw new InvalidOperationException("Position is already inactive.");

        row.Status = HrPositionStatus.Inactive;
        row.EffectiveTo = effectiveTo;
        row.Version = checked(row.Version + 1);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrOrganisationConcurrencyException("Position was changed by another request.", exception);
        }
        return Map(row);
    }

    private async Task ValidateReferencesAsync(
        long businessId,
        HrPositionWriteData data,
        long? currentPositionId,
        CancellationToken cancellationToken)
    {
        var code = data.Code.Trim();
        var duplicateCodeQuery = db.HrPositions.Where(row =>
            row.BusinessId == businessId && row.Code == code);
        if (currentPositionId.HasValue)
            duplicateCodeQuery = duplicateCodeQuery.Where(row => row.Id != currentPositionId.Value);
        if (await duplicateCodeQuery.AnyAsync(cancellationToken))
            throw new InvalidOperationException("A position with this code already exists in this Business.");

        if (!await db.HrOrgUnits.AnyAsync(row =>
                row.BusinessId == businessId && row.Id == data.OrgUnitId, cancellationToken))
            throw new InvalidOperationException("Department / Team was not found in this Business.");

        if (!data.ReportsToPositionId.HasValue)
            return;
        if (currentPositionId == data.ReportsToPositionId)
            throw new InvalidOperationException("A position cannot report to itself.");

        var ancestors = await db.HrPositions.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .Select(row => new { row.Id, row.ReportsToPositionId })
            .ToDictionaryAsync(row => row.Id, row => row.ReportsToPositionId, cancellationToken);
        if (!ancestors.ContainsKey(data.ReportsToPositionId.Value))
            throw new InvalidOperationException("Reports-to position was not found in this Business.");

        var visited = new HashSet<long>();
        long? ancestorId = data.ReportsToPositionId;
        while (ancestorId.HasValue)
        {
            if (ancestorId == currentPositionId)
                throw new InvalidOperationException("Position reporting hierarchy cannot contain a cycle.");
            if (!visited.Add(ancestorId.Value))
                throw new InvalidOperationException("Existing position reporting hierarchy contains a cycle.");
            if (!ancestors.TryGetValue(ancestorId.Value, out ancestorId))
                throw new InvalidOperationException("Reports-to hierarchy contains a position outside this Business.");
        }
    }

    private async Task LockReportingHierarchyAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            if (db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Position hierarchy changes require the Audit v2 transaction.");
            var lockKey = $"hr-position:{businessId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
    }

    private static HrPositionStatus ParseStatus(string status) =>
        Enum.Parse<HrPositionStatus>(status, ignoreCase: true);

    private static HrPositionReadModel Map(HrPosition row) => new(
        row.Id, row.BusinessId, row.Code, row.Name, row.OrgUnitId,
        row.OperationalLocationProjectionId, row.ReportsToPositionId,
        row.Status.ToString(), row.EffectiveFrom, row.EffectiveTo, row.Version);
}
