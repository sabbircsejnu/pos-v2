using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record CreatePurchaseOrderItemCommand(
    long VariantId,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? Unit,
    long? PurchaseOptionId = null,
    long? ExpectedConfigVersionId = null);

public sealed record CreatePurchaseOrderCommand(
    long SupplierId,
    PurchaseOrderDestination Destination,
    DateTime OrderDate,
    DateTime? ExpectedDelivery,
    string Status,
    string? Notes,
    IReadOnlyList<CreatePurchaseOrderItemCommand>? Items,
    long? UserId) : ICommand<PurchaseOrderReadModel>;

public sealed class CreatePurchaseOrderCommandHandler(
    IPurchaseOrderCreationStore creationStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope,
    IPurchaseOrderLocationAuthorizer locationAuthorizer,
    IVariantUomReader? uomReader = null)
    : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var items = request.Items;
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        var destination = request.Destination;
        var location = await creationStore.FindLocationAsync(
            destination,
            businessId,
            cancellationToken);
        if (location is null)
            throw new KeyNotFoundException(
                $"{PurchaseOrderLocationText.Label(destination.LocationType)} with ID {destination.LocationId} not found");

        await locationAuthorizer.AuthorizeAsync(
            destination.LocationType,
            destination.LocationId,
            cancellationToken);

        var supplierBusinessId = businessId ?? location.BusinessId
            ?? throw new InvalidOperationException("Destination location business is required.");
        if (!await creationStore.SupplierExistsAsync(
                request.SupplierId,
                supplierBusinessId,
                cancellationToken))
            throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");

        foreach (var item in items)
        {
            if (!await creationStore.ProductVariantExistsAsync(item.VariantId, cancellationToken))
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");
        }

        var uomConfigs = uomReader is null
            ? new Dictionary<long, ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(supplierBusinessId, items.Select(item => item.VariantId).ToList(), cancellationToken);
        var lines = items
            .Select(item => (Item: item, Line: PurchaseUomRules.Resolve(
                item.Quantity, item.UnitPrice, item.PurchaseOptionId, item.ExpectedConfigVersionId, uomConfigs.GetValueOrDefault(item.VariantId))))
            .ToArray();
        var totalAmount = lines.Sum(line =>
            MonetaryRounding.ToCurrency(
                line.Line.Quantity * line.Line.UnitPrice *
                (1 - line.Item.Discount / 100m) *
                (1 + line.Item.Tax / 100m)));
        var purchaseOrder = new PurchaseOrder
        {
            SupplierId = request.SupplierId,
            OrderDate = DateTime.SpecifyKind(request.OrderDate, DateTimeKind.Utc),
            ExpectedDelivery = request.ExpectedDelivery.HasValue
                ? DateTime.SpecifyKind(request.ExpectedDelivery.Value, DateTimeKind.Utc)
                : null,
            Status = request.Status.ToLower(),
            Notes = request.Notes,
            TotalAmount = totalAmount,
            CreatedBy = request.UserId,
            Items = lines.Select(line =>
            {
                var orderItem = new PurchaseOrderItem
                {
                    VariantId = line.Item.VariantId,
                    Quantity = line.Line.Quantity,
                    UnitPrice = line.Line.UnitPrice,
                    Discount = line.Item.Discount,
                    Tax = line.Item.Tax,
                    Unit = line.Line.Snapshot?.BaseUnitCode ?? line.Item.Unit
                };
                if (line.Line.Snapshot is not null)
                    orderItem.CaptureUomSnapshot(line.Line.Snapshot);
                return orderItem;
            }).ToList()
        };
        purchaseOrder.SetDestination(destination.LocationType, destination.LocationId);

        var purchaseOrderId = await creationStore.CreateAsync(purchaseOrder, cancellationToken);
        var poNumber = $"PO-{purchaseOrder.OrderDate:yyyyMMdd}-{purchaseOrderId:D4}";
        await creationStore.SetPoNumberAsync(purchaseOrderId, poNumber, cancellationToken);

        return await readStore.FindByIdAsync(purchaseOrderId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order with ID {purchaseOrderId} not found");
    }
}
