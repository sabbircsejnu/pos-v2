using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed class HrEmployeeLocationAssignmentStore(RetailPOSDbContext db)
    : IHrEmployeeLocationAssignmentStore, IHrEmployeeLocationTransferActivationStore
{
    public async Task<bool> IsEmployeeAccessibleAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken)
    {
        var employmentStatus = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == employeeId)
            .Select(row => row.CurrentEmploymentStatus)
            .SingleOrDefaultAsync(cancellationToken);
        if (employmentStatus is null)
            return false;
        if (allowedLocations is null)
            return true;
        if (allowedLocations.Count == 0)
            return false;
        var sources = allowedLocations.ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assignments = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .Join(db.OperationalLocationProjections.AsNoTracking(),
                row => new { row.BusinessId, row.OperationalLocationProjectionId },
                location => new { location.BusinessId, OperationalLocationProjectionId = location.Id },
                (row, location) => new
                {
                    location.SourceEntityType,
                    location.SourceEntityId,
                    row.AssignmentType,
                    row.Status,
                    row.EffectiveFrom,
                    row.EffectiveTo
                })
            .ToArrayAsync(cancellationToken);
        if (assignments.Any(assignment => assignment.AssignmentType == "Primary"
                && assignment.Status == "Effective"
                && assignment.EffectiveFrom <= today
                && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today)
                && sources.Contains(new OperationalLocationSourceIdentity(
                    assignment.SourceEntityType, assignment.SourceEntityId))))
            return true;
        var hasCurrentAssignment = assignments.Any(assignment => assignment.AssignmentType == "Primary"
            && assignment.Status == "Effective"
            && assignment.EffectiveFrom <= today
            && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today));
        return employmentStatus == "Exited" && !hasCurrentAssignment && assignments.Any(assignment =>
            assignment.AssignmentType == "Primary" && sources.Contains(new OperationalLocationSourceIdentity(
                assignment.SourceEntityType, assignment.SourceEntityId)));
    }

    public async Task<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>> GetHistoryAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken)
    {
        IQueryable<HrEmployeeLocationAssignment> query = db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId);
        if (allowedLocations is not null)
        {
            if (allowedLocations.Count == 0)
                return [];
            var sources = allowedLocations.ToHashSet();
            var businessLocations = await db.OperationalLocationProjections.AsNoTracking()
                .Where(location => location.BusinessId == businessId)
                .Select(location => new { location.Id, location.SourceEntityType, location.SourceEntityId })
                .ToArrayAsync(cancellationToken);
            var allowedIds = businessLocations
                .Where(location => sources.Contains(new OperationalLocationSourceIdentity(
                    location.SourceEntityType, location.SourceEntityId)))
                .Select(location => location.Id)
                .ToArray();
            if (allowedIds.Length == 0)
                return [];
            query = query.Where(row => allowedIds.Contains(row.OperationalLocationProjectionId));
        }

        return await query.OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .Select(row => ToReadModel(row))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<HrEmployeeLocationAssignmentReadModel?> GetEffectivePrimaryAsync(
        long businessId,
        long employeeId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary" && row.Status == "Effective"
                && row.EffectiveFrom <= today
                && (!row.EffectiveTo.HasValue || row.EffectiveTo >= today))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (rows.Length > 1)
            throw new InvalidOperationException("Employee has overlapping effective Primary location assignments.");
        return rows.Length == 0 ? null : ToReadModel(rows[0]);
    }

    public async Task<HrEmployeeLocationAssignmentReadModel?> CreateAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedEmployeeVersion,
        HrEmployeeLocationAssignmentData data,
        CancellationToken cancellationToken)
    {
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == employeeId, cancellationToken);
        if (employee is null)
            return null;
        RequireEmployeeVersion(employee, expectedEmployeeVersion);
        if (employee.CurrentEmploymentStatus == "Exited")
            throw new InvalidOperationException("An exited employee must be rehired before receiving a location assignment.");
        if (!await db.HrEmploymentPeriods.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId && row.Status == "Active",
                cancellationToken))
            throw new InvalidOperationException("Employee has no active employment period.");

        var location = await db.OperationalLocationProjections.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == data.OperationalLocationProjectionId,
            cancellationToken);
        if (location is null)
            throw new InvalidOperationException("Operational location was not found in this Business.");
        EnsureLocationActive(location);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var status = data.EffectiveTo.HasValue && data.EffectiveTo.Value < today ? "Closed" : "Effective";
        if (data.AssignmentType == "Primary")
        {
            var conflicts = await db.HrEmployeeLocationAssignments.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary"
                && row.EffectiveFrom <= data.EffectiveTo.GetValueOrDefault(DateOnly.MaxValue)
                && (!row.EffectiveTo.HasValue || row.EffectiveTo >= data.EffectiveFrom),
                cancellationToken);
            if (conflicts)
                throw new InvalidOperationException(
                    "A Primary assignment already exists for this effective period; request a transfer instead.");
        }

        var assignment = new HrEmployeeLocationAssignment
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            OperationalLocationProjectionId = location.Id,
            LocationNameSnapshot = location.Name,
            SourceSystemSnapshot = location.SourceSystem,
            SourceEntityTypeSnapshot = location.SourceEntityType,
            SourceEntityIdSnapshot = location.SourceEntityId,
            LocationSourceVersionSnapshot = location.SourceVersion,
            AssignmentType = data.AssignmentType,
            IsTemporary = data.IsTemporary,
            EffectiveFrom = data.EffectiveFrom,
            EffectiveTo = data.EffectiveTo,
            Status = status,
            Reason = data.Reason.Trim(),
            RequestedByUserId = actorUserId,
            RequestedAtUtc = DateTime.UtcNow,
            ApprovedByUserId = actorUserId,
            ApprovedAtUtc = DateTime.UtcNow,
            Version = 1
        };
        db.HrEmployeeLocationAssignments.Add(assignment);
        if (data.AssignmentType == "Primary" && data.EffectiveFrom <= today
            && (!data.EffectiveTo.HasValue || data.EffectiveTo >= today))
            employee.CurrentPrimaryLocationProjectionId = location.Id;
        employee.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return ToReadModel(assignment);
    }

    public async Task<HrEmployeeLocationTransferReadModel?> GetTransferAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        var row = await db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId
                && item.EmployeeId == employeeId && item.Id == transferId, cancellationToken);
        return row is null ? null : ToReadModel(row);
    }

    public async Task<IReadOnlyList<HrEmployeeLocationTransferReadModel>> GetTransfersAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken)
    {
        IQueryable<HrEmployeeLocationTransferRequest> query = db.HrEmployeeLocationTransferRequests
            .AsNoTracking().Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId);
        if (allowedLocations is not null)
        {
            if (allowedLocations.Count == 0)
                return [];
            var sources = allowedLocations.ToHashSet();
            var projectionIds = (await db.OperationalLocationProjections.AsNoTracking()
                    .Where(location => location.BusinessId == businessId)
                    .Select(location => new { location.Id, location.SourceEntityType, location.SourceEntityId })
                    .ToArrayAsync(cancellationToken))
                .Where(location => sources.Contains(new OperationalLocationSourceIdentity(
                    location.SourceEntityType, location.SourceEntityId)))
                .Select(location => location.Id)
                .ToArray();
            query = query.Where(row => projectionIds.Contains(row.TargetLocationProjectionId));
        }
        return await query.OrderByDescending(row => row.RequestedAtUtc)
            .ThenByDescending(row => row.Id)
            .Select(row => ToReadModel(row))
            .ToArrayAsync(cancellationToken);
    }

    public Task<long?> GetTransferApprovalInstanceIdAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        CancellationToken cancellationToken) =>
        db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.EmployeeId == employeeId && item.Id == transferId)
            .Select(item => item.ApprovalInstanceId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> CancelScheduledTransferAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        long actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        var transfer = await db.HrEmployeeLocationTransferRequests.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId && row.Id == transferId,
            cancellationToken);
        if (transfer?.Status != "ApprovedPendingEffective")
            return false;
        transfer.Status = "Cancelled";
        transfer.DecidedByUserId = actorUserId;
        transfer.DecidedAtUtc = DateTime.UtcNow;
        transfer.DecisionReason = reason.Trim();
        transfer.Version++;
        return true;
    }

    public async Task<IReadOnlyList<long>> GetBusinessesWithDueTransfersAsync(
        DateOnly effectiveThrough,
        int batchSize,
        CancellationToken cancellationToken) =>
        await db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .Where(row => row.Status == "ApprovedPendingEffective" && row.EffectiveFrom <= effectiveThrough)
            .OrderBy(row => row.BusinessId)
            .Select(row => row.BusinessId)
            .Distinct()
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueTransferIdsAsync(
        long businessId,
        DateOnly effectiveThrough,
        int batchSize,
        CancellationToken cancellationToken) =>
        await db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.Status == "ApprovedPendingEffective" && row.EffectiveFrom <= effectiveThrough)
            .OrderBy(row => row.EffectiveFrom).ThenBy(row => row.Id)
            .Select(row => row.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

    public async Task<HrEmployeeLocationTransferActivationResult> ActivateDueTransferAsync(
        long businessId,
        Guid transferId,
        DateOnly effectiveThrough,
        CancellationToken cancellationToken)
    {
        var employeeId = await db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == transferId)
            .Select(row => (long?)row.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!employeeId.HasValue)
            return new HrEmployeeLocationTransferActivationResult(false, false, false);

        await LockEmployeeAsync(businessId, employeeId.Value, cancellationToken);
        var transfer = await db.HrEmployeeLocationTransferRequests.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == transferId, cancellationToken);
        if (transfer is null || transfer.EmployeeId != employeeId.Value
            || transfer.Status != "ApprovedPendingEffective"
            || transfer.EffectiveFrom > effectiveThrough)
            return new HrEmployeeLocationTransferActivationResult(false, false, false);

        var employee = await db.HrEmployees.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == transfer.EmployeeId, cancellationToken);
        var staleReason = employee is null
            ? "Employee no longer exists in the transfer Business."
            : employee.Version != transfer.ExpectedEmployeeVersion
                ? "Employee changed after this transfer was requested."
                : employee.CurrentEmploymentStatus == "Exited"
                    ? "Employee exited before the scheduled transfer became effective."
                    : null;
        if (staleReason is not null)
            return MarkSuperseded(transfer, staleReason);

        if (!await db.HrEmploymentPeriods.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == transfer.EmployeeId && row.Status == "Active",
                cancellationToken))
            return MarkSuperseded(transfer, "Employee has no active employment period.");

        var location = await db.OperationalLocationProjections.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == transfer.TargetLocationProjectionId,
            cancellationToken);
        if (location is null || !location.IsSourceActive || location.IsSourceDeleted
            || location.SyncStatus != "Current")
            return MarkSuperseded(transfer, "Transfer target is no longer an active operational location.");

        var previous = await db.HrEmployeeLocationAssignments.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == transfer.EmployeeId
            && row.AssignmentType == "Primary" && row.Status == "Effective"
            && row.EffectiveFrom < transfer.EffectiveFrom
            && (!row.EffectiveTo.HasValue || row.EffectiveTo >= transfer.EffectiveFrom),
            cancellationToken);
        if (previous is null
            || employee!.CurrentPrimaryLocationProjectionId != previous.OperationalLocationProjectionId)
            return MarkSuperseded(transfer, "Current Primary assignment changed before transfer activation.");

        var conflict = await db.HrEmployeeLocationAssignments.AnyAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == transfer.EmployeeId
            && row.AssignmentType == "Primary"
            && row.EffectiveFrom <= DateOnly.MaxValue
            && (!row.EffectiveTo.HasValue || row.EffectiveTo >= transfer.EffectiveFrom)
            && row.Id != previous.Id, cancellationToken);
        if (conflict)
            return MarkSuperseded(transfer, "A conflicting Primary assignment exists at activation.");

        previous.EffectiveTo = transfer.EffectiveFrom.AddDays(-1);
        previous.Status = "Closed";
        previous.Version++;
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = businessId,
            EmployeeId = employee.Id,
            OperationalLocationProjectionId = transfer.TargetLocationProjectionId,
            LocationNameSnapshot = transfer.LocationNameSnapshot,
            SourceSystemSnapshot = transfer.SourceSystemSnapshot,
            SourceEntityTypeSnapshot = transfer.SourceEntityTypeSnapshot,
            SourceEntityIdSnapshot = transfer.SourceEntityIdSnapshot,
            LocationSourceVersionSnapshot = transfer.LocationSourceVersionSnapshot,
            AssignmentType = "Primary",
            EffectiveFrom = transfer.EffectiveFrom,
            Status = "Effective",
            Reason = transfer.Reason,
            RequestedByUserId = transfer.RequestedByUserId,
            RequestedAtUtc = transfer.RequestedAtUtc,
            ApprovedByUserId = transfer.DecidedByUserId,
            ApprovedAtUtc = transfer.DecidedAtUtc,
            ApprovalInstanceId = transfer.ApprovalInstanceId,
            Version = 1
        });
        employee.CurrentPrimaryLocationProjectionId = transfer.TargetLocationProjectionId;
        employee.Version++;
        transfer.Status = "Effective";
        transfer.Version++;
        return new HrEmployeeLocationTransferActivationResult(true, true, false);
    }

    private static HrEmployeeLocationTransferActivationResult MarkSuperseded(
        HrEmployeeLocationTransferRequest transfer,
        string reason)
    {
        transfer.Status = "Superseded";
        transfer.DecisionReason = reason;
        transfer.Version++;
        return new HrEmployeeLocationTransferActivationResult(true, false, true);
    }

    internal async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {businessId} AND id = {employeeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                return;
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException(
                "Employee location assignment serialization is configured only for PostgreSQL and SQLite tests.");
        }
    }

    internal static void RequireEmployeeVersion(HrEmployee employee, long expectedVersion)
    {
        if (employee.Version != expectedVersion)
            throw new HrEmployeeConcurrencyException("Employee version is stale.");
    }

    internal static void EnsureLocationActive(
        RetailPOS.Core.Entities.HR.Organisation.OperationalLocationProjection location)
    {
        if (!location.IsSourceActive || location.IsSourceDeleted || location.SyncStatus != "Current")
            throw new InvalidOperationException("Inactive, deleted or stale operational locations cannot be assigned.");
    }

    private static HrEmployeeLocationAssignmentReadModel ToReadModel(HrEmployeeLocationAssignment row) =>
        new(row.Id, row.EmployeeId, row.OperationalLocationProjectionId, row.LocationNameSnapshot,
            row.SourceSystemSnapshot, row.SourceEntityTypeSnapshot, row.SourceEntityIdSnapshot,
            row.LocationSourceVersionSnapshot, row.AssignmentType, row.IsTemporary, row.EffectiveFrom,
            row.EffectiveTo, row.Status, row.Reason, row.RequestedByUserId,
            new DateTimeOffset(DateTime.SpecifyKind(row.RequestedAtUtc, DateTimeKind.Utc)),
            row.ApprovedByUserId,
            row.ApprovedAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.ApprovedAtUtc.Value, DateTimeKind.Utc))
                : null,
            row.ApprovalInstanceId, row.Version);

    private static HrEmployeeLocationTransferReadModel ToReadModel(HrEmployeeLocationTransferRequest row) =>
        new(row.Id, row.EmployeeId, row.TargetLocationProjectionId, row.LocationNameSnapshot,
            row.SourceSystemSnapshot, row.SourceEntityTypeSnapshot, row.SourceEntityIdSnapshot,
            row.EffectiveFrom, row.Status, row.Reason, row.RequestedByUserId,
            new DateTimeOffset(DateTime.SpecifyKind(row.RequestedAtUtc, DateTimeKind.Utc)),
            row.ApprovalInstanceId, row.Version);
}
