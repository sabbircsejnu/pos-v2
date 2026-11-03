using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed class HrEmployeeLocationApprovalWorkflow(RetailPOSDbContext db) : IApprovalKernelWorkflow
{
    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId,
        long requesterUserId,
        SubmitApprovalCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessType != ApprovalProcessType.EmployeeTransfer)
            return null;
        if (request.TargetType != HrEmployeeLocationTransferConstants.TargetType
            || !Guid.TryParse(request.TargetId, out var transferId)
            || string.IsNullOrWhiteSpace(request.TargetPayload))
            throw new ArgumentException("Employee transfer approval requires its explicit HR transfer target payload.");

        var payload = JsonSerializer.Deserialize<HrEmployeeLocationTransferPayload>(request.TargetPayload)
            ?? throw new ArgumentException("Employee transfer target payload is invalid.");
        if (payload.TransferId != transferId || payload.EmployeeId <= 0
            || payload.OperationalLocationProjectionId <= 0 || payload.ExpectedEmployeeVersion <= 0
            || requesterUserId <= 0 || payload.EffectiveFrom < DateOnly.FromDateTime(DateTime.UtcNow)
            || string.IsNullOrWhiteSpace(payload.Reason) || payload.Reason.Trim().Length > 500)
            throw new ArgumentException("Employee transfer target fields are invalid.");

        var canonicalPayload = JsonSerializer.Serialize(payload with { Reason = payload.Reason.Trim() });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        if (!string.Equals(requestHash, request.RequestHash, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalKernelConflictException("Employee transfer content does not match its request hash.");

        await HrEmployeeLocationAssignmentStoreLock.LockAsync(db, businessId, payload.EmployeeId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == payload.EmployeeId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Employee was not found in the current Business.");
        HrEmployeeLocationAssignmentStore.RequireEmployeeVersion(employee, payload.ExpectedEmployeeVersion);
        if (employee.CurrentEmploymentStatus == "Exited"
            || !await db.HrEmploymentPeriods.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employee.Id && row.Status == "Active",
                cancellationToken))
            throw new InvalidOperationException("An exited employee must be rehired before a location transfer.");

        if (await db.HrEmployeeLocationTransferRequests.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employee.Id
                && (row.Status == "PendingApproval" || row.Status == "ApprovedPendingEffective"),
                cancellationToken))
            throw new ApprovalKernelConflictException(
                "Employee already has a pending or scheduled Primary location transfer.");

        var currentPrimary = await db.HrEmployeeLocationAssignments.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employee.Id
            && row.AssignmentType == "Primary" && row.Status == "Effective"
            && row.EffectiveFrom <= payload.EffectiveFrom
            && (!row.EffectiveTo.HasValue || row.EffectiveTo >= payload.EffectiveFrom), cancellationToken)
            ?? throw new InvalidOperationException("Employee has no effective Primary location to transfer from.");
        if (currentPrimary.EffectiveFrom >= payload.EffectiveFrom)
            throw new InvalidOperationException("Transfer effective date must follow the current Primary assignment start date.");
        if (currentPrimary.OperationalLocationProjectionId == payload.OperationalLocationProjectionId)
            throw new InvalidOperationException("The transfer target is already the employee's Primary location.");

        var location = await db.OperationalLocationProjections.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == payload.OperationalLocationProjectionId,
            cancellationToken)
            ?? throw new InvalidOperationException("Transfer location was not found in this Business.");
        HrEmployeeLocationAssignmentStore.EnsureLocationActive(location);

        db.HrEmployeeLocationTransferRequests.Add(new HrEmployeeLocationTransferRequest
        {
            Id = payload.TransferId,
            BusinessId = businessId,
            EmployeeId = employee.Id,
            TargetLocationProjectionId = location.Id,
            LocationNameSnapshot = location.Name,
            SourceSystemSnapshot = location.SourceSystem,
            SourceEntityTypeSnapshot = location.SourceEntityType,
            SourceEntityIdSnapshot = location.SourceEntityId,
            LocationSourceVersionSnapshot = location.SourceVersion,
            EffectiveFrom = payload.EffectiveFrom,
            ExpectedEmployeeVersion = payload.ExpectedEmployeeVersion,
            Reason = payload.Reason.Trim(),
            RequestHash = requestHash,
            Status = "PendingApproval",
            RequestedByUserId = requesterUserId,
            RequestedAtUtc = DateTime.UtcNow,
            Version = 1
        });
        return new ApprovalTargetSnapshot(1, requestHash);
    }

    public Task SubmissionCreatedAsync(
        long businessId,
        ApprovalInstance instance,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.EmployeeTransfer)
            return Task.CompletedTask;
        if (instance.TargetType != HrEmployeeLocationTransferConstants.TargetType
            || !Guid.TryParse(instance.TargetId, out var transferId))
            throw new ApprovalKernelConflictException("Approval instance has an invalid EmployeeTransfer target.");
        var request = db.HrEmployeeLocationTransferRequests.Local.SingleOrDefault(row =>
            row.BusinessId == businessId && row.Id == transferId)
            ?? throw new ApprovalKernelConflictException("Prepared EmployeeTransfer request was not tracked.");
        request.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId,
        ApprovalInstance instance,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.EmployeeTransfer)
            return;
        if (instance.TargetType != HrEmployeeLocationTransferConstants.TargetType
            || !Guid.TryParse(instance.TargetId, out var transferId))
            throw new ApprovalKernelConflictException("Approval instance has an invalid EmployeeTransfer target.");
        var transfer = await db.HrEmployeeLocationTransferRequests.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == transferId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("EmployeeTransfer target was not found.");
        if (transfer.Status != "PendingApproval")
            throw new ApprovalKernelConflictException("EmployeeTransfer target is already terminal.");

        transfer.DecidedByUserId = actorUserId;
        transfer.DecidedAtUtc = DateTime.UtcNow;
        transfer.DecisionReason = reason?.Trim();
        if (instance.Status == ApprovalInstanceStatus.Rejected)
        {
            transfer.Status = "Rejected";
            transfer.Version++;
            return;
        }
        if (instance.Status == ApprovalInstanceStatus.Cancelled)
        {
            transfer.Status = "Cancelled";
            transfer.Version++;
            return;
        }
        if (instance.Status != ApprovalInstanceStatus.Approved)
            throw new ApprovalKernelConflictException(
                "Only a terminal Approval Kernel decision can complete a transfer.");

        await HrEmployeeLocationAssignmentStoreLock.LockAsync(
            db, businessId, transfer.EmployeeId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == transfer.EmployeeId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Employee was not found in the current Business.");
        HrEmployeeLocationAssignmentStore.RequireEmployeeVersion(employee, transfer.ExpectedEmployeeVersion);
        if (employee.CurrentEmploymentStatus == "Exited"
            || !await db.HrEmploymentPeriods.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employee.Id && row.Status == "Active",
                cancellationToken))
            throw new InvalidOperationException("Employee must have an active employment period when a transfer is approved.");

        var location = await db.OperationalLocationProjections.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == transfer.TargetLocationProjectionId,
            cancellationToken)
            ?? throw new InvalidOperationException("Transfer location was not found in this Business.");
        HrEmployeeLocationAssignmentStore.EnsureLocationActive(location);

        var previous = await db.HrEmployeeLocationAssignments.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employee.Id
            && row.AssignmentType == "Primary" && row.Status == "Effective"
            && row.EffectiveFrom <= DateOnly.FromDateTime(DateTime.UtcNow)
            && (!row.EffectiveTo.HasValue || row.EffectiveTo >= DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken)
            ?? throw new InvalidOperationException("No effective Primary assignment exists when the transfer is approved.");
        if (previous.EffectiveFrom >= transfer.EffectiveFrom
            || employee.CurrentPrimaryLocationProjectionId != previous.OperationalLocationProjectionId
            || previous.EffectiveTo.HasValue && previous.EffectiveTo < transfer.EffectiveFrom)
            throw new HrEmployeeConcurrencyException("Current Primary location changed after the transfer was requested.");

        if (transfer.EffectiveFrom > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            transfer.Status = "ApprovedPendingEffective";
            transfer.Version++;
            return;
        }

        ApplyTransfer(transfer, employee, previous, instance, actorUserId);
        transfer.Status = "Effective";
        transfer.Version++;
    }

    private void ApplyTransfer(
        HrEmployeeLocationTransferRequest transfer,
        HrEmployee employee,
        HrEmployeeLocationAssignment previous,
        ApprovalInstance instance,
        long actorUserId)
    {
        previous.EffectiveTo = transfer.EffectiveFrom.AddDays(-1);
        previous.Status = "Closed";
        previous.Version++;
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = transfer.BusinessId,
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
            ApprovedByUserId = actorUserId,
            ApprovedAtUtc = DateTime.UtcNow,
            ApprovalInstance = instance,
            Version = 1
        });
        employee.CurrentPrimaryLocationProjectionId = transfer.TargetLocationProjectionId;
        employee.Version++;
    }
}

public sealed class HrEmployeeLocationApprovalTargetSnapshotReader(RetailPOSDbContext db)
    : IApprovalTargetSnapshotReader
{
    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId,
        string targetType,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (targetType != HrEmployeeLocationTransferConstants.TargetType
            || !Guid.TryParse(targetId, out var transferId))
            return null;
        var request = await db.HrEmployeeLocationTransferRequests.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == transferId,
                cancellationToken);
        return request is null ? null : new ApprovalTargetSnapshot(1, request.RequestHash);
    }
}

internal static class HrEmployeeLocationAssignmentStoreLock
{
    public static async Task LockAsync(
        RetailPOSDbContext db,
        long businessId,
        long employeeId,
        CancellationToken cancellationToken)
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
}
