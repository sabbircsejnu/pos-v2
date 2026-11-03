using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPaymentStore(RetailPOSDbContext db) : IPayrollPaymentStore
{
    public async Task<long> GetLocationIdAsync(
        long businessId, long payrollEmployeeResultId, CancellationToken cancellationToken)
    {
        var result = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == payrollEmployeeResultId)
            .Select(row => new { row.LocationIdSnapshot })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Payroll result was not found.");
        return result.LocationIdSnapshot is > 0
            ? result.LocationIdSnapshot.Value
            : throw new PayrollPaymentConflictException(
                "The approved Payroll result has no employee location evidence.");
    }

    public async Task<PayrollPaymentResponse> CreateAsync(
        long businessId,
        long actorUserId,
        CreatePayrollPaymentRequest request,
        string idempotencyPayloadHash,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var replay = await FindReplayAsync(
            businessId, request.IdempotencyKey, idempotencyPayloadHash, cancellationToken);
        if (replay is not null)
        {
            var replayLocation = await GetLocationIdAsync(
                businessId, replay.PayrollEmployeeResultId
                    ?? throw new InvalidDataException("Payroll payment replay has no Payroll result."),
                cancellationToken);
            return Map(replay, isReplay: true) with
            {
                LocationIdSnapshot = replayLocation
            };
        }

        var result = await db.PayrollEmployeeResults.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.PayrollEmployeeResultId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Payroll result was not found.");
        var run = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == result.PayrollRunId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Payroll result was not found.");
        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == run.PayrollPeriodId,
                cancellationToken)
            ?? throw new PayrollPaymentConflictException(
                "The approved Payroll result has no period evidence.");

        if (run.Version != request.ExpectedRunVersion)
            throw new PayrollPaymentConflictException(
                "The Payroll run changed; refresh the run before recording payment.");
        if (run.Status != PayrollRunStatuses.Approved
            || period.Status != PayrollPeriodStatuses.Approved
            || run.DecidedAtUtc is null
            || run.CalculationVersion <= 0
            || result.CalculationVersion != run.CalculationVersion
            || result.Status != PayrollEmployeeResultStatuses.Calculated
            || result.CalculationSnapshotHash is null
            || result.NetPay is not > 0
            || string.IsNullOrWhiteSpace(run.CurrencyCode)
            || run.DecimalPrecision is not >= 0 and <= 6)
            throw new PayrollPaymentConflictException(
                "Only a current, calculated employee result from an approved Payroll run can be paid.");
        if (result.LocationIdSnapshot is not > 0)
            throw new PayrollPaymentConflictException(
                "The approved Payroll result has no employee location evidence.");

        var previous = await db.PayrollPayments.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId
            && row.PayrollEmployeeResultId == result.Id
            && row.AttemptNo == 1, cancellationToken);
        if (previous is not null)
            throw new PayrollPaymentConflictException(
                "A payment attempt already exists for this Payroll employee result.");

        var decimalPrecision = run.DecimalPrecision.GetValueOrDefault();
        var roundingMode = Enum.TryParse<MidpointRounding>(run.RoundingMode, out var parsedMode)
            ? parsedMode
            : throw new PayrollPaymentConflictException(
                "The Payroll run has an invalid frozen rounding mode.");
        var allocationTotal = 0m;
        for (var index = 0; index < request.Allocations.Count; index++)
        {
            var allocation = request.Allocations[index];
            if (decimal.Round(allocation.Amount, decimalPrecision, roundingMode)
                != allocation.Amount)
                throw new ArgumentException(
                    "Payment allocation amounts must use the Payroll run's frozen currency precision.");
            allocationTotal = checked(allocationTotal + allocation.Amount);
        }
        if (allocationTotal > result.NetPay.Value)
            throw new ArgumentException(
                "Payment allocations cannot exceed the approved Payroll payable amount.");

        var methods = request.Allocations.Select(row => row.Method)
            .Distinct(StringComparer.Ordinal).ToArray();
        var payment = new PayrollPayment
        {
            BusinessId = businessId,
            PayrollPeriodId = period.Id,
            PayrollRunId = run.Id,
            PayrollEmployeeResultId = result.Id,
            EmployeeId = result.EmployeeId,
            PayableAmount = result.NetPay.Value,
            PaidAmount = 0m,
            OutstandingAmount = result.NetPay.Value,
            CurrencyCode = run.CurrencyCode,
            Method = methods.Length == 1 ? methods[0] : PayrollPaymentMethods.Mixed,
            Status = PayrollPaymentStatuses.Pending,
            Version = 1,
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyPayloadHash = idempotencyPayloadHash,
            AttemptNo = 1,
            Notes = request.Notes,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var destinationCache = new Dictionary<string, PayrollEmployeePaymentDestination>(
            StringComparer.Ordinal);
        for (var index = 0; index < request.Allocations.Count; index++)
        {
            var input = request.Allocations[index];
            var allocation = new PayrollPaymentAllocation
            {
                BusinessId = businessId,
                AllocationNo = index + 1,
                Method = input.Method,
                Amount = input.Amount,
                SettledAmount = 0m,
                State = PayrollPaymentAllocationStates.Pending,
                ExternalReference = input.ExternalReference
            };
            if (input.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile)
            {
                if (!destinationCache.TryGetValue(input.Method, out var destination))
                {
                    destination = await GetEffectiveDestinationAsync(
                        businessId, result.EmployeeId, period.PeriodEnd,
                        input.Method, cancellationToken);
                    destinationCache.Add(input.Method, destination);
                }
                FreezeDestination(allocation, destination);
            }
            payment.Allocations.Add(allocation);
        }

        db.PayrollPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A payment attempt or idempotency identity conflicts with another request.", exception);
        }
        return Map(payment, isReplay: false) with
        {
            LocationIdSnapshot = result.LocationIdSnapshot
        };
    }

    public async Task<PayrollPaymentResponse> GetAsync(
        long businessId, long paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations)
            .Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == paymentId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Payroll payment was not found.");
        var locationId = payment.HistoricalLocationIdSnapshot;
        if (payment.PayrollEmployeeResultId is long resultId)
            locationId = await db.PayrollEmployeeResults.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id == resultId)
                .Select(row => row.LocationIdSnapshot)
                .SingleAsync(cancellationToken);
        return Map(payment, isReplay: false) with
        {
            LocationIdSnapshot = locationId
        };
    }

    public async Task<long> GetPaymentLocationIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.PayrollPayments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == paymentId)
            .Select(row => new { row.PayrollEmployeeResultId, row.HistoricalLocationIdSnapshot })
            .SingleOrDefaultAsync(cancellationToken);
        var locationId = payment is null ? null : payment.HistoricalLocationIdSnapshot;
        if (payment?.PayrollEmployeeResultId is long resultId)
            locationId = await db.PayrollEmployeeResults.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id == resultId)
                .Select(row => row.LocationIdSnapshot)
                .SingleOrDefaultAsync(cancellationToken);
        return locationId is > 0
            ? locationId.Value
            : throw new KeyNotFoundException("Payroll payment was not found.");
    }

    public async Task<PayrollPaymentResponse> ChangeStateAsync(
        long businessId,
        long actorUserId,
        long paymentId,
        long expectedVersion,
        string operation,
        string reason,
        string? externalReference,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var payment = await LoadPaymentAsync(businessId, paymentId, cancellationToken);
        if (payment.PayrollPeriodId is long periodId)
            await EnsurePeriodNotClosedAsync(businessId, periodId, cancellationToken);
        EnsureExpectedVersion(payment, expectedVersion);
        var now = DateTime.UtcNow;
        switch (operation)
        {
            case "Hold" when (payment.Status is PayrollPaymentStatuses.Pending
                or PayrollPaymentStatuses.Ready) && payment.PaidAmount == 0m:
                payment.Status = PayrollPaymentStatuses.Held;
                payment.HeldByUserId = actorUserId;
                payment.HeldAtUtc = now;
                payment.HoldReason = reason;
                break;
            case "Release" when payment.Status == PayrollPaymentStatuses.Held:
                payment.Status = PayrollPaymentStatuses.Ready;
                payment.ReleasedByUserId = actorUserId;
                payment.ReleasedAtUtc = now;
                payment.ReleaseReason = reason;
                break;
            case "Process" when payment.Status is PayrollPaymentStatuses.Pending
                or PayrollPaymentStatuses.Ready:
                payment.Status = PayrollPaymentStatuses.Processing;
                payment.ExternalReference = externalReference;
                break;
            case "Fail" when payment.Status == PayrollPaymentStatuses.Processing
                && payment.PaidAmount == 0m:
                payment.Status = PayrollPaymentStatuses.Failed;
                payment.FailureReturnReason = reason;
                payment.ExternalReference = externalReference;
                payment.ResolutionStatus = PayrollPaymentResolutionStatuses.PendingInvestigation;
                payment.ResolutionEvidenceReference = null;
                break;
            default:
                throw new PayrollPaymentConflictException(
                    "The Payroll payment cannot perform this action from its current state.");
        }

        payment.Version++;
        if (operation == "Fail")
            await RefreshBatchStatusAsync(payment, cancellationToken);
        await SavePaymentAsync(cancellationToken);
        return await MapWithLocationAsync(payment, isReplay: false, cancellationToken);
    }

    public async Task<PayrollPaymentResponse> CreateFinalSettlementAsync(
        long businessId,
        long actorUserId,
        long finalSettlementId,
        CreateFinalSettlementPaymentRequest request,
        string idempotencyPayloadHash,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var replay = await FindReplayAsync(
            businessId, request.IdempotencyKey, idempotencyPayloadHash, cancellationToken);
        if (replay is not null)
            return await MapWithLocationAsync(replay, isReplay: true, cancellationToken);

        var evidence = await LoadApprovedSettlementAsync(
            businessId, finalSettlementId, request.RevisionNo, cancellationToken);
        var allocationTotal = request.Allocations.Aggregate(0m, (total, allocation) =>
        {
            if (decimal.Round(allocation.Amount, evidence.Revision.DecimalPrecision,
                    ParseRoundingMode(evidence.Revision.RoundingMode)) != allocation.Amount)
                throw new ArgumentException(
                    "Payment allocations must use the approved settlement's frozen currency precision.");
            return checked(total + allocation.Amount);
        });
        if (allocationTotal <= 0m || allocationTotal > evidence.Revision.NetSettlement)
            throw new PayrollPaymentConflictException(
                "Final Settlement payments must be positive and cannot exceed the approved payable amount.");

        var existing = await db.PayrollPayments.Where(row =>
                row.BusinessId == businessId
                && row.FinalSettlementId == finalSettlementId
                && row.FinalSettlementRevisionNo == request.RevisionNo)
            .ToArrayAsync(cancellationToken);
        var paid = existing.Sum(row => row.PaidAmount);
        var reserved = existing.Where(row => row.Status != PayrollPaymentStatuses.Cancelled
                && !((row.Status is PayrollPaymentStatuses.Failed or PayrollPaymentStatuses.Returned)
                    && row.ResolutionStatus == PayrollPaymentResolutionStatuses.ResolvedReissue))
            .Sum(row => row.OutstandingAmount);
        if (allocationTotal > evidence.Revision.NetSettlement - paid - reserved)
            throw new PayrollPaymentConflictException(
                "Final Settlement payment allocations exceed the remaining approved liability.");

        var methods = request.Allocations.Select(row => row.Method)
            .Distinct(StringComparer.Ordinal).ToArray();
        var payment = new PayrollPayment
        {
            BusinessId = businessId,
            FinalSettlementId = finalSettlementId,
            FinalSettlementRevisionId = evidence.Revision.Id,
            FinalSettlementRevisionNo = evidence.Revision.RevisionNo,
            ApprovalInstanceId = evidence.Submission.ApprovalInstanceId,
            HistoricalLocationIdSnapshot = evidence.ExitCase.PrimaryLocationProjectionIdSnapshot,
            DecimalPrecisionSnapshot = evidence.Revision.DecimalPrecision,
            RoundingModeSnapshot = evidence.Revision.RoundingMode,
            FinalSettlementEvidenceHash = evidence.Submission.SubmittedEvidenceHash,
            EmployeeId = evidence.Header.EmployeeId,
            PayableAmount = allocationTotal,
            PaidAmount = 0m,
            OutstandingAmount = allocationTotal,
            CurrencyCode = evidence.Revision.CurrencyCode,
            Method = methods.Length == 1 ? methods[0] : PayrollPaymentMethods.Mixed,
            Status = PayrollPaymentStatuses.Pending,
            Version = 1,
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyPayloadHash = idempotencyPayloadHash,
            AttemptNo = (existing.Select(row => (int?)row.AttemptNo).Max() ?? 0) + 1,
            Notes = request.Notes,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        var destinationCache = new Dictionary<string, PayrollEmployeePaymentDestination>(
            StringComparer.Ordinal);
        for (var index = 0; index < request.Allocations.Count; index++)
        {
            var input = request.Allocations[index];
            var allocation = new PayrollPaymentAllocation
            {
                BusinessId = businessId,
                AllocationNo = index + 1,
                Method = input.Method,
                Amount = input.Amount,
                SettledAmount = 0m,
                State = PayrollPaymentAllocationStates.Pending,
                ExternalReference = input.ExternalReference
            };
            if (input.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile)
            {
                if (!destinationCache.TryGetValue(input.Method, out var destination))
                {
                    destination = await GetEffectiveDestinationAsync(
                        businessId, evidence.Header.EmployeeId, evidence.Revision.SettlementDate,
                        input.Method, cancellationToken);
                    destinationCache.Add(input.Method, destination);
                }
                FreezeDestination(allocation, destination);
            }
            payment.Allocations.Add(allocation);
        }

        db.PayrollPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A Final Settlement payment attempt or idempotency identity conflicts with another request.",
                exception);
        }
        return await MapWithLocationAsync(payment, isReplay: false, cancellationToken);
    }

    public async Task<HrExitScope?> GetFinalSettlementPaymentScopeAsync(
        long businessId, long paymentId, CancellationToken cancellationToken)
    {
        var settlementId = await GetFinalSettlementPaymentIdAsync(
            businessId, paymentId, cancellationToken);
        return settlementId is > 0
            ? await db.PayrollFinalSettlements.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id == settlementId.Value)
                .Join(db.HrExitCases.AsNoTracking(),
                    settlement => new { settlement.BusinessId, Id = settlement.ExitCaseId },
                    exit => new { exit.BusinessId, exit.Id },
                    (_, exit) => new HrExitScope(
                        exit.EmployeeId, exit.PrimaryLocationProjectionIdSnapshot,
                        exit.LocationTypeSnapshot))
                .SingleOrDefaultAsync(cancellationToken)
            : null;
    }

    public Task<HrExitScope?> GetFinalSettlementScopeAsync(
        long businessId, long finalSettlementId, CancellationToken cancellationToken) =>
        db.PayrollFinalSettlements.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == finalSettlementId)
            .Join(db.HrExitCases.AsNoTracking(),
                settlement => new { settlement.BusinessId, Id = settlement.ExitCaseId },
                exit => new { exit.BusinessId, exit.Id },
                (_, exit) => new HrExitScope(
                    exit.EmployeeId, exit.PrimaryLocationProjectionIdSnapshot,
                    exit.LocationTypeSnapshot))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollPaymentResponse>> GetFinalSettlementPaymentsAsync(
        long businessId, long finalSettlementId, int revisionNo,
        CancellationToken cancellationToken)
    {
        var rows = await db.PayrollPayments.AsNoTracking()
            .Include(row => row.Allocations)
            .Include(row => row.Evidence)
            .Where(row => row.BusinessId == businessId
                && row.FinalSettlementId == finalSettlementId
                && row.FinalSettlementRevisionNo == revisionNo)
            .OrderBy(row => row.AttemptNo)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var responses = new List<PayrollPaymentResponse>(rows.Length);
        foreach (var row in rows)
            responses.Add(await MapWithLocationAsync(row, isReplay: false, cancellationToken));
        return responses;
    }

    public Task<long?> GetFinalSettlementPaymentIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken) =>
        db.PayrollPayments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == paymentId)
            .Select(row => row.FinalSettlementId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PayrollPaymentResponse> RecordEvidenceAsync(
        long businessId,
        long actorUserId,
        long paymentId,
        PayrollPaymentEvidenceRequest request,
        string payloadHash,
        string evidenceType,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var priorEvidence = await db.PayrollPaymentEvidence.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId
                && row.PayrollPaymentId == paymentId
                && row.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (priorEvidence is not null)
        {
            if (!FixedHashEquals(priorEvidence.PayloadHash, payloadHash)
                || priorEvidence.Type != evidenceType)
                throw new PayrollPaymentConflictException(
                    "The evidence idempotency key was already used with a different payload.");
            var replayPayment = await LoadPaymentAsync(businessId, paymentId, cancellationToken);
            return await MapWithLocationAsync(replayPayment, isReplay: true, cancellationToken);
        }

        var payment = await LoadPaymentAsync(businessId, paymentId, cancellationToken);
        if (payment.PayrollPeriodId is long periodId)
            await EnsurePeriodNotClosedAsync(businessId, periodId, cancellationToken);
        EnsureExpectedVersion(payment, request.ExpectedVersion);
        var (precision, roundingMode) = await GetPrecisionAsync(
            payment, cancellationToken);
        var evidenceAllocations = request.Allocations.Select(input =>
        {
            var allocation = payment.Allocations.SingleOrDefault(row =>
                row.AllocationNo == input.AllocationNo)
                ?? throw new PayrollPaymentConflictException(
                    "The evidence references an allocation outside this payment.");
            if (decimal.Round(input.Amount, precision, roundingMode)
                != input.Amount)
                throw new ArgumentException(
                    "Evidence amounts must use the Payroll run's frozen currency precision.");
            var available = evidenceType == PayrollPaymentEvidenceTypes.Settlement
                ? allocation.Amount - allocation.SettledAmount
                : allocation.SettledAmount;
            if (input.Amount > available)
                throw new PayrollPaymentConflictException(
                    "Evidence amount exceeds the unsettled allocation balance.");
            return new
            {
                Allocation = allocation,
                input.Amount
            };
        }).ToArray();
        var amount = evidenceAllocations.Aggregate(
            0m, (total, item) => checked(total + item.Amount));
        if (amount <= 0m)
            throw new ArgumentException("Evidence amount must be positive.");

        if (evidenceType == PayrollPaymentEvidenceTypes.Settlement
            && payment.Status is not (PayrollPaymentStatuses.Processing
                or PayrollPaymentStatuses.PartiallyPaid))
            throw new PayrollPaymentConflictException(
                "Settlement evidence can only be recorded for a Processing or PartiallyPaid payment.");
        if (evidenceType == PayrollPaymentEvidenceTypes.Return
            && payment.Status is not (PayrollPaymentStatuses.PartiallyPaid
                or PayrollPaymentStatuses.Paid or PayrollPaymentStatuses.Returned))
            throw new PayrollPaymentConflictException(
                "Return evidence requires a previously settled payment.");
        if (evidenceType == PayrollPaymentEvidenceTypes.Return && amount > payment.PaidAmount)
            throw new PayrollPaymentConflictException(
                "Return evidence cannot exceed the currently settled payment amount.");

        var evidence = new PayrollPaymentEvidence
        {
            BusinessId = businessId,
            PayrollPaymentId = payment.Id,
            Type = evidenceType,
            Amount = amount,
            EvidenceDate = request.EvidenceDate,
            EvidenceReference = request.EvidenceReference,
            AllocationEvidenceJson = JsonSerializer.Serialize(evidenceAllocations.Select(item =>
                new { item.Allocation.AllocationNo, item.Allocation.Method, item.Amount })),
            IdempotencyKey = request.IdempotencyKey,
            PayloadHash = payloadHash,
            VerifiedByUserId = actorUserId,
            VerifiedAtUtc = DateTime.UtcNow
        };
        db.PayrollPaymentEvidence.Add(evidence);

        foreach (var item in evidenceAllocations)
        {
            item.Allocation.SettledAmount += evidenceType == PayrollPaymentEvidenceTypes.Settlement
                ? item.Amount
                : -item.Amount;
            item.Allocation.State = item.Allocation.SettledAmount switch
            {
                0m => PayrollPaymentAllocationStates.Pending,
                var settled when settled == item.Allocation.Amount =>
                    PayrollPaymentAllocationStates.Settled,
                _ => PayrollPaymentAllocationStates.PartiallySettled
            };
        }
        payment.PaidAmount = payment.Allocations.Sum(row => row.SettledAmount);
        payment.OutstandingAmount = payment.PayableAmount - payment.PaidAmount;
        if (evidenceType == PayrollPaymentEvidenceTypes.Settlement)
        {
            payment.Status = payment.OutstandingAmount == 0m
                ? PayrollPaymentStatuses.Paid
                : PayrollPaymentStatuses.PartiallyPaid;
            if (payment.Status == PayrollPaymentStatuses.Paid)
                payment.PaidAtUtc = DateTime.UtcNow;
        }
        else
        {
            payment.Status = PayrollPaymentStatuses.Returned;
            payment.FailureReturnReason = request.Reason;
            payment.ExternalReference = request.EvidenceReference;
            payment.ResolutionStatus = payment.PaidAmount == 0m
                ? PayrollPaymentResolutionStatuses.ResolvedReturn
                : PayrollPaymentResolutionStatuses.PendingInvestigation;
            payment.ResolutionEvidenceReference = request.EvidenceReference;
        }
        payment.Version++;
        if (payment.PaymentBatchId is not null)
            await RefreshBatchStatusAsync(payment, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A payment evidence reference or idempotency identity conflicts with another request.",
                exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPaymentConflictException(
                "The Payroll payment changed while evidence was being recorded.", exception);
        }
        return await MapWithLocationAsync(payment, isReplay: false, cancellationToken);
    }

    public async Task<PayrollPaymentResponse> RetryAsync(
        long businessId,
        long actorUserId,
        long paymentId,
        RetryPayrollPaymentRequest request,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var replay = await FindReplayAsync(
            businessId, request.IdempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
            return await MapWithLocationAsync(replay, isReplay: true, cancellationToken);

        var original = await LoadPaymentAsync(businessId, paymentId, cancellationToken);
        EnsureExpectedVersion(original, request.ExpectedVersion);
        if (original.Status is not (PayrollPaymentStatuses.Failed or PayrollPaymentStatuses.Returned)
            || original.PaidAmount != 0m
            || original.OutstandingAmount <= 0m)
            throw new PayrollPaymentConflictException(
                "Only a failed or fully returned payment with an outstanding balance can be retried.");
        if (original.Status == PayrollPaymentStatuses.Failed
            && original.ResolutionStatus != PayrollPaymentResolutionStatuses.PendingInvestigation)
            throw new PayrollPaymentConflictException(
                "This failed payment has already been resolved or reissued.");
        if (original.Status == PayrollPaymentStatuses.Returned
            && original.ResolutionStatus != PayrollPaymentResolutionStatuses.ResolvedReturn)
            throw new PayrollPaymentConflictException(
                "Returned payment funds must be fully evidenced before retry.");

        var (precision, roundingMode) = await GetPrecisionAsync(original, cancellationToken);
        var allocationTotal = request.Allocations.Aggregate(0m, (total, allocation) =>
        {
            if (decimal.Round(allocation.Amount, precision, roundingMode)
                != allocation.Amount)
                throw new ArgumentException(
                    "Retry allocation amounts must use the source's frozen currency precision.");
            return checked(total + allocation.Amount);
        });
        if (allocationTotal > original.OutstandingAmount)
            throw new ArgumentException(
                "Retry allocations cannot exceed the outstanding payment balance.");

        DateOnly effectiveOn;
        if (original.FinalSettlementId is long settlementId
            && original.FinalSettlementRevisionNo is int revisionNo)
        {
            var source = await LoadApprovedSettlementAsync(
                businessId, settlementId, revisionNo, cancellationToken);
            if (source.Revision.Id != original.FinalSettlementRevisionId
                || !FixedHashEquals(source.Submission.SubmittedEvidenceHash,
                    original.FinalSettlementEvidenceHash ?? ""))
                throw new PayrollPaymentConflictException(
                    "The payment does not match the approved Final Settlement evidence.");
            effectiveOn = source.Revision.SettlementDate;
        }
        else
        {
            var period = await db.PayrollPeriods.AsNoTracking()
                .SingleOrDefaultAsync(row => row.BusinessId == businessId
                    && row.Id == original.PayrollPeriodId, cancellationToken)
                ?? throw new PayrollPaymentConflictException(
                    "The Payroll payment has no period evidence.");
            if (period.Status == PayrollPeriodStatuses.Closed)
                throw new PayrollPaymentConflictException(
                    "Payments for a closed Payroll period cannot be retried.");
            effectiveOn = period.PeriodEnd;
        }
        var latestAttempt = await db.PayrollPayments.Where(row =>
                row.BusinessId == businessId
                && (original.FinalSettlementId.HasValue
                    ? row.FinalSettlementId == original.FinalSettlementId
                        && row.FinalSettlementRevisionNo == original.FinalSettlementRevisionNo
                    : row.PayrollEmployeeResultId == original.PayrollEmployeeResultId))
            .MaxAsync(row => (int?)row.AttemptNo, cancellationToken) ?? 0;
        var payment = new PayrollPayment
        {
            BusinessId = businessId,
            PayrollPeriodId = original.PayrollPeriodId,
            PayrollRunId = original.PayrollRunId,
            PayrollEmployeeResultId = original.PayrollEmployeeResultId,
            FinalSettlementId = original.FinalSettlementId,
            FinalSettlementRevisionId = original.FinalSettlementRevisionId,
            FinalSettlementRevisionNo = original.FinalSettlementRevisionNo,
            ApprovalInstanceId = original.ApprovalInstanceId,
            HistoricalLocationIdSnapshot = original.HistoricalLocationIdSnapshot,
            DecimalPrecisionSnapshot = original.DecimalPrecisionSnapshot,
            RoundingModeSnapshot = original.RoundingModeSnapshot,
            FinalSettlementEvidenceHash = original.FinalSettlementEvidenceHash,
            EmployeeId = original.EmployeeId,
            PayableAmount = original.OutstandingAmount,
            PaidAmount = 0m,
            OutstandingAmount = original.OutstandingAmount,
            CurrencyCode = original.CurrencyCode,
            Method = request.Allocations.Select(row => row.Method)
                .Distinct(StringComparer.Ordinal).Count() == 1
                    ? request.Allocations[0].Method
                    : PayrollPaymentMethods.Mixed,
            Status = PayrollPaymentStatuses.Ready,
            Version = 1,
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyPayloadHash = payloadHash,
            OriginalPaymentId = original.Id,
            AttemptNo = latestAttempt + 1,
            Notes = request.Notes,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        var destinationCache = new Dictionary<string, PayrollEmployeePaymentDestination>(
            StringComparer.Ordinal);
        for (var index = 0; index < request.Allocations.Count; index++)
        {
            var input = request.Allocations[index];
            var allocation = new PayrollPaymentAllocation
            {
                BusinessId = businessId,
                AllocationNo = index + 1,
                Method = input.Method,
                Amount = input.Amount,
                State = PayrollPaymentAllocationStates.Pending,
                ExternalReference = input.ExternalReference
            };
            if (input.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile)
            {
                if (!destinationCache.TryGetValue(input.Method, out var destination))
                {
                    destination = await GetEffectiveDestinationAsync(
                        businessId, original.EmployeeId, effectiveOn,
                        input.Method, cancellationToken);
                    destinationCache.Add(input.Method, destination);
                }
                FreezeDestination(allocation, destination);
            }
            payment.Allocations.Add(allocation);
        }

        original.ResolutionStatus = PayrollPaymentResolutionStatuses.ResolvedReissue;
        original.ResolutionEvidenceReference = request.ResolutionEvidenceReference;
        original.Version++;
        db.PayrollPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A payment attempt or idempotency identity conflicts with another request.", exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPaymentConflictException(
                "The Payroll payment changed while the retry was being recorded.", exception);
        }
        return await MapWithLocationAsync(payment, isReplay: false, cancellationToken);
    }

    private async Task<PayrollPayment?> FindReplayAsync(
        long businessId,
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await db.PayrollPayments.Include(row => row.Allocations)
            .Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (prior is null)
            return null;
        if (!FixedHashEquals(prior.IdempotencyPayloadHash, payloadHash))
            throw new PayrollPaymentConflictException(
                "The idempotency key was already used with a different payment payload.");
        return prior;
    }

    private async Task<PayrollPayment> LoadPaymentAsync(
        long businessId, long paymentId, CancellationToken cancellationToken) =>
        await db.PayrollPayments
            .Include(row => row.Allocations)
            .Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == paymentId,
                cancellationToken)
        ?? throw new KeyNotFoundException("Payroll payment was not found.");

    private async Task<PayrollPaymentResponse> MapWithLocationAsync(
        PayrollPayment payment, bool isReplay, CancellationToken cancellationToken)
    {
        var locationId = payment.HistoricalLocationIdSnapshot;
        if (payment.PayrollEmployeeResultId is long resultId)
            locationId = await db.PayrollEmployeeResults.AsNoTracking()
                .Where(row => row.BusinessId == payment.BusinessId && row.Id == resultId)
                .Select(row => row.LocationIdSnapshot)
                .SingleOrDefaultAsync(cancellationToken);
        return Map(payment, isReplay) with { LocationIdSnapshot = locationId };
    }

    private async Task<(int Precision, MidpointRounding RoundingMode)> GetPrecisionAsync(
        PayrollPayment payment, CancellationToken cancellationToken)
    {
        if (payment.FinalSettlementRevisionId is not null)
        {
            if (payment.DecimalPrecisionSnapshot is not int precision
                || payment.RoundingModeSnapshot is null)
                throw new PayrollPaymentConflictException(
                    "The Final Settlement payment has no frozen currency precision evidence.");
            return (precision, ParseRoundingMode(payment.RoundingModeSnapshot));
        }

        if (payment.PayrollRunId is not long runId)
            throw new PayrollPaymentConflictException(
                "The payment has no source precision evidence.");
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(
            row => row.BusinessId == payment.BusinessId && row.Id == runId,
            cancellationToken)
            ?? throw new PayrollPaymentConflictException(
                "The Payroll payment has no run precision evidence.");
        return (run.DecimalPrecision.GetValueOrDefault(), ParseRoundingMode(run.RoundingMode));
    }

    private async Task<ApprovedSettlementEvidence> LoadApprovedSettlementAsync(
        long businessId,
        long finalSettlementId,
        int revisionNo,
        CancellationToken cancellationToken)
    {
        var evidence = await (
            from settlement in db.PayrollFinalSettlements.AsNoTracking()
            join revision in db.PayrollFinalSettlementRevisions.AsNoTracking()
                on new { settlement.BusinessId, FinalSettlementId = settlement.Id }
                equals new { revision.BusinessId, revision.FinalSettlementId }
            join submission in db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
                on new { revision.BusinessId, RevisionId = revision.Id }
                equals new { submission.BusinessId, submission.RevisionId }
            join exitCase in db.HrExitCases.AsNoTracking()
                on new { settlement.BusinessId, Id = settlement.ExitCaseId }
                equals new { exitCase.BusinessId, exitCase.Id }
            join instance in db.ApprovalInstances.AsNoTracking()
                on new { submission.BusinessId, Id = submission.ApprovalInstanceId!.Value }
                equals new { instance.BusinessId, instance.Id }
            where settlement.BusinessId == businessId
                && settlement.Id == finalSettlementId
                && settlement.CurrentRevisionNo == revisionNo
                && revision.RevisionNo == revisionNo
                && submission.ApprovalInstanceId != null
                && instance.ProcessType == ApprovalProcessType.FinalSettlement
                && instance.Status == ApprovalInstanceStatus.Approved
            select new ApprovedSettlementEvidence(settlement, revision, submission, exitCase))
            .SingleOrDefaultAsync(cancellationToken);
        return evidence
            ?? throw new PayrollPaymentConflictException(
                "The Final Settlement revision is not currently approved.");
    }

    private static MidpointRounding ParseRoundingMode(string value) =>
        Enum.TryParse<MidpointRounding>(value, out var roundingMode)
            ? roundingMode
            : throw new PayrollPaymentConflictException(
                "The source has an invalid frozen rounding mode.");

    private static void EnsureExpectedVersion(PayrollPayment payment, long expectedVersion)
    {
        if (payment.Version != expectedVersion)
            throw new PayrollPaymentConflictException(
                "The Payroll payment changed; refresh it before retrying this action.");
    }

    private async Task SavePaymentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPaymentConflictException(
                "The Payroll payment changed while the action was being recorded.", exception);
        }
    }

    private async Task RefreshBatchStatusAsync(
        PayrollPayment payment, CancellationToken cancellationToken)
    {
        if (payment.PaymentBatchId is not long batchId)
            return;
        var batch = await db.PayrollPaymentBatches
            .Include(row => row.Payments)
            .SingleOrDefaultAsync(row => row.BusinessId == payment.BusinessId
                && row.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException(
                "A Payroll payment references a missing payment batch.");
        if (batch.Status != PayrollPaymentBatchStatuses.Processing)
            throw new PayrollPaymentConflictException(
                "The payment batch is no longer processing.");

        if (batch.Payments.All(row => row.Status == PayrollPaymentStatuses.Paid))
            batch.Status = PayrollPaymentBatchStatuses.Completed;
        else if (batch.Payments.All(row => row.Status is PayrollPaymentStatuses.Paid
                     or PayrollPaymentStatuses.Failed
                     or PayrollPaymentStatuses.Returned
                     or PayrollPaymentStatuses.Cancelled))
            batch.Status = batch.Payments.Any(row => row.Status == PayrollPaymentStatuses.Paid)
                ? PayrollPaymentBatchStatuses.PartiallyFailed
                : PayrollPaymentBatchStatuses.Failed;
        batch.Version++;
    }

    private async Task<PayrollEmployeePaymentDestination> GetEffectiveDestinationAsync(
        long businessId,
        long employeeId,
        DateOnly effectiveOn,
        string method,
        CancellationToken cancellationToken)
    {
        var destination = await db.PayrollEmployeePaymentDestinations.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.EmployeeId == employeeId
                && row.EffectiveFrom <= effectiveOn)
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
        if (destination is null || !destination.IsActive || !destination.IsPrimary
            || destination.Method != method)
            throw new PayrollPaymentConflictException(
                $"No active {method} payment destination is effective for the Payroll period.");
        return destination;
    }

    private static void FreezeDestination(
        PayrollPaymentAllocation allocation,
        PayrollEmployeePaymentDestination destination)
    {
        allocation.PaymentDestinationId = destination.Id;
        allocation.PaymentDestinationVersionNo = destination.VersionNo;
        allocation.BankNameSnapshot = destination.BankName;
        allocation.AccountNameSnapshot = destination.AccountName;
        allocation.AccountNumberSnapshot = destination.AccountNumber;
        allocation.MobileProviderSnapshot = destination.MobileProvider;
        allocation.MobileAccountNameSnapshot = destination.MobileAccountName;
        allocation.MobileAccountNumberSnapshot = destination.MobileAccountNumber;
    }

    private static PayrollPaymentResponse Map(PayrollPayment payment, bool isReplay) =>
        new(payment.Id, payment.BusinessId, payment.PayrollPeriodId, payment.PayrollRunId,
            payment.PayrollEmployeeResultId, payment.EmployeeId, null, payment.PayableAmount,
            payment.PaidAmount, payment.OutstandingAmount, payment.CurrencyCode, payment.Method,
            payment.Status, payment.Version, payment.AttemptNo, payment.OriginalPaymentId,
            payment.IdempotencyKey,
            payment.CreatedByUserId, payment.CreatedAtUtc, payment.Notes,
            payment.Allocations.OrderBy(row => row.AllocationNo)
                .Select(row => new PayrollPaymentAllocationResponse(
                    row.Id, row.AllocationNo, row.Method, row.Amount, row.SettledAmount,
                    row.State, row.ExternalReference, row.PaymentDestinationId,
                    row.PaymentDestinationVersionNo, row.BankNameSnapshot, row.AccountNameSnapshot,
                    row.AccountNumberSnapshot, row.MobileProviderSnapshot,
                    row.MobileAccountNameSnapshot, row.MobileAccountNumberSnapshot))
                .ToArray(), payment.PaidAtUtc, payment.ExternalReference,
            payment.FailureReturnReason, payment.HeldByUserId, payment.HeldAtUtc,
            payment.HoldReason, payment.ReleasedByUserId, payment.ReleasedAtUtc,
            payment.ReleaseReason, payment.ResolutionStatus, payment.ResolutionEvidenceReference,
            payment.Evidence.OrderBy(row => row.EvidenceDate).ThenBy(row => row.Id)
                .Select(row => new PayrollPaymentEvidenceResponse(
                    row.Id, row.Type, row.Amount, row.EvidenceDate, row.EvidenceReference,
                    row.VerifiedByUserId, row.VerifiedAtUtc,
                    ReadEvidenceAllocations(row.AllocationEvidenceJson)))
                .ToArray(), isReplay, payment.PaymentBatchId, payment.FinalSettlementId,
            payment.FinalSettlementRevisionNo, payment.ApprovalInstanceId);

    private static IReadOnlyList<PayrollPaymentEvidenceAllocationResponse> ReadEvidenceAllocations(
        string allocationEvidenceJson)
    {
        using var document = JsonDocument.Parse(allocationEvidenceJson);
        return document.RootElement.EnumerateArray()
            .Select(row => new PayrollPaymentEvidenceAllocationResponse(
                row.GetProperty("AllocationNo").GetInt32(),
                row.GetProperty("Method").GetString()
                    ?? throw new InvalidDataException("Payment evidence allocation method is missing."),
                row.GetProperty("Amount").GetDecimal()))
            .ToArray();
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})",
                cancellationToken);
    }

    private async Task EnsurePeriodNotClosedAsync(
        long businessId, long periodId, CancellationToken cancellationToken)
    {
        var status = await db.PayrollPeriods.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == periodId)
            .Select(row => row.Status)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PayrollPaymentConflictException(
                "The Payroll payment has no period evidence.");
        if (status == PayrollPeriodStatuses.Closed)
            throw new PayrollPaymentConflictException(
                "Payments for a closed Payroll period cannot be changed.");
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment writes require the audited transaction boundary.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    private static bool FixedHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private sealed record ApprovedSettlementEvidence(
        PayrollFinalSettlement Header,
        PayrollFinalSettlementRevision Revision,
        PayrollFinalSettlementApprovalSubmission Submission,
        HrExitCase ExitCase);
}

public sealed class PayrollPaymentConflictException : Exception
{
    public PayrollPaymentConflictException(string message) : base(message) { }
    public PayrollPaymentConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
