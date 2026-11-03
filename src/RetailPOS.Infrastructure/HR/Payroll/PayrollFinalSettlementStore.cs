using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollFinalSettlementStore(
    RetailPOSDbContext db,
    TimeProvider clock) : IPayrollFinalSettlementStore, IApprovalKernelWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] IncludedRunStatuses =
    [
        PayrollRunStatuses.Approved,
        PayrollRunStatuses.PaymentInProgress,
        PayrollRunStatuses.Paid,
        PayrollRunStatuses.Completed
    ];

    public async Task<HrExitScope?> GetExitScopeAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken)
    {
        var row = await db.HrExitCases.AsNoTracking().SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == exitCaseId, cancellationToken);
        return row is null
            ? null
            : new HrExitScope(row.EmployeeId, row.PrimaryLocationProjectionIdSnapshot,
                row.LocationTypeSnapshot);
    }

    public async Task<HrExitScope?> GetSettlementScopeAsync(
        long businessId, long settlementId, CancellationToken cancellationToken)
    {
        var row = await db.PayrollFinalSettlements.AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == settlementId)
            .Join(db.HrExitCases.AsNoTracking(),
                settlement => new { settlement.BusinessId, Id = settlement.ExitCaseId },
                exitCase => new { exitCase.BusinessId, exitCase.Id },
                (_, exitCase) => new HrExitScope(exitCase.EmployeeId,
                    exitCase.PrimaryLocationProjectionIdSnapshot, exitCase.LocationTypeSnapshot))
            .SingleOrDefaultAsync(cancellationToken);
        return row;
    }

    public Task<PayrollFinalSettlementModel?> GetForExitAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken) =>
        GetLatestAsync(db.PayrollFinalSettlements.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.ExitCaseId == exitCaseId)
            .Select(row => row.Id), cancellationToken);

    public Task<PayrollFinalSettlementModel?> GetByIdAsync(
        long businessId, long settlementId, CancellationToken cancellationToken) =>
        GetLatestAsync(db.PayrollFinalSettlements.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == settlementId)
            .Select(row => row.Id), cancellationToken);

    public async Task<PayrollFinalSettlementModel> CalculateAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedExitVersion,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);

        var idempotencyPayloadHash = Hash(new { businessId, exitCaseId, expectedExitVersion });
        var prior = await db.PayrollFinalSettlementRevisions.AsNoTracking()
            .Include(row => row.Lines)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (prior.IdempotencyPayloadHash != idempotencyPayloadHash)
                throw new InvalidOperationException(
                    "The final settlement idempotency key was already used for a different request.");
            var priorHeader = await db.PayrollFinalSettlements.AsNoTracking()
                .SingleAsync(row => row.BusinessId == businessId && row.Id == prior.FinalSettlementId,
                    cancellationToken);
            return Map(priorHeader, prior, isReplay: true);
        }

        var activeApproval = await db.PayrollFinalSettlementApprovalSubmissions
            .Where(row => row.BusinessId == businessId
                && row.FinalSettlementId == db.PayrollFinalSettlements
                    .Where(settlement => settlement.BusinessId == businessId
                        && settlement.ExitCaseId == exitCaseId)
                    .Select(settlement => settlement.Id)
                    .SingleOrDefault())
            .Join(db.ApprovalInstances,
                submission => new { submission.BusinessId, Id = submission.ApprovalInstanceId },
                instance => new { instance.BusinessId, Id = (long?)instance.Id },
                (_, instance) => instance.Status)
            .AnyAsync(status => status == ApprovalInstanceStatus.Pending
                || status == ApprovalInstanceStatus.Approved,
                cancellationToken);
        if (activeApproval)
            throw new InvalidOperationException(
                "A pending or approved final settlement cannot be recalculated.");

        var exitCase = await db.HrExitCases.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == exitCaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        if (exitCase.Version != expectedExitVersion)
            throw new InvalidOperationException("The exit case changed; refresh it before calculating settlement.");
        if (exitCase.Status != "Approved")
            throw new InvalidOperationException(
                "Final settlement calculation requires an approved, not-yet-completed exit case.");

        var settlementDate = exitCase.ActualLastWorkingDate ?? exitCase.ProposedLastWorkingDate;
        var monetaryConfiguration = await db.PayrollMonetaryConfigurationVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EffectiveFrom <= settlementDate)
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.VersionNo)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No effective Payroll currency and rounding configuration exists for the settlement date.");
        if (string.IsNullOrWhiteSpace(monetaryConfiguration.CurrencyCode)
            || monetaryConfiguration.CurrencyCode.Length != 3
            || monetaryConfiguration.DecimalPrecision is < 0 or > 6
            || !Enum.TryParse<MidpointRounding>(monetaryConfiguration.RoundingMode, out var roundingMode)
            || !Enum.IsDefined(roundingMode))
            throw new InvalidOperationException(
                "The effective Payroll currency and rounding configuration is invalid.");

        var source = await LoadSourceEvidenceAsync(
            businessId, exitCase.EmployeeId, settlementDate, cancellationToken);
        var lines = BuildLines(source, settlementDate, monetaryConfiguration.CurrencyCode,
            monetaryConfiguration.DecimalPrecision, roundingMode);
        var grossDue = Round(lines.Where(row => row.LineType == PayrollFinalSettlementLineTypes.Earning)
            .Sum(row => row.Amount), monetaryConfiguration.DecimalPrecision, roundingMode);
        var deductions = Round(lines.Where(row => row.LineType == PayrollFinalSettlementLineTypes.Deduction)
            .Sum(row => row.Amount), monetaryConfiguration.DecimalPrecision, roundingMode);
        var netSettlement = Round(grossDue - deductions, monetaryConfiguration.DecimalPrecision, roundingMode);

        var evidenceJson = JsonSerializer.Serialize(new
        {
            RuleSet = "Phase7A-v1",
            BusinessId = businessId,
            ExitCase = new
            {
                exitCase.Id,
                exitCase.EmployeeId,
                exitCase.Version,
                exitCase.Status,
                exitCase.ProposedLastWorkingDate,
                exitCase.ActualLastWorkingDate,
                SettlementDate = settlementDate
            },
            MonetaryConfiguration = new
            {
                monetaryConfiguration.Id,
                monetaryConfiguration.VersionNo,
                monetaryConfiguration.EffectiveFrom,
                monetaryConfiguration.CurrencyCode,
                monetaryConfiguration.DecimalPrecision,
                monetaryConfiguration.RoundingMode
            },
            source.Evidence
        }, JsonOptions);
        var calculationInputHash = Hash(evidenceJson);
        using var evidenceDocument = JsonDocument.Parse(evidenceJson);
        var snapshotJson = JsonSerializer.Serialize(new
        {
            CalculationInputHash = calculationInputHash,
            SourceEvidence = evidenceDocument.RootElement,
            Totals = new { GrossDue = grossDue, TotalDeductions = deductions, NetSettlement = netSettlement },
            Lines = lines.Select(line => new
            {
                line.ComponentCode,
                line.ComponentName,
                line.LineType,
                line.Amount,
                line.IsApplicable,
                line.NotApplicableReason,
                line.SourceType,
                line.SourceId,
                Evidence = ParseJson(line.EvidenceSnapshotJson)
            })
        }, JsonOptions);
        var snapshotHash = Hash(snapshotJson);

        var header = await db.PayrollFinalSettlements.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.ExitCaseId == exitCaseId, cancellationToken);
        if (header is null)
        {
            header = new PayrollFinalSettlement
            {
                BusinessId = businessId,
                ExitCaseId = exitCaseId,
                EmployeeId = exitCase.EmployeeId,
                CurrentRevisionNo = 1,
                Version = 1,
                CreatedAtUtc = NowUtc(),
                CreatedByUserId = actorUserId
            };
            db.PayrollFinalSettlements.Add(header);
            await SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (header.EmployeeId != exitCase.EmployeeId)
                throw new InvalidOperationException(
                    "The existing final settlement identity does not match its exit case.");
            header.CurrentRevisionNo = checked(header.CurrentRevisionNo + 1);
            header.Version = checked(header.Version + 1);
        }

        var revision = new PayrollFinalSettlementRevision
        {
            BusinessId = businessId,
            FinalSettlementId = header.Id,
            RevisionNo = header.CurrentRevisionNo,
            SettlementDate = settlementDate,
            CurrencyCode = monetaryConfiguration.CurrencyCode,
            DecimalPrecision = monetaryConfiguration.DecimalPrecision,
            RoundingMode = monetaryConfiguration.RoundingMode,
            GrossDue = grossDue,
            TotalDeductions = deductions,
            NetSettlement = netSettlement,
            CalculationInputHash = calculationInputHash,
            SnapshotHash = snapshotHash,
            SnapshotJson = snapshotJson,
            IdempotencyKey = idempotencyKey,
            IdempotencyPayloadHash = idempotencyPayloadHash,
            CalculatedAtUtc = NowUtc(),
            CalculatedByUserId = actorUserId,
            Lines = lines.Select((line, index) => new PayrollFinalSettlementLine
            {
                BusinessId = businessId,
                LineNo = index + 1,
                ComponentCode = line.ComponentCode,
                ComponentName = line.ComponentName,
                LineType = line.LineType,
                Amount = line.Amount,
                IsApplicable = line.IsApplicable,
                NotApplicableReason = line.NotApplicableReason,
                SourceType = line.SourceType,
                SourceId = line.SourceId,
                EvidenceSnapshotJson = line.EvidenceSnapshotJson
            }).ToList()
        };
        db.PayrollFinalSettlementRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "Final settlement changed concurrently; refresh and calculate again.", exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new InvalidOperationException(
                "A final settlement revision or idempotency key conflicts with another request.",
                exception);
        }

        return Map(header, revision, isReplay: false);
    }

    public async Task<ApprovalTargetSnapshot?> ReadApprovalTargetAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != PayrollFinalSettlementApprovalConstants.TargetType
            || !long.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out var settlementId)
            || settlementId <= 0)
            return null;
        var header = await db.PayrollFinalSettlements.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == settlementId, cancellationToken);
        if (header is null)
            return null;
        var revision = await LoadCurrentRevisionAsync(header, cancellationToken);
        return revision is null
            ? null
            : await BuildApprovalTargetAsync(header, revision, cancellationToken);
    }

    public async Task<ApprovalTargetSnapshot> GetSubmissionTargetAsync(
        long businessId, long settlementId, int revisionNo, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var header = await db.PayrollFinalSettlements.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == settlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement was not found in this Business.");
        if (header.CurrentRevisionNo != revisionNo)
            throw new InvalidOperationException("Only the current final settlement revision can be submitted.");
        var revision = await LoadRevisionAsync(businessId, settlementId, revisionNo, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement revision was not found.");
        var prior = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (prior is not null)
            return new ApprovalTargetSnapshot(revisionNo, prior.SubmittedEvidenceHash);
        return await BuildApprovalTargetAsync(header, revision, cancellationToken);
    }

    public async Task<long?> GetPendingApprovalInstanceIdAsync(
        long businessId, long settlementId, int revisionNo, CancellationToken cancellationToken)
    {
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.FinalSettlementId == settlementId && row.RevisionNo == revisionNo,
                cancellationToken);
        if (submission?.ApprovalInstanceId is not long approvalInstanceId)
            return null;
        return await db.ApprovalInstances.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == approvalInstanceId
                && row.Status == ApprovalInstanceStatus.Pending)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ApprovalDecisionResult?> FindSubmissionReplayAsync(
        long businessId, SubmitApprovalCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return null;
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.IdempotencyKey == request.IdempotencyKey.Trim(), cancellationToken);
        if (submission is null)
            return null;
        if (request.ProcessType != ApprovalProcessType.FinalSettlement
            || request.TargetType != PayrollFinalSettlementApprovalConstants.TargetType
            || request.TargetId != submission.FinalSettlementId.ToString(CultureInfo.InvariantCulture)
            || request.TargetVersion != submission.RevisionNo
            || !FixedHashEquals(request.RequestHash, submission.SubmittedEvidenceHash))
            throw new ApprovalKernelConflictException(
                "The final settlement approval idempotency key was reused for different evidence.");
        if (submission.ApprovalInstanceId is not long instanceId)
            return null;
        var instance = await db.ApprovalInstances.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == instanceId, cancellationToken)
            ?? throw new ApprovalKernelConflictException(
                "The final settlement submission references a missing Approval Kernel instance.");
        return new ApprovalDecisionResult(instance.Id, instance.Status,
            instance.CurrentStepSequence, true, instance.Status.ToString());
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessType != ApprovalProcessType.FinalSettlement
            || request.TargetType != PayrollFinalSettlementApprovalConstants.TargetType
            || !long.TryParse(request.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out var settlementId))
            return null;
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ApprovalKernelConflictException("A final settlement approval idempotency key is required.");

        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var header = await db.PayrollFinalSettlements.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == settlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement was not found in this Business.");
        if (header.CurrentRevisionNo != request.TargetVersion)
            throw new ApprovalKernelConflictException("Only the current final settlement revision can be submitted.");
        var revision = await LoadRevisionAsync(
                businessId, settlementId, checked((int)request.TargetVersion), cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement revision was not found.");
        var target = await BuildApprovalTargetAsync(header, revision, cancellationToken);
        if (!FixedHashEquals(target.Hash, request.RequestHash))
            throw new ApprovalKernelConflictException(
                "Final settlement evidence changed; refresh and calculate again before submitting.");

        var priorKey = await db.PayrollFinalSettlementApprovalSubmissions.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.IdempotencyKey == request.IdempotencyKey.Trim(),
            cancellationToken);
        if (priorKey is not null)
        {
            var replay = await FindSubmissionReplayAsync(businessId, request, cancellationToken);
            if (replay is null)
                throw new ApprovalKernelConflictException(
                    "The final settlement approval idempotency key is already in use.");
            return new ApprovalTargetSnapshot(revision.RevisionNo, request.RequestHash,
                ReplayResult: replay);
        }
        var priorRevision = await db.PayrollFinalSettlementApprovalSubmissions.AnyAsync(row =>
            row.BusinessId == businessId && row.RevisionId == revision.Id, cancellationToken);
        if (priorRevision)
            throw new ApprovalKernelConflictException(
                "This final settlement revision has already been submitted; calculate a new revision to resubmit.");

        var exitCase = await db.HrExitCases.AsNoTracking().Include(row => row.ClearanceItems)
            .SingleAsync(row => row.BusinessId == businessId && row.Id == header.ExitCaseId,
                cancellationToken);
        var clearances = exitCase.ClearanceItems.OrderBy(row => row.Sequence).Select(row => new
        {
            row.Id,
            row.TemplateItemId,
            row.Code,
            row.IsMandatory,
            row.Status,
            row.Version
        }).ToArray();
        var lineEvidence = revision.Lines.OrderBy(row => row.LineNo).Select(row => new
        {
            row.LineNo,
            row.ComponentCode,
            row.LineType,
            row.Amount,
            row.IsApplicable,
            row.SourceType,
            row.SourceId,
            row.EvidenceSnapshotJson
        }).ToArray();
        var linesHash = Hash(lineEvidence);
        var clearanceJson = JsonSerializer.Serialize(clearances, JsonOptions);
        var clearanceHash = Hash(clearanceJson);
        var submittedEvidence = CreateSubmittedEvidenceJson(header, revision, linesHash, clearanceHash);
        var submittedHash = Hash(submittedEvidence);
        var submission = new PayrollFinalSettlementApprovalSubmission
        {
            BusinessId = businessId,
            FinalSettlementId = settlementId,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            EmployeeId = header.EmployeeId,
            ExitCaseId = header.ExitCaseId,
            CalculationSnapshotHash = revision.SnapshotHash,
            ComponentLinesHash = linesHash,
            GrossPayable = revision.GrossDue,
            TotalRecovery = revision.TotalDeductions,
            NetSettlement = revision.NetSettlement,
            CurrencyCode = revision.CurrencyCode,
            SourceEvidenceHash = revision.CalculationInputHash,
            CalculationPolicyVersion = "Phase7A-v1",
            ClearanceReferencesJson = clearanceJson,
            ClearanceReferencesHash = clearanceHash,
            SubmitterUserId = requesterUserId,
            SubmittedAtUtc = NowUtc(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            PayloadHash = Hash(new
            {
                businessId,
                settlementId,
                revision.RevisionNo,
                request.IdempotencyKey
            }),
            SubmittedEvidenceJson = submittedEvidence,
            SubmittedEvidenceHash = submittedHash
        };
        db.PayrollFinalSettlementApprovalSubmissions.Add(submission);
        return new ApprovalTargetSnapshot(revision.RevisionNo, submittedHash,
            [revision.CalculatedByUserId]);
    }

    public async Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.FinalSettlement)
            return;
        if (instance.TargetType != PayrollFinalSettlementApprovalConstants.TargetType
            || !long.TryParse(instance.TargetId, NumberStyles.None, CultureInfo.InvariantCulture, out var settlementId))
            throw new ApprovalKernelConflictException("Final settlement approval instance has an invalid target.");
        var submission = db.PayrollFinalSettlementApprovalSubmissions.Local.SingleOrDefault(row =>
            row.BusinessId == businessId && row.FinalSettlementId == settlementId
            && row.RevisionNo == instance.TargetVersion)
            ?? await db.PayrollFinalSettlementApprovalSubmissions.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId
                && row.FinalSettlementId == settlementId
                && row.RevisionNo == instance.TargetVersion, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Prepared final settlement evidence was not tracked.");
        if (submission.ApprovalInstanceId is not null
            || !FixedHashEquals(submission.SubmittedEvidenceHash, instance.RequestHash))
            throw new ApprovalKernelConflictException(
                "Prepared final settlement evidence no longer matches its Approval Kernel instance.");
        await db.SaveChangesAsync(cancellationToken);
        submission.ApprovalInstanceId = instance.Id;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.FinalSettlement)
            return;
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.ApprovalInstanceId == instance.Id,
            cancellationToken)
            ?? throw new ApprovalKernelConflictException("Final settlement approval evidence was not found.");
        if (instance.Status != ApprovalInstanceStatus.Approved)
            return;
        var header = await db.PayrollFinalSettlements.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == submission.FinalSettlementId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Approved final settlement was not found.");
        var revision = await LoadRevisionAsync(
                businessId, submission.FinalSettlementId, submission.RevisionNo, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Approved final settlement revision was not found.");
        var current = await BuildApprovalTargetAsync(header, revision, cancellationToken);
        if (header.CurrentRevisionNo != submission.RevisionNo
            || !FixedHashEquals(current.Hash, submission.SubmittedEvidenceHash)
            || !FixedHashEquals(instance.RequestHash, submission.SubmittedEvidenceHash))
            throw new ApprovalKernelConflictException(
                "Final settlement evidence changed after submission; approval cannot be completed.");
    }

    private async Task<ApprovalTargetSnapshot> BuildApprovalTargetAsync(
        PayrollFinalSettlement header,
        PayrollFinalSettlementRevision revision,
        CancellationToken cancellationToken)
    {
        var exitCase = await db.HrExitCases.AsNoTracking().Include(row => row.ClearanceItems)
            .SingleOrDefaultAsync(row => row.BusinessId == header.BusinessId
                && row.Id == header.ExitCaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement exit case was not found.");
        var mandatoryOutstanding = exitCase.ClearanceItems.FirstOrDefault(row =>
            row.IsMandatory && row.Status is not ("Cleared" or "NotApplicable"));
        if (mandatoryOutstanding is not null)
            throw new ApprovalKernelConflictException(
                "Final settlement cannot be submitted while mandatory clearances remain unresolved.");
        var lines = revision.Lines.OrderBy(row => row.LineNo).Select(row => new
        {
            row.LineNo,
            row.ComponentCode,
            row.LineType,
            row.Amount,
            row.IsApplicable,
            row.SourceType,
            row.SourceId,
            row.EvidenceSnapshotJson
        }).ToArray();
        var clearances = exitCase.ClearanceItems.OrderBy(row => row.Sequence).Select(row => new
        {
            row.Id,
            row.TemplateItemId,
            row.Code,
            row.IsMandatory,
            row.Status,
            row.Version
        }).ToArray();
        var submittedEvidence = CreateSubmittedEvidenceJson(
            header, revision, Hash(lines), Hash(JsonSerializer.Serialize(clearances, JsonOptions)));
        return new ApprovalTargetSnapshot(revision.RevisionNo, Hash(submittedEvidence),
            [revision.CalculatedByUserId]);
    }

    private static string CreateSubmittedEvidenceJson(
        PayrollFinalSettlement settlement,
        PayrollFinalSettlementRevision revision,
        string linesHash,
        string clearanceHash) =>
        JsonSerializer.Serialize(new
        {
            BusinessId = settlement.BusinessId,
            SettlementId = settlement.Id,
            RevisionId = revision.Id,
            RevisionNo = revision.RevisionNo,
            EmployeeId = settlement.EmployeeId,
            ExitCaseId = settlement.ExitCaseId,
            CalculationSnapshotHash = revision.SnapshotHash,
            ComponentLinesHash = linesHash,
            revision.GrossDue,
            TotalRecovery = revision.TotalDeductions,
            revision.NetSettlement,
            revision.CurrencyCode,
            SourceEvidenceHash = revision.CalculationInputHash,
            CalculationPolicyVersion = "Phase7A-v1",
            ClearanceReferencesHash = clearanceHash
        }, JsonOptions);

    private Task<PayrollFinalSettlementRevision?> LoadCurrentRevisionAsync(
        PayrollFinalSettlement header, CancellationToken cancellationToken) =>
        LoadRevisionAsync(header.BusinessId, header.Id, header.CurrentRevisionNo, cancellationToken);

    private async Task<PayrollFinalSettlementRevision?> LoadRevisionAsync(
        long businessId, long settlementId, int revisionNo, CancellationToken cancellationToken) =>
        await db.PayrollFinalSettlementRevisions.AsNoTracking().Include(row => row.Lines)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.FinalSettlementId == settlementId && row.RevisionNo == revisionNo,
                cancellationToken);

    private async Task<SettlementSource> LoadSourceEvidenceAsync(
        long businessId, long employeeId, DateOnly settlementDate, CancellationToken cancellationToken)
    {
        var results = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var runIds = results.Select(row => row.PayrollRunId).Distinct().ToArray();
        var runIdList = runIds.ToList();
        var runs = runIds.Length == 0
            ? []
            : await db.PayrollRuns.AsNoTracking()
                .Where(row => row.BusinessId == businessId && runIdList.Contains(row.Id))
                .ToArrayAsync(cancellationToken);
        var periodIds = runs.Select(row => row.PayrollPeriodId).Distinct().ToArray();
        var periodIdList = periodIds.ToList();
        var periods = periodIds.Length == 0
            ? []
            : await db.PayrollPeriods.AsNoTracking()
                .Where(row => row.BusinessId == businessId && periodIdList.Contains(row.Id))
                .ToArrayAsync(cancellationToken);
        var runById = runs.ToDictionary(row => row.Id);
        var periodById = periods.ToDictionary(row => row.Id);
        var approvedRuns = runs.Where(row => IncludedRunStatuses.Contains(row.Status, StringComparer.Ordinal))
            .ToArray();
        var approvedRunIds = approvedRuns.Select(row => row.Id).ToHashSet();
        var supersededRunIds = new HashSet<long>();
        foreach (var run in approvedRuns.Where(row => row.OriginalPayrollRunId.HasValue))
        {
            var ancestorId = run.OriginalPayrollRunId;
            while (ancestorId.HasValue && supersededRunIds.Add(ancestorId.Value)
                   && runById.TryGetValue(ancestorId.Value, out var ancestor))
                ancestorId = ancestor.OriginalPayrollRunId;
        }
        var currentRuns = approvedRuns.Where(row => !supersededRunIds.Contains(row.Id)).ToArray();
        var employeeResultsByRun = results.GroupBy(row => row.PayrollRunId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var periodGroup in currentRuns
            .Where(row => employeeResultsByRun.ContainsKey(row.Id))
            .GroupBy(row => row.PayrollPeriodId))
        {
            if (periodGroup.Count() > 1)
                throw new InvalidOperationException(
                    "Multiple current approved Payroll runs cover the employee in one period; resolve the overlapping run history first.");
        }

        var includedResults = new List<PayrollEmployeeResult>();
        var hasUnresolvedCorrection = runs.Any(row => row.RunType == PayrollRunTypes.Correction
                && row.Status is PayrollRunStatuses.Draft or PayrollRunStatuses.PendingApproval
                && row.OriginalPayrollRunId.HasValue
                && approvedRunIds.Contains(row.OriginalPayrollRunId.Value));
        if (hasUnresolvedCorrection)
            throw new InvalidOperationException(
                "An approved Payroll result has an unresolved correction in progress.");

        foreach (var run in currentRuns)
        {
            if (!employeeResultsByRun.TryGetValue(run.Id, out var runResults))
                continue;
            var period = periodById[run.PayrollPeriodId];
            if (run.RunType == PayrollRunTypes.Correction
                && (run.PaymentCorrectionRequired || run.DownstreamReversalRequired))
                throw new InvalidOperationException(
                    "A corrected Payroll run still requires downstream payment resolution.");
            if (period.PeriodEnd <= settlementDate)
            {
                if (period.Status is not (PayrollPeriodStatuses.Approved
                        or PayrollPeriodStatuses.Posted or PayrollPeriodStatuses.Closed)
                    || run.DecidedAtUtc is null
                    || run.CalculationVersion <= 0
                    || string.IsNullOrWhiteSpace(run.CalculationInputHash)
                    || run.DecimalPrecision is not >= 0 and <= 6
                    || string.IsNullOrWhiteSpace(run.CurrencyCode)
                    || run.RoundingMode is null
                    || !Enum.TryParse<MidpointRounding>(run.RoundingMode, out _))
                    throw new InvalidOperationException(
                        "An approved Payroll run or period has incomplete approval or frozen calculation evidence.");
                if (run.CalculationVersion != runResults[0].CalculationVersion
                    || runResults.Any(result => result.CalculationVersion != run.CalculationVersion
                        || result.Status != PayrollEmployeeResultStatuses.Calculated
                        || result.NetPay is null
                        || result.GrossEarnings is null
                        || result.TotalDeductions is null
                        || string.IsNullOrWhiteSpace(result.CalculationInputHash)
                        || string.IsNullOrWhiteSpace(result.CalculationSnapshotJson)
                        || result.CalculationSnapshotHash != Hash(result.CalculationSnapshotJson)))
                    throw new InvalidOperationException(
                        "An approved Payroll result does not match its frozen run calculation evidence.");
                includedResults.AddRange(runResults);
            }
        }

        var allEmployeeResultIds = results.Select(row => row.Id).ToArray();
        var allEmployeeResultIdList = allEmployeeResultIds.ToList();
        var resultLines = allEmployeeResultIds.Length == 0
            ? []
            : await db.PayrollEmployeeResultLines.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && allEmployeeResultIdList.Contains(row.PayrollEmployeeResultId))
                .OrderBy(row => row.PayrollEmployeeResultId)
                .ThenBy(row => row.CalculationOrder)
                .ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        foreach (var result in includedResults)
        {
            var resultLinesForResult = resultLines.Where(row =>
                row.PayrollEmployeeResultId == result.Id).ToArray();
            var lineGross = resultLinesForResult.Where(row => row.LineType == "Earning")
                .Sum(row => row.Amount);
            var lineDeductions = resultLinesForResult.Where(row => row.LineType == "Deduction")
                .Sum(row => row.Amount);
            if (lineGross != result.GrossEarnings
                || lineDeductions != result.TotalDeductions
                || result.NetPay != result.GrossEarnings - result.TotalDeductions)
                throw new InvalidOperationException(
                    "An approved Payroll result does not reconcile to its persisted calculation lines.");
        }
        var includedResultIds = includedResults.Select(row => row.Id).ToArray();
        var includedResultIdList = includedResultIds.ToList();
        var payments = includedResultIds.Length == 0
            ? []
            : await db.PayrollPayments.AsNoTracking().Include(row => row.Allocations)
                .Where(row => row.BusinessId == businessId
                    && row.PayrollEmployeeResultId != null
                    && includedResultIdList.Contains(row.PayrollEmployeeResultId.Value))
                .OrderBy(row => row.PayrollEmployeeResultId).ThenBy(row => row.AttemptNo)
                .ToArrayAsync(cancellationToken);
        var paymentIds = payments.Select(row => row.Id).ToArray();
        var paymentIdList = paymentIds.ToList();
        var paymentEvidence = paymentIds.Length == 0
            ? []
            : await db.PayrollPaymentEvidence.AsNoTracking()
                .Where(row => row.BusinessId == businessId && paymentIdList.Contains(row.PayrollPaymentId))
                .OrderBy(row => row.PayrollPaymentId).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
        var reconciliations = paymentIds.Length == 0
            ? []
            : await db.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.BusinessId == businessId && paymentIdList.Contains(row.PayrollPaymentId))
                .OrderBy(row => row.PayrollPaymentId)
                .ThenBy(row => row.PayrollPaymentAllocationId)
                .ThenBy(row => row.Version)
                .ToArrayAsync(cancellationToken);
        var paymentByResultId = payments.GroupBy(row => row.PayrollEmployeeResultId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var resultGroup in paymentByResultId.Values)
            if (resultGroup.Length > 1)
                throw new InvalidOperationException(
                    "A Payroll result has multiple payment attempts; reconcile its payment lineage before calculating final settlement.");
        foreach (var payment in payments)
        {
            var evidence = paymentEvidence.Where(row => row.PayrollPaymentId == payment.Id).ToArray();
            var evidencedPaidAmount = evidence.Sum(row =>
                row.Type == PayrollPaymentEvidenceTypes.Settlement
                    ? row.Amount
                    : row.Type == PayrollPaymentEvidenceTypes.Return ? -row.Amount : 0m);
            var result = includedResults.Single(row => row.Id == payment.PayrollEmployeeResultId);
            var payrollRun = runById[result.PayrollRunId];
            if (payment.CurrencyCode != payrollRun.CurrencyCode
                || payment.PayableAmount < 0m
                || payment.PaidAmount < 0m
                || payment.OutstandingAmount < 0m
                || payment.PayableAmount != result.NetPay
                || payment.PayableAmount != payment.PaidAmount + payment.OutstandingAmount
                || payment.PaidAmount != evidencedPaidAmount
                || payment.Allocations.Sum(row => row.Amount) < payment.PaidAmount
                || payment.Allocations.Sum(row => row.SettledAmount) != payment.PaidAmount
                || payment.ResolutionStatus != PayrollPaymentResolutionStatuses.None
                || (payment.Status == PayrollPaymentStatuses.Paid && payment.OutstandingAmount != 0m)
                || (payment.Status == PayrollPaymentStatuses.PartiallyPaid
                    && (payment.PaidAmount == 0m || payment.OutstandingAmount == 0m)))
                throw new InvalidOperationException(
                    "A Payroll payment has unresolved or inconsistent financial evidence.");
        }

        var adjustments = await db.PayrollEmployeeAdjustmentVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.EffectiveFrom <= settlementDate)
            .OrderBy(row => row.AdjustmentIdentity)
            .ThenByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.Version)
            .ToArrayAsync(cancellationToken);
        var adjustmentHeads = adjustments.GroupBy(row => row.AdjustmentIdentity)
            .Select(group => group.First())
            .Where(row => row.Status == "Active"
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= settlementDate))
            .OrderBy(row => row.AdjustmentIdentity)
            .ToArray();
        var loans = await db.PayrollEmployeeLoans.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.Status == "Active" && row.OutstandingAmount > 0m)
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var loanIds = loans.Select(row => row.Id).ToArray();
        var loanIdList = loanIds.ToList();
        var repayments = loanIds.Length == 0
            ? []
            : await db.PayrollLoanRepayments.AsNoTracking()
                .Where(row => row.BusinessId == businessId && loanIdList.Contains(row.LoanId))
                .OrderBy(row => row.LoanId).ThenBy(row => row.PaymentDate).ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);

        var includedResultIdSet = includedResultIds.ToHashSet();
        var lineGroupsByResultId = resultLines.GroupBy(row => row.PayrollEmployeeResultId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var recoveredLoanAmounts = new Dictionary<long, decimal>();
        foreach (var loan in loans)
        {
            var prefix = loan.Id.ToString(CultureInfo.InvariantCulture) + ":";
            foreach (var lineGroup in resultLines.Where(row => includedResultIdSet.Contains(row.PayrollEmployeeResultId)
                    && row.SourceType == "LoanRecovery"
                    && row.SourceId?.StartsWith(prefix, StringComparison.Ordinal) == true)
                .GroupBy(row => row.PayrollEmployeeResultId))
            {
                if (!paymentByResultId.TryGetValue(lineGroup.Key, out var attempts)
                    || attempts.Length != 1
                    || !IsFullyPaidAndReconciled(attempts[0],
                        reconciliations.Where(row => row.PayrollPaymentId == attempts[0].Id)))
                    throw new InvalidOperationException(
                        "A Payroll loan recovery is unpaid, partially paid or unreconciled; resolve it before calculating final settlement.");
                recoveredLoanAmounts[loan.Id] = recoveredLoanAmounts.GetValueOrDefault(loan.Id)
                    + lineGroup.Sum(row => row.Amount);
            }
        }

        var overtime = await db.HrOvertimeEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.WorkDate <= settlementDate && row.Status == "PayrollEligible")
            .OrderBy(row => row.WorkDate).ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.WorkDate, row.CalculatedMinutes, row.ApprovedMinutes, row.PolicyVersionId })
            .ToArrayAsync(cancellationToken);

        var eligibleResultEvidence = includedResults.Select(result =>
        {
            var run = runById[result.PayrollRunId];
            var period = periodById[run.PayrollPeriodId];
            paymentByResultId.TryGetValue(result.Id, out var resultPayments);
            var payment = resultPayments?.SingleOrDefault();
            var relatedEvidence = payment is null
                ? []
                : paymentEvidence.Where(row => row.PayrollPaymentId == payment.Id).ToArray();
            var relatedReconciliations = payment is null
                ? []
                : reconciliations.Where(row => row.PayrollPaymentId == payment.Id)
                    .GroupBy(row => row.PayrollPaymentAllocationId)
                    .Select(group => group.OrderByDescending(row => row.Version).First())
                    .OrderBy(row => row.PayrollPaymentAllocationId)
                    .ToArray();
            return new PayrollResultEvidence(
                result.Id,
                run.Id,
                run.RunType,
                run.Status,
                run.Version,
                run.CalculationVersion,
                run.CalculationInputHash,
                run.CurrencyCode,
                run.DecimalPrecision,
                run.RoundingMode,
                period.Id,
                period.PeriodStart,
                period.PeriodEnd,
                result.CalculationVersion,
                result.CalculationInputHash,
                result.CalculationSnapshotHash,
                result.GrossEarnings,
                result.TotalDeductions,
                result.NetPay,
                payment is null ? result.NetPay!.Value : payment.OutstandingAmount,
                payment is null ? null : new PayrollPaymentEvidenceSnapshot(
                    payment.Id, payment.Status, payment.PayableAmount, payment.PaidAmount,
                    payment.OutstandingAmount, payment.ResolutionStatus,
                    relatedEvidence.Select(row => new PaymentEvidenceItem(
                        row.Id, row.Type, row.Amount, row.EvidenceDate, row.VerifiedAtUtc)).ToArray(),
                    relatedReconciliations.Select(row => new PaymentReconciliationItem(
                        row.PayrollPaymentAllocationId, row.Version, row.Status,
                        row.ExpectedAmount, row.SettledAmount)).ToArray()),
                lineGroupsByResultId.GetValueOrDefault(result.Id, [])
                    .Select(row => new PayrollComponentEvidence(
                        row.ComponentCodeSnapshot, row.ComponentNameSnapshot, row.LineType,
                        row.Amount, row.SourceType, row.SourceId, row.SourceVersion)).ToArray());
        }).ToArray();

        var adjustmentEvidence = adjustmentHeads.Select(row =>
        {
            var sourceId = AdjustmentSourceId(row);
            var matchingLines = resultLines.Where(line =>
                includedResultIdSet.Contains(line.PayrollEmployeeResultId)
                && line.SourceId == sourceId && line.SourceType is
                    "ManualEarning" or "ManualDeduction" or "TaxStatutory").ToArray();
            return new AdjustmentEvidence(
                row.Id, sourceId, row.AdjustmentIdentity, row.Version, row.AdjustmentKind,
                row.Classification, row.ComponentCodeSnapshot, row.ComponentNameSnapshot,
                row.ComponentTypeSnapshot, row.Amount, row.EffectiveFrom, row.EffectiveTo,
                row.Status, matchingLines.Select(line => new AdjustmentConsumption(
                    line.PayrollEmployeeResultId, line.SourceType)).ToArray());
        }).ToArray();

        var loanEvidence = loans.Select(row => new LoanEvidence(
            row.Id,
            row.LoanType,
            row.Status,
            row.Version,
            row.OutstandingAmount,
            recoveredLoanAmounts.GetValueOrDefault(row.Id),
            repayments.Where(repayment => repayment.LoanId == row.Id)
                .Select(repayment => new LoanRepaymentEvidence(
                    repayment.Id, repayment.Amount, repayment.PaymentDate)).ToArray())).ToArray();

        var overlappingPeriods = currentRuns.Where(run => employeeResultsByRun.ContainsKey(run.Id))
            .Select(run => periodById[run.PayrollPeriodId])
            .Where(period => period.PeriodStart <= settlementDate && period.PeriodEnd > settlementDate)
            .Select(period => new { period.Id, period.PeriodStart, period.PeriodEnd })
            .OrderBy(period => period.Id)
            .ToArray();

        return new SettlementSource(
            new
            {
                PayrollResults = eligibleResultEvidence,
                ActiveAdjustments = adjustmentEvidence,
                ActiveLoans = loanEvidence,
                EligibleOvertime = overtime,
                OverlappingPeriods = overlappingPeriods
            },
            adjustmentEvidence,
            loanEvidence,
            eligibleResultEvidence,
            overtime,
            overlappingPeriods);
    }

    private static List<SettlementLineDraft> BuildLines(
        SettlementSource source,
        DateOnly settlementDate,
        string currencyCode,
        int precision,
        MidpointRounding roundingMode)
    {
        var lines = new List<SettlementLineDraft>();
        foreach (var result in source.PayrollResults)
        {
            if (result.CurrencyCode != currencyCode
                || result.DecimalPrecision != precision
                || result.RoundingMode != roundingMode.ToString())
                throw new InvalidOperationException(
                    "Outstanding Payroll results use a different or incomplete currency configuration; currency conversion is not supported.");
            var outstanding = result.OutstandingNetAmount;
            if (outstanding < 0m)
            {
                lines.Add(new SettlementLineDraft(
                    "PAYROLL_NET_DEBIT", "Outstanding Payroll net debit",
                    PayrollFinalSettlementLineTypes.Deduction,
                    Round(decimal.Abs(outstanding), precision, roundingMode), true, null,
                    "PayrollEmployeeResult", result.ResultId.ToString(CultureInfo.InvariantCulture),
                    JsonSerializer.Serialize(result, JsonOptions)));
            }
            else if (outstanding > 0m)
            {
                lines.Add(new SettlementLineDraft(
                    "PAYROLL_NET_DUE", "Outstanding approved Payroll net pay",
                    PayrollFinalSettlementLineTypes.Earning,
                    Round(outstanding, precision, roundingMode), true, null,
                    "PayrollEmployeeResult", result.ResultId.ToString(CultureInfo.InvariantCulture),
                    JsonSerializer.Serialize(result, JsonOptions)));
            }
        }

        foreach (var adjustment in source.Adjustments.Where(row => row.ConsumedByPayroll.Length == 0))
        {
            if (adjustment.AdjustmentKind is "RecurringAllowance" or "RecurringDeduction")
                continue;
            if (adjustment.Classification == "Statutory")
                continue;
            var isEarning = adjustment.AdjustmentKind == "OneTimeEarning";
            if (!isEarning && adjustment.AdjustmentKind != "OneTimeDeduction")
                continue;
            lines.Add(new SettlementLineDraft(
                adjustment.ComponentCode,
                adjustment.ComponentName,
                isEarning ? PayrollFinalSettlementLineTypes.Earning : PayrollFinalSettlementLineTypes.Deduction,
                Round(adjustment.Amount, precision, roundingMode),
                true,
                null,
                "PayrollEmployeeAdjustmentVersion",
                adjustment.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(new
                {
                    adjustment.Id,
                    adjustment.AdjustmentIdentity,
                    adjustment.Version,
                    adjustment.AdjustmentKind,
                    adjustment.Classification,
                    adjustment.ComponentCode,
                    adjustment.Amount,
                    adjustment.EffectiveFrom,
                    adjustment.EffectiveTo
                }, JsonOptions)));
        }

        foreach (var loan in source.Loans)
        {
            var remaining = loan.OutstandingAmount - loan.RecoveredByPayroll;
            if (remaining < 0m)
                throw new InvalidOperationException(
                    "Verified Payroll loan recoveries exceed the current loan balance.");
            if (remaining == 0m)
                continue;
            lines.Add(new SettlementLineDraft(
                "LOAN_RECOVERY", $"Outstanding {loan.LoanType} / advance",
                PayrollFinalSettlementLineTypes.Deduction,
                Round(remaining, precision, roundingMode), true, null,
                "PayrollEmployeeLoan", loan.Id.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(loan, JsonOptions)));
        }

        if (source.OverlappingPeriods.Length > 0)
            lines.Add(NotApplicable(
                "PARTIAL_PERIOD_PAY", "Final-period salary proration",
                "No effective settlement proration rule is configured; overlapping Payroll periods require a Payroll-calculated result or an authorized one-time adjustment.",
                "PayrollPeriod",
                JsonSerializer.Serialize(source.OverlappingPeriods, JsonOptions)));

        lines.Add(NotApplicable(
            "OVERTIME_NOT_SEPARATELY_PRICED", "Overtime not separately priced",
            source.Overtime.Length == 0
                ? "No separate overtime amount is due; overtime included in an outstanding approved Payroll result is already reflected in its net amount."
                : "Approved overtime without an outstanding approved Payroll result is not independently priced by final settlement.",
            "HrOvertimeEntry",
            JsonSerializer.Serialize(source.Overtime, JsonOptions)));
        lines.Add(NotApplicable(
            "LEAVE_ENCASHMENT", "Leave encashment",
            "No effective monetary leave-encashment policy is configured; leave balances are not converted to money.",
            "Policy",
            JsonSerializer.Serialize(new { AsOf = settlementDate }, JsonOptions)));
        lines.Add(NotApplicable(
            "UNPAID_LEAVE", "Unpaid leave not separately priced",
            "Unpaid leave is included only where it has already been priced in an outstanding approved Payroll result.",
            "PayrollEmployeeResult",
            JsonSerializer.Serialize(new { AsOf = settlementDate }, JsonOptions)));
        lines.Add(NotApplicable(
            "ASSET_RECOVERY", "Asset recovery",
            "No authoritative asset valuation source is configured; use an authorized fixed Payroll deduction where applicable.",
            "HrClearance",
            "{}"));
        var recurring = source.Adjustments.Where(row =>
            row.AdjustmentKind is "RecurringAllowance" or "RecurringDeduction").ToArray();
        if (recurring.Length > 0)
            lines.Add(NotApplicable(
                "RECURRING_ADJUSTMENTS", "Recurring Payroll adjustments not separately priced",
                "Recurring adjustments are represented through approved Payroll results; no extra proration is applied.",
                "PayrollEmployeeAdjustmentVersion",
                JsonSerializer.Serialize(recurring.Select(row => new { row.Id, row.AdjustmentKind, row.EffectiveFrom }),
                    JsonOptions)));
        var statutory = source.Adjustments.Where(row => row.Classification == "Statutory").ToArray();
        if (statutory.Length > 0)
            lines.Add(NotApplicable(
                "STATUTORY_ADJUSTMENTS", "Statutory adjustments not separately priced",
                "Statutory deductions require the applicable Payroll calculation and tax policy; they are not independently estimated.",
                "PayrollEmployeeAdjustmentVersion",
                JsonSerializer.Serialize(statutory.Select(row => new { row.Id, row.AdjustmentKind, row.EffectiveFrom }),
                    JsonOptions)));

        return lines;
    }

    private static SettlementLineDraft NotApplicable(
        string code, string name, string reason, string sourceType, string evidenceJson) =>
        new(code, name, PayrollFinalSettlementLineTypes.NotApplicable, 0m,
            false, reason, sourceType, null, evidenceJson);

    private static bool IsFullyPaidAndReconciled(
        PayrollPayment payment,
        IEnumerable<PayrollPaymentReconciliation> reconciliations)
    {
        if (payment.Status != PayrollPaymentStatuses.Paid
            || payment.OutstandingAmount != 0m
            || payment.PaidAmount != payment.PayableAmount
            || payment.Allocations.Count == 0)
            return false;
        var latest = reconciliations.GroupBy(row => row.PayrollPaymentAllocationId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Version).First());
        return payment.Allocations.All(allocation =>
            latest.TryGetValue(allocation.Id, out var row)
            && row.Status == PayrollPaymentReconciliationStatuses.Reconciled
            && row.SettledAmount == allocation.Amount);
    }

    private static string AdjustmentSourceId(PayrollEmployeeAdjustmentVersion row) =>
        FormattableString.Invariant(
            $"{row.EmployeeId}:{row.AdjustmentIdentity:N}:{row.Version}");

    private static decimal Round(decimal amount, int precision, MidpointRounding roundingMode) =>
        decimal.Round(amount, precision, roundingMode);

    private async Task<PayrollFinalSettlementModel?> GetLatestAsync(
        IQueryable<long> settlementIds, CancellationToken cancellationToken)
    {
        var settlementId = await settlementIds.SingleOrDefaultAsync(cancellationToken);
        if (settlementId <= 0)
            return null;
        var header = await db.PayrollFinalSettlements.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == settlementId, cancellationToken);
        if (header is null)
            return null;
        var revision = await db.PayrollFinalSettlementRevisions.AsNoTracking()
            .Include(row => row.Lines)
            .SingleOrDefaultAsync(row => row.BusinessId == header.BusinessId
                && row.FinalSettlementId == header.Id
                && row.RevisionNo == header.CurrentRevisionNo, cancellationToken);
        if (revision is null)
            return null;
        var model = Map(header, revision, isReplay: false);
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == header.BusinessId
                && row.FinalSettlementId == header.Id && row.RevisionNo == revision.RevisionNo,
                cancellationToken);
        if (submission is null)
            return model;
        var instance = submission.ApprovalInstanceId is long instanceId
            ? await db.ApprovalInstances.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == header.BusinessId && row.Id == instanceId, cancellationToken)
            : null;
        var history = instance is null
            ? []
            : await db.ApprovalDecisions.AsNoTracking()
                .Where(row => row.BusinessId == header.BusinessId && row.ApprovalInstanceId == instance.Id)
                .OrderBy(row => row.OccurredAtUtc)
                .Select(row => new PayrollFinalSettlementApprovalDecisionModel(
                    row.Decision.ToString(), row.ActorRealUserId, row.OccurredAtUtc,
                    row.Reason, row.InstanceStatusAfterDecision.ToString()))
                .ToArrayAsync(cancellationToken);
        return model with
        {
            Status = instance?.Status.ToString() ?? "Submitted",
            ApprovalInstanceId = instance?.Id,
            RequestedByUserId = submission.SubmitterUserId,
            RequestedAtUtc = submission.SubmittedAtUtc,
            ApprovalPolicyCode = instance?.PolicyCodeSnapshot,
            ApprovalPolicyVersion = instance?.PolicyVersionSnapshot,
            SubmittedEvidenceHash = submission.SubmittedEvidenceHash,
            ApprovalHistory = history
        };
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Final settlement calculation requires the audited transaction boundary.");
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql())
            return;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 2)})", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 8)})", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})", cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "Final settlement changed concurrently; refresh and calculate again.", exception);
        }
    }

    private DateTime NowUtc() => clock.GetUtcNow().UtcDateTime;

    private static PayrollFinalSettlementModel Map(
        PayrollFinalSettlement header,
        PayrollFinalSettlementRevision revision,
        bool isReplay) =>
        new(header.Id, header.ExitCaseId, header.EmployeeId, revision.RevisionNo,
            revision.SettlementDate, revision.CurrencyCode, revision.DecimalPrecision,
            revision.RoundingMode, revision.GrossDue, revision.TotalDeductions,
            revision.NetSettlement, revision.CalculationInputHash, revision.SnapshotHash,
            revision.SnapshotJson, revision.CalculatedAtUtc, revision.CalculatedByUserId,
            revision.Lines.OrderBy(row => row.LineNo).Select(row =>
                new PayrollFinalSettlementLineModel(row.LineNo, row.ComponentCode,
                    row.ComponentName, row.LineType, row.Amount, row.IsApplicable,
                    row.NotApplicableReason, row.SourceType, row.SourceId,
                    row.EvidenceSnapshotJson)).ToArray(),
            isReplay)
        {
            RevisionId = revision.Id
        };

    private static string Hash(object value) =>
        Hash(JsonSerializer.Serialize(value, JsonOptions));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedHashEquals(string left, string right)
    {
        if (left.Length != 64 || right.Length != 64)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record SettlementLineDraft(
        string ComponentCode,
        string ComponentName,
        string LineType,
        decimal Amount,
        bool IsApplicable,
        string? NotApplicableReason,
        string SourceType,
        string? SourceId,
        string EvidenceSnapshotJson);

    private sealed record SettlementSource(
        object Evidence,
        AdjustmentEvidence[] Adjustments,
        LoanEvidence[] Loans,
        PayrollResultEvidence[] PayrollResults,
        object[] Overtime,
        object[] OverlappingPeriods);

    private sealed record PayrollResultEvidence(
        long ResultId,
        long RunId,
        string RunType,
        string RunStatus,
        long RunVersion,
        int RunCalculationVersion,
        string? RunCalculationInputHash,
        string? CurrencyCode,
        int? DecimalPrecision,
        string? RoundingMode,
        long PeriodId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int ResultCalculationVersion,
        string CalculationInputHash,
        string? CalculationSnapshotHash,
        decimal? GrossEarnings,
        decimal? TotalDeductions,
        decimal? NetPay,
        decimal OutstandingNetAmount,
        PayrollPaymentEvidenceSnapshot? Payment,
        PayrollComponentEvidence[] Lines);

    private sealed record PayrollPaymentEvidenceSnapshot(
        long PaymentId,
        string Status,
        decimal PayableAmount,
        decimal PaidAmount,
        decimal OutstandingAmount,
        string ResolutionStatus,
        PaymentEvidenceItem[] Evidence,
        PaymentReconciliationItem[] Reconciliations);

    private sealed record PaymentEvidenceItem(
        long Id, string Type, decimal Amount, DateOnly EvidenceDate, DateTime VerifiedAtUtc);

    private sealed record PaymentReconciliationItem(
        long AllocationId, long Version, string Status, decimal ExpectedAmount, decimal SettledAmount);

    private sealed record PayrollComponentEvidence(
        string ComponentCode,
        string ComponentName,
        string LineType,
        decimal Amount,
        string SourceType,
        string? SourceId,
        long? SourceVersion);

    private sealed record AdjustmentEvidence(
        long Id,
        string SourceId,
        Guid AdjustmentIdentity,
        int Version,
        string AdjustmentKind,
        string Classification,
        string ComponentCode,
        string ComponentName,
        string ComponentType,
        decimal Amount,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string Status,
        AdjustmentConsumption[] ConsumedByPayroll);

    private sealed record AdjustmentConsumption(long ResultId, string SourceType);

    private sealed record LoanEvidence(
        long Id,
        string LoanType,
        string Status,
        long Version,
        decimal OutstandingAmount,
        decimal RecoveredByPayroll,
        LoanRepaymentEvidence[] Repayments);

    private sealed record LoanRepaymentEvidence(long Id, decimal Amount, DateOnly PaymentDate);
}
