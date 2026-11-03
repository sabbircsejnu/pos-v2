using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;
using RetailPOS.Infrastructure.HR.Employees;

namespace RetailPOS.Infrastructure.HR.Exit;

public sealed class HrExitStore(RetailPOSDbContext db, TimeProvider clock)
    : IHrExitStore, IApprovalKernelWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HrClearanceTemplateModel> CreateTemplateAsync(
        long businessId, long actorUserId, HrClearanceTemplateInput input, CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var code = input.Code.Trim();
        var latestVersion = await db.HrClearanceTemplates.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Code == code)
            .Select(row => (int?)row.VersionNo).MaxAsync(cancellationToken) ?? 0;
        if (input.VersionNo != latestVersion + 1)
            throw new InvalidOperationException("Clearance template versions must be created sequentially.");

        var scopeReference = input.ScopeType == HrClearanceTemplateScopeType.Business
            ? null : input.ScopeReference?.Trim();
        var overlaps = await db.HrClearanceTemplates.AnyAsync(row =>
            row.BusinessId == businessId && row.Code == code && row.ScopeType == input.ScopeType
            && row.ScopeReference == scopeReference && row.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
            && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= input.EffectiveFrom),
            cancellationToken);
        if (overlaps)
            throw new InvalidOperationException("Clearance template effective dates cannot overlap in the same scope.");

        var template = new HrClearanceTemplate
        {
            BusinessId = businessId,
            Code = code,
            Name = input.Name.Trim(),
            ScopeType = input.ScopeType,
            ScopeReference = scopeReference,
            VersionNo = input.VersionNo,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Items = input.Items.OrderBy(item => item.Sequence).Select(item => new HrClearanceTemplateItem
            {
                BusinessId = businessId,
                Sequence = item.Sequence,
                Code = item.Code.Trim(),
                Name = item.Name.Trim(),
                ResponsibleRole = Normalize(item.ResponsibleRole),
                ResponsibleReference = Normalize(item.ResponsibleReference),
                IsMandatory = item.IsMandatory
            }).ToList()
        };
        db.HrClearanceTemplates.Add(template);
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
        return Map(template);
    }

    public async Task<IReadOnlyList<HrClearanceTemplateModel>> ListTemplatesAsync(
        long businessId, CancellationToken cancellationToken) =>
        (await db.HrClearanceTemplates.AsNoTracking().Include(row => row.Items)
            .Where(row => row.BusinessId == businessId)
            .OrderBy(row => row.Code).ThenBy(row => row.ScopeType)
            .ThenBy(row => row.ScopeReference).ThenBy(row => row.VersionNo)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<HrExitCaseModel> InitiateAsync(
        long businessId, long actorUserId, HrExitCaseInput input, CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == input.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        if (employee.CurrentEmploymentStatus is "Exited" or "Suspended")
            throw new InvalidOperationException("Employee is not eligible for an exit case.");
        if (await db.HrExitCases.AnyAsync(row => row.BusinessId == businessId
            && row.EmployeeId == input.EmployeeId
            && (row.Status == "Draft" || row.Status == "PendingApproval" || row.Status == "Approved"),
            cancellationToken))
            throw new InvalidOperationException("Employee already has a non-terminal exit case.");

        var period = await db.HrEmploymentPeriods.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employee.Id && row.Status == "Active",
            cancellationToken)
            ?? throw new InvalidOperationException("Employee has no active employment period.");
        if (input.InitiatedDate < period.StartDate)
            throw new InvalidOperationException("Exit initiation date cannot precede the active employment period.");

        var locationId = employee.CurrentPrimaryLocationProjectionId;
        var locationType = locationId is null ? null : await db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == locationId.Value)
            .Select(row => row.SourceEntityType)
            .SingleOrDefaultAsync(cancellationToken);
        var template = await ResolveTemplateAsync(
            businessId, input.InitiatedDate, locationId, locationType, cancellationToken)
            ?? throw new InvalidOperationException(
                "No active clearance template applies to the employee's primary location and initiation date.");
        var templateItems = await db.HrClearanceTemplateItems.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.TemplateId == template.Id)
            .OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        if (templateItems.Count == 0)
            throw new InvalidOperationException("The effective clearance template has no checklist items.");

        var snapshot = JsonSerializer.Serialize(new
        {
            template.Id,
            template.Code,
            template.Name,
            template.ScopeType,
            template.ScopeReference,
            template.VersionNo,
            Items = templateItems.Select(item => new
            {
                item.Sequence, item.Code, item.Name, item.ResponsibleRole,
                item.ResponsibleReference, item.IsMandatory
            })
        }, JsonOptions);
        var exitCase = new HrExitCase
        {
            BusinessId = businessId,
            EmployeeId = employee.Id,
            ExitType = input.ExitType.Trim(),
            InitiatedDate = input.InitiatedDate,
            ProposedLastWorkingDate = input.ProposedLastWorkingDate,
            Reason = input.Reason.Trim(),
            Status = "Draft",
            InitiatedByUserId = actorUserId,
            InitiatedAtUtc = clock.GetUtcNow().UtcDateTime,
            PrimaryLocationProjectionIdSnapshot = locationId,
            LocationTypeSnapshot = locationType,
            ClearanceTemplateId = template.Id,
            ClearanceTemplateVersionNo = template.VersionNo,
            ClearanceTemplateSnapshotJson = snapshot,
            Version = 1,
            ClearanceItems = templateItems.Select(item => new HrClearanceItem
            {
                BusinessId = businessId,
                TemplateItemId = item.Id,
                Sequence = item.Sequence,
                Code = item.Code,
                Name = item.Name,
                ResponsibleRole = item.ResponsibleRole,
                ResponsibleReference = item.ResponsibleReference,
                IsMandatory = item.IsMandatory,
                Status = "Pending",
                Version = 1
            }).ToList()
        };
        AddEvent(exitCase, actorUserId, "Initiated", null, exitCase.Status, input.Reason);
        db.HrExitCases.Add(exitCase);
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
        return await GetAsync(businessId, exitCase.Id, cancellationToken)
            ?? throw new InvalidOperationException("Created exit case could not be reloaded.");
    }

    public async Task<IReadOnlyList<HrExitCaseModel>> ListAsync(
        long businessId, DateOnly? from, DateOnly? to, string? status, long? employeeId,
        IReadOnlyCollection<long>? allowedLocationIds, CancellationToken cancellationToken)
    {
        var query = Cases(businessId);
        if (from.HasValue)
            query = query.Where(row => row.InitiatedDate >= from.Value && row.InitiatedDate <= to!.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(row => row.Status == status);
        if (employeeId.HasValue)
            query = query.Where(row => row.EmployeeId == employeeId.Value);
        if (allowedLocationIds is not null)
        {
            if (allowedLocationIds.Count == 0)
                return [];
            query = query.Where(row => row.PrimaryLocationProjectionIdSnapshot.HasValue
                && allowedLocationIds.Contains(row.PrimaryLocationProjectionIdSnapshot.Value));
        }
        return (await query.OrderByDescending(row => row.InitiatedDate).ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();
    }

    public async Task<HrExitCaseModel?> GetAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken)
    {
        var row = await Cases(businessId).SingleOrDefaultAsync(item => item.Id == exitCaseId, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<HrExitCompletionPreviewModel?> GetCompletionPreviewAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken)
    {
        var exitCase = await db.HrExitCases.AsNoTracking()
            .Include(row => row.ClearanceItems)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == exitCaseId,
                cancellationToken);
        if (exitCase is null)
            return null;
        var evaluation = await EvaluateCompletionAsync(businessId, exitCase, cancellationToken);
        return MapCompletionPreview(exitCase, evaluation);
    }

    public Task<HrExitScope?> GetScopeAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken) =>
        db.HrExitCases.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == exitCaseId)
            .Select(row => new HrExitScope(row.EmployeeId,
                row.PrimaryLocationProjectionIdSnapshot, row.LocationTypeSnapshot))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<HrExitScope?> GetClearanceItemScopeAsync(
        long businessId, long clearanceItemId, CancellationToken cancellationToken) =>
        db.HrClearanceItems.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == clearanceItemId)
            .Join(db.HrExitCases.AsNoTracking(),
                item => new { item.BusinessId, ExitCaseId = item.ExitCaseId },
                exitCase => new { exitCase.BusinessId, ExitCaseId = exitCase.Id },
                (item, exitCase) => new HrExitScope(exitCase.EmployeeId,
                    exitCase.PrimaryLocationProjectionIdSnapshot, exitCase.LocationTypeSnapshot))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HrExitApprovalSubmission?> PrepareSubmitAsync(
        long businessId, long exitCaseId, long expectedVersion, CancellationToken cancellationToken)
    {
        var row = await db.HrExitCases.AsNoTracking().SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken);
        if (row is null)
            return null;
        if (row.Status != "Draft" || row.Version != expectedVersion)
            throw new HrExitConcurrencyException("Exit case changed or is not a draft.");
        return new HrExitApprovalSubmission(row.Id, row.Version, RequestHash(row));
    }

    public async Task<ApprovalScopeType?> FindApprovalScopeTypeAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken)
    {
        var row = await db.HrExitCases.AsNoTracking().SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken);
        if (row is null)
            return null;
        var scopes = await db.Set<ApprovalPolicy>().AsNoTracking()
            .Where(policy => policy.BusinessId == businessId && policy.IsActive
                && policy.ProcessType == ApprovalProcessType.Exit
                && policy.EffectiveFrom <= row.InitiatedDate
                && (!policy.EffectiveTo.HasValue || policy.EffectiveTo.Value >= row.InitiatedDate))
            .Select(policy => new { policy.ScopeType, policy.ScopeReference })
            .ToListAsync(cancellationToken);
        return scopes.Where(policy => policy.ScopeType switch
            {
                ApprovalScopeType.Location => row.PrimaryLocationProjectionIdSnapshot.HasValue
                    && policy.ScopeReference == row.PrimaryLocationProjectionIdSnapshot.Value.ToString(CultureInfo.InvariantCulture),
                ApprovalScopeType.LocationType => policy.ScopeReference == row.LocationTypeSnapshot,
                ApprovalScopeType.Business => policy.ScopeReference is null,
                _ => false
            })
            .OrderByDescending(policy => policy.ScopeType)
            .Select(policy => (ApprovalScopeType?)policy.ScopeType)
            .FirstOrDefault();
    }

    public async Task<string?> GetApprovalScopeReferenceAsync(
        long businessId, long exitCaseId, ApprovalScopeType scopeType, CancellationToken cancellationToken)
    {
        var row = await db.HrExitCases.AsNoTracking().SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken);
        if (row is null)
            return null;
        return scopeType switch
        {
            ApprovalScopeType.Business => null,
            ApprovalScopeType.Location => row.PrimaryLocationProjectionIdSnapshot?.ToString(CultureInfo.InvariantCulture),
            ApprovalScopeType.LocationType => row.LocationTypeSnapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(scopeType))
        };
    }

    public Task<long?> GetApprovalInstanceIdAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken) =>
        db.HrExitCases.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == exitCaseId
                && row.Status == "PendingApproval")
            .Select(row => row.ApprovalInstanceId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HrExitCaseModel?> UpdateClearanceAsync(
        long businessId, long actorUserId, HrClearanceUpdateInput input, CancellationToken cancellationToken)
    {
        if (input.ItemId <= 0 || input.ExpectedVersion <= 0
            || input.Status is not ("Cleared" or "Exception" or "NotApplicable" or "Pending")
            || (input.Status is "Exception" or "NotApplicable") && string.IsNullOrWhiteSpace(input.Comment)
            || input.Comment?.Trim().Length > 1000 || input.EvidenceReference?.Trim().Length > 500)
            throw new ArgumentException("Clearance status, version or evidence fields are invalid.");
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var item = await db.HrClearanceItems.Include(row => row.ExitCase)
                .ThenInclude(row => row.Events)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == input.ItemId,
                cancellationToken);
        if (item is null)
            return null;
        if (item.ExitCase.Status is not ("Draft" or "Approved"))
            throw new InvalidOperationException("Clearance cannot be changed after the exit case enters a terminal or pending state.");
        if (item.Version != input.ExpectedVersion)
            throw new HrExitConcurrencyException("Clearance item changed since it was read.");
        var nextVersion = checked(item.Version + 1);
        var nextCaseVersion = checked(item.ExitCase.Version + 1);
        item.Status = input.Status;
        item.ClearedByUserId = input.Status == "Cleared" ? actorUserId : null;
        item.ClearedAtUtc = input.Status == "Cleared" ? clock.GetUtcNow().UtcDateTime : null;
        item.Comment = Normalize(input.Comment);
        item.EvidenceReference = Normalize(input.EvidenceReference);
        item.Version = nextVersion;
        item.ExitCase.Version = nextCaseVersion;
        AddEvent(item.ExitCase, actorUserId, "ClearanceUpdated",
            item.ExitCase.Status, item.ExitCase.Status,
            $"{item.Code}: {item.Status}; {item.Comment}");
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
        return await GetAsync(businessId, item.ExitCaseId, cancellationToken);
    }

    public async Task<HrExitCaseModel?> CompleteNonFinancialAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion,
        DateOnly actualLastWorkingDate, string reason, CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var exitCase = await db.HrExitCases.Include(row => row.ClearanceItems).Include(row => row.Events)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == exitCaseId,
                cancellationToken);
        if (exitCase is null)
            return null;
        if (exitCase.Status != "Approved" || exitCase.Version != expectedVersion)
            throw new HrExitConcurrencyException("Exit case changed or has not been approved.");
        if (actualLastWorkingDate < exitCase.InitiatedDate
            || actualLastWorkingDate > exitCase.ProposedLastWorkingDate)
            throw new InvalidOperationException("Actual last working date must be between initiation and proposed exit dates.");
        if (exitCase.ClearanceItems.Any(item => item.IsMandatory
                && item.Status is not ("Cleared" or "NotApplicable")))
            throw new InvalidOperationException(
                "Every mandatory clearance item must be Cleared or reasoned NotApplicable before completion.");
        if (exitCase.ClearanceItems.Any(item => item.Status == "NotApplicable"
                && string.IsNullOrWhiteSpace(item.Comment)))
            throw new InvalidOperationException("NotApplicable clearance items require a reason.");
        var completion = await EvaluateCompletionAsync(businessId, exitCase, cancellationToken);
        if (!completion.CanComplete)
            throw new InvalidOperationException(string.Join(" ", completion.Blockers));

        var employee = await db.HrEmployees.SingleAsync(row =>
            row.BusinessId == businessId && row.Id == exitCase.EmployeeId, cancellationToken);
        var period = await db.HrEmploymentPeriods.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == exitCase.EmployeeId
            && row.Status == "Active", cancellationToken)
            ?? throw new InvalidOperationException("Employee has no active employment period.");
        if (actualLastWorkingDate < period.StartDate)
            throw new InvalidOperationException("Actual last working date cannot precede employment.");
        var primaryAssignment = await db.HrEmployeePositionAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employee.Id
                && row.AssignmentType == HrEmployeeAssignmentType.Primary
                && row.EffectiveFrom <= actualLastWorkingDate
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= actualLastWorkingDate))
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var previousState = HrEmployeeHistoryJson.State(employee.CurrentEmploymentStatus,
            employee.CurrentEmploymentTypeId, employee.CurrentPositionId, employee.CurrentOrgUnitId,
            primaryAssignment?.ReportsToEmployeeId);
        period.EndDate = actualLastWorkingDate;
        period.EndReason = reason.Trim();
        period.Status = "Closed";
        employee.CurrentEmploymentStatus = "Exited";
        employee.SuspendedFromStatus = null;
        employee.LastWorkingDate = actualLastWorkingDate;
        employee.CurrentPositionId = null;
        employee.CurrentOrgUnitId = null;
        employee.CurrentPrimaryLocationProjectionId = null;
        employee.Version = checked(employee.Version + 1);
        foreach (var assignment in await db.HrEmployeePositionAssignments
                     .Where(row => row.BusinessId == businessId && row.EmployeeId == employee.Id
                         && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value > actualLastWorkingDate))
                     .ToListAsync(cancellationToken))
        {
            if (assignment.EffectiveFrom > actualLastWorkingDate)
                throw new InvalidOperationException("A future position assignment prevents exit completion.");
            assignment.EffectiveTo = actualLastWorkingDate;
            assignment.IsCurrent = false;
            assignment.Version = checked(assignment.Version + 1);
        }
        foreach (var assignment in await db.HrEmployeeLocationAssignments
                     .Where(row => row.BusinessId == businessId && row.EmployeeId == employee.Id
                         && row.Status == "Effective"
                         && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value > actualLastWorkingDate))
                     .ToListAsync(cancellationToken))
        {
            if (assignment.EffectiveFrom > actualLastWorkingDate)
                throw new InvalidOperationException("A future location assignment prevents exit completion.");
            assignment.EffectiveTo = actualLastWorkingDate;
            assignment.Status = "Closed";
            assignment.Version = checked(assignment.Version + 1);
        }
        var newState = HrEmployeeHistoryJson.State(employee.CurrentEmploymentStatus,
            employee.CurrentEmploymentTypeId, null, null, null);
        db.HrEmployeeLifecycleEvents.Add(new HrEmployeeLifecycleEvent
        {
            BusinessId = businessId,
            EmployeeId = employee.Id,
            EmploymentPeriodId = period.Id,
            EventType = "Exited",
            EffectiveDate = actualLastWorkingDate,
            PreviousStateJson = previousState,
            NewStateJson = newState,
            Reason = reason.Trim(),
            ActorUserId = actorUserId,
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime
        });
        var completionReason = reason.Trim();
        var completedVersion = checked(exitCase.Version + 1);
        var evidenceJson = CreateCompletionEvidenceJson(
            exitCase, completion, actualLastWorkingDate, completionReason, completedVersion);
        db.HrExitCompletionEvidence.Add(new HrExitCompletionEvidence
        {
            BusinessId = businessId,
            ExitCaseId = exitCase.Id,
            ExitVersion = completedVersion,
            PayrollEnabled = completion.PayrollEnabled,
            CompletionMode = completion.CompletionMode,
            FinalSettlementId = completion.Settlement?.SettlementId,
            FinalSettlementRevisionId = completion.Settlement?.RevisionId,
            FinalSettlementRevisionNo = completion.Settlement?.RevisionNo,
            FinalSettlementStatus = completion.Settlement?.Status,
            FinalSettlementEvidenceHash = completion.Settlement?.SubmittedEvidenceHash,
            NetSettlement = completion.Settlement?.NetSettlement,
            CurrencyCode = completion.Settlement?.CurrencyCode,
            EvidenceJson = evidenceJson,
            EvidenceHash = Hash(evidenceJson),
            CompletedByUserId = actorUserId,
            CompletedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        var previousStatus = exitCase.Status;
        exitCase.ActualLastWorkingDate = actualLastWorkingDate;
        exitCase.Status = "Completed";
        exitCase.Version = completedVersion;
        AddEvent(exitCase, actorUserId, "Completed", previousStatus, exitCase.Status, reason);
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
        return await GetAsync(businessId, exitCaseId, cancellationToken);
    }

    public async Task<HrExitCaseModel?> CancelDraftAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion,
        string reason, CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.HrExitCases.Include(item => item.Events).SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken);
        if (row is null)
            return null;
        if (row.Status != "Draft" || row.Version != expectedVersion)
            throw new HrExitConcurrencyException("Exit case changed or is not a cancellable draft.");
        var previousStatus = row.Status;
        row.Status = "Cancelled";
        row.Version = checked(row.Version + 1);
        AddEvent(row, actorUserId, "Cancelled", previousStatus, row.Status, reason);
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
        return await GetAsync(businessId, exitCaseId, cancellationToken);
    }

    public async Task ApproveWithoutApprovalAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion,
        CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.HrExitCases.Include(item => item.Events).SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        if (row.Status != "Draft" || row.Version != expectedVersion)
            throw new HrExitConcurrencyException("Exit case changed or is not a draft.");
        row.Status = "Approved";
        row.Version = checked(row.Version + 1);
        AddEvent(row, actorUserId, "ApprovedWithoutPolicy", "Draft", row.Status, null);
        await SaveChangesWithConcurrencyTranslationAsync(cancellationToken);
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != HrExitApprovalConstants.TargetType
            || !long.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return null;
        var row = await db.HrExitCases.AsNoTracking().Include(item => item.ApprovalInstance)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id,
                cancellationToken);
        if (row is null)
            return null;
        var targetVersion = row.Status == "PendingApproval" && row.ApprovalInstance is not null
            ? row.ApprovalInstance.TargetVersion : row.Version;
        return new ApprovalTargetSnapshot(targetVersion, RequestHash(row));
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand approval,
        CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.Exit)
            return null;
        var id = ParseTarget(approval.TargetType, approval.TargetId);
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.HrExitCases.Include(item => item.ClearanceItems).Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        if (row.Status != "Draft" || row.Version != approval.TargetVersion
            || !string.Equals(RequestHash(row), approval.RequestHash, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalKernelConflictException("Exit case changed or is not a draft.");
        row.Status = "PendingApproval";
        row.Version = checked(row.Version + 1);
        AddEvent(row, requesterUserId, "Submitted", "Draft", row.Status, null);
        return new ApprovalTargetSnapshot(approval.TargetVersion, approval.RequestHash);
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.Exit)
            return Task.CompletedTask;
        var id = ParseTarget(instance.TargetType, instance.TargetId);
        var row = db.HrExitCases.Local.SingleOrDefault(item =>
            item.BusinessId == businessId && item.Id == id)
            ?? throw new ApprovalKernelConflictException("Prepared exit case was not tracked.");
        if (row.Status != "PendingApproval" || row.Version != instance.TargetVersion + 1)
            throw new ApprovalKernelConflictException("Exit case no longer matches its approval instance.");
        row.ApprovalInstance = instance;
        row.ApprovalInstanceId = instance.Id;
        AddEvent(row, instance.RequestedByUserId, "ApprovalSubmitted", null, row.Status, null);
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.Exit)
            return;
        var id = ParseTarget(instance.TargetType, instance.TargetId);
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.HrExitCases.SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Exit case target was not found.");
        if (row.Status != "PendingApproval" || row.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Exit case is terminal or mismatched.");
        var previousStatus = row.Status;
        row.Status = instance.Status switch
        {
            ApprovalInstanceStatus.Approved => "Approved",
            ApprovalInstanceStatus.Rejected => "Rejected",
            ApprovalInstanceStatus.Cancelled => "Cancelled",
            _ => throw new ApprovalKernelConflictException("A non-terminal approval cannot update an exit case.")
        };
        row.Version = checked(row.Version + 1);
        AddEvent(row, actorUserId, "Approval" + row.Status, previousStatus, row.Status, reason);
    }

    private async Task<CompletionEvaluation> EvaluateCompletionAsync(
        long businessId,
        HrExitCase exitCase,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        if (exitCase.Status != "Approved")
            blockers.Add("Only an approved exit can be completed.");
        var pendingMandatory = exitCase.ClearanceItems.Count(item =>
            item.IsMandatory && item.Status is not ("Cleared" or "NotApplicable"));
        if (pendingMandatory > 0)
            blockers.Add(
                "Every mandatory clearance item must be Cleared or reasoned NotApplicable before completion.");
        var reasonlessNotApplicable = exitCase.ClearanceItems.Count(item =>
            item.Status == "NotApplicable" && string.IsNullOrWhiteSpace(item.Comment));
        if (reasonlessNotApplicable > 0)
            blockers.Add("NotApplicable clearance items require a reason.");

        var payrollEnabled = await IsFeatureEnabledAsync(
            businessId, HrPayrollCapabilityKeys.Payroll, cancellationToken);
        if (!payrollEnabled)
        {
            return new CompletionEvaluation(
                payrollEnabled, "HrOnly", pendingMandatory, reasonlessNotApplicable, blockers, null);
        }

        var settlement = await LoadCompletionSettlementAsync(
            businessId, exitCase.Id, cancellationToken);
        if (settlement is null)
        {
            blockers.Add("An approved final settlement is required before exit completion.");
            return new CompletionEvaluation(
                payrollEnabled, "PositiveSettlement", pendingMandatory,
                reasonlessNotApplicable, blockers, null);
        }

        if (settlement.Status != ApprovalInstanceStatus.Approved.ToString())
            blockers.Add("The current final settlement revision must be approved before exit completion.");

        if (settlement.NetSettlement > 0m)
        {
            settlement = await EvaluatePositiveSettlementAsync(
                settlement, cancellationToken);
        }
        else if (settlement.NetSettlement < 0m)
        {
            settlement = await EvaluateNegativeSettlementAsync(
                settlement, cancellationToken);
        }
        else
        {
            settlement = await EvaluateZeroSettlementAsync(
                settlement, cancellationToken);
        }

        blockers.AddRange(settlement.Blockers);
        return new CompletionEvaluation(
            payrollEnabled, settlement.CompletionMode, pendingMandatory, reasonlessNotApplicable,
            blockers, settlement);
    }

    private async Task<CompletionSettlementEvaluation> EvaluatePositiveSettlementAsync(
        CompletionSettlementEvaluation settlement,
        CancellationToken cancellationToken)
    {
        var blockers = settlement.Blockers.ToList();
        var accountingEnabled = await IsFeatureEnabledAsync(
            settlement.BusinessId, "accounting.enabled", cancellationToken);
        var payments = await db.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations)
            .Include(row => row.Evidence)
            .Where(row => row.BusinessId == settlement.BusinessId
                && row.FinalSettlementId == settlement.SettlementId
                && row.FinalSettlementRevisionNo == settlement.RevisionNo)
            .OrderBy(row => row.AttemptNo)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var activePayments = payments.Where(IsActiveSettlementPayment).ToArray();
        var outstandingAmount = activePayments.Sum(row => row.OutstandingAmount);
        var paymentsSatisfied = activePayments.Length > 0
            && activePayments.All(IsFullyPaid)
            && outstandingAmount == 0m;
        if (activePayments.Length == 0)
            blockers.Add("A positive net final settlement requires verified payment evidence before exit completion.");
        else if (!paymentsSatisfied)
            blockers.Add(
                "Final settlement payments remain unpaid, partially paid, failed, returned or unresolved.");

        var paymentIds = activePayments.Select(row => row.Id).ToList();
        var reconciliations = paymentIds.Count == 0
            ? Array.Empty<PayrollPaymentReconciliation>()
            : await db.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.BusinessId == settlement.BusinessId
                    && paymentIds.Contains(row.PayrollPaymentId))
                .ToArrayAsync(cancellationToken);
        var reconciliationSatisfied = paymentsSatisfied && activePayments.All(payment =>
            IsFullyPaidAndReconciled(payment,
                reconciliations.Where(row => row.PayrollPaymentId == payment.Id)));
        if (activePayments.Length > 0 && !reconciliationSatisfied)
            blockers.Add("Verified final settlement payments must be fully reconciled before exit completion.");

        var postings = await db.PayrollFinalSettlementAccountingPostings.AsNoTracking()
            .Where(row => row.BusinessId == settlement.BusinessId
                && row.FinalSettlementId == settlement.SettlementId
                && row.RevisionNo == settlement.RevisionNo)
            .ToArrayAsync(cancellationToken);
        var requiredPaymentAccountingCount = activePayments
            .SelectMany(row => row.Evidence.Where(evidence =>
                    evidence.Type == PayrollPaymentEvidenceTypes.Settlement)
                .Select(evidence => evidence.Id))
            .Count();
        var postedPaymentAccountingCount = activePayments
            .SelectMany(row => row.Evidence.Where(evidence =>
                    evidence.Type == PayrollPaymentEvidenceTypes.Settlement)
                .Select(evidence => new
                {
                    PaymentId = row.Id,
                    PaymentEvidenceId = evidence.Id
                }))
            .Count(identity => postings.Any(row =>
                row.PostingType == PayrollFinalSettlementPostingTypes.Payment
                && row.PaymentId == identity.PaymentId
                && row.PaymentEvidenceId == identity.PaymentEvidenceId
                && row.Status == PayrollAccountingPostingStatuses.Posted));
        var accountingSatisfied = !accountingEnabled
            || (postings.Any(row =>
                    row.PostingType == PayrollFinalSettlementPostingTypes.Expense
                    && row.PaymentId is null
                    && row.Status == PayrollAccountingPostingStatuses.Posted)
                && requiredPaymentAccountingCount == postedPaymentAccountingCount);
        if (accountingEnabled && !accountingSatisfied)
            blockers.Add("Required final settlement Accounting postings are incomplete.");

        return settlement with
        {
            CompletionMode = "PositiveSettlement",
            PaymentRequired = true,
            ActivePaymentCount = activePayments.Length,
            OutstandingAmount = outstandingAmount,
            PaymentsSatisfied = paymentsSatisfied,
            ReconciliationRequired = true,
            ReconciliationSatisfied = reconciliationSatisfied,
            AccountingRequired = accountingEnabled,
            RequiredPostingType = accountingEnabled
                ? PayrollFinalSettlementPostingTypes.Expense
                : null,
            AccountingSatisfied = accountingSatisfied,
            RequiredPaymentAccountingCount = requiredPaymentAccountingCount,
            PostedPaymentAccountingCount = postedPaymentAccountingCount,
            AccountingFeatureEnabled = accountingEnabled,
            Blockers = blockers.ToArray()
        };
    }

    private async Task<CompletionSettlementEvaluation> EvaluateZeroSettlementAsync(
        CompletionSettlementEvaluation settlement,
        CancellationToken cancellationToken)
    {
        var blockers = settlement.Blockers.ToList();
        var paymentCount = await db.PayrollPayments.AsNoTracking().CountAsync(row =>
            row.BusinessId == settlement.BusinessId
            && row.FinalSettlementId == settlement.SettlementId
            && row.FinalSettlementRevisionNo == settlement.RevisionNo,
            cancellationToken);
        if (paymentCount > 0)
            blockers.Add("A zero-net final settlement must not create outgoing payment attempts.");
        return settlement with
        {
            CompletionMode = "ZeroSettlement",
            PaymentRequired = false,
            ActivePaymentCount = paymentCount,
            OutstandingAmount = 0m,
            PaymentsSatisfied = paymentCount == 0,
            ReconciliationRequired = false,
            ReconciliationSatisfied = paymentCount == 0,
            AccountingRequired = false,
            RequiredPostingType = null,
            AccountingSatisfied = true,
            RequiredPaymentAccountingCount = 0,
            PostedPaymentAccountingCount = 0,
            Blockers = blockers.ToArray()
        };
    }

    private async Task<CompletionSettlementEvaluation> EvaluateNegativeSettlementAsync(
        CompletionSettlementEvaluation settlement,
        CancellationToken cancellationToken)
    {
        var blockers = settlement.Blockers.ToList();
        var accountingEnabled = await IsFeatureEnabledAsync(
            settlement.BusinessId, "accounting.enabled", cancellationToken);
        var paymentCount = await db.PayrollPayments.AsNoTracking().CountAsync(row =>
            row.BusinessId == settlement.BusinessId
            && row.FinalSettlementId == settlement.SettlementId
            && row.FinalSettlementRevisionNo == settlement.RevisionNo,
            cancellationToken);
        if (paymentCount > 0)
            blockers.Add("Employee-receivable final settlements cannot create outgoing payment attempts.");

        var accountingSatisfied = true;
        if (accountingEnabled)
        {
            accountingSatisfied = await db.PayrollFinalSettlementAccountingPostings.AsNoTracking()
                .AnyAsync(row => row.BusinessId == settlement.BusinessId
                    && row.FinalSettlementId == settlement.SettlementId
                    && row.RevisionNo == settlement.RevisionNo
                    && row.PostingType == PayrollFinalSettlementPostingTypes.Receivable
                    && row.PaymentId == null
                    && row.Status == PayrollAccountingPostingStatuses.Posted,
                    cancellationToken);
            if (!accountingSatisfied)
                blockers.Add("The employee receivable must be posted to Accounting before exit completion.");
        }

        return settlement with
        {
            CompletionMode = "NegativeSettlement",
            PaymentRequired = false,
            ActivePaymentCount = paymentCount,
            OutstandingAmount = 0m,
            PaymentsSatisfied = paymentCount == 0,
            ReconciliationRequired = false,
            ReconciliationSatisfied = paymentCount == 0,
            AccountingRequired = accountingEnabled,
            RequiredPostingType = accountingEnabled
                ? PayrollFinalSettlementPostingTypes.Receivable
                : null,
            AccountingSatisfied = accountingSatisfied,
            RequiredPaymentAccountingCount = 0,
            PostedPaymentAccountingCount = 0,
            AccountingFeatureEnabled = accountingEnabled,
            Blockers = blockers.ToArray()
        };
    }

    private async Task<CompletionSettlementEvaluation?> LoadCompletionSettlementAsync(
        long businessId,
        long exitCaseId,
        CancellationToken cancellationToken)
    {
        var settlement = await db.PayrollFinalSettlements.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.ExitCaseId == exitCaseId,
                cancellationToken);
        if (settlement is null)
            return null;

        var revision = await db.PayrollFinalSettlementRevisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.FinalSettlementId == settlement.Id
                && row.RevisionNo == settlement.CurrentRevisionNo,
                cancellationToken);
        if (revision is null)
            return null;

        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.FinalSettlementId == settlement.Id
                && row.RevisionNo == revision.RevisionNo,
                cancellationToken);
        var instance = submission?.ApprovalInstanceId is long instanceId
            ? await db.ApprovalInstances.AsNoTracking()
                .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == instanceId,
                    cancellationToken)
            : null;
        var status = submission is null
            ? "Calculated"
            : instance?.Status.ToString() ?? "Submitted";
        return new CompletionSettlementEvaluation(
            businessId,
            settlement.Id,
            revision.Id,
            revision.RevisionNo,
            status,
            "PositiveSettlement",
            revision.NetSettlement,
            revision.CurrencyCode,
            submission?.SubmittedEvidenceHash,
            PaymentRequired: false,
            ActivePaymentCount: 0,
            OutstandingAmount: 0m,
            PaymentsSatisfied: false,
            ReconciliationRequired: false,
            ReconciliationSatisfied: false,
            AccountingRequired: false,
            RequiredPostingType: null,
            AccountingSatisfied: false,
            RequiredPaymentAccountingCount: 0,
            PostedPaymentAccountingCount: 0,
            AccountingFeatureEnabled: false,
            Blockers: []);
    }

    private static HrExitCompletionPreviewModel MapCompletionPreview(
        HrExitCase exitCase,
        CompletionEvaluation evaluation) =>
        new(
            exitCase.Id,
            exitCase.Version,
            evaluation.PayrollEnabled,
            evaluation.CanComplete,
            evaluation.PendingMandatoryClearanceCount,
            evaluation.ReasonlessNotApplicableCount,
            evaluation.Blockers,
            evaluation.Settlement is null
                ? null
                : new HrExitCompletionSettlementModel(
                    evaluation.Settlement.SettlementId,
                    evaluation.Settlement.RevisionNo,
                    evaluation.Settlement.Status,
                    evaluation.Settlement.CompletionMode,
                    evaluation.Settlement.NetSettlement,
                    evaluation.Settlement.CurrencyCode,
                    evaluation.Settlement.SubmittedEvidenceHash,
                    evaluation.Settlement.PaymentRequired,
                    evaluation.Settlement.ActivePaymentCount,
                    evaluation.Settlement.OutstandingAmount,
                    evaluation.Settlement.PaymentsSatisfied,
                    evaluation.Settlement.ReconciliationRequired,
                    evaluation.Settlement.ReconciliationSatisfied,
                    evaluation.Settlement.AccountingRequired,
                    evaluation.Settlement.RequiredPostingType,
                    evaluation.Settlement.AccountingSatisfied,
                    evaluation.Settlement.RequiredPaymentAccountingCount,
                    evaluation.Settlement.PostedPaymentAccountingCount));

    private static string CreateCompletionEvidenceJson(
        HrExitCase exitCase,
        CompletionEvaluation completion,
        DateOnly actualLastWorkingDate,
        string reason,
        long completedVersion) =>
        JsonSerializer.Serialize(new
        {
            BusinessId = exitCase.BusinessId,
            ExitCaseId = exitCase.Id,
            ExitVersion = completedVersion,
            EmployeeId = exitCase.EmployeeId,
            ActualLastWorkingDate = actualLastWorkingDate,
            Reason = reason,
            PayrollEnabled = completion.PayrollEnabled,
            CompletionMode = completion.CompletionMode,
            ClearanceItems = exitCase.ClearanceItems.OrderBy(item => item.Sequence).Select(item => new
            {
                item.Id,
                item.TemplateItemId,
                item.Sequence,
                item.Code,
                item.IsMandatory,
                item.Status,
                item.Comment,
                item.EvidenceReference,
                item.Version
            }),
            Settlement = completion.Settlement is null
                ? null
                : new
                {
                    completion.Settlement.SettlementId,
                    completion.Settlement.RevisionId,
                    completion.Settlement.RevisionNo,
                    completion.Settlement.Status,
                    completion.Settlement.CompletionMode,
                    completion.Settlement.NetSettlement,
                    completion.Settlement.CurrencyCode,
                    completion.Settlement.SubmittedEvidenceHash,
                    completion.Settlement.PaymentRequired,
                    completion.Settlement.ActivePaymentCount,
                    completion.Settlement.OutstandingAmount,
                    completion.Settlement.PaymentsSatisfied,
                    completion.Settlement.ReconciliationRequired,
                    completion.Settlement.ReconciliationSatisfied,
                    completion.Settlement.AccountingRequired,
                    completion.Settlement.RequiredPostingType,
                    completion.Settlement.AccountingSatisfied,
                    completion.Settlement.RequiredPaymentAccountingCount,
                    completion.Settlement.PostedPaymentAccountingCount
                }
        }, JsonOptions);

    private Task<bool> IsFeatureEnabledAsync(
        long businessId,
        string featureKey,
        CancellationToken cancellationToken) =>
        db.BusinessFeatureSettings.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.FeatureKey == featureKey)
            .Select(row => row.IsEnabled)
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsActiveSettlementPayment(PayrollPayment payment) =>
        payment.Status != PayrollPaymentStatuses.Cancelled
        && !(payment.Status is PayrollPaymentStatuses.Failed or PayrollPaymentStatuses.Returned
            && payment.ResolutionStatus == PayrollPaymentResolutionStatuses.ResolvedReissue);

    private static bool IsFullyPaid(PayrollPayment payment) =>
        payment.Status == PayrollPaymentStatuses.Paid
        && payment.OutstandingAmount == 0m
        && payment.PaidAmount == payment.PayableAmount
        && payment.Allocations.Count > 0;

    private static bool IsFullyPaidAndReconciled(
        PayrollPayment payment,
        IEnumerable<PayrollPaymentReconciliation> reconciliations)
    {
        if (!IsFullyPaid(payment))
            return false;
        var latest = reconciliations.GroupBy(row => row.PayrollPaymentAllocationId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Version).First());
        return payment.Allocations.All(allocation =>
            latest.TryGetValue(allocation.Id, out var row)
            && row.Status == PayrollPaymentReconciliationStatuses.Reconciled
            && row.SettledAmount == allocation.Amount);
    }

    private sealed record CompletionEvaluation(
        bool PayrollEnabled,
        string CompletionMode,
        int PendingMandatoryClearanceCount,
        int ReasonlessNotApplicableCount,
        IReadOnlyList<string> Blockers,
        CompletionSettlementEvaluation? Settlement)
    {
        public bool CanComplete => Blockers.Count == 0;
    }

    private sealed record CompletionSettlementEvaluation(
        long BusinessId,
        long SettlementId,
        long RevisionId,
        int RevisionNo,
        string Status,
        string CompletionMode,
        decimal NetSettlement,
        string CurrencyCode,
        string? SubmittedEvidenceHash,
        bool PaymentRequired,
        int ActivePaymentCount,
        decimal OutstandingAmount,
        bool PaymentsSatisfied,
        bool ReconciliationRequired,
        bool ReconciliationSatisfied,
        bool AccountingRequired,
        string? RequiredPostingType,
        bool AccountingSatisfied,
        int RequiredPaymentAccountingCount,
        int PostedPaymentAccountingCount,
        bool AccountingFeatureEnabled,
        IReadOnlyList<string> Blockers);

    private async Task<HrClearanceTemplate?> ResolveTemplateAsync(
        long businessId, DateOnly effectiveDate, long? locationId, string? locationType,
        CancellationToken cancellationToken)
    {
        var candidates = await db.HrClearanceTemplates.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.IsActive
                && row.EffectiveFrom <= effectiveDate
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= effectiveDate))
            .ToListAsync(cancellationToken);
        return candidates.Where(row => row.ScopeType switch
            {
                HrClearanceTemplateScopeType.Location => locationId.HasValue
                    && row.ScopeReference == locationId.Value.ToString(CultureInfo.InvariantCulture),
                HrClearanceTemplateScopeType.LocationType => row.ScopeReference == locationType,
                HrClearanceTemplateScopeType.Business => row.ScopeReference is null,
                _ => false
            })
            .OrderByDescending(row => row.ScopeType)
            .ThenByDescending(row => row.VersionNo)
            .FirstOrDefault();
    }

    private IQueryable<HrExitCase> Cases(long businessId) =>
        db.HrExitCases.AsNoTracking().Include(row => row.ClearanceItems)
            .Include(row => row.Events)
            .Include(row => row.Employee)
            .Where(row => row.BusinessId == businessId);

    private static HrClearanceTemplateModel Map(HrClearanceTemplate row) =>
        new(row.Id, row.Code, row.Name, row.ScopeType, row.ScopeReference, row.VersionNo,
            row.EffectiveFrom, row.EffectiveTo, row.IsActive,
            row.Items.OrderBy(item => item.Sequence).Select(item => new HrClearanceTemplateItemModel(
                item.Id, item.Sequence, item.Code, item.Name, item.ResponsibleRole,
                item.ResponsibleReference, item.IsMandatory)).ToArray());

    private static HrExitCaseModel Map(HrExitCase row) =>
        new(row.Id, row.EmployeeId, row.Employee.EmployeeCode, row.Employee.DisplayName,
            row.ExitType, row.InitiatedDate, row.ProposedLastWorkingDate, row.ActualLastWorkingDate,
            row.Reason, row.Status, row.ApprovalInstanceId, row.PrimaryLocationProjectionIdSnapshot,
            row.LocationTypeSnapshot, row.ClearanceTemplateId, row.ClearanceTemplateVersionNo, row.Version,
            row.ClearanceItems.OrderBy(item => item.Sequence).Select(item => new HrClearanceItemModel(
                item.Id, item.Sequence, item.Code, item.Name, item.ResponsibleRole, item.ResponsibleReference,
                item.IsMandatory, item.Status, item.ClearedByUserId,
                item.ClearedAtUtc.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(item.ClearedAtUtc.Value, DateTimeKind.Utc))
                    : null,
                item.Comment, item.EvidenceReference, item.Version)).ToArray(),
            row.Events.OrderBy(item => item.Sequence).Select(item => new HrExitCaseEventModel(
                item.Sequence, item.EventType, item.PreviousStatus, item.NewStatus,
                item.ActualLastWorkingDate, item.Reason, item.ActorUserId,
                new DateTimeOffset(DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc)))).ToArray());

    private void AddEvent(
        HrExitCase row, long actorUserId, string eventType, string? previousStatus,
        string newStatus, string? reason)
    {
        row.Events.Add(new HrExitCaseEvent
        {
            BusinessId = row.BusinessId,
            Sequence = row.Events.Count == 0 ? 1 : row.Events.Max(item => item.Sequence) + 1,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ActualLastWorkingDate = row.ActualLastWorkingDate,
            Reason = Normalize(reason),
            ActorUserId = actorUserId,
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime
        });
    }

    private static string RequestHash(HrExitCase row)
    {
        var value = string.Join("|", new object?[] { row.BusinessId, row.EmployeeId, row.ExitType,
            row.InitiatedDate, row.ProposedLastWorkingDate, row.Reason, row.ClearanceTemplateId,
            row.ClearanceTemplateVersionNo, row.ClearanceTemplateSnapshotJson });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task SaveChangesWithConcurrencyTranslationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrExitConcurrencyException("Exit evidence changed concurrently.", exception);
        }
    }

    private static long ParseTarget(string targetType, string targetId)
    {
        if (targetType != HrExitApprovalConstants.TargetType
            || !long.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            throw new ApprovalKernelConflictException("Approval target is not a valid exit case.");
        return id;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
