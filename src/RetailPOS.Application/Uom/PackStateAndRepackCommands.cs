using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

public sealed record PackStateReadModel(
    long VariantId,
    long LocationId,
    string LocationType,
    string BaseUnitCode,
    decimal OnHand,
    decimal AllocatedTotal,
    bool IsReconciled,
    IReadOnlyList<PackStateBalanceRow> Balances);

public sealed record GetPackStateQuery(long VariantId, long LocationId, string LocationType) : IQuery<PackStateReadModel>;

public sealed class GetPackStateQueryHandler(IVariantUomReader reader, IPackStateStore store, IUomOperationScope scope)
    : IRequestHandler<GetPackStateQuery, PackStateReadModel>
{
    public async Task<PackStateReadModel> Handle(GetPackStateQuery request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var locationType = PackStateRules.LocationType(request.LocationType);
        await scope.AuthorizeReadLocationAsync(request.LocationId, locationType, cancellationToken);
        var configs = await reader.GetCurrentAsync(businessId, [request.VariantId], cancellationToken);
        var config = configs.GetValueOrDefault(request.VariantId);
        if (config is null || !config.PackStateTrackingEnabled)
            throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, "This item does not track sealed and loose stock.");

        var rows = await store.ReadBalancesAsync(businessId, request.VariantId, request.LocationId, locationType, cancellationToken);
        var onHand = await store.GetOnHandAsync(request.VariantId, request.LocationId, locationType, cancellationToken);
        var allocated = rows.Sum(row => row.BaseQuantity);
        return new PackStateReadModel(request.VariantId, request.LocationId, locationType, config.BaseUnit.Code, onHand, allocated, allocated == onHand, rows);
    }
}

public sealed record OpenSealedPackCommand(
    long VariantId,
    long LocationId,
    string LocationType,
    string PackCode,
    decimal BaseQtyPerPack,
    int Count,
    string IdempotencyKey) : ICommand<PackStateReadModel>;

/// <summary>
/// Sealed packs become loose stock. <c>Inventory.Quantity</c>, the ledger and the journal are untouched, so the item's
/// quantity and value do not change (STATE-004). No empty container is created (PACK-006).
/// </summary>
public sealed class OpenSealedPackCommandHandler(
    IVariantUomReader reader,
    IPackStateStore store,
    IPackStateLedger ledger,
    IUomOperationScope scope,
    IUomAuditWriter audit,
    ISender sender) : IRequestHandler<OpenSealedPackCommand, PackStateReadModel>
{
    public async Task<PackStateReadModel> Handle(OpenSealedPackCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        var locationType = PackStateRules.LocationType(request.LocationType);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "An idempotency key is required.");
        if (request.Count <= 0)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Choose at least one pack to open.");
        await scope.AuthorizeWriteLocationAsync(request.LocationId, locationType, cancellationToken);

        var configs = await reader.GetCurrentAsync(businessId, [request.VariantId], cancellationToken);
        if (!configs.TryGetValue(request.VariantId, out var config) || !config.PackStateTrackingEnabled)
            throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, "This item does not track sealed and loose stock.");

        if (!await store.IdempotencyKeyUsedAsync(businessId, request.IdempotencyKey, cancellationToken))
        {
            await using var transaction = await store.BeginAsync(cancellationToken);
            try
            {
                var baseQuantity = request.Count * request.BaseQtyPerPack;
                await ledger.ApplyAsync(
                    new PackStateChange(businessId, request.VariantId, request.LocationId, locationType,
                        PackStates.Sealed, request.PackCode, request.BaseQtyPerPack, -request.Count, -baseQuantity, 0m,
                        PackStateOperations.Open, "pack_open", request.VariantId, actor, request.IdempotencyKey),
                    cancellationToken);
                await ledger.ApplyAsync(
                    new PackStateChange(businessId, request.VariantId, request.LocationId, locationType,
                        PackStates.Loose, string.Empty, 0m, 0, baseQuantity, 0m,
                        PackStateOperations.Open, "pack_open", request.VariantId, actor, request.IdempotencyKey),
                    cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                await ledger.EnsureReconciledAsync(
                    businessId, request.VariantId, request.LocationId, locationType,
                    await store.GetOnHandAsync(request.VariantId, request.LocationId, locationType, cancellationToken),
                    cancellationToken);
                await audit.RecordAsync($"Opened {request.Count} sealed {request.PackCode} of variant {request.VariantId}", "PackStateBalance", request.VariantId.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["variantId"] = request.VariantId,
                        ["locationId"] = request.LocationId,
                        ["locationType"] = locationType,
                        ["packCode"] = request.PackCode,
                        ["count"] = request.Count,
                        ["baseQuantity"] = baseQuantity
                    },
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        return await sender.Send(new GetPackStateQuery(request.VariantId, request.LocationId, locationType), cancellationToken);
    }
}

public interface IPackStateStore
{
    Task<IReadOnlyList<PackStateBalanceRow>> ReadBalancesAsync(long businessId, long variantId, long locationId, string locationType, CancellationToken cancellationToken);
    Task<decimal> GetOnHandAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken);
    Task<bool> IdempotencyKeyUsedAsync(long businessId, string key, CancellationToken cancellationToken);
    Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public static class PackStateRules
{
    public static string LocationType(string? locationType)
    {
        var normalized = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "outlet" or "warehouse"
            ? normalized
            : throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "Location type must be outlet or warehouse.");
    }
}

public sealed record RepackComponentReadModel(long ComponentVariantId, string? ComponentName, string? ComponentSku, decimal Quantity, decimal? UnitCost, decimal? Value);

public sealed record RepackJobReadModel(
    long Id,
    string Reference,
    string Status,
    long LocationId,
    string LocationType,
    long SourceVariantId,
    string? SourceName,
    string? SourceSku,
    string OutputPackCode,
    string OutputLabel,
    decimal PackBaseQuantity,
    int OutputCount,
    decimal ContentBaseQuantity,
    string? BaseUnitCode,
    decimal? ContentUnitCost,
    decimal? ContentValue,
    decimal? PackagingValue,
    /// <summary>Content plus packaging per finished pack, once executed.</summary>
    decimal? CostPerPack,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ExecutedAt,
    DateTime? ReversedAt,
    IReadOnlyList<RepackComponentReadModel> Components);

public sealed record RepackJobPage(IReadOnlyList<RepackJobReadModel> Items, int TotalCount);

public interface IRepackStore
{
    Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken);
    Task<RepackJob?> FindForUpdateAsync(long businessId, long jobId, CancellationToken cancellationToken);
    Task<RepackJob?> FindByIdempotencyKeyAsync(long businessId, string key, CancellationToken cancellationToken);
    Task<string> NextReferenceAsync(long businessId, CancellationToken cancellationToken);
    Task AddAsync(RepackJob job, CancellationToken cancellationToken);
    Task<Core.Entities.Inventory?> FindInventoryForUpdateAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken);
    Task<Core.Entities.Inventory> GetOrCreateInventoryForUpdateAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken);
    Task<StockCostPosition> GetCostPositionAsync(long businessId, long variantId, long locationId, string locationType, decimal onHand, CancellationToken cancellationToken);
    Task<bool> LocationBelongsToBusinessAsync(long businessId, long locationId, string locationType, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<RepackJobReadModel?> ReadAsync(long businessId, long jobId, CancellationToken cancellationToken);
    Task<RepackJobPage> ListAsync(long businessId, long? locationId, string? locationType, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record GetRepackJobsQuery(long? LocationId, string? LocationType, string? Status, int Page = 1, int PageSize = 25) : IQuery<RepackJobPage>;

public sealed class GetRepackJobsQueryHandler(IRepackStore store, IUomOperationScope scope)
    : IRequestHandler<GetRepackJobsQuery, RepackJobPage>
{
    public Task<RepackJobPage> Handle(GetRepackJobsQuery request, CancellationToken cancellationToken) =>
        store.ListAsync(
            scope.RequireBusinessId(),
            request.LocationId,
            request.LocationType,
            request.Status,
            Math.Max(request.Page, 1),
            Math.Clamp(request.PageSize, 1, 100),
            cancellationToken);
}

public sealed record GetRepackJobQuery(long JobId) : IQuery<RepackJobReadModel>;

public sealed class GetRepackJobQueryHandler(IRepackStore store, IUomOperationScope scope)
    : IRequestHandler<GetRepackJobQuery, RepackJobReadModel>
{
    public async Task<RepackJobReadModel> Handle(GetRepackJobQuery request, CancellationToken cancellationToken) =>
        await store.ReadAsync(scope.RequireBusinessId(), request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Repack job {request.JobId} not found");
}

public sealed record CreateRepackJobCommand(
    long SourceVariantId,
    long LocationId,
    string LocationType,
    long OutputSellingOptionId,
    int OutputCount,
    string IdempotencyKey,
    string? Notes) : ICommand<RepackJobReadModel>;

public sealed class CreateRepackJobCommandHandler(
    IRepackStore store,
    IVariantUomReader reader,
    IUomOperationScope scope,
    IUomAuditWriter audit) : IRequestHandler<CreateRepackJobCommand, RepackJobReadModel>
{
    public async Task<RepackJobReadModel> Handle(CreateRepackJobCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        var locationType = PackStateRules.LocationType(request.LocationType);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "An idempotency key is required.");
        if (request.OutputCount <= 0)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Choose at least one pack to prepare.");
        if (!await store.LocationBelongsToBusinessAsync(businessId, request.LocationId, locationType, cancellationToken))
            throw new KeyNotFoundException($"Location {request.LocationId} not found");
        await scope.AuthorizeWriteLocationAsync(request.LocationId, locationType, cancellationToken);

        var existing = await store.FindByIdempotencyKeyAsync(businessId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return (await store.ReadAsync(businessId, existing.Id, cancellationToken))!;

        var configs = await reader.GetCurrentAsync(businessId, [request.SourceVariantId], cancellationToken);
        if (!configs.TryGetValue(request.SourceVariantId, out var config) || !config.RepackingEnabled || !config.PackStateTrackingEnabled)
            throw new UomValidationException(UomErrorCodes.RepackNotEnabled, "Repacking is not enabled for this item.");
        var option = config.SellingOptions.FirstOrDefault(candidate => candidate.Id == request.OutputSellingOptionId)
            ?? throw new UomValidationException(UomErrorCodes.OptionNotFound, "The chosen pack is not available for this item.");
        if (option.Kind != SellingOptionKinds.Pack || !option.IsPreparedEligible)
            throw new UomValidationException(UomErrorCodes.RepackNotEnabled, $"'{option.Label}' cannot be prepared by repacking.");

        var packBase = option.BaseQuantity!.Value;
        var job = new RepackJob
        {
            BusinessId = businessId,
            Reference = await store.NextReferenceAsync(businessId, cancellationToken),
            LocationId = request.LocationId,
            LocationType = locationType,
            SourceVariantId = request.SourceVariantId,
            ConfigVersionId = config.ConfigId,
            OutputOptionId = option.Id,
            OutputPackCode = option.Code,
            OutputLabel = option.Label,
            PackBaseQuantity = packBase,
            OutputCount = request.OutputCount,
            ContentBaseQuantity = UomQuantityMath.Normalize(request.OutputCount * packBase),
            IdempotencyKey = request.IdempotencyKey,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = actor,
            Components = option.Components
                .Where(component => component.IsRequired)
                .Select(component => new RepackComponentLine
                {
                    ComponentVariantId = component.VariantId,
                    Quantity = UomQuantityMath.Normalize(component.Quantity * request.OutputCount)
                })
                .ToList()
        };
        await store.AddAsync(job, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync($"Repack job {job.Reference} created", "RepackJob", job.Id.ToString(),
            new Dictionary<string, object?> { ["sourceVariantId"] = job.SourceVariantId, ["outputCount"] = job.OutputCount, ["pack"] = job.OutputPackCode },
            cancellationToken);
        return (await store.ReadAsync(businessId, job.Id, cancellationToken))!;
    }
}

public sealed record CancelRepackJobCommand(long JobId) : ICommand<RepackJobReadModel>;

public sealed class CancelRepackJobCommandHandler(IRepackStore store, IUomOperationScope scope, IUomAuditWriter audit)
    : IRequestHandler<CancelRepackJobCommand, RepackJobReadModel>
{
    public async Task<RepackJobReadModel> Handle(CancelRepackJobCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        await using var transaction = await store.BeginAsync(cancellationToken);
        var job = await store.FindForUpdateAsync(businessId, request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Repack job {request.JobId} not found");
        await scope.AuthorizeWriteLocationAsync(job.LocationId, job.LocationType, cancellationToken);
        if (job.Status != RepackJobStatuses.Draft)
            throw new UomConflictException(UomErrorCodes.RepackStatus, $"A {job.Status} job cannot be cancelled.");
        job.Status = RepackJobStatuses.Cancelled;
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync($"Repack job {job.Reference} cancelled", "RepackJob", job.Id.ToString(), new Dictionary<string, object?>(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await store.ReadAsync(businessId, job.Id, cancellationToken))!;
    }
}

/// <param name="WastageBaseQuantity">
/// Must be zero. No approved rule values wastage inside a repack (REP-008): record a real loss as a stock adjustment,
/// which has an approval workflow and its own journal.
/// </param>
public sealed record ExecuteRepackJobCommand(long JobId, decimal WastageBaseQuantity = 0m) : ICommand<RepackJobReadModel>;

/// <summary>
/// Consumes loose stock and packaging once and creates prepared packs, in one transaction (Pattern P1). The content's
/// quantity and pool do not move: it only changes physical state. Packaging leaves its own pool at weighted-average
/// cost and the same value becomes capitalized in the prepared balance. Total inventory value is therefore unchanged and
/// no journal is needed, because both sides sit in the one Inventory account.
/// </summary>
public sealed class ExecuteRepackJobCommandHandler(
    IRepackStore store,
    IVariantUomReader reader,
    IPackStateLedger packState,
    IStockLedgerWriter stockLedger,
    IUomOperationScope scope,
    IUomAuditWriter audit) : IRequestHandler<ExecuteRepackJobCommand, RepackJobReadModel>
{
    public async Task<RepackJobReadModel> Handle(ExecuteRepackJobCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        if (request.WastageBaseQuantity != 0m)
            throw new UomValidationException(
                UomErrorCodes.RepackWastageNotSupported,
                "A repack job cannot record wastage. Record the loss as a stock adjustment, then prepare the packs.");

        await using var transaction = await store.BeginAsync(cancellationToken);
        try
        {
            var job = await store.FindForUpdateAsync(businessId, request.JobId, cancellationToken)
                ?? throw new KeyNotFoundException($"Repack job {request.JobId} not found");
            await scope.AuthorizeWriteLocationAsync(job.LocationId, job.LocationType, cancellationToken);

            // A retry of a completed execution changes nothing and returns the same result.
            if (job.Status == RepackJobStatuses.Executed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return (await store.ReadAsync(businessId, job.Id, cancellationToken))!;
            }

            if (job.Status != RepackJobStatuses.Draft)
                throw new UomConflictException(UomErrorCodes.RepackStatus, $"A {job.Status} job cannot be executed.");

            var variantIds = job.Components.Select(component => component.ComponentVariantId).Append(job.SourceVariantId).Distinct().ToList();
            var configs = await reader.GetCurrentAsync(businessId, variantIds, cancellationToken);
            if (!configs.TryGetValue(job.SourceVariantId, out var config) || config.ConfigId != job.ConfigVersionId)
                throw new UomConflictException(UomErrorCodes.ConfigChanged, "The item's unit configuration changed after this job was created. Create a new job.");

            // Ordered by variant so concurrent jobs and sales take locks in one order.
            decimal packagingValue = 0m;
            foreach (var component in job.Components.OrderBy(line => line.ComponentVariantId))
            {
                var inventory = await store.FindInventoryForUpdateAsync(component.ComponentVariantId, job.LocationId, job.LocationType, cancellationToken);
                if (inventory is null || inventory.Quantity < component.Quantity)
                    throw new UomValidationException(
                        UomErrorCodes.InsufficientPackagingStock,
                        $"Not enough packaging item {component.ComponentVariantId}. Available: {inventory?.Quantity ?? 0}, needed: {component.Quantity}.");

                var position = await store.GetCostPositionAsync(businessId, component.ComponentVariantId, job.LocationId, job.LocationType, inventory.Quantity, cancellationToken);
                if (position.Kind == StockCostKind.Unvalued)
                    throw new UomValidationException(
                        UomErrorCodes.RepackCostUnreliable,
                        $"Packaging item {component.ComponentVariantId} has no reliable cost at this location, so its value cannot move into prepared stock.");

                var value = position.Kind == StockCostKind.Valued
                    ? Math.Round(position.Value!.Value / inventory.Quantity * component.Quantity, 2, MidpointRounding.AwayFromZero)
                    : 0m;
                var unitCost = position.UnitCost ?? 0m;
                inventory.Quantity -= component.Quantity;
                component.UnitCost = unitCost;
                component.Value = value;
                packagingValue += value;

                if (position.Kind == StockCostKind.Valued)
                    stockLedger.WriteEntryWithValuation(
                        component.ComponentVariantId, job.LocationId, job.LocationType, UomStockLedgerTypes.RepackConsumption,
                        0m, component.Quantity, inventory.Quantity, UomStockLedgerTypes.RepackReference, job.Id,
                        $"Repack {job.Reference}", actor, unitCost: unitCost, movementValue: value);
                else
                    stockLedger.WriteVerifiedZeroCostEntry(
                        component.ComponentVariantId, job.LocationId, job.LocationType, UomStockLedgerTypes.RepackConsumption,
                        0m, component.Quantity, inventory.Quantity, UomStockLedgerTypes.RepackReference, job.Id,
                        $"Repack {job.Reference}", actor);

                if (configs.GetValueOrDefault(component.ComponentVariantId)?.PackStateTrackingEnabled == true)
                    await packState.ApplyAsync(
                        new PackStateChange(businessId, component.ComponentVariantId, job.LocationId, job.LocationType,
                            PackStates.Loose, string.Empty, 0m, 0, -component.Quantity, 0m,
                            PackStateOperations.RepackConsume, UomStockLedgerTypes.RepackReference, job.Id, actor),
                        cancellationToken);
            }

            var source = await store.FindInventoryForUpdateAsync(job.SourceVariantId, job.LocationId, job.LocationType, cancellationToken);
            if (source is null)
                throw new UomValidationException(UomErrorCodes.InsufficientLooseStock, "There is no stock of this item at this location.");

            await packState.ApplyAsync(
                new PackStateChange(businessId, job.SourceVariantId, job.LocationId, job.LocationType,
                    PackStates.Loose, string.Empty, 0m, 0, -job.ContentBaseQuantity, 0m,
                    PackStateOperations.RepackConsume, UomStockLedgerTypes.RepackReference, job.Id, actor),
                cancellationToken);
            await packState.ApplyAsync(
                new PackStateChange(businessId, job.SourceVariantId, job.LocationId, job.LocationType,
                    PackStates.Prepared, job.OutputPackCode, job.PackBaseQuantity, job.OutputCount, job.ContentBaseQuantity, packagingValue,
                    PackStateOperations.RepackProduce, UomStockLedgerTypes.RepackReference, job.Id, actor),
                cancellationToken);

            var contentPosition = await store.GetCostPositionAsync(businessId, job.SourceVariantId, job.LocationId, job.LocationType, source.Quantity, cancellationToken);
            job.ContentUnitCost = contentPosition.UnitCost;
            job.ContentValue = contentPosition.Kind == StockCostKind.Valued
                ? Math.Round(contentPosition.Value!.Value / source.Quantity * job.ContentBaseQuantity, 2, MidpointRounding.AwayFromZero)
                : contentPosition.Kind == StockCostKind.VerifiedZero ? 0m : null;
            job.PackagingValue = packagingValue;
            job.Status = RepackJobStatuses.Executed;
            job.ExecutedBy = actor;
            job.ExecutedAt = DateTime.UtcNow;

            await store.SaveChangesAsync(cancellationToken);
            await packState.EnsureReconciledAsync(businessId, job.SourceVariantId, job.LocationId, job.LocationType, source.Quantity, cancellationToken);
            await audit.RecordAsync($"Repack job {job.Reference} executed", "RepackJob", job.Id.ToString(),
                new Dictionary<string, object?>
                {
                    ["sourceVariantId"] = job.SourceVariantId,
                    ["locationId"] = job.LocationId,
                    ["locationType"] = job.LocationType,
                    ["outputCount"] = job.OutputCount,
                    ["pack"] = job.OutputPackCode,
                    ["contentBaseQuantity"] = job.ContentBaseQuantity,
                    ["contentValue"] = job.ContentValue,
                    ["packagingValue"] = packagingValue,
                    ["configVersionId"] = job.ConfigVersionId
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (await store.ReadAsync(businessId, job.Id, cancellationToken))!;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

public sealed record ReverseRepackJobCommand(long JobId) : ICommand<RepackJobReadModel>;

/// <summary>
/// Undoes a mistaken job while its packs are still unsold: packs return to loose stock and the exact packaging quantity
/// and value return to their pools. A single prepared pack cannot be opened: its bottle's value would have no approved
/// destination (PACK-006, REP-008).
/// </summary>
public sealed class ReverseRepackJobCommandHandler(
    IRepackStore store,
    IPackStateLedger packState,
    IVariantUomReader reader,
    IStockLedgerWriter stockLedger,
    IUomOperationScope scope,
    IUomAuditWriter audit) : IRequestHandler<ReverseRepackJobCommand, RepackJobReadModel>
{
    public async Task<RepackJobReadModel> Handle(ReverseRepackJobCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        await using var transaction = await store.BeginAsync(cancellationToken);
        try
        {
            var job = await store.FindForUpdateAsync(businessId, request.JobId, cancellationToken)
                ?? throw new KeyNotFoundException($"Repack job {request.JobId} not found");
            await scope.AuthorizeWriteLocationAsync(job.LocationId, job.LocationType, cancellationToken);
            if (job.Status != RepackJobStatuses.Executed)
                throw new UomConflictException(UomErrorCodes.RepackStatus, $"A {job.Status} job cannot be reversed.");

            var balances = await packState.GetBalancesForUpdateAsync(businessId, job.SourceVariantId, job.LocationId, job.LocationType, cancellationToken);
            var prepared = balances.FirstOrDefault(row =>
                row.State == PackStates.Prepared && row.PackCode == job.OutputPackCode && row.BaseQtyPerPack == job.PackBaseQuantity);
            var value = job.PackagingValue ?? 0m;
            if (prepared is null || prepared.PackCount < job.OutputCount || prepared.CapitalizedValue < value)
                throw new UomConflictException(
                    UomErrorCodes.RepackReverseBlocked,
                    "Some of these packs have already been sold or moved, so this job can no longer be reversed.");

            var configs = await reader.GetCurrentAsync(
                businessId,
                job.Components.Select(component => component.ComponentVariantId).ToList(),
                cancellationToken);
            foreach (var component in job.Components.OrderBy(line => line.ComponentVariantId))
            {
                var inventory = await store.GetOrCreateInventoryForUpdateAsync(component.ComponentVariantId, job.LocationId, job.LocationType, cancellationToken);
                inventory.Quantity += component.Quantity;
                if ((component.Value ?? 0m) > 0m)
                    stockLedger.WriteEntryWithValuation(
                        component.ComponentVariantId, job.LocationId, job.LocationType, UomStockLedgerTypes.RepackReversal,
                        component.Quantity, 0m, inventory.Quantity, UomStockLedgerTypes.RepackReference, job.Id,
                        $"Repack {job.Reference} reversed", actor, unitCost: component.UnitCost, movementValue: component.Value);
                else
                    stockLedger.WriteVerifiedZeroCostEntry(
                        component.ComponentVariantId, job.LocationId, job.LocationType, UomStockLedgerTypes.RepackReversal,
                        component.Quantity, 0m, inventory.Quantity, UomStockLedgerTypes.RepackReference, job.Id,
                        $"Repack {job.Reference} reversed", actor);

                if (configs.GetValueOrDefault(component.ComponentVariantId)?.PackStateTrackingEnabled == true)
                    await packState.ApplyAsync(
                        new PackStateChange(businessId, component.ComponentVariantId, job.LocationId, job.LocationType,
                            PackStates.Loose, string.Empty, 0m, 0, component.Quantity, 0m,
                            PackStateOperations.RepackReverse, UomStockLedgerTypes.RepackReference, job.Id, actor),
                        cancellationToken);
            }

            await packState.ApplyAsync(
                new PackStateChange(businessId, job.SourceVariantId, job.LocationId, job.LocationType,
                    PackStates.Prepared, job.OutputPackCode, job.PackBaseQuantity, -job.OutputCount, -job.ContentBaseQuantity, -value,
                    PackStateOperations.RepackReverse, UomStockLedgerTypes.RepackReference, job.Id, actor),
                cancellationToken);
            await packState.ApplyAsync(
                new PackStateChange(businessId, job.SourceVariantId, job.LocationId, job.LocationType,
                    PackStates.Loose, string.Empty, 0m, 0, job.ContentBaseQuantity, 0m,
                    PackStateOperations.RepackReverse, UomStockLedgerTypes.RepackReference, job.Id, actor),
                cancellationToken);

            job.Status = RepackJobStatuses.Reversed;
            job.ReversedBy = actor;
            job.ReversedAt = DateTime.UtcNow;
            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync($"Repack job {job.Reference} reversed", "RepackJob", job.Id.ToString(),
                new Dictionary<string, object?> { ["packagingValue"] = value, ["outputCount"] = job.OutputCount },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (await store.ReadAsync(businessId, job.Id, cancellationToken))!;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
