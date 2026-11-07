using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Money;

namespace RetailPOS.Application.Grns.Commands;

public sealed record CreateGrnItemCommand(
    long PurchaseOrderItemId,
    decimal ReceivedQuantity,
    decimal? UnitCost,
    string? Notes,
    decimal DamagedQuantity = 0m,
    decimal RejectedQuantity = 0m,
    int? AcceptedSealedPacks = null,
    decimal? AcceptedLooseQuantity = null);

public sealed record CreateGrnCommand(
    long PurchaseOrderId,
    DateTime ReceivedDate,
    string? Notes,
    string? SupplierInvoiceNumber,
    DateTime? SupplierInvoiceDate,
    IReadOnlyList<CreateGrnItemCommand>? Items,
    long? ActorUserId) : ICommand<GrnReadModel>;

public sealed class CreateGrnCommandHandler(
    IGrnCreationStore creationStore,
    IGrnTenantScope tenantScope,
    IGrnCreationAuditWriter auditWriter,
    IGrnReceiptLocationAuthorizer locationAuthorizer,
    RetailPOS.Application.Uom.IVariantUomReader? uomReader = null)
    : IRequestHandler<CreateGrnCommand, GrnReadModel>
{
    public async Task<GrnReadModel> Handle(
        CreateGrnCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var items = request.Items;
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("GRN must have at least one item");

        var purchaseOrder = await creationStore.FindPurchaseOrderAsync(
            request.PurchaseOrderId,
            businessId,
            cancellationToken);
        if (purchaseOrder is null)
            throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");

        var purchaseOrderStatus = purchaseOrder.Status.ToLower();
        if (purchaseOrderStatus is not "approved" and not "partial")
            throw new InvalidOperationException(
                "Can only create a GRN for an approved or partially-received purchase order. " +
                $"Current status: {purchaseOrder.Status}");

        // Goods are always received into the destination selected on the PO.
        await locationAuthorizer.AuthorizeAsync(
            purchaseOrder.LocationType,
            purchaseOrder.LocationId,
            cancellationToken);

        var purchaseOrderItems = purchaseOrder.Items.ToDictionary(item => item.Id);
        var uomConfigs = uomReader is null || purchaseOrder.BusinessId is not { } uomBusinessId
            ? new Dictionary<long, RetailPOS.Application.Uom.ResolvedVariantUom>()
            : await uomReader.GetCurrentAsync(uomBusinessId, purchaseOrder.Items.Select(item => item.VariantId).ToList(), cancellationToken);
        foreach (var item in items)
        {
            if (!purchaseOrderItems.ContainsKey(item.PurchaseOrderItemId))
                throw new InvalidOperationException(
                    $"PO item with ID {item.PurchaseOrderItemId} does not belong to purchase order " +
                    $"{request.PurchaseOrderId}");

            if (item.ReceivedQuantity <= 0)
                throw new InvalidOperationException(
                    $"Received quantity for PO item {item.PurchaseOrderItemId} must be greater than zero");
        }

        var previouslyReceived = await creationStore.GetCompletedReceivedQuantitiesAsync(
            request.PurchaseOrderId,
            cancellationToken);
        foreach (var item in items)
        {
            var orderedItem = purchaseOrderItems[item.PurchaseOrderItemId];
            var alreadyReceived = previouslyReceived.GetValueOrDefault(item.PurchaseOrderItemId, 0);
            var totalAfter = alreadyReceived + item.ReceivedQuantity;
            if (totalAfter > orderedItem.Quantity)
                throw new InvalidOperationException(
                    $"Over-receiving not allowed for PO item {item.PurchaseOrderItemId} " +
                    $"(ordered: {orderedItem.Quantity}, already received: {alreadyReceived}, " +
                    $"this GRN: {item.ReceivedQuantity}, total would be: {totalAfter})");
        }

        var status = items.Any(item =>
            item.ReceivedQuantity < purchaseOrderItems[item.PurchaseOrderItemId].Quantity)
                ? "partial"
                : "full";
        var sealedOptions = uomReader is null
            ? new HashSet<long>()
            : await uomReader.GetSealedOptionIdsAsync(
                purchaseOrder.Items.Where(line => line.Snapshot?.OptionId is not null).Select(line => line.Snapshot!.OptionId!.Value).ToList(),
                cancellationToken);
        foreach (var item in items)
        {
            var orderLine = purchaseOrderItems[item.PurchaseOrderItemId];
            var config = uomConfigs.GetValueOrDefault(orderLine.VariantId);
            RetailPOS.Application.Uom.UomLineResolver.EnsureBaseQuantityAllowed(config, item.ReceivedQuantity, "Received quantity");
            if (item.DamagedQuantity < 0m || item.RejectedQuantity < 0m)
                throw new RetailPOS.Application.Uom.UomValidationException(
                    RetailPOS.Application.Uom.UomErrorCodes.InvalidQuantity, "Damaged and rejected quantities cannot be negative.");
            RetailPOS.Application.Uom.UomLineResolver.EnsureBaseQuantityAllowed(config, item.DamagedQuantity, "Damaged quantity");
            RetailPOS.Application.Uom.UomLineResolver.EnsureBaseQuantityAllowed(config, item.RejectedQuantity, "Rejected quantity");
            if (config?.PackStateTrackingEnabled == true)
                RetailPOS.Application.Uom.PurchaseUomRules.Breakdown(
                    item.ReceivedQuantity, item.AcceptedSealedPacks, item.AcceptedLooseQuantity, orderLine.Snapshot,
                    orderLine.Snapshot?.OptionId is { } optionId && sealedOptions.Contains(optionId));
            else if (item.AcceptedSealedPacks is not null || item.AcceptedLooseQuantity is not null)
                throw new RetailPOS.Application.Uom.UomValidationException(
                    RetailPOS.Application.Uom.UomErrorCodes.PackStateNotEnabled, "This item does not track sealed and loose stock.");
        }

        var receiptItems = items.Select(item =>
        {
            var purchaseOrderItem = purchaseOrderItems[item.PurchaseOrderItemId];
            // A cost typed against a purchase unit is converted by the order's own snapshot, not today's configuration.
            var unitCost = item.UnitCost is { } enteredCost
                ? RetailPOS.Application.Uom.PurchaseUomRules.PerBaseCost(enteredCost, purchaseOrderItem.Snapshot)
                : purchaseOrderItem.UnitPrice;
            var discountedAmount = item.ReceivedQuantity * unitCost *
                (1 - purchaseOrderItem.Discount / 100m);
            return new GrnCreationItemRecord(
                item.PurchaseOrderItemId,
                item.ReceivedQuantity,
                unitCost,
                MonetaryRounding.ToCurrency(discountedAmount),
                purchaseOrderItem.Tax,
                MonetaryRounding.ToCurrency(discountedAmount * purchaseOrderItem.Tax / 100m),
                purchaseOrderItem.Tax > 0 ? "VAT" : null,
                purchaseOrderItem.TaxInclusive,
                item.Notes)
            {
                DamagedQuantity = item.DamagedQuantity,
                RejectedQuantity = item.RejectedQuantity,
                AcceptedSealedPacks = item.AcceptedSealedPacks,
                AcceptedLooseQuantity = item.AcceptedLooseQuantity
            };
        }).ToArray();

        var record = new GrnCreationRecord(
            request.PurchaseOrderId,
            DateTime.SpecifyKind(request.ReceivedDate, DateTimeKind.Utc),
            status,
            request.Notes,
            request.SupplierInvoiceNumber,
            request.SupplierInvoiceDate?.Date,
            request.ActorUserId,
            receiptItems);
        var created = await creationStore.CreateAsync(record, businessId, cancellationToken);
        await auditWriter.RecordCreatedAsync(
            new GrnCreationAuditRecord(
                created.Id,
                purchaseOrder.BusinessId,
                purchaseOrder.LocationId,
                purchaseOrder.LocationType,
                purchaseOrder.SupplierId,
                purchaseOrder.Id,
                request.ActorUserId,
                created.Status,
                created.ReceivedDate,
                created.Items.Count,
                created.Items.Sum(item => item.ReceivedQuantity),
                receiptItems.Sum(item => item.TaxableAmount),
                receiptItems.Sum(item => item.VatAmount)),
            cancellationToken);

        return created;
    }
}
