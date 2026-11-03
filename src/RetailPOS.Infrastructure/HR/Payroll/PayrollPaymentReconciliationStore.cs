using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPaymentReconciliationStore(RetailPOSDbContext db)
    : IPayrollPaymentReconciliationStore
{
    public async Task<long> GetPaymentLocationIdAsync(
        long businessId, long paymentId, CancellationToken cancellationToken)
    {
        var locationId = await db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.BusinessId == businessId && payment.Id == paymentId)
            .Select(payment => payment.HistoricalLocationIdSnapshot
                ?? (payment.PayrollEmployeeResultId.HasValue
                    ? db.PayrollEmployeeResults.Where(result =>
                            result.BusinessId == businessId
                            && result.Id == payment.PayrollEmployeeResultId.Value)
                        .Select(result => result.LocationIdSnapshot).SingleOrDefault()
                    : null))
            .SingleOrDefaultAsync(cancellationToken);
        return locationId is > 0
            ? locationId.Value
            : throw new KeyNotFoundException("Payroll payment was not found.");
    }

    public async Task<IReadOnlyList<long>> GetBatchLocationIdsAsync(
        long businessId, long batchId, CancellationToken cancellationToken)
    {
        var exists = await db.PayrollPaymentBatches.AsNoTracking()
            .AnyAsync(batch => batch.BusinessId == businessId && batch.Id == batchId,
                cancellationToken);
        if (!exists)
            throw new KeyNotFoundException("Payroll payment batch was not found.");
        var locations = await db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.BusinessId == businessId
                && payment.PaymentBatchId == batchId)
            .Select(payment => payment.HistoricalLocationIdSnapshot
                ?? (payment.PayrollEmployeeResultId.HasValue
                    ? db.PayrollEmployeeResults.Where(result =>
                            result.BusinessId == businessId
                            && result.Id == payment.PayrollEmployeeResultId.Value)
                        .Select(result => result.LocationIdSnapshot).SingleOrDefault()
                    : null))
            .Select(locationId => locationId ?? 0)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (locations.Any(locationId => locationId <= 0))
            throw new PayrollPaymentConflictException(
                "The payment batch contains an employee without location evidence.");
        return locations;
    }

    public async Task<PayrollPaymentReconciliationOperationResponse> ReconcileAsync(
        long businessId,
        long actorUserId,
        long? batchId,
        long? expectedBatchVersion,
        string idempotencyKey,
        string payloadHash,
        IReadOnlyList<PayrollPaymentReconciliationTargetInput> items,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var prior = await db.PayrollPaymentReconciliations.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.IdempotencyKey == idempotencyKey)
            .OrderBy(row => row.PayrollPaymentId)
            .ThenBy(row => row.PayrollPaymentAllocationId)
            .ToArrayAsync(cancellationToken);
        if (prior.Length > 0)
        {
            if (prior.Any(row => !FixedHashEquals(row.PayloadHash, payloadHash))
                || prior.Length != items.Count)
                throw new PayrollPaymentConflictException(
                    "The reconciliation idempotency key was already used with different content.");
            return new PayrollPaymentReconciliationOperationResponse(
                await MapAsync(prior, cancellationToken), IsReplay: true);
        }

        PayrollPaymentBatch? batch = null;
        if (batchId is not null)
        {
            batch = await db.PayrollPaymentBatches.SingleOrDefaultAsync(row =>
                    row.BusinessId == businessId && row.Id == batchId.Value,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Payroll payment batch was not found.");
            if (expectedBatchVersion is null || batch.Version != expectedBatchVersion.Value)
                throw new PayrollPaymentConflictException(
                    "The payment batch changed; refresh it before reconciliation.");
        }

        var paymentIds = items.Select(item => item.PaymentId).Distinct().ToArray();
        var payments = await db.PayrollPayments
            .Include(payment => payment.Allocations)
            .Where(payment => payment.BusinessId == businessId
                && Enumerable.Contains(paymentIds, payment.Id))
            .ToArrayAsync(cancellationToken);
        if (payments.Length != paymentIds.Length)
            throw new KeyNotFoundException("A Payroll payment was not found.");
        var periodIds = payments.Where(payment => payment.PayrollPeriodId != null)
            .Select(payment => payment.PayrollPeriodId!.Value).Distinct().ToArray();
        if (periodIds.Length != 0 && await db.PayrollPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId
                && Enumerable.Contains(periodIds, period.Id)
                && period.Status == PayrollPeriodStatuses.Closed,
                cancellationToken))
            throw new PayrollPaymentConflictException(
                "Payments for a closed Payroll period cannot be reconciled.");
        var paymentById = payments.ToDictionary(payment => payment.Id);
        if (batch is not null && payments.Any(payment => payment.PaymentBatchId != batch.Id))
            throw new PayrollPaymentConflictException(
                "Every reconciled payment must belong to the specified batch.");

        var latest = await GetLatestForPaymentsAsync(businessId, paymentIds, cancellationToken);
        var latestByAllocation = latest.ToDictionary(row => row.PayrollPaymentAllocationId);
        var now = DateTime.UtcNow;
        var additions = new List<PayrollPaymentReconciliation>(items.Count);
        foreach (var item in items)
        {
            var payment = paymentById[item.PaymentId];
            if (payment.Version != item.ExpectedPaymentVersion)
                throw new PayrollPaymentConflictException(
                    "A payment changed; refresh it before reconciliation.");
            var allocation = payment.Allocations.SingleOrDefault(row =>
                row.AllocationNo == item.AllocationNo)
                ?? throw new PayrollPaymentConflictException(
                    "A reconciliation references an allocation outside the payment.");
            if (item.EvidenceSource != allocation.Method)
                throw new ArgumentException(
                    "The evidence source must match the payment allocation method.");
            var (precision, roundingMode) = await GetPrecisionAsync(payment, cancellationToken);
            if (decimal.Round(item.SettledAmount, precision, roundingMode) != item.SettledAmount)
                throw new ArgumentException(
                    "Reconciled amounts must use the payment source's frozen currency precision.");
            if (item.SettledAmount > allocation.SettledAmount)
                throw new PayrollPaymentConflictException(
                    "Reconciled settlement cannot exceed verified payment settlement evidence.");

            latestByAllocation.TryGetValue(allocation.Id, out var previous);
            var currentVersion = previous?.Version ?? 0;
            if (currentVersion != item.ExpectedReconciliationVersion)
                throw new PayrollPaymentConflictException(
                    "The allocation reconciliation changed; refresh it before retrying.");

            var requiresException = item.IsException
                || item.SettledAmount != allocation.SettledAmount
                || payment.Status is PayrollPaymentStatuses.Held
                    or PayrollPaymentStatuses.Failed
                    or PayrollPaymentStatuses.Returned
                    or PayrollPaymentStatuses.Cancelled
                || payment.ResolutionStatus
                    == PayrollPaymentResolutionStatuses.PendingInvestigation;
            if (requiresException && string.IsNullOrWhiteSpace(item.Notes))
                throw new ArgumentException(
                    "A difference or payment exception requires a reconciliation reason.");

            var status = requiresException
                ? PayrollPaymentReconciliationStatuses.Exception
                : item.SettledAmount == allocation.Amount
                    ? PayrollPaymentReconciliationStatuses.Reconciled
                    : item.SettledAmount > 0m
                        ? PayrollPaymentReconciliationStatuses.PartiallyReconciled
                        : PayrollPaymentReconciliationStatuses.Unreconciled;
            additions.Add(new PayrollPaymentReconciliation
            {
                BusinessId = businessId,
                PayrollPaymentId = payment.Id,
                PayrollPaymentAllocationId = allocation.Id,
                PaymentBatchId = payment.PaymentBatchId,
                Version = checked(currentVersion + 1),
                PaymentVersion = payment.Version,
                ExpectedAmount = allocation.Amount,
                SettledAmount = item.SettledAmount,
                Difference = allocation.Amount - item.SettledAmount,
                Status = status,
                SettlementDate = item.SettlementDate,
                EvidenceSource = item.EvidenceSource,
                ExternalReference = item.ExternalReference,
                Notes = item.Notes,
                IdempotencyKey = idempotencyKey,
                PayloadHash = payloadHash,
                ReconciledByUserId = actorUserId,
                ReconciledAtUtc = now
            });
        }

        db.PayrollPaymentReconciliations.AddRange(additions);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A reconciliation idempotency key, external reference, or version conflicts with existing evidence.",
                exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPaymentConflictException(
                "A reconciliation changed while the evidence was being recorded.", exception);
        }

        return new PayrollPaymentReconciliationOperationResponse(
            await MapAsync(additions, cancellationToken));
    }

    public async Task<IReadOnlyList<PayrollPaymentReconciliationResponse>> ListLatestAsync(
        long businessId,
        long? paymentId,
        long? batchId,
        CancellationToken cancellationToken)
    {
        var query = db.PayrollPaymentReconciliations.AsNoTracking()
            .Where(row => row.BusinessId == businessId);
        if (paymentId is > 0)
            query = query.Where(row => row.PayrollPaymentId == paymentId.Value);
        if (batchId is > 0)
            query = query.Where(row => row.PaymentBatchId == batchId.Value);
        var latestVersions = query
            .GroupBy(row => row.PayrollPaymentAllocationId)
            .Select(group => new
            {
                AllocationId = group.Key,
                Version = group.Max(row => row.Version)
            });
        var rows = await query.Join(latestVersions,
                row => new { AllocationId = row.PayrollPaymentAllocationId, row.Version },
                latest => new { latest.AllocationId, latest.Version },
                (row, _) => row)
            .OrderByDescending(row => row.ReconciledAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        return await MapAsync(rows, cancellationToken);
    }

    private async Task<IReadOnlyList<PayrollPaymentReconciliation>> GetLatestForPaymentsAsync(
        long businessId, IReadOnlyList<long> paymentIds, CancellationToken cancellationToken)
    {
        var paymentIdArray = paymentIds.ToArray();
        var query = db.PayrollPaymentReconciliations
            .Where(row => row.BusinessId == businessId
                && Enumerable.Contains(paymentIdArray, row.PayrollPaymentId));
        var latestVersions = query
            .GroupBy(row => row.PayrollPaymentAllocationId)
            .Select(group => new
            {
                AllocationId = group.Key,
                Version = group.Max(row => row.Version)
            });
        return await query.Join(latestVersions,
                row => new { AllocationId = row.PayrollPaymentAllocationId, row.Version },
                latest => new { latest.AllocationId, latest.Version },
                (row, _) => row)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<PayrollPaymentReconciliationResponse>> MapAsync(
        IReadOnlyList<PayrollPaymentReconciliation> rows,
        CancellationToken cancellationToken)
    {
        var paymentIds = rows.Select(row => row.PayrollPaymentId).Distinct().ToArray();
        var payments = await db.PayrollPayments.AsNoTracking()
            .Where(payment => Enumerable.Contains(paymentIds, payment.Id))
            .ToDictionaryAsync(payment => payment.Id, cancellationToken);
        var allocationIds = rows.Select(row => row.PayrollPaymentAllocationId).Distinct().ToArray();
        var allocationNumbers = await db.PayrollPaymentAllocations.AsNoTracking()
            .Where(allocation => Enumerable.Contains(allocationIds, allocation.Id))
            .ToDictionaryAsync(allocation => allocation.Id,
                allocation => allocation.AllocationNo, cancellationToken);
        return rows.Select(row =>
        {
            var payment = payments[row.PayrollPaymentId];
            return new PayrollPaymentReconciliationResponse(
                row.Id, row.BusinessId, row.PayrollPaymentId, row.PaymentBatchId,
                payment.EmployeeId, payment.PayrollPeriodId,
                allocationNumbers[row.PayrollPaymentAllocationId],
                row.ExpectedAmount, row.SettledAmount, row.Difference, row.Status,
                row.SettlementDate, row.EvidenceSource, row.ExternalReference, row.Notes,
                row.Version, row.PaymentVersion, row.ReconciledByUserId,
                new DateTimeOffset(DateTime.SpecifyKind(row.ReconciledAtUtc, DateTimeKind.Utc)),
                false);
        }).ToArray();
    }

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment reconciliation writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})",
                cancellationToken);
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

    private static MidpointRounding ParseRoundingMode(string value) =>
        Enum.TryParse<MidpointRounding>(value, out var roundingMode)
            ? roundingMode
            : throw new PayrollPaymentConflictException(
                "The payment source has an invalid frozen rounding mode.");

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment reconciliation writes require the audited transaction boundary.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    private static bool FixedHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
