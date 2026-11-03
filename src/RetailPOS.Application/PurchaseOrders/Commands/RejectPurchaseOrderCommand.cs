using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record RejectPurchaseOrderCommand(long PurchaseOrderId, string Reason)
    : ICommand<PurchaseOrderReadModel>;

public sealed class RejectPurchaseOrderCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<RejectPurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        RejectPurchaseOrderCommand request,
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

        if (purchaseOrder.Status.ToLower() != "pending")
            throw new InvalidOperationException(
                $"Can only reject pending purchase orders. Current status: {purchaseOrder.Status}");

        purchaseOrder.Status = "rejected";
        purchaseOrder.RejectionReason = request.Reason;
        await updateStore.SaveUpdateAsync(purchaseOrder, cancellationToken);

        return await readStore.FindByIdAsync(
                request.PurchaseOrderId,
                businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
