using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record PurchaseAndReceiveCommand(
    long SupplierId,
    PurchaseOrderDestination Destination,
    DateTime OrderDate,
    DateTime? ExpectedDelivery,
    string? Notes,
    IReadOnlyList<PurchaseAndReceiveItemCommand>? Items,
    string? IdempotencyKey,
    long UserId) : ICommand<PurchaseAndReceiveResultReadModel>;

public sealed record PurchaseAndReceiveItemCommand(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? Unit,
    long? PurchaseOptionId = null,
    long? ExpectedConfigVersionId = null,
    int? AcceptedSealedPacks = null,
    decimal? AcceptedLooseQuantity = null)
{
    /// <summary>Set by the handler: Quantity and UnitPrice are then already in base units.</summary>
    public UnitTransactionSnapshot? Snapshot { get; init; }

    public bool IsSealedPackLine { get; init; }

    public bool TrackPackState { get; init; }
}

public sealed record PurchaseAndReceiveResultReadModel(
    PurchaseOrderReadModel PurchaseOrder,
    long GrnId,
    bool IsDuplicateRequest);

public sealed class PurchaseAndReceiveCommandHandler(
    IPurchaseAndReceiveStore store,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope,
    IGrnReceiptLocationAuthorizer locationAuthorizer,
    IVariantUomReader? uomReader = null)
    : IRequestHandler<PurchaseAndReceiveCommand, PurchaseAndReceiveResultReadModel>
{
    public async Task<PurchaseAndReceiveResultReadModel> Handle(
        PurchaseAndReceiveCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required for purchase and receive action");

        await locationAuthorizer.AuthorizeAsync(
            request.Destination.LocationType,
            request.Destination.LocationId,
            cancellationToken);

        var uomConfigs = uomReader is null || businessId is not { } uomBusinessId
            ? new Dictionary<long, ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(uomBusinessId, (request.Items ?? []).Select(item => item.VariantId).ToList(), cancellationToken);
        var resolvedItems = request.Items?.Select(item =>
        {
            var config = uomConfigs.GetValueOrDefault(item.VariantId);
            var line = PurchaseUomRules.Resolve(item.Quantity, item.UnitPrice, item.PurchaseOptionId, item.ExpectedConfigVersionId, config);
            return item with
            {
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Unit = line.Snapshot?.BaseUnitCode ?? item.Unit,
                Snapshot = line.Snapshot,
                IsSealedPackLine = config?.PurchaseOptions.Any(option => option.Id == item.PurchaseOptionId && option.IsSealedPack) == true,
                TrackPackState = config?.PackStateTrackingEnabled == true
            };
        }).ToArray();

        var result = await store.ExecuteAsync(
            request with { IdempotencyKey = request.IdempotencyKey.Trim(), Items = resolvedItems },
            businessId,
            cancellationToken);
        var purchaseOrder = await readStore.FindByIdAsync(
            result.PurchaseOrderId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order with ID {result.PurchaseOrderId} not found");

        return new PurchaseAndReceiveResultReadModel(
            purchaseOrder,
            result.GrnId,
            result.IsDuplicateRequest);
    }
}
