using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPeriodStore(
    RetailPOSDbContext db,
    IPayrollPeriodCloseExceptionPolicy? exceptionPolicy = null) : IPayrollPeriodStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExceptionEligibleGateCodes =
    [
        "held_payment_exception",
        "unpaid_partial_liability"
    ];

    public async Task<IReadOnlyList<PayrollPeriodResponse>> ListAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollPeriods.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderByDescending(row => row.PeriodStart)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(row => Map(row)).ToArray();
    }

    public async Task<PayrollPeriodResponse> GetAsync(
        long businessId, long id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollPeriods.AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        return Map(row);
    }

    public async Task<PayrollPeriodResponse> CreateAsync(
        long businessId,
        long actorUserId,
        CreatePayrollPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PeriodCode) || request.PeriodCode.Trim().Length > 30
            || request.PeriodCode.Any(char.IsControl))
            throw new ArgumentException("A valid Payroll period code of at most 30 characters is required.");
        if (request.PeriodStart > request.PeriodEnd)
            throw new ArgumentException("Payroll period start must not be after its end.");
        await LockBusinessAsync(businessId, cancellationToken);
        var periodCode = request.PeriodCode.Trim().ToUpperInvariant();
        if (await db.PayrollPeriods.AnyAsync(row =>
                row.BusinessId == businessId
                && (row.PeriodCode == periodCode
                    || row.PeriodStart <= request.PeriodEnd && row.PeriodEnd >= request.PeriodStart),
                cancellationToken))
            throw new PayrollPeriodConflictException(
                "Payroll period code is already used or its dates overlap another period in this Business.");

        var now = DateTime.UtcNow;
        var row = new PayrollPeriod
        {
            BusinessId = businessId,
            PeriodCode = periodCode,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            PayDate = request.PayDate,
            Status = PayrollPeriodStatuses.Open,
            Version = 1,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            OpenedAtUtc = now,
            OpenedByUserId = actorUserId
        };
        db.PayrollPeriods.Add(row);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollPeriodResponse> StartProcessingAsync(
        long businessId,
        long actorUserId,
        long id,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await LockBusinessAsync(businessId, cancellationToken);
        var row = await db.PayrollPeriods.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (row.Status == PayrollPeriodStatuses.Processing)
            return Map(row, isReplay: true);
        if (row.Version != expectedVersion)
            throw new PayrollPeriodConflictException("Payroll period changed since it was read.");
        if (row.Status != PayrollPeriodStatuses.Open)
            throw new PayrollPeriodConflictException(
                $"A Payroll period in {row.Status} cannot start processing.");

        row.Status = PayrollPeriodStatuses.Processing;
        row.ProcessingAtUtc = DateTime.UtcNow;
        row.ProcessingByUserId = actorUserId;
        row.Version = checked(row.Version + 1);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollPeriodResponse> LockAsync(
        long businessId,
        long actorUserId,
        long id,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await LockBusinessAsync(businessId, cancellationToken);
        var row = await db.PayrollPeriods.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (row.Status == PayrollPeriodStatuses.Locked)
            return Map(row, isReplay: true);
        if (row.Version != expectedVersion)
            throw new PayrollPeriodConflictException("Payroll period changed since it was read.");
        if (row.Status != PayrollPeriodStatuses.Processing)
            throw new PayrollPeriodConflictException(
                $"A Payroll period in {row.Status} cannot be locked.");

        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(run => run.BusinessId == businessId && run.PayrollPeriodId == id)
            .ToArrayAsync(cancellationToken);
        if (runs.Length == 0 || runs.Any(run =>
                run.Status != PayrollRunStatuses.PendingApproval
                && run.Status != PayrollRunStatuses.Approved))
            throw new PayrollPeriodConflictException(
                "A Payroll period can be locked only after every run has been submitted for approval.");

        var now = DateTime.UtcNow;
        row.Status = PayrollPeriodStatuses.Locked;
        row.LockedAtUtc = now;
        row.LockedByUserId = actorUserId;
        row.Version = checked(row.Version + 1);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollPeriodResponse> ReopenAsync(
        long businessId,
        long actorUserId,
        long id,
        ReopenPayrollPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await LockBusinessAsync(businessId, cancellationToken);
        var row = await db.PayrollPeriods.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        var reason = request.Reason.Trim();
        if (row.Version == checked(request.ExpectedVersion + 1)
            && row.Status is PayrollPeriodStatuses.Processing or PayrollPeriodStatuses.Locked
            && row.ReopenedAtUtc.HasValue
            && row.ReopenedByUserId == actorUserId
            && string.Equals(row.ReopenReason, reason, StringComparison.Ordinal))
            return Map(row, isReplay: true);
        if (row.Version != request.ExpectedVersion)
            throw new PayrollPeriodConflictException("Payroll period changed since it was read.");
        if (row.Status is not (PayrollPeriodStatuses.Locked or PayrollPeriodStatuses.Approved))
            throw new PayrollPeriodConflictException(
                $"A Payroll period in {row.Status} cannot be reopened through the ordinary lifecycle.");

        if (row.Status == PayrollPeriodStatuses.Locked
            && await db.PayrollRuns.AnyAsync(run =>
                run.BusinessId == businessId && run.PayrollPeriodId == id
                && run.Status == PayrollRunStatuses.PendingApproval, cancellationToken))
            throw new PayrollPeriodConflictException(
                "A Payroll period with pending run approvals cannot be reopened.");

        var now = DateTime.UtcNow;
        row.Status = row.Status == PayrollPeriodStatuses.Approved
            ? PayrollPeriodStatuses.Locked
            : PayrollPeriodStatuses.Processing;
        row.ReopenedAtUtc = now;
        row.ReopenedByUserId = actorUserId;
        row.ReopenReason = reason;
        row.Version = checked(row.Version + 1);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollPeriodResponse> ReturnToOpenAsync(
        long businessId,
        long actorUserId,
        long id,
        ReopenPayrollPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await LockBusinessAsync(businessId, cancellationToken);
        var row = await db.PayrollPeriods.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        var reason = request.Reason.Trim();
        if (row.Version == checked(request.ExpectedVersion + 1)
            && row.Status == PayrollPeriodStatuses.Open
            && row.ReopenedAtUtc.HasValue
            && row.ReopenedByUserId == actorUserId
            && string.Equals(row.ReopenReason, reason, StringComparison.Ordinal))
            return Map(row, isReplay: true);
        if (row.Version != request.ExpectedVersion)
            throw new PayrollPeriodConflictException("Payroll period changed since it was read.");
        if (row.Status != PayrollPeriodStatuses.Processing)
            throw new PayrollPeriodConflictException(
                $"A Payroll period in {row.Status} cannot return to Open.");
        if (await db.PayrollRuns.AnyAsync(
                run => run.BusinessId == businessId && run.PayrollPeriodId == id,
                cancellationToken))
            throw new PayrollPeriodConflictException(
                "A Payroll period cannot return to Open after a run has been created.");

        row.Status = PayrollPeriodStatuses.Open;
        row.ReopenedAtUtc = DateTime.UtcNow;
        row.ReopenedByUserId = actorUserId;
        row.ReopenReason = reason;
        row.Version = checked(row.Version + 1);
        await SaveAsync(cancellationToken);
        return Map(row);
    }

    public async Task<PayrollPeriodClosePreview> PreviewCloseAsync(
        long businessId,
        long actorUserId,
        long id,
        ClosePayrollPeriodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.IdempotencyKey.Trim();
        var normalizedRequest = request with
        {
            IdempotencyKey = key,
            Reason = request.Reason.Trim()
        };
        var requestHash = HashCloseRequest(id, normalizedRequest);
        var prior = await db.PayrollPeriodCloseAssessments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.PayrollPeriodId == id && row.IdempotencyKey == key, cancellationToken);
        if (prior is not null)
        {
            EnsureSameHash(prior.RequestHash, requestHash);
            return new PayrollPeriodClosePreview(MapAssessment(prior, isReplay: true), "replay");
        }

        var plan = await BuildClosePlanAsync(
            businessId, actorUserId, id, normalizedRequest, cancellationToken);
        return new PayrollPeriodClosePreview(plan.Response, plan.Fingerprint);
    }

    public async Task<PayrollPeriodCloseResponse> CloseAsync(
        long businessId,
        long actorUserId,
        long id,
        ClosePayrollPeriodRequest request,
        string expectedStateFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await LockBusinessAsync(businessId, cancellationToken);
        await LockPaymentBusinessAsync(businessId, cancellationToken);

        var key = request.IdempotencyKey.Trim();
        var normalizedRequest = request with { IdempotencyKey = key, Reason = request.Reason.Trim() };
        var requestHash = HashCloseRequest(id, normalizedRequest);
        var prior = await db.PayrollPeriodCloseAssessments.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == id && row.IdempotencyKey == key,
            cancellationToken);
        if (prior is not null)
        {
            EnsureSameHash(prior.RequestHash, requestHash);
            return MapAssessment(prior, isReplay: true);
        }

        var plan = await BuildClosePlanAsync(businessId, actorUserId, id, normalizedRequest, cancellationToken);
        if (!string.Equals(plan.Fingerprint, expectedStateFingerprint, StringComparison.Ordinal))
            throw new PayrollPeriodConflictException(
                "Payroll period close evidence changed during assessment. Assess and retry with a new key.");
        var canClose = plan.Response.Gates.All(gate => !gate.Blocking || gate.Passed);
        if (canClose != plan.Response.Closed)
            throw new PayrollPeriodConflictException("Payroll period close assessment is inconsistent.");

        var now = DateTime.UtcNow;
        var row = await db.PayrollPeriods.SingleAsync(
            item => item.BusinessId == businessId && item.Id == id, cancellationToken);
        var assessment = new PayrollPeriodCloseAssessment
        {
            BusinessId = businessId,
            PayrollPeriodId = id,
            PeriodVersion = row.Version,
            StatusSnapshot = canClose ? PayrollPeriodStatuses.Closed : row.Status,
            PolicyVersion = normalizedRequest.PolicyVersion,
            IdempotencyKey = key,
            RequestHash = requestHash,
            GateResultsJson = JsonSerializer.Serialize(plan.Response.Gates, JsonOptions),
            RunRevisionRefsJson = plan.RunRevisionRefsJson,
            PaymentStateVersionsJson = plan.PaymentStateVersionsJson,
            PostingStateVersionsJson = "[]",
            ExceptionsJson = plan.ExceptionEvidenceJson,
            Reason = normalizedRequest.Reason,
            ActorUserId = actorUserId,
            AssessedAtUtc = now,
            ClosedAtUtc = canClose ? now : null,
            WasClosed = canClose,
            ResultPeriodVersion = canClose ? checked(row.Version + 1) : row.Version
        };
        db.PayrollPeriodCloseAssessments.Add(assessment);
        if (canClose)
        {
            row.Status = PayrollPeriodStatuses.Closed;
            row.ClosedAtUtc = now;
            row.ClosedByUserId = actorUserId;
            row.ClosedReason = normalizedRequest.Reason;
            row.Version = checked(row.Version + 1);
        }
        await SaveAsync(cancellationToken);

        return new PayrollPeriodCloseResponse(id, row.Status, row.Version, assessment.Id,
            assessment.PolicyVersion, plan.Response.Gates, canClose);
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll period writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 8)})", cancellationToken);
    }

    private async Task LockPaymentBusinessAsync(
        long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})",
                cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPeriodConflictException("Payroll period changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollPeriodConflictException(
                "Payroll period code or date range conflicts with another period.", exception);
        }
    }

    private static PayrollPeriodResponse Map(PayrollPeriod row, bool isReplay = false) =>
        new(row.Id, row.BusinessId, row.PeriodCode, row.PeriodStart, row.PeriodEnd, row.PayDate,
            row.Status, row.Version, row.CreatedAtUtc, row.CreatedByUserId, row.OpenedAtUtc,
            row.OpenedByUserId, row.ProcessingAtUtc, row.ProcessingByUserId, row.LockedAtUtc,
            row.LockedByUserId, row.ApprovedAtUtc, row.ApprovedByUserId, row.PostedAtUtc,
            row.PostedByUserId, row.ClosedAtUtc, row.ClosedByUserId, row.ClosedReason, row.ReopenedAtUtc,
            row.ReopenedByUserId, row.ReopenReason, isReplay);

    private async Task<ClosePlan> BuildClosePlanAsync(
        long businessId,
        long actorUserId,
        long id,
        ClosePayrollPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var period = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (period.Status == PayrollPeriodStatuses.Closed)
            throw new PayrollPeriodConflictException("Closed Payroll periods cannot be closed again.");
        if (period.Version != request.ExpectedVersion)
            throw new PayrollPeriodConflictException("Payroll period changed since it was read.");

        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(run => run.BusinessId == businessId && run.PayrollPeriodId == id)
            .OrderBy(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var selections = await (
            from selection in db.PayrollRunEmployeeSelections.AsNoTracking()
            join run in db.PayrollRuns.AsNoTracking()
                on selection.PayrollRunId equals run.Id
            where selection.BusinessId == businessId
                && selection.IsIncluded
                && run.BusinessId == businessId
                && run.PayrollPeriodId == id
            select new { selection.PayrollRunId, selection.EmployeeId })
            .ToArrayAsync(cancellationToken);
        var results = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(result => result.BusinessId == businessId
                && db.PayrollRuns.Any(run => run.BusinessId == businessId
                    && run.PayrollPeriodId == id && run.Id == result.PayrollRunId))
            .OrderBy(result => result.PayrollRunId).ThenBy(result => result.EmployeeId)
            .ToArrayAsync(cancellationToken);
        var currentResults = runs.SelectMany(run => results.Where(result =>
                result.PayrollRunId == run.Id && result.CalculationVersion == run.CalculationVersion))
            .ToArray();
        var resultIds = currentResults.Select(result => result.Id).ToArray();
        var payments = await db.PayrollPayments.AsNoTracking()
            .Include(payment => payment.Allocations)
            .Include(payment => payment.Evidence)
            .Where(payment => payment.BusinessId == businessId
                && payment.PayrollEmployeeResultId != null
                && Enumerable.Contains(resultIds, payment.PayrollEmployeeResultId.Value))
            .OrderBy(payment => payment.Id)
            .ToArrayAsync(cancellationToken);
        var paymentIds = payments.Select(payment => payment.Id).ToArray();
        var reconciliationHistory = paymentIds.Length == 0
            ? []
            : await db.PayrollPaymentReconciliations.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && Enumerable.Contains(paymentIds, row.PayrollPaymentId))
                .ToArrayAsync(cancellationToken);
        var latestReconciliations = reconciliationHistory
            .GroupBy(row => row.PayrollPaymentAllocationId)
            .ToDictionary(group => group.Key, group => group.MaxBy(row => row.Version)!);
        var paymentStateVersions = payments.Select(payment => new
        {
            payment.Id,
            payment.PayrollEmployeeResultId,
            payment.Version,
            payment.Status,
            payment.ResolutionStatus,
            payment.PaidAmount,
            payment.OutstandingAmount,
            Allocations = payment.Allocations.OrderBy(allocation => allocation.AllocationNo)
                .Select(allocation =>
                {
                    latestReconciliations.TryGetValue(allocation.Id, out var reconciliation);
                    return new
                    {
                        allocation.Id,
                        allocation.Amount,
                        allocation.SettledAmount,
                        ReconciliationId = reconciliation?.Id,
                        ReconciliationVersion = reconciliation?.Version,
                        ReconciliationStatus = reconciliation?.Status,
                        ReconciledAmount = reconciliation?.SettledAmount
                    };
                }).ToArray()
        }).ToArray();
        var paymentReconciliationComplete = currentResults
            .Where(result => result.NetPay.GetValueOrDefault() > 0m)
            .All(result =>
            {
                var resultPayments = payments.Where(payment =>
                        payment.PayrollEmployeeResultId == result.Id
                        && payment.ResolutionStatus
                            != PayrollPaymentResolutionStatuses.ResolvedReissue)
                    .ToArray();
                if (resultPayments.Length == 0
                    || resultPayments.Sum(payment => payment.PaidAmount)
                        != result.NetPay.GetValueOrDefault())
                    return false;
                return resultPayments.All(payment =>
                    payment.Status == PayrollPaymentStatuses.Paid
                    && payment.OutstandingAmount == 0m
                    && payment.Allocations.All(allocation =>
                    {
                        latestReconciliations.TryGetValue(allocation.Id, out var reconciliation);
                        return allocation.SettledAmount == allocation.Amount
                            && reconciliation?.Status
                                == PayrollPaymentReconciliationStatuses.Reconciled
                            && reconciliation.ExpectedAmount == allocation.Amount
                            && reconciliation.SettledAmount == allocation.SettledAmount;
                    }));
            });
        var settlementEvidenceIdentities = GetSettlementEvidenceIdentities(payments);
        var approved = runs.Length > 0
            && runs.All(run => run.Status is PayrollRunStatuses.Approved
                or PayrollRunStatuses.PaymentInProgress or PayrollRunStatuses.Paid
                or PayrollRunStatuses.Completed)
            && period.Status is PayrollPeriodStatuses.Approved or PayrollPeriodStatuses.Posted;
        var snapshotsComplete = runs.Length > 0 && runs.All(run =>
        {
            var runResults = currentResults.Where(result => result.PayrollRunId == run.Id).ToArray();
            var includedCount = selections.Count(selection => selection.PayrollRunId == run.Id);
            return run.CalculationVersion > 0
                && !string.IsNullOrWhiteSpace(run.CalculationInputHash)
                && !string.IsNullOrWhiteSpace(run.ApprovalEvidenceHash)
                && runResults.Length == includedCount
                && runResults.All(result => result.Status == PayrollEmployeeResultStatuses.Calculated
                    && result.NetPay.HasValue
                    && result.CalculationSnapshotJson is not null
                    && result.CalculationSnapshotHash is not null
                    && HashText(result.CalculationSnapshotJson) == result.CalculationSnapshotHash);
        });
        var hasPayableAmount = currentResults.Any(result => result.NetPay.GetValueOrDefault() > 0);
        var unresolvedFailedOrReturnedPayments = payments.Any(payment =>
            payment.ResolutionStatus == PayrollPaymentResolutionStatuses.PendingInvestigation
            && payment.Status is PayrollPaymentStatuses.Failed or PayrollPaymentStatuses.Returned);
        var heldPayments = payments.Any(payment =>
            payment.Status == PayrollPaymentStatuses.Held
            && payment.ResolutionStatus != PayrollPaymentResolutionStatuses.ResolvedReissue);
        var unpaidLiability = currentResults
            .Where(result => result.NetPay.GetValueOrDefault() > 0m)
            .Any(result =>
            {
                var resultPayments = payments.Where(payment =>
                        payment.PayrollEmployeeResultId == result.Id
                        && payment.Status != PayrollPaymentStatuses.Cancelled
                        && payment.ResolutionStatus
                            != PayrollPaymentResolutionStatuses.ResolvedReissue)
                    .ToArray();
                return resultPayments.Length == 0
                    || resultPayments.Sum(payment => payment.OutstandingAmount) > 0m
                    || resultPayments.Sum(payment => payment.PayableAmount)
                        != result.NetPay.GetValueOrDefault();
            });
        var paymentInProgress = runs.Any(run => run.Status == PayrollRunStatuses.PaymentInProgress);
        paymentInProgress |= payments.Any(payment =>
            payment.Status == PayrollPaymentStatuses.Processing);
        var hasUnresolvedCorrection = await db.PayrollRuns.AsNoTracking().AnyAsync(run =>
            run.BusinessId == businessId && run.PayrollPeriodId == id
            && run.RunType == PayrollRunTypes.Correction
            && run.Status != PayrollRunStatuses.Cancelled
            && run.Status != PayrollRunStatuses.Rejected,
            cancellationToken);
        var accountingSetting = await db.BusinessFeatureSettings.AsNoTracking()
            .Where(setting => setting.BusinessId == businessId && setting.FeatureKey == "accounting.enabled")
            .Select(setting => (bool?)setting.IsEnabled)
            .SingleOrDefaultAsync(cancellationToken);
        var runIds = runs.Select(run => run.Id).ToList();
        var accountingPostings = runIds.Count == 0
            ? []
            : await db.PayrollAccountingPostings.AsNoTracking()
                .Where(posting => posting.BusinessId == businessId && runIds.Contains(posting.PayrollRunId))
                .OrderBy(posting => posting.PayrollRunId)
                .ThenBy(posting => posting.RunRevision)
                .Select(posting => new
                {
                    posting.PayrollRunId,
                    posting.RunRevision,
                    posting.Status,
                    posting.AccountingJournalId,
                    posting.RequestPayloadHash,
                    posting.PostingType,
                    posting.PayrollPaymentEvidenceId,
                    posting.PayrollPaymentAllocationId
                })
                .ToArrayAsync(cancellationToken);
        var accountingPostingComplete = runs.Length > 0 && runs.All(run =>
            accountingPostings.Any(posting => posting.PayrollRunId == run.Id
                && posting.RunRevision == run.CalculationVersion
                && posting.PostingType == PayrollAccountingPostingTypes.PayrollExpense
                && posting.Status == PayrollAccountingPostingStatuses.Posted
                && posting.AccountingJournalId.HasValue
                && !string.IsNullOrWhiteSpace(posting.RequestPayloadHash)));
        var settlementPostingComplete = !hasPayableAmount
            || (paymentReconciliationComplete && settlementEvidenceIdentities.Count > 0
                && settlementEvidenceIdentities.All(identity => accountingPostings.Any(posting =>
                    posting.PostingType == PayrollAccountingPostingTypes.SalaryPayment
                    && posting.PayrollPaymentEvidenceId == identity.EvidenceId
                    && posting.PayrollPaymentAllocationId == identity.AllocationId
                    && posting.Status == PayrollAccountingPostingStatuses.Posted
                    && posting.AccountingJournalId.HasValue
                    && !string.IsNullOrWhiteSpace(posting.RequestPayloadHash))));
        var gates = new List<PayrollPeriodCloseGate>
        {
            Gate("approved_results", approved,
                "All required Payroll runs must be approved and the period must be Approved or Posted."),
            Gate("calculation_snapshots", snapshotsComplete,
                "Every run must have complete, frozen calculation snapshots matching its approved version."),
            Gate("no_pending_work", runs.Length > 0 && runs.All(run =>
                    run.Status is not (PayrollRunStatuses.Draft or PayrollRunStatuses.PendingApproval
                        or PayrollRunStatuses.Rejected or PayrollRunStatuses.Cancelled)),
                "Draft, pending, rejected, or cancelled runs block period close."),
            Gate("processing_payment", !paymentInProgress,
                "An in-progress or uncertain payment blocks period close."),
            Gate("failed_returned_payment_resolution", !unresolvedFailedOrReturnedPayments,
                "Failed or returned payments must have a recorded resolution before close."),
            Gate("held_payment_exception", !heldPayments,
                "Held payments remain outstanding and block close without an approved exception policy."),
            Gate("unpaid_partial_liability", !unpaidLiability,
                "Unpaid or partially paid payroll liability blocks close."),
            Gate("payment_reconciliation",
                !hasPayableAmount || paymentReconciliationComplete,
                "Required payment reconciliation evidence is unavailable or incomplete."),
            Gate("accounting_configuration", accountingSetting.HasValue,
                "Business Accounting integration state must be explicitly configured."),
            Gate("accounting_posting",
                accountingSetting == false || (accountingSetting == true && accountingPostingComplete),
                accountingSetting == true
                    ? "Every approved Payroll run revision must have a confirmed Accounting journal."
                    : "Accounting posting may be skipped only when the Business explicitly disables integration."),
            Gate("accounting_settlement_posting",
                accountingSetting == false || (accountingSetting == true && settlementPostingComplete),
                accountingSetting == true
                    ? "Every verified Payroll settlement evidence allocation must have a confirmed SalaryPayment journal."
                    : "Accounting settlement posting may be skipped only when the Business explicitly disables integration."),
            Gate("finalization", snapshotsComplete,
                "Required calculated Payroll result artifacts must be complete and frozen."),
            Gate("unresolved_correction", !hasUnresolvedCorrection,
                "A Payroll period with an unresolved correction cannot be closed.")
        };
        var exceptionRequests = request.Exceptions ?? [];
        var approvedPolicy = exceptionRequests.Count == 0 || exceptionPolicy is null
            ? null
            : await exceptionPolicy.GetApprovedPolicyAsync(businessId, cancellationToken);
        var exceptionPolicyValid = IsCloseExceptionPolicyValid(
            id, request.PolicyVersion, exceptionRequests, approvedPolicy, gates);
        foreach (var gate in gates.Where(gate => exceptionPolicyValid
                     && exceptionRequests.Any(exception => exception.GateCode == gate.Code)).ToArray())
        {
            var exception = exceptionRequests.Single(item => item.GateCode == gate.Code);
            var original = gates.Single(item => item.Code == gate.Code);
            var index = gates.IndexOf(original);
            gates[index] = gate with
            {
                Passed = true,
                Message = $"{gate.Message} Approved exception: {exception.ApprovalReference}."
            };
        }
        gates.Add(Gate("close_exceptions",
            exceptionRequests.Count == 0 || exceptionPolicyValid,
            "Close exceptions require a matching approved, versioned policy and approval reference."));
        var runRefs = runs.Select(run => new
        {
            run.Id,
            run.Version,
            run.Status,
            run.CalculationVersion,
            run.CalculationInputHash,
            run.ApprovalEvidenceHash
        }).ToArray();
        var runRefsJson = JsonSerializer.Serialize(runRefs, JsonOptions);
        var state = new
        {
            period.Id,
            period.BusinessId,
            period.Version,
            period.Status,
            runs = runRefs,
            snapshots = currentResults.Select(result => new
            {
                result.PayrollRunId, result.EmployeeId, result.CalculationVersion,
                result.CalculationSnapshotHash, result.NetPay
            }).ToArray(),
            paymentStateVersions,
            accountingSetting,
            accountingPostings,
            gates,
            request.PolicyVersion,
            request.Exceptions,
            exceptionPolicy = approvedPolicy is null ? null : new
            {
                approvedPolicy.Version,
                AllowedGateCodes = approvedPolicy.AllowedGateCodes.Order(StringComparer.Ordinal).ToArray(),
                approvedPolicy.RequiresApprovalReference
            }
        };
        var fingerprint = HashText(JsonSerializer.Serialize(state, JsonOptions));
        var canClose = gates.All(gate => !gate.Blocking || gate.Passed);
        return new ClosePlan(
            new PayrollPeriodCloseResponse(id, period.Status, period.Version, 0,
                request.PolicyVersion, gates, canClose),
            fingerprint,
            runRefsJson,
            JsonSerializer.Serialize(paymentStateVersions, JsonOptions),
            exceptionRequests.Count == 0
                ? "[]"
                : JsonSerializer.Serialize(new
                {
                    PolicyVersion = approvedPolicy?.Version,
                    AllowedGateCodes = approvedPolicy?.AllowedGateCodes
                        .Order(StringComparer.Ordinal).ToArray() ?? [],
                    Exceptions = exceptionRequests
                }, JsonOptions));
    }

    private static PayrollPeriodCloseGate Gate(string code, bool passed, string message) =>
        new(code, passed, Blocking: true, message);

    private static bool IsCloseExceptionPolicyValid(
        long periodId,
        int requestedPolicyVersion,
        IReadOnlyList<PayrollPeriodCloseExceptionRequest> exceptions,
        PayrollPeriodCloseExceptionPolicySnapshot? policy,
        IReadOnlyList<PayrollPeriodCloseGate> gates)
    {
        if (exceptions.Count == 0)
            return true;
        if (policy is null || policy.Version != requestedPolicyVersion || policy.Version <= 0)
            return false;

        var gateCodes = exceptions.Select(exception => exception.GateCode).ToArray();
        if (gateCodes.Distinct(StringComparer.Ordinal).Count() != gateCodes.Length)
            return false;

        foreach (var exception in exceptions)
        {
            if (string.IsNullOrWhiteSpace(exception.GateCode)
                || !ExceptionEligibleGateCodes.Contains(exception.GateCode, StringComparer.Ordinal)
                || !policy.AllowedGateCodes.Contains(exception.GateCode, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(exception.TargetReference)
                || !string.Equals(exception.TargetReference,
                    periodId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(exception.Reason)
                || policy.RequiresApprovalReference
                    && string.IsNullOrWhiteSpace(exception.ApprovalReference))
                return false;

            var gate = gates.SingleOrDefault(candidate => candidate.Code == exception.GateCode);
            if (gate is null || !gate.Blocking || gate.Passed)
                return false;
        }

        return true;
    }

    private static IReadOnlyList<(long EvidenceId, long AllocationId)> GetSettlementEvidenceIdentities(
        IEnumerable<PayrollPayment> payments)
    {
        var identities = new List<(long EvidenceId, long AllocationId)>();
        foreach (var payment in payments.Where(row =>
                     row.Status != PayrollPaymentStatuses.Cancelled
                     && row.ResolutionStatus != PayrollPaymentResolutionStatuses.ResolvedReissue))
        {
            foreach (var evidence in payment.Evidence.Where(row =>
                         row.Type == PayrollPaymentEvidenceTypes.Settlement))
            {
                using var document = JsonDocument.Parse(evidence.AllocationEvidenceJson);
                foreach (var line in document.RootElement.EnumerateArray())
                {
                    var allocationNo = line.GetProperty("AllocationNo").GetInt32();
                    var method = line.GetProperty("Method").GetString();
                    var amount = line.GetProperty("Amount").GetDecimal();
                    var allocation = payment.Allocations.SingleOrDefault(row =>
                        row.AllocationNo == allocationNo && row.Method == method);
                    if (allocation is null || amount <= 0m)
                        throw new InvalidDataException(
                            "Payroll settlement evidence references an invalid allocation.");
                    identities.Add((evidence.Id, allocation.Id));
                }
            }
        }
        return identities;
    }

    private static string HashCloseRequest(long periodId, ClosePayrollPeriodRequest request) =>
        HashText(JsonSerializer.Serialize(new
        {
            periodId,
            request.IdempotencyKey,
            request.Reason,
            request.ExpectedVersion,
            request.PolicyVersion,
            request.Exceptions
        }, JsonOptions));

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void EnsureSameHash(string expected, string actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
            throw new PayrollPeriodConflictException(
                "The Payroll period close idempotency key was already used for different content.");
    }

    private static PayrollPeriodCloseResponse MapAssessment(
        PayrollPeriodCloseAssessment row, bool isReplay)
    {
        var gates = JsonSerializer.Deserialize<IReadOnlyList<PayrollPeriodCloseGate>>(
            row.GateResultsJson, JsonOptions) ?? [];
        return new PayrollPeriodCloseResponse(row.PayrollPeriodId, row.StatusSnapshot,
            row.ResultPeriodVersion, row.Id, row.PolicyVersion, gates, row.WasClosed, isReplay);
    }

    private sealed record ClosePlan(
        PayrollPeriodCloseResponse Response,
        string Fingerprint,
        string RunRevisionRefsJson,
        string PaymentStateVersionsJson,
        string ExceptionEvidenceJson);
}
