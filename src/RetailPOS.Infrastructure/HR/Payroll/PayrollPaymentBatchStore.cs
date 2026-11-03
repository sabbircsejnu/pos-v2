using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPaymentBatchStore(RetailPOSDbContext db)
    : IPayrollPaymentBatchStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PayrollPaymentBatchSelectionScope>> GetPaymentScopesAsync(
        long businessId, IReadOnlyList<long> paymentIds, CancellationToken cancellationToken)
    {
        var paymentIdArray = paymentIds.ToArray();
        var payments = await db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.BusinessId == businessId
                && Enumerable.Contains(paymentIdArray, payment.Id))
            .Select(payment => new
            {
                payment.Id,
                payment.Method,
                payment.HistoricalLocationIdSnapshot,
                payment.PayrollEmployeeResultId
            })
            .ToArrayAsync(cancellationToken);
        var resultIds = payments.Where(payment => payment.PayrollEmployeeResultId != null)
            .Select(payment => payment.PayrollEmployeeResultId!.Value).Distinct().ToArray();
        var resultLocations = resultIds.Length == 0
            ? new Dictionary<long, long?>()
            : await db.PayrollEmployeeResults.AsNoTracking()
                .Where(result => result.BusinessId == businessId
                    && Enumerable.Contains(resultIds, result.Id))
                .ToDictionaryAsync(result => result.Id, result => result.LocationIdSnapshot,
                    cancellationToken);
        return payments.Select(payment => new PayrollPaymentBatchSelectionScope(
                payment.Id,
                payment.HistoricalLocationIdSnapshot
                    ?? (payment.PayrollEmployeeResultId is long resultId
                        ? resultLocations.GetValueOrDefault(resultId) ?? 0
                        : 0),
                payment.Method))
            .ToArray();
    }

    public async Task<IReadOnlyList<long>> GetBatchLocationIdsAsync(
        long businessId, long batchId, CancellationToken cancellationToken)
    {
        var exists = await db.PayrollPaymentBatches.AsNoTracking()
            .AnyAsync(batch => batch.BusinessId == businessId && batch.Id == batchId,
                cancellationToken);
        if (!exists)
            throw new KeyNotFoundException("Payroll payment batch was not found.");
        var payments = await db.PayrollPayments.AsNoTracking()
            .Where(payment => payment.BusinessId == businessId
                && payment.PaymentBatchId == batchId)
            .Select(payment => new
            {
                payment.HistoricalLocationIdSnapshot,
                payment.PayrollEmployeeResultId
            })
            .ToArrayAsync(cancellationToken);
        var resultIds = payments.Where(payment => payment.PayrollEmployeeResultId != null)
            .Select(payment => payment.PayrollEmployeeResultId!.Value).Distinct().ToArray();
        var resultLocations = resultIds.Length == 0
            ? new Dictionary<long, long?>()
            : await db.PayrollEmployeeResults.AsNoTracking()
                .Where(result => result.BusinessId == businessId
                    && Enumerable.Contains(resultIds, result.Id))
                .ToDictionaryAsync(result => result.Id, result => result.LocationIdSnapshot,
                    cancellationToken);
        return payments.Select(payment => payment.HistoricalLocationIdSnapshot
                ?? (payment.PayrollEmployeeResultId is long resultId
                    ? resultLocations.GetValueOrDefault(resultId) ?? 0
                    : 0))
            .Where(locationId => locationId > 0)
            .Distinct()
            .ToArray();
    }

    public async Task<IReadOnlyList<PayrollPaymentBatchResponse>> ListAsync(
        long businessId, long? payrollRunId, CancellationToken cancellationToken)
    {
        var query = db.PayrollPaymentBatches.AsNoTracking()
            .Where(batch => batch.BusinessId == businessId);
        if (payrollRunId is > 0)
            query = query.Where(batch => batch.PayrollRunId == payrollRunId.Value);
        var batches = await query
            .Include(batch => batch.Payments)
            .Include(batch => batch.BankFileVersions)
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .ThenByDescending(batch => batch.Id)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        return batches.Select(batch => Map(batch, isReplay: false)).ToArray();
    }

    public async Task<PayrollPaymentBatchResponse> GetAsync(
        long businessId, long batchId, CancellationToken cancellationToken)
    {
        var batch = await LoadBatchAsync(businessId, batchId, cancellationToken);
        return Map(batch, isReplay: false);
    }

    public async Task<PayrollPaymentBatchResponse> CreateAsync(
        long businessId, long actorUserId, long payrollRunId,
        CreatePayrollPaymentBatchRequest request, string payloadHash,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var paymentIds = request.Payments.Select(item => item.PaymentId).ToHashSet();
        var replay = await db.PayrollPaymentBatches
            .Include(batch => batch.Payments)
            .Include(batch => batch.BankFileVersions)
            .SingleOrDefaultAsync(batch => batch.BusinessId == businessId
                && batch.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (!FixedHashEquals(replay.IdempotencyPayloadHash, payloadHash))
                throw new PayrollPaymentConflictException(
                    "The payment-batch idempotency key was already used with a different payload.");
            return Map(replay, isReplay: true);
        }

        var run = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == payrollRunId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll run was not found.");
        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == run.PayrollPeriodId,
                cancellationToken)
            ?? throw new PayrollPaymentConflictException(
                "The Payroll run has no period evidence.");
        if (run.Status != PayrollRunStatuses.Approved
            || period.Status != PayrollPeriodStatuses.Approved
            || run.DecidedAtUtc is null
            || string.IsNullOrWhiteSpace(run.CurrencyCode))
            throw new PayrollPaymentConflictException(
                "Payment batches can only be created from a current approved Payroll run.");

        var payments = await db.PayrollPayments
            .Include(payment => payment.Allocations)
            .Where(payment => payment.BusinessId == businessId
                && paymentIds.Contains(payment.Id))
            .ToArrayAsync(cancellationToken);
        if (payments.Length != request.Payments.Count)
            throw new KeyNotFoundException("A selected Payroll payment was not found.");

        var expectedVersions = request.Payments.ToDictionary(item => item.PaymentId);
        foreach (var payment in payments)
        {
            if (payment.Version != expectedVersions[payment.Id].ExpectedVersion)
                throw new PayrollPaymentConflictException(
                    "A selected payment changed; refresh the batch before creating it.");
            if (payment.PayrollRunId != payrollRunId
                || payment.PayrollPeriodId != period.Id
                || payment.CurrencyCode != run.CurrencyCode)
                throw new PayrollPaymentConflictException(
                    "All batched payments must belong to the specified approved Payroll run and currency.");
            if (payment.PaymentBatchId is not null
                || payment.Status is not (PayrollPaymentStatuses.Pending or PayrollPaymentStatuses.Ready)
                || payment.PaidAmount != 0m
                || payment.OutstandingAmount != payment.PayableAmount)
                throw new PayrollPaymentConflictException(
                    "Only unbatched, wholly outstanding payments can be added to a payment batch.");
            if (payment.Allocations.Count == 0
                || payment.Allocations.Any(allocation =>
                    allocation.Method is PayrollPaymentMethods.Bank or PayrollPaymentMethods.Mobile
                    && (allocation.PaymentDestinationId is null
                        || string.IsNullOrWhiteSpace(allocation.AccountNameSnapshot)
                            && string.IsNullOrWhiteSpace(allocation.MobileAccountNameSnapshot))))
                throw new PayrollPaymentConflictException(
                    "A payment destination snapshot is incomplete.");
        }

        var methodSet = payments.Select(payment => payment.Method)
            .Distinct(StringComparer.Ordinal).ToArray();
        var batch = new PayrollPaymentBatch
        {
            BusinessId = businessId,
            PayrollRunId = run.Id,
            PayrollPeriodId = period.Id,
            BatchReference = request.BatchReference,
            Method = methodSet.Length == 1 ? methodSet[0] : PayrollPaymentMethods.Mixed,
            CurrencyCode = run.CurrencyCode,
            ExpectedAmount = payments.Aggregate(
                0m, (total, payment) => checked(total + payment.OutstandingAmount)),
            Status = PayrollPaymentBatchStatuses.Ready,
            Version = 1,
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyPayloadHash = payloadHash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        foreach (var payment in payments)
        {
            payment.PaymentBatch = batch;
            payment.Status = PayrollPaymentStatuses.Ready;
            payment.Version++;
        }
        db.PayrollPaymentBatches.Add(batch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "The batch reference or idempotency identity conflicts with another request.",
                exception);
        }
        return Map(batch, isReplay: false);
    }

    public async Task<PayrollPaymentBatchResponse> SubmitAsync(
        long businessId, long actorUserId, long batchId,
        SubmitPayrollPaymentBatchRequest request, string payloadHash,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var batch = await LoadBatchAsync(businessId, batchId, cancellationToken);
        if (batch.SubmissionIdempotencyKey is not null)
        {
            if (batch.SubmissionIdempotencyKey != request.IdempotencyKey
                || !FixedHashEquals(batch.SubmissionPayloadHash!, payloadHash))
                throw new PayrollPaymentConflictException(
                    "The payment batch has already been submitted with a different request.");
            return Map(batch, isReplay: true);
        }

        if (batch.Version != request.ExpectedVersion
            || batch.Status != PayrollPaymentBatchStatuses.Ready)
            throw new PayrollPaymentConflictException(
                "The payment batch changed; refresh it before submission.");
        if (batch.Payments.Count == 0
            || batch.Payments.Any(payment =>
                payment.Status != PayrollPaymentStatuses.Ready
                || payment.PaidAmount != 0m
                || payment.OutstandingAmount != payment.PayableAmount))
            throw new PayrollPaymentConflictException(
                "Only a ready batch with wholly outstanding payments can be submitted.");

        var hasExternalAllocations = batch.Payments
            .SelectMany(payment => payment.Allocations)
            .Any(allocation => allocation.Method is PayrollPaymentMethods.Bank
                or PayrollPaymentMethods.Mobile);
        if (hasExternalAllocations && batch.BankFileVersions.Count == 0)
            throw new PayrollPaymentConflictException(
                "Generate a bank file before submitting a batch with bank or mobile allocations.");

        var now = DateTime.UtcNow;
        batch.Status = PayrollPaymentBatchStatuses.Processing;
        batch.SubmittedAtUtc = now;
        batch.SubmittedByUserId = actorUserId;
        batch.SubmissionExternalReference = request.ExternalReference;
        batch.SubmissionReason = request.Reason;
        batch.SubmissionIdempotencyKey = request.IdempotencyKey;
        batch.SubmissionPayloadHash = payloadHash;
        batch.Version++;
        foreach (var payment in batch.Payments)
        {
            payment.Status = PayrollPaymentStatuses.Processing;
            payment.Version++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A concurrent payment-batch submission conflicts with this request.", exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollPaymentConflictException(
                "The payment batch changed during submission; refresh and retry.", exception);
        }
        return Map(batch, isReplay: false);
    }

    public async Task<PayrollPaymentBankFileDraft> PrepareBankFileAsync(
        long businessId, long batchId, PayrollPaymentBankFileRequest request,
        string normalizedReason, CancellationToken cancellationToken)
    {
        var batch = await LoadBatchAsync(businessId, batchId, cancellationToken);
        if (batch.Status != PayrollPaymentBatchStatuses.Ready)
            throw new PayrollPaymentConflictException(
                "A bank file can only be generated for a ready payment batch.");
        var bankAllocations = GetBankAllocations(batch);
        if (bankAllocations.Count == 0)
            throw new PayrollPaymentConflictException(
                "The payment batch contains no bank allocations.");
        var content = BuildCsv(batch, bankAllocations);
        var contentHash = Hash(content);
        var previousVersion = await db.PayrollPaymentBankFileVersions.AsNoTracking()
            .SingleOrDefaultAsync(version => version.BusinessId == businessId
                && version.PaymentBatchId == batchId
                && version.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        var versionNo = previousVersion?.VersionNo ?? batch.BankFileVersionNo + 1;
        if (previousVersion is not null && previousVersion.ContentHash != contentHash)
            throw new PayrollPaymentConflictException(
                "The bank-file idempotency key resolves to different frozen payment data.");

        var fileName = $"payroll-bank-{batch.BatchReference}-v{versionNo}.csv";
        var payloadHash = Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            businessId,
            batchId,
            request.ExpectedVersion,
            request.IdempotencyKey,
            normalizedReason,
            contentHash
        }, JsonOptions));
        return new PayrollPaymentBankFileDraft(
            businessId,
            batchId,
            request.ExpectedVersion,
            request.IdempotencyKey,
            payloadHash,
            contentHash,
            content,
            fileName,
            "text/csv; charset=utf-8",
            versionNo,
            bankAllocations.Count,
            bankAllocations.Sum(row => row.Amount),
            normalizedReason);
    }

    public async Task<PayrollPaymentBankFileResult> PersistBankFileAsync(
        long businessId, long actorUserId, PayrollPaymentBankFileDraft draft,
        CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var previous = await db.PayrollPaymentBankFileVersions.SingleOrDefaultAsync(version =>
                version.BusinessId == businessId
                && version.PaymentBatchId == draft.PaymentBatchId
                && version.IdempotencyKey == draft.IdempotencyKey,
                cancellationToken);
        if (previous is not null)
        {
            if (!FixedHashEquals(previous.PayloadHash, draft.PayloadHash)
                || !FixedHashEquals(previous.ContentHash, draft.ContentHash))
                throw new PayrollPaymentConflictException(
                    "The bank-file idempotency key was already used with a different payload.");
            return new PayrollPaymentBankFileResult(
                draft.Content,
                previous.FileName,
                previous.ContentType,
                previous.ContentHash,
                previous.VersionNo,
                previous.RowCount,
                previous.ExpectedAmount,
                IsReplay: true);
        }

        var batch = await LoadBatchAsync(
            businessId, draft.PaymentBatchId, cancellationToken);
        if (batch.Version != draft.ExpectedBatchVersion
            || batch.Status != PayrollPaymentBatchStatuses.Ready)
            throw new PayrollPaymentConflictException(
                "The payment batch changed; refresh it before generating a bank file.");
        var bankAllocations = GetBankAllocations(batch);
        var currentContent = BuildCsv(batch, bankAllocations);
        var currentHash = Hash(currentContent);
        if (!FixedHashEquals(currentHash, draft.ContentHash)
            || !currentContent.AsSpan().SequenceEqual(draft.Content))
            throw new PayrollPaymentConflictException(
                "Frozen payment destination data changed during bank-file generation.");

        var version = new PayrollPaymentBankFileVersion
        {
            BusinessId = businessId,
            PaymentBatchId = batch.Id,
            VersionNo = draft.VersionNo,
            IdempotencyKey = draft.IdempotencyKey,
            PayloadHash = draft.PayloadHash,
            ContentHash = draft.ContentHash,
            FileName = draft.FileName,
            ContentType = draft.ContentType,
            RowCount = draft.RowCount,
            ExpectedAmount = draft.ExpectedAmount,
            GeneratedByUserId = actorUserId,
            GeneratedAtUtc = DateTime.UtcNow,
            GenerationReason = draft.Reason
        };
        batch.BankFileVersionNo = version.VersionNo;
        batch.GeneratedByUserId = actorUserId;
        batch.GeneratedAtUtc = version.GeneratedAtUtc;
        batch.Version++;
        db.PayrollPaymentBankFileVersions.Add(version);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollPaymentConflictException(
                "A concurrent bank-file version conflicts with this request.", exception);
        }
        return new PayrollPaymentBankFileResult(
            draft.Content,
            draft.FileName,
            draft.ContentType,
            draft.ContentHash,
            draft.VersionNo,
            draft.RowCount,
            draft.ExpectedAmount,
            IsReplay: false);
    }

    private async Task<PayrollPaymentBatch> LoadBatchAsync(
        long businessId, long batchId, CancellationToken cancellationToken) =>
        await db.PayrollPaymentBatches
            .Include(batch => batch.Payments)
                .ThenInclude(payment => payment.Allocations)
            .Include(batch => batch.BankFileVersions)
            .SingleOrDefaultAsync(batch => batch.BusinessId == businessId
                && batch.Id == batchId, cancellationToken)
        ?? throw new KeyNotFoundException("Payroll payment batch was not found.");

    private static List<BankAllocationRow> GetBankAllocations(PayrollPaymentBatch batch) =>
        batch.Payments
            .OrderBy(payment => payment.Id)
            .SelectMany(payment => payment.Allocations
                .Where(allocation => allocation.Method == PayrollPaymentMethods.Bank)
                .OrderBy(allocation => allocation.AllocationNo)
                .Select(allocation => new BankAllocationRow(
                    payment.Id,
                    allocation.AllocationNo,
                    allocation.AccountNameSnapshot
                        ?? throw new PayrollPaymentConflictException(
                            "A bank account name snapshot is required for bank-file generation."),
                    allocation.AccountNumberSnapshot
                        ?? throw new PayrollPaymentConflictException(
                            "A bank account number snapshot is required for bank-file generation."),
                    allocation.BankNameSnapshot
                        ?? throw new PayrollPaymentConflictException(
                            "A bank name snapshot is required for bank-file generation."),
                    allocation.Amount,
                    payment.CurrencyCode,
                    allocation.ExternalReference)))
            .ToList();

    private static byte[] BuildCsv(
        PayrollPaymentBatch batch, IReadOnlyList<BankAllocationRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PaymentReference,AccountName,AccountNumber,BankName,Amount,CurrencyCode,AllocationReference");
        foreach (var row in rows)
        {
            AppendCsv(builder, $"{batch.BatchReference}-{row.PaymentId.ToString(CultureInfo.InvariantCulture)}");
            builder.Append(',');
            AppendCsv(builder, row.AccountName);
            builder.Append(',');
            AppendCsv(builder, row.AccountNumber);
            builder.Append(',');
            AppendCsv(builder, row.BankName);
            builder.Append(',');
            AppendCsv(builder, row.Amount.ToString("0.################", CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsv(builder, row.CurrencyCode);
            builder.Append(',');
            AppendCsv(builder, row.ExternalReference ?? $"A{row.AllocationNo.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine();
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
    }

    private static void AppendCsv(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    private static PayrollPaymentBatchResponse Map(
        PayrollPaymentBatch batch, bool isReplay) =>
        new(
            batch.Id,
            batch.BusinessId,
            batch.PayrollRunId,
            batch.PayrollPeriodId,
            batch.BatchReference,
            batch.Method,
            batch.CurrencyCode,
            batch.ExpectedAmount,
            batch.Status,
            batch.Version,
            batch.CreatedByUserId,
            batch.CreatedAtUtc,
            batch.Payments.OrderBy(payment => payment.Id)
                .Select(payment => new PayrollPaymentBatchItemResponse(
                    payment.Id,
                    payment.EmployeeId,
                    payment.PayableAmount,
                    payment.Status,
                    payment.Version,
                    payment.Method))
                .ToArray(),
            batch.BankFileVersions.OrderBy(version => version.VersionNo)
                .Select(version => new PayrollPaymentBankFileVersionResponse(
                    version.VersionNo,
                    version.ContentHash,
                    version.RowCount,
                    version.ExpectedAmount,
                    version.FileName,
                    version.GeneratedByUserId,
                    version.GeneratedAtUtc,
                    version.GenerationReason))
                .ToArray(),
            isReplay,
            batch.SubmittedAtUtc,
            batch.SubmittedByUserId);

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment batch writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 13)})",
                cancellationToken);
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Payroll payment batch writes require the audited transaction boundary.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException?.Message.Contains(
            "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static bool FixedHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private sealed record BankAllocationRow(
        long PaymentId,
        int AllocationNo,
        string AccountName,
        string AccountNumber,
        string BankName,
        decimal Amount,
        string CurrencyCode,
        string? ExternalReference);
}
