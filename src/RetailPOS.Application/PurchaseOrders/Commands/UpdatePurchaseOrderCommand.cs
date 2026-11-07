using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record UpdatePurchaseOrderItemCommand(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? Unit,
    long? PurchaseOptionId = null,
    long? ExpectedConfigVersionId = null);

public sealed record UpdatePurchaseOrderCommand(
    long PurchaseOrderId,
    long SupplierId,
    PurchaseOrderDestination Destination,
    DateTime OrderDate,
    DateTime? ExpectedDelivery,
    string? Notes,
    IReadOnlyList<UpdatePurchaseOrderItemCommand>? Items) : ICommand<PurchaseOrderReadModel>;

public sealed class UpdatePurchaseOrderCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope,
    IPurchaseOrderCreationStore locationStore,
    IPurchaseOrderLocationAuthorizer locationAuthorizer,
    IVariantUomReader? uomReader = null)
    : IRequestHandler<UpdatePurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        UpdatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var purchaseOrder = await updateStore.FindForUpdateAsync(
            request.PurchaseOrderId,
            businessId,
            cancellationToken);
        if (purchaseOrder is null)
            throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");

        var status = purchaseOrder.Status.ToLower();
        if (status != "draft" && status != "sent_back")
            throw new InvalidOperationException(
                $"Cannot update purchase order in '{purchaseOrder.Status}' status. Only draft or sent-back orders can be edited.");

        var items = request.Items;
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        var destination = request.Destination;
        if (await locationStore.FindLocationAsync(destination, businessId, cancellationToken) is null)
            throw new KeyNotFoundException(
                $"{PurchaseOrderLocationText.Label(destination.LocationType)} with ID {destination.LocationId} not found");

        await locationAuthorizer.AuthorizeAsync(
            destination.LocationType,
            destination.LocationId,
            cancellationToken);

        purchaseOrder.SupplierId = request.SupplierId;
        purchaseOrder.SetDestination(destination.LocationType, destination.LocationId);
        purchaseOrder.OrderDate = DateTime.SpecifyKind(request.OrderDate, DateTimeKind.Utc);
        purchaseOrder.ExpectedDelivery = request.ExpectedDelivery.HasValue
            ? DateTime.SpecifyKind(request.ExpectedDelivery.Value, DateTimeKind.Utc)
            : null;
        purchaseOrder.Notes = request.Notes;
        // Editing a draft reinterprets it under the current configuration; a stale version is refused (CALC-007).
        var uomConfigs = uomReader is null || purchaseOrder.LocationBusinessId is not { } uomBusinessId
            ? new Dictionary<long, ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(uomBusinessId, items.Select(item => item.VariantId).ToList(), cancellationToken);
        var lines = items
            .Select(item => (Item: item, Line: PurchaseUomRules.Resolve(
                item.Quantity, item.UnitPrice, item.PurchaseOptionId, item.ExpectedConfigVersionId, uomConfigs.GetValueOrDefault(item.VariantId))))
            .ToArray();
        purchaseOrder.TotalAmount = lines.Sum(line =>
            MonetaryRounding.ToCurrency(
                line.Line.Quantity * line.Line.UnitPrice *
                (1 - line.Item.Discount / 100m) *
                (1 + line.Item.Tax / 100m)));

        purchaseOrder.Items.Clear();
        foreach (var (item, line) in lines)
        {
            var orderItem = new PurchaseOrderItem
            {
                PoId = request.PurchaseOrderId,
                VariantId = item.VariantId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = item.Discount,
                Tax = item.Tax,
                Unit = line.Snapshot?.BaseUnitCode ?? item.Unit
            };
            if (line.Snapshot is not null)
                orderItem.CaptureUomSnapshot(line.Snapshot);
            purchaseOrder.Items.Add(orderItem);
        }

        await updateStore.SaveUpdateAsync(purchaseOrder, cancellationToken);
        return await readStore.FindByIdAsync(
                request.PurchaseOrderId,
                businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
