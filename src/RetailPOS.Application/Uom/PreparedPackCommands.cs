using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

public sealed record OpenPreparedPackCommand(
    long VariantId,
    long LocationId,
    string LocationType,
    string PackCode,
    decimal BaseQtyPerPack,
    int Count,
    bool PackagingReusable,
    string IdempotencyKey) : ICommand<PackStateReadModel>;

/// <summary>
/// Opens prepared packs one or more at a time, with the treatment a repack reversal already uses (Pattern P1): the
/// content becomes loose stock without moving, since it never left its pool, and each pack's packaging returns to
/// packaging stock carrying its share of the capitalized value. Nothing is created, lost or posted.
/// <para>Packaging that cannot be used again would have to leave inventory with no approved destination for its value
/// (REP-008), so that case is refused. So is an option with several packaging items, whose value split was never recorded.</para>
/// </summary>
public sealed class OpenPreparedPackCommandHandler(
    IVariantUomReader reader,
    IPackStateStore store,
    IRepackStore repackStore,
    IPackStateLedger ledger,
    IStockLedgerWriter stockLedger,
    IUomOperationScope scope,
    IUomAuditWriter audit,
    ISender sender) : IRequestHandler<OpenPreparedPackCommand, PackStateReadModel>
{
    public async Task<PackStateReadModel> Handle(OpenPreparedPackCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var actor = scope.RequireActorUserId();
        var locationType = PackStateRules.LocationType(request.LocationType);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new UomValidationException(UomErrorCodes.InvalidConfiguration, "An idempotency key is required.");
        if (request.Count <= 0)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Choose at least one pack to open.");
        if (!request.PackagingReusable)
            throw new UomValidationException(
                UomErrorCodes.UnsupportedOperation,
                "Opening a prepared pack returns its packaging to stock. Packaging that cannot be used again has no approved " +
                "treatment for its cost, so these packs cannot be opened this way. Sell them, or ask Finance how to write them off.");
        await scope.AuthorizeWriteLocationAsync(request.LocationId, locationType, cancellationToken);

        var configs = await reader.GetCurrentAsync(businessId, [request.VariantId], cancellationToken);
        if (!configs.TryGetValue(request.VariantId, out var config) || !config.PackStateTrackingEnabled)
            throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, "This item does not track sealed and loose stock.");
        var option = config.SellingOptions.FirstOrDefault(candidate =>
            candidate.Kind == SellingOptionKinds.Pack && candidate.Code == request.PackCode && candidate.BaseQuantity == request.BaseQtyPerPack)
            ?? throw new UomValidationException(UomErrorCodes.OptionNotFound, $"'{request.PackCode}' is not a pack of this item.");
        if (option.Components.Count != 1)
            throw new UomValidationException(
                UomErrorCodes.UnsupportedOperation,
                option.Components.Count == 0
                    ? $"'{option.Label}' has no packaging item to return the packaging to."
                    : $"'{option.Label}' uses several packaging items, and how its packaging value splits between them was never recorded.");
        var component = option.Components[0];
        var packagingTracksState = (await reader.GetCurrentAsync(businessId, [component.VariantId], cancellationToken))
            .GetValueOrDefault(component.VariantId)?.PackStateTrackingEnabled == true;

        if (!await store.IdempotencyKeyUsedAsync(businessId, request.IdempotencyKey, cancellationToken))
        {
            await using var transaction = await store.BeginAsync(cancellationToken);
            try
            {
                // Inventory rows before pack-state balances: the lock order every stock operation uses.
                var packaging = await repackStore.GetOrCreateInventoryForUpdateAsync(component.VariantId, request.LocationId, locationType, cancellationToken);
                var balances = await ledger.GetBalancesForUpdateAsync(businessId, request.VariantId, request.LocationId, locationType, cancellationToken);
                var prepared = balances.FirstOrDefault(row =>
                    row.State == PackStates.Prepared && row.PackCode == request.PackCode && row.BaseQtyPerPack == request.BaseQtyPerPack);
                if (prepared is null || prepared.PackCount < request.Count)
                    throw new UomValidationException(
                        UomErrorCodes.InsufficientPreparedStock,
                        $"Only {prepared?.PackCount ?? 0} prepared {option.Label} are here.");

                var value = CapitalizedValueShare.For(prepared.CapitalizedValue, prepared.PackCount, request.Count);
                var packagingQuantity = UomQuantityMath.Normalize(component.Quantity * request.Count);
                var contentQuantity = request.Count * request.BaseQtyPerPack;

                packaging.Quantity += packagingQuantity;
                var remarks = $"Packaging back from {request.Count} opened {option.Label} of variant {request.VariantId}";
                if (value > 0m)
                    stockLedger.WriteEntryWithValuation(
                        component.VariantId, request.LocationId, locationType, UomStockLedgerTypes.PreparedOpen,
                        packagingQuantity, 0m, packaging.Quantity, UomStockLedgerTypes.PreparedOpenReference, request.VariantId,
                        remarks, actor, unitCost: value / packagingQuantity, movementValue: value);
                else
                    stockLedger.WriteVerifiedZeroCostEntry(
                        component.VariantId, request.LocationId, locationType, UomStockLedgerTypes.PreparedOpen,
                        packagingQuantity, 0m, packaging.Quantity, UomStockLedgerTypes.PreparedOpenReference, request.VariantId,
                        remarks, actor);

                if (packagingTracksState)
                    await ledger.ApplyAsync(
                        new PackStateChange(businessId, component.VariantId, request.LocationId, locationType,
                            PackStates.Loose, string.Empty, 0m, 0, packagingQuantity, 0m,
                            PackStateOperations.Open, UomStockLedgerTypes.PreparedOpenReference, request.VariantId, actor),
                        cancellationToken);

                await ledger.ApplyAsync(
                    new PackStateChange(businessId, request.VariantId, request.LocationId, locationType,
                        PackStates.Prepared, request.PackCode, request.BaseQtyPerPack, -request.Count, -contentQuantity, -value,
                        PackStateOperations.Open, UomStockLedgerTypes.PreparedOpenReference, request.VariantId, actor, request.IdempotencyKey),
                    cancellationToken);
                await ledger.ApplyAsync(
                    new PackStateChange(businessId, request.VariantId, request.LocationId, locationType,
                        PackStates.Loose, string.Empty, 0m, 0, contentQuantity, 0m,
                        PackStateOperations.Open, UomStockLedgerTypes.PreparedOpenReference, request.VariantId, actor, request.IdempotencyKey),
                    cancellationToken);

                await store.SaveChangesAsync(cancellationToken);
                await ledger.EnsureReconciledAsync(
                    businessId, request.VariantId, request.LocationId, locationType,
                    await store.GetOnHandAsync(request.VariantId, request.LocationId, locationType, cancellationToken),
                    cancellationToken);
                await audit.RecordAsync(
                    $"Opened {request.Count} prepared {request.PackCode} of variant {request.VariantId}",
                    "PackStateBalance",
                    request.VariantId.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["variantId"] = request.VariantId,
                        ["locationId"] = request.LocationId,
                        ["locationType"] = locationType,
                        ["packCode"] = request.PackCode,
                        ["count"] = request.Count,
                        ["contentBaseQuantity"] = contentQuantity,
                        ["packagingVariantId"] = component.VariantId,
                        ["packagingQuantity"] = packagingQuantity,
                        ["packagingValue"] = value
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
