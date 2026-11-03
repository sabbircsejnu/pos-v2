using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class MobileStockCountOperationStore(RetailPOSDbContext dbContext)
    : IMobileStockCountOperationStore
{
    private const decimal MaximumQuantity = 999_999_999_999_999.999m;
    private const int MaximumOperationsPageSize = 500;

    public async Task<MobileStockCountOperationsBatchResult> ApplyBatchAsync(
        ApplyMobileStockCountOperationsInput input,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var session = await dbContext.StockCounts
            .FromSqlInterpolated(
                $"SELECT * FROM stock_counts WHERE id = {input.StockCountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
            return Failure("STOCK_TAKE_NOT_FOUND", 0);
        if (session.BusinessId != input.BusinessId
            || session.LocationId != input.OutletId
            || session.LocationType != "outlet")
        {
            return Failure("STOCK_TAKE_NOT_FOUND", session.CountRevision);
        }
        var operationIds = input.Operations
            .Select(operation => operation.ClientOperationId)
            .ToList();
        var payloadHashes = input.Operations.ToDictionary(
            operation => operation.ClientOperationId,
            operation => ComputePayloadHash(input.DeviceSessionId, operation));
        var existing = await dbContext.StockCountOperationReceipts
            .Where(receipt =>
                receipt.StockCountId == session.Id
                && Enumerable.Contains(operationIds, receipt.ClientOperationId))
            .ToDictionaryAsync(receipt => receipt.ClientOperationId, cancellationToken);

        foreach (var operation in input.Operations)
        {
            if (existing.TryGetValue(operation.ClientOperationId, out var receipt)
                && !string.Equals(
                    receipt.PayloadHash,
                    payloadHashes[operation.ClientOperationId],
                    StringComparison.Ordinal))
            {
                return Failure("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH", session.CountRevision);
            }
        }

        var freshOperations = input.Operations
            .Where(operation => !existing.ContainsKey(operation.ClientOperationId))
            .ToArray();
        var duplicateReceipts = input.Operations
            .Where(operation => existing.ContainsKey(operation.ClientOperationId))
            .Select(operation => ToReadModel(existing[operation.ClientOperationId]) with
            {
                IsDuplicate = true
            })
            .ToList();

        if (freshOperations.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MobileStockCountOperationsBatchResult(
                session.CountRevision,
                null,
                duplicateReceipts);
        }

        if (!string.Equals(session.Status, StockCount.StatusDraft, StringComparison.Ordinal))
            return Failure("STOCK_TAKE_NOT_DRAFT", session.CountRevision);

        // The active submission job is the durable session lock established by API-05C.
        if (await dbContext.StockCountSubmissionJobs.AnyAsync(
                job => job.StockCountId == session.Id
                    && Enumerable.Contains(StockCountSubmissionJobStatus.Active, job.Status),
                cancellationToken))
        {
            return Failure("STOCK_TAKE_SUBMISSION_IN_PROGRESS", session.CountRevision);
        }

        var receivedAt = DateTime.UtcNow;
        var validationErrors = await ValidateFreshOperationsAsync(
            session,
            input,
            freshOperations,
            cancellationToken);
        var hasValidationError = validationErrors.Values.Any(code => code is not null);
        var staleRevision = input.ExpectedRevision != session.CountRevision;
        var canAccept = !hasValidationError && !staleRevision;

        var freshVariantIds = freshOperations
            .Select(operation => operation.ProductVariantId)
            .Distinct()
            .ToArray();
        var lines = canAccept
            ? await dbContext.StockCountLines
                .Where(line =>
                    line.StockCountId == session.Id
                    && Enumerable.Contains(freshVariantIds, line.VariantId))
                .ToDictionaryAsync(line => line.VariantId, cancellationToken)
            : new Dictionary<long, StockCountLine>();

        var currentRevision = session.CountRevision;
        var newReceipts = new List<StockCountOperationReceipt>(freshOperations.Length);
        foreach (var operation in freshOperations)
        {
            var rejectionCode = validationErrors.GetValueOrDefault(operation.ClientOperationId);
            if (rejectionCode is null && staleRevision)
                rejectionCode = "STALE_SESSION_REVISION";
            else if (rejectionCode is null && hasValidationError)
                rejectionCode = "BATCH_REJECTED";

            long? acceptedRevision = null;
            if (canAccept)
            {
                currentRevision++;
                acceptedRevision = currentRevision;
                var line = lines[operation.ProductVariantId];
                line.PhysicalStock = operation.CountedQuantity;
                line.Difference = operation.CountedQuantity - line.CurrentStock;
            }

            newReceipts.Add(new StockCountOperationReceipt
            {
                StockCountId = session.Id,
                ClientOperationId = operation.ClientOperationId,
                DeviceSessionId = input.DeviceSessionId,
                ClientSequence = operation.ClientSequence,
                ProductVariantId = operation.ProductVariantId,
                OperationType = "SetQuantity",
                CountedQuantity = operation.CountedQuantity,
                RawBarcode = operation.RawBarcode,
                PayloadHash = payloadHashes[operation.ClientOperationId],
                ActorUserId = input.ActorUserId,
                BusinessId = input.BusinessId,
                OutletId = input.OutletId,
                ClientObservedAtUtc = operation.ClientObservedAtUtc,
                ServerReceivedAtUtc = receivedAt,
                DecidedAtUtc = receivedAt,
                AcceptedRevision = acceptedRevision,
                SessionRevisionAtDecision = acceptedRevision ?? session.CountRevision,
                Outcome = rejectionCode is null
                    ? StockCountOperationOutcome.Accepted
                    : StockCountOperationOutcome.Rejected,
                RejectionCode = rejectionCode,
                CorrelationId = input.CorrelationId
            });
        }

        if (canAccept)
        {
            session.CountRevision = currentRevision;
            session.MovementReconciliationStatus =
                StockCount.MovementReconciliationUnresolved;
        }

        dbContext.StockCountOperationReceipts.AddRange(newReceipts);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var resultReceipts = input.Operations
            .Select(operation =>
            {
                var prior = duplicateReceipts.FirstOrDefault(
                    receipt => receipt.ClientOperationId == operation.ClientOperationId);
                return prior ?? ToReadModel(newReceipts.Single(
                    receipt => receipt.ClientOperationId == operation.ClientOperationId));
            })
            .ToArray();

        var errorCode = staleRevision
            ? "STALE_SESSION_REVISION"
            : hasValidationError
                ? "BATCH_REJECTED"
                : null;
        return new MobileStockCountOperationsBatchResult(
            session.CountRevision,
            errorCode,
            resultReceipts);
    }

    public async Task<MobileStockCountOperationsPageReadModel?> GetAfterRevisionAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        long afterRevision,
        CancellationToken cancellationToken)
    {
        var session = await AuthorizedSession(
            businessId,
            outletId,
            authorizedOutletIds,
            stockCountId)
            .Select(stockCount => new { stockCount.Id, stockCount.CountRevision })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
            return null;

        var receipts = await dbContext.StockCountOperationReceipts
            .AsNoTracking()
            .Where(receipt =>
                receipt.StockCountId == session.Id
                && receipt.AcceptedRevision.HasValue
                && receipt.AcceptedRevision.Value > afterRevision)
            .OrderBy(receipt => receipt.AcceptedRevision)
            .Take(MaximumOperationsPageSize + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = receipts.Length > MaximumOperationsPageSize;
        if (hasMore)
            receipts = receipts[..MaximumOperationsPageSize];
        var nextRevision = receipts.Length == 0
            ? afterRevision
            : receipts[^1].AcceptedRevision!.Value;

        return new MobileStockCountOperationsPageReadModel(
            session.CountRevision,
            nextRevision,
            hasMore,
            receipts.Select(ToReadModel).ToArray());
    }

    public async Task<MobileStockCountOperationReceiptReadModel?> FindReceiptAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        Guid clientOperationId,
        CancellationToken cancellationToken)
    {
        var receipt = await (
            from operation in dbContext.StockCountOperationReceipts.AsNoTracking()
            join session in AuthorizedSession(
                    businessId,
                    outletId,
                    authorizedOutletIds,
                    stockCountId)
                on operation.StockCountId equals session.Id
            where operation.ClientOperationId == clientOperationId
            select operation)
            .SingleOrDefaultAsync(cancellationToken);

        return receipt is null ? null : ToReadModel(receipt);
    }

    private static string ComputePayloadHash(
        string deviceSessionId,
        MobileStockCountOperationInput operation) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
                new CanonicalOperation(
                    deviceSessionId,
                    operation.ClientOperationId,
                    operation.ClientSequence,
                    operation.ProductVariantId,
                    "SetQuantity",
                    operation.CountedQuantity.ToString("G29", CultureInfo.InvariantCulture),
                    operation.RawBarcode,
                    operation.ClientObservedAtUtc)))));

    private async Task<Dictionary<Guid, string?>> ValidateFreshOperationsAsync(
        StockCount session,
        ApplyMobileStockCountOperationsInput input,
        IReadOnlyCollection<MobileStockCountOperationInput> operations,
        CancellationToken cancellationToken)
    {
        var errors = operations.ToDictionary(
            operation => operation.ClientOperationId,
            _ => (string?)null);

        var clientSequences = operations
            .Select(operation => operation.ClientSequence)
            .ToArray();
        var usedSequences = await dbContext.StockCountOperationReceipts
            .Where(receipt =>
                receipt.StockCountId == session.Id
                && receipt.DeviceSessionId == input.DeviceSessionId
                && Enumerable.Contains(clientSequences, receipt.ClientSequence))
            .Select(receipt => receipt.ClientSequence)
            .ToArrayAsync(cancellationToken);
        var usedSequenceSet = usedSequences.ToHashSet();

        var variantIds = operations
            .Select(operation => operation.ProductVariantId)
            .Distinct()
            .ToArray();
        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => Enumerable.Contains(variantIds, variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.Barcode,
                variant.ProductId,
                ProductStatus = variant.Product.Status,
                ProductBarcode = variant.Product.Barcode
            })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);
        var sessionVariantIds = await dbContext.StockCountLines
            .AsNoTracking()
            .Where(line =>
                line.StockCountId == session.Id
                && Enumerable.Contains(variantIds, line.VariantId))
            .Select(line => line.VariantId)
            .ToHashSetAsync(cancellationToken);

        foreach (var operation in operations)
        {
            if (operation.ClientSequence <= 0)
            {
                errors[operation.ClientOperationId] = "INVALID_CLIENT_SEQUENCE";
                continue;
            }

            if (operation.CountedQuantity < 0
                || operation.CountedQuantity > MaximumQuantity
                || decimal.Round(operation.CountedQuantity, 3) != operation.CountedQuantity
                || (operation.RawBarcode?.Length ?? 0) > 50)
            {
                errors[operation.ClientOperationId] = "INVALID_COUNTED_QUANTITY";
                continue;
            }

            if (usedSequenceSet.Contains(operation.ClientSequence))
            {
                errors[operation.ClientOperationId] = "CLIENT_SEQUENCE_ALREADY_USED";
                continue;
            }

            if (operation.ProductVariantId <= 0)
            {
                errors[operation.ClientOperationId] = "VARIANT_NOT_FOUND";
                continue;
            }

            if (!variants.TryGetValue(operation.ProductVariantId, out var variant))
            {
                errors[operation.ClientOperationId] = "VARIANT_NOT_FOUND";
                continue;
            }

            if (variant.ProductStatus != ProductStatus.Active)
            {
                errors[operation.ClientOperationId] = "PRODUCT_INACTIVE";
                continue;
            }

            if (!sessionVariantIds.Contains(operation.ProductVariantId))
            {
                errors[operation.ClientOperationId] = "VARIANT_NOT_IN_SESSION";
                continue;
            }

            if (operation.RawBarcode is not null)
            {
                var matchingVariantCount = await dbContext.ProductVariants
                    .AsNoTracking()
                    .CountAsync(candidate => candidate.Barcode == operation.RawBarcode, cancellationToken);
                var matchesProductBarcode = await dbContext.Products
                    .AsNoTracking()
                    .AnyAsync(product => product.Barcode == operation.RawBarcode, cancellationToken);
                if (!string.Equals(variant.Barcode, operation.RawBarcode, StringComparison.Ordinal)
                    || matchingVariantCount != 1
                    || matchesProductBarcode)
                {
                    errors[operation.ClientOperationId] = "BARCODE_NOT_UNAMBIGUOUS_VARIANT";
                }
            }
        }

        return errors;
    }

    private IQueryable<StockCount> AuthorizedSession(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId) =>
        dbContext.StockCounts
            .AsNoTracking()
            .Where(session =>
                session.Id == stockCountId
                && session.BusinessId == businessId
                && session.LocationId == outletId
                && session.LocationType == "outlet"
                && Enumerable.Contains(authorizedOutletIds, session.LocationId)
                && dbContext.Outlets.Any(outlet =>
                    outlet.Id == session.LocationId
                    && outlet.BusinessId == businessId));

    private static MobileStockCountOperationReceiptReadModel ToReadModel(
        StockCountOperationReceipt receipt) =>
        new(
            receipt.ClientOperationId,
            receipt.DeviceSessionId,
            receipt.ClientSequence,
            receipt.ProductVariantId,
            receipt.OperationType,
            receipt.CountedQuantity,
            receipt.RawBarcode,
            receipt.PayloadHash,
            receipt.ClientObservedAtUtc,
            receipt.ServerReceivedAtUtc,
            receipt.DecidedAtUtc,
            receipt.AcceptedRevision,
            receipt.SessionRevisionAtDecision,
            receipt.Outcome,
            receipt.RejectionCode);

    private static MobileStockCountOperationsBatchResult Failure(string errorCode, long revision) =>
        new(revision, errorCode, Array.Empty<MobileStockCountOperationReceiptReadModel>());

    private sealed record CanonicalOperation(
        string DeviceSessionId,
        Guid ClientOperationId,
        long ClientSequence,
        long ProductVariantId,
        string OperationType,
        string CountedQuantity,
        string? RawBarcode,
        DateTime? ClientObservedAtUtc);
}
