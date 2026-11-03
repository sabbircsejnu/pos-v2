using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record UpdatePurchaseOrderItemCommand(
    long VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? Unit);

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
    IPurchaseOrderLocationAuthorizer locationAuthorizer)
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
        purchaseOrder.TotalAmount = items.Sum(item =>
            MonetaryRounding.ToCurrency(
                item.Quantity * item.UnitPrice *
                (1 - item.Discount / 100m) *
                (1 + item.Tax / 100m)));

        purchaseOrder.Items.Clear();
        foreach (var item in items)
        {
            purchaseOrder.Items.Add(new PurchaseOrderItem
            {
                PoId = request.PurchaseOrderId,
                VariantId = item.VariantId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Discount = item.Discount,
                Tax = item.Tax,
                Unit = item.Unit
            });
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
