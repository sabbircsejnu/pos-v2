using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Leave;

public sealed class HrLeaveRequestStore(
    RetailPOSDbContext db,
    IHrCalendarStore calendars,
    IHrLeaveStore leaveStore,
    TimeProvider clock)
    : IHrLeaveRequestStore, IApprovalTargetSnapshotReader, IApprovalKernelWorkflow,
        IConfidentialFileVersionStore
{
    public async Task<HrLeaveRequestModel> CreateDraftAsync(
        long businessId,
        long employeeId,
        long actorUserId,
        HrLeaveRequestDraftInput input,
        CancellationToken cancellationToken)
    {
        HrLeaveRequestRules.ValidateDraft(input);
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == employeeId && row.CurrentEmploymentStatus == "Active",
                cancellationToken)
            ?? throw new KeyNotFoundException("Active employee was not found in this Business.");
        var type = await db.HrLeaveTypes.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == input.LeaveTypeId && row.IsActive,
                cancellationToken)
            ?? throw new KeyNotFoundException("Active leave type was not found in this Business.");

        var days = new List<HrLeaveRequestDay>(input.EndDate.DayNumber - input.StartDate.DayNumber + 1);
        long? requestLocationId = null;
        decimal units = 0;
        for (var date = input.StartDate; date <= input.EndDate; date = date.AddDays(1))
        {
            if (!await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                    period.BusinessId == businessId && period.EmployeeId == employeeId
                    && period.StartDate <= date && (period.EndDate == null || period.EndDate >= date),
                    cancellationToken))
                throw new InvalidOperationException($"Employee has no effective employment period on {date:yyyy-MM-dd}.");

            var locationId = await PrimaryLocationOnAsync(businessId, employee, date, cancellationToken);
            if (requestLocationId.HasValue && requestLocationId.Value != locationId)
                throw new InvalidOperationException(
                    "A leave request cannot cross a Primary location transfer; create separate requests for each location.");
            requestLocationId = locationId;

            var roster = await db.Set<HrRosterAssignment>().AsNoTracking().SingleOrDefaultAsync(row =>
                    row.BusinessId == businessId && row.EmployeeId == employeeId
                    && row.WorkDate == date && row.Status == "Published",
                    cancellationToken);
            if (roster is not null && roster.OperationalLocationProjectionId != locationId)
                throw new InvalidOperationException(
                    $"Published roster location does not match the employee's Primary location on {date:yyyy-MM-dd}.");

            var classification = roster is null
                ? await calendars.ClassifyAsync(businessId, date, locationId, cancellationToken)
                : ReadPublishedClassification(roster);
            var dayUnits = HrLeaveRequestRules.UnitsForDay(
                classification.Classification, roster is not null);
            var policyContext = await HrLeaveRules.ResolvePolicyForLeaveYearAsync(
                leaveStore, businessId, employeeId, type.Id, date, cancellationToken);
            days.Add(new HrLeaveRequestDay
            {
                BusinessId = businessId,
                EmployeeId = employeeId,
                WorkDate = date,
                RosterId = roster?.Id,
                OperationalLocationProjectionId = locationId,
                PolicyVersionId = policyContext.Policy.Id,
                Units = dayUnits,
                DayClassification = classification.Classification,
                ShiftSnapshotJson = roster?.ShiftSnapshotJson ?? "",
                CalendarEvidenceJson = JsonSerializer.Serialize(classification)
            });
            units += dayUnits;
            if (date == input.EndDate)
                break;
        }
        if (units <= 0 || requestLocationId is null)
            throw new InvalidOperationException("The selected date range contains no scheduled working days.");

        var request = new HrLeaveRequest
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            LeaveTypeId = type.Id,
            ScopeLocationProjectionId = requestLocationId.Value,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            RequestedUnits = units,
            Reason = input.Reason.Trim(),
            Status = "Draft",
            RequestedByUserId = actorUserId,
            RequestedAtUtc = clock.GetUtcNow().UtcDateTime,
            Version = 1,
            Days = days
        };
        request.RequestHash = RequestHash(request);
        db.HrLeaveRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return await ReadModelAsync(businessId, request.Id, cancellationToken)
            ?? throw new InvalidOperationException("Created leave request could not be reloaded.");
    }

    public Task<HrLeaveRequestModel?> GetAsync(
        long businessId, long requestId, CancellationToken cancellationToken) =>
        ReadModelAsync(businessId, requestId, cancellationToken);

    public async Task<IReadOnlyList<HrLeaveRequestModel>> ListAsync(
        long businessId,
        long? employeeId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = db.HrLeaveRequests.AsNoTracking().Where(row => row.BusinessId == businessId);
        if (employeeId.HasValue)
            query = query.Where(row => row.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(row => row.Status == status);
        var ids = await query.OrderByDescending(row => row.RequestedAtUtc).ThenByDescending(row => row.Id)
            .Select(row => row.Id).Take(500).ToArrayAsync(cancellationToken);
        var result = new List<HrLeaveRequestModel>(ids.Length);
        foreach (var id in ids)
            if (await ReadModelAsync(businessId, id, cancellationToken) is { } model)
                result.Add(model);
        return result;
    }

    public async Task<HrLeaveRequestSnapshot?> FindForApprovalAsync(
        long businessId, long requestId, CancellationToken cancellationToken)
    {
        var row = await db.HrLeaveRequests.AsNoTracking()
            .Include(item => item.Days)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == requestId,
                cancellationToken);
        return row is null ? null : Snapshot(row);
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId,
        long requesterUserId,
        SubmitApprovalCommand approval,
        CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.Leave)
            return null;
        if (approval.TargetType != HrLeaveApprovalConstants.TargetType
            || !long.TryParse(approval.TargetId, out var requestId) || requestId <= 0
            || approval.ScopeType != ApprovalScopeType.Location
            || !long.TryParse(approval.ScopeReference, out var scopeLocationId)
            || scopeLocationId <= 0)
            throw new ArgumentException("Leave approval requires a valid location-scoped leave request target.");

        await LockRequestEmployeeAsync(businessId, requestId, cancellationToken);
        var request = await db.HrLeaveRequests.Include(row => row.Days)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (request.Status != "Draft" || request.RequestedByUserId != requesterUserId
            || request.ScopeLocationProjectionId != scopeLocationId
            || request.Version != approval.TargetVersion
            || !string.Equals(request.RequestHash, approval.RequestHash, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalKernelConflictException(
                "Leave request changed, is not a draft, or does not match its approval scope.");

        var leaveType = await db.HrLeaveTypes.AsNoTracking().SingleAsync(row =>
            row.BusinessId == businessId && row.Id == request.LeaveTypeId, cancellationToken);
        if (leaveType.RequiresDocument
            && (!request.CurrentAttachmentVersionId.HasValue
                || !await IsCurrentCleanAttachmentAsync(
                    businessId, request.Id, request.CurrentAttachmentVersionId.Value, cancellationToken)))
            throw new InvalidOperationException(
                "A current clean scanned attachment is required before submitting this leave type.");

        var overlapping = await db.HrLeaveRequestDays.AsNoTracking().AnyAsync(day =>
            day.BusinessId == businessId && day.EmployeeId == request.EmployeeId
            && day.LeaveRequestId != request.Id && day.Units > 0
            && day.WorkDate >= request.StartDate && day.WorkDate <= request.EndDate
            && db.HrLeaveRequests.Any(other =>
                other.BusinessId == businessId && other.Id == day.LeaveRequestId
                && (other.Status == "PendingApproval" || other.Status == "Approved")),
            cancellationToken);
        if (overlapping)
            throw new ApprovalKernelConflictException(
                "An overlapping pending or approved leave request already exists.");

        request.SubmittedTargetVersion = approval.TargetVersion;
        request.Status = "PendingApproval";
        request.Version++;
        return new ApprovalTargetSnapshot(approval.TargetVersion, request.RequestHash!);
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.Leave)
            return Task.CompletedTask;
        if (instance.TargetType != HrLeaveApprovalConstants.TargetType
            || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Leave approval instance has an invalid request target.");
        var request = db.HrLeaveRequests.Local.SingleOrDefault(row =>
            row.BusinessId == businessId && row.Id == requestId)
            ?? throw new ApprovalKernelConflictException("Prepared leave request was not tracked.");
        if (request.Status != "PendingApproval" || request.SubmittedTargetVersion != instance.TargetVersion)
            throw new ApprovalKernelConflictException("Prepared leave request no longer matches its approval instance.");
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
        if (instance.ProcessType != ApprovalProcessType.Leave)
            return;
        if (instance.TargetType != HrLeaveApprovalConstants.TargetType
            || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Leave approval instance has an invalid request target.");
        await LockRequestEmployeeAsync(businessId, requestId, cancellationToken);
        var request = await db.HrLeaveRequests.Include(row => row.Days)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == requestId,
                cancellationToken)
            ?? throw new ApprovalKernelConflictException("Leave request target was not found.");
        if (request.Status != "PendingApproval" || request.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Leave request is already terminal or mismatched.");

        if (instance.Status == ApprovalInstanceStatus.Approved)
        {
            await EnsureNoOverlappingRequestAsync(request, cancellationToken);
            await PostRequestLedgerEntriesAsync(
                request, actorUserId, HrLeaveLedgerTransactionType.Consumption, "consume",
                cancellationToken);
            request.Status = "Approved";
        }
        else if (instance.Status == ApprovalInstanceStatus.Rejected)
            request.Status = "Rejected";
        else if (instance.Status == ApprovalInstanceStatus.Cancelled)
            request.Status = "Cancelled";
        else
            throw new ApprovalKernelConflictException("A non-terminal leave approval cannot update the request.");

        request.DecidedByUserId = actorUserId;
        request.DecidedAtUtc = clock.GetUtcNow().UtcDateTime;
        request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.Version++;
    }

    public async Task CancelAsync(
        long businessId,
        long requestId,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        await LockRequestEmployeeAsync(businessId, requestId, cancellationToken);
        var request = await db.HrLeaveRequests.Include(row => row.Days)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == requestId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (request.Status == "PendingApproval")
            throw new InvalidOperationException(
                "Cancel pending requests through the Approval Kernel cancellation command.");
        if (request.Status is not ("Draft" or "Approved"))
            throw new InvalidOperationException("Only a draft or approved leave request can be cancelled.");

        if (request.Status == "Approved")
            await PostRequestLedgerEntriesAsync(
                request, actorUserId, HrLeaveLedgerTransactionType.Restoration, "restore",
                cancellationToken);

        request.Status = "Cancelled";
        request.DecidedByUserId = actorUserId;
        request.DecidedAtUtc = clock.GetUtcNow().UtcDateTime;
        request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.Version++;
    }

    public async Task<bool> IsCurrentCleanAttachmentAsync(
        long businessId, long requestId, Guid versionId, CancellationToken cancellationToken)
    {
        var request = await db.HrLeaveRequests.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == requestId, cancellationToken);
        return request?.CurrentAttachmentVersionId == versionId
            && await db.HrLeaveRequestAttachmentVersions.AsNoTracking().AnyAsync(row =>
                row.BusinessId == businessId && row.LeaveRequestId == requestId
                && row.Id == versionId && row.ScanStatus == "Clean" && row.RevokedAtUtc == null
                && row.StorageKey != null, cancellationToken);
    }

    public async Task<IReadOnlyList<HrLeaveRequestAttachmentModel>> GetAttachmentsAsync(
        long businessId, long requestId, CancellationToken cancellationToken) =>
        await db.HrLeaveRequestAttachmentVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.LeaveRequestId == requestId)
            .OrderByDescending(row => row.VersionNo)
            .Select(row => new HrLeaveRequestAttachmentModel(
                row.Id, row.VersionNo, row.OriginalFileName, row.ContentType, row.SizeBytes,
                Enum.Parse<ConfidentialFileScanStatus>(row.ScanStatus), row.UploadedByUserId,
                new DateTimeOffset(DateTime.SpecifyKind(row.UploadedAtUtc, DateTimeKind.Utc))))
            .ToArrayAsync(cancellationToken);

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != HrLeaveApprovalConstants.TargetType
            || !long.TryParse(targetId, out var requestId) || requestId <= 0)
            return null;
        return await db.HrLeaveRequests.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == requestId)
            .Select(row => row.SubmittedTargetVersion == null || row.RequestHash == null
                ? null
                : new ApprovalTargetSnapshot(row.SubmittedTargetVersion.Value, row.RequestHash))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(ConfidentialFileVersion version, CancellationToken cancellationToken)
    {
        if (version.Classification != ConfidentialFileClassification.LeaveRequestAttachment)
            throw new InvalidOperationException("Leave attachment store received another confidential file classification.");
        var request = await db.HrLeaveRequests.SingleOrDefaultAsync(row =>
            row.BusinessId == version.BusinessId && row.Id == version.SubjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (request.Status != "Draft" || request.Version != version.ExpectedSubjectVersion)
            throw new InvalidOperationException("Only the current draft can receive an attachment.");
        var next = await NextAttachmentVersionAsync(version.BusinessId, request.Id, cancellationToken);
        if (version.Version != next)
            throw new InvalidOperationException("Leave attachment version changed during upload.");
        db.HrLeaveRequestAttachmentVersions.Add(AttachmentEntity(version, clock.GetUtcNow().UtcDateTime));
        request.CurrentAttachmentVersionId = version.Id;
        request.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRejectedAsync(ConfidentialFileVersion version, CancellationToken cancellationToken)
    {
        if (version.Classification != ConfidentialFileClassification.LeaveRequestAttachment)
            throw new InvalidOperationException("Leave attachment store received another confidential file classification.");
        var request = await db.HrLeaveRequests.SingleOrDefaultAsync(row =>
            row.BusinessId == version.BusinessId && row.Id == version.SubjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (await NextAttachmentVersionAsync(version.BusinessId, request.Id, cancellationToken) != version.Version)
            throw new InvalidOperationException("Leave attachment version changed during scanning.");
        db.HrLeaveRequestAttachmentVersions.Add(AttachmentEntity(version, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConfidentialFileVersion?> FindAsync(
        long businessId, Guid versionId, CancellationToken cancellationToken)
    {
        var row = await db.HrLeaveRequestAttachmentVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == versionId,
                cancellationToken);
        return row is null ? null : new ConfidentialFileVersion(
            row.Id, row.BusinessId, row.LeaveRequestId, row.VersionNo,
            ConfidentialFileClassification.LeaveRequestAttachment, row.StorageKey ?? "",
            row.OriginalFileName, row.ContentType, row.SizeBytes, row.ContentHash,
            row.EncryptionKeyReference, Enum.Parse<ConfidentialFileScanStatus>(row.ScanStatus),
            null, row.RevokedAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.RevokedAtUtc.Value, DateTimeKind.Utc))
                : null,
            null, UploadedByUserId: row.UploadedByUserId);
    }

    private async Task<HrLeaveRequestModel?> ReadModelAsync(
        long businessId, long requestId, CancellationToken cancellationToken)
    {
        var row = await db.HrLeaveRequests.AsNoTracking()
            .Include(item => item.Days)
            .Include(item => item.ApprovalInstance)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == requestId,
                cancellationToken);
        if (row is null)
            return null;
        var employee = await db.HrEmployees.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == row.EmployeeId)
            .Select(item => item.DisplayName)
            .SingleAsync(cancellationToken);
        var type = await db.HrLeaveTypes.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == row.LeaveTypeId)
            .Select(item => new { item.Code, item.Name })
            .SingleAsync(cancellationToken);
        var policies = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(item => item.BusinessId == businessId
                && row.Days.Select(day => day.PolicyVersionId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var days = row.Days.OrderBy(day => day.WorkDate).Select(day => new HrLeaveRequestDayModel(
            day.WorkDate, day.RosterId, day.OperationalLocationProjectionId,
            day.DayClassification, day.Units, policies[day.PolicyVersionId].VersionNo,
            day.ShiftSnapshotJson, day.CalendarEvidenceJson)).ToArray();
        return new HrLeaveRequestModel(
            row.Id, row.EmployeeId, employee, row.LeaveTypeId, type.Code, type.Name,
            row.StartDate, row.EndDate, row.RequestedUnits, row.Reason, row.Status,
            row.ScopeLocationProjectionId, row.RequestedByUserId,
            new DateTimeOffset(DateTime.SpecifyKind(row.RequestedAtUtc, DateTimeKind.Utc)),
            row.DecidedByUserId,
            row.DecidedAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.DecidedAtUtc.Value, DateTimeKind.Utc))
                : null,
            row.DecisionReason, row.ApprovalInstanceId, row.ApprovalInstance?.Status,
            row.ApprovalInstance?.CurrentStepSequence, row.CurrentAttachmentVersionId, row.Version, days);
    }

    private async Task<long> PrimaryLocationOnAsync(
        long businessId, HrEmployee employee, DateOnly date, CancellationToken cancellationToken)
    {
        var hasPrimaryHistory = await db.HrEmployeeLocationAssignments.AsNoTracking().AnyAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employee.Id
            && row.AssignmentType == "Primary", cancellationToken);
        var locations = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employee.Id
                && row.AssignmentType == "Primary"
                && (row.Status == "Effective" || row.Status == "Closed")
                && row.EffectiveFrom <= date && (row.EffectiveTo == null || row.EffectiveTo >= date))
            .Select(row => row.OperationalLocationProjectionId).Distinct().Take(2).ToArrayAsync(cancellationToken);
        if (locations.Length > 1)
            throw new InvalidOperationException($"Employee has overlapping Primary locations on {date:yyyy-MM-dd}.");
        var locationId = locations.SingleOrDefault();
        if (locationId <= 0 && !hasPrimaryHistory)
            locationId = employee.CurrentPrimaryLocationProjectionId ?? 0;
        if (locationId <= 0)
            throw new InvalidOperationException($"Employee has no effective Primary location on {date:yyyy-MM-dd}.");
        return locationId;
    }

    private async Task EnsureNoOverlappingRequestAsync(HrLeaveRequest request, CancellationToken cancellationToken)
    {
        var requestedDates = request.Days.Where(day => day.Units > 0)
            .Select(day => day.WorkDate).ToList();
        if (await db.HrLeaveRequestDays.AsNoTracking().AnyAsync(day =>
                day.BusinessId == request.BusinessId && day.EmployeeId == request.EmployeeId
                && day.LeaveRequestId != request.Id && day.Units > 0
                && requestedDates.Contains(day.WorkDate)
                && db.HrLeaveRequests.Any(other => other.BusinessId == request.BusinessId
                    && other.Id == day.LeaveRequestId && other.Status == "Approved"),
                cancellationToken))
            throw new ApprovalKernelConflictException("An overlapping approved leave request already exists.");
    }

    private async Task LockRequestEmployeeAsync(long businessId, long requestId, CancellationToken cancellationToken)
    {
        var employeeId = await db.HrLeaveRequests.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == requestId)
            .Select(row => (long?)row.EmployeeId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        await LockEmployeeAsync(businessId, employeeId, cancellationToken);
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
            return;
        await db.Database.SqlQuery<long>(
            $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {businessId} AND id = {employeeId} FOR UPDATE")
            .ToListAsync(cancellationToken);
    }

    private async Task<int> NextAttachmentVersionAsync(
        long businessId, long requestId, CancellationToken cancellationToken) =>
        (await db.HrLeaveRequestAttachmentVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.LeaveRequestId == requestId)
            .Select(row => (int?)row.VersionNo).MaxAsync(cancellationToken) ?? 0) + 1;

    private static HrDayClassificationResponse ReadPublishedClassification(HrRosterAssignment roster)
    {
        if (string.IsNullOrWhiteSpace(roster.CalendarEvidenceJson))
            throw new InvalidOperationException("Published roster is missing frozen calendar evidence.");
        var classification = JsonSerializer.Deserialize<HrDayClassificationResponse>(roster.CalendarEvidenceJson)
            ?? throw new InvalidOperationException("Published roster has invalid calendar evidence.");
        if (classification.WorkDate != roster.WorkDate
            || classification.Classification != roster.DayClassification)
            throw new InvalidOperationException("Published roster classification does not match its frozen evidence.");
        return classification;
    }

    private static HrLeaveRequestSnapshot Snapshot(HrLeaveRequest row) =>
        new(row.Id, row.EmployeeId, row.LeaveTypeId, row.ScopeLocationProjectionId,
            row.StartDate, row.EndDate, row.RequestedUnits, row.Reason, row.Status,
            row.RequestedByUserId, row.CurrentAttachmentVersionId, row.RequestHash, row.Version,
            row.Days.OrderBy(day => day.WorkDate).Select(day => new HrLeaveRequestDayEvidence(
                day.WorkDate, day.RosterId, day.OperationalLocationProjectionId, day.PolicyVersionId,
                day.Units, day.DayClassification, day.ShiftSnapshotJson, day.CalendarEvidenceJson)).ToArray());

    private static HrLeaveLedgerWrite MakeWrite(
        HrLeaveRequest request, HrLeaveRequestDay day,
        HrLeaveLedgerTransactionType transactionType, decimal quantity, string action)
    {
        var sourceId = $"{request.Id}:{day.WorkDate:yyyyMMdd}";
        var key = $"leave-{action}:{request.Id}:{day.WorkDate:yyyyMMdd}";
        var payload = $"{request.Id}|{day.WorkDate:yyyy-MM-dd}|{transactionType}|{quantity}|{day.PolicyVersionId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new(transactionType, quantity, day.WorkDate, "LeaveRequest", sourceId,
            key, hash, $"Leave request {request.Id} {action}.");
    }

    private async Task PostRequestLedgerEntriesAsync(
        HrLeaveRequest request,
        long actorUserId,
        HrLeaveLedgerTransactionType transactionType,
        string action,
        CancellationToken cancellationToken)
    {
        var policyIds = request.Days.Where(day => day.Units > 0)
            .Select(day => day.PolicyVersionId).Distinct().ToList();
        var policies = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(policy => policy.BusinessId == request.BusinessId && policyIds.Contains(policy.Id))
            .ToDictionaryAsync(policy => policy.Id, cancellationToken);
        if (policies.Count != policyIds.Count)
            throw new InvalidOperationException("Leave request policy evidence is incomplete.");

        var groups = request.Days.Where(day => day.Units > 0)
            .GroupBy(day =>
            {
                var policy = policies[day.PolicyVersionId];
                var year = HrLeaveRules.GetLeaveYear(
                    day.WorkDate, policy.LeaveYearStartMonth, policy.LeaveYearStartDay);
                return (PolicyId: day.PolicyVersionId, year.Start);
            })
            .OrderBy(group => group.Key.Start);
        foreach (var group in groups)
        {
            var policyDay = group.Min(day => day.WorkDate);
            var context = await HrLeaveRules.ResolvePolicyForLeaveYearAsync(
                leaveStore, request.BusinessId, request.EmployeeId, request.LeaveTypeId,
                policyDay, cancellationToken);
            if (context.Policy.Id != group.Key.PolicyId
                || HrLeaveRules.GetLeaveYear(
                    policyDay, context.Policy.LeaveYearStartMonth, context.Policy.LeaveYearStartDay).Start
                    != group.Key.Start)
                throw new InvalidOperationException(
                    "Leave policy evidence changed after request creation; the request cannot be posted.");
            var writes = group.Select(day => MakeWrite(request, day, transactionType,
                transactionType == HrLeaveLedgerTransactionType.Consumption ? -day.Units : day.Units,
                action)).ToArray();
            await leaveStore.PostLedgerEntriesAsync(
                request.BusinessId, request.EmployeeId, request.LeaveTypeId, context,
                writes, actorUserId, cancellationToken);
        }
    }

    private static HrLeaveRequestAttachmentVersion AttachmentEntity(
        ConfidentialFileVersion version, DateTime uploadedAtUtc) =>
        new()
        {
            Id = version.Id,
            BusinessId = version.BusinessId,
            LeaveRequestId = version.SubjectId,
            VersionNo = version.Version,
            StorageKey = string.IsNullOrEmpty(version.StorageKey) ? null : version.StorageKey,
            OriginalFileName = version.FileName,
            ContentType = version.ContentType,
            SizeBytes = version.SizeBytes,
            ContentHash = version.Sha256,
            ScanStatus = version.ScanStatus.ToString(),
            EncryptionKeyReference = version.EncryptionKeyReference,
            UploadedByUserId = version.UploadedByUserId,
            UploadedAtUtc = uploadedAtUtc
        };

    private static string RequestHash(HrLeaveRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.BusinessId,
            request.EmployeeId,
            request.LeaveTypeId,
            request.ScopeLocationProjectionId,
            request.StartDate,
            request.EndDate,
            request.RequestedUnits,
            request.Reason,
            Days = request.Days.OrderBy(day => day.WorkDate).Select(day => new
            {
                day.WorkDate,
                day.RosterId,
                day.OperationalLocationProjectionId,
                day.PolicyVersionId,
                day.Units,
                day.DayClassification,
                day.ShiftSnapshotJson,
                day.CalendarEvidenceJson
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
