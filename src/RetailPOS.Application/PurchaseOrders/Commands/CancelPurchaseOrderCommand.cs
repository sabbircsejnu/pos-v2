using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record CancelPurchaseOrderCommand(long PurchaseOrderId, string Reason)
    : ICommand<PurchaseOrderReadModel>;

public sealed class CancelPurchaseOrderCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<CancelPurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        CancelPurchaseOrderCommand request,
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

        var cancelableStatuses = new[] { "draft", "pending", "sent_back", "approved" };
        if (!cancelableStatuses.Contains(purchaseOrder.Status.ToLower()))
            throw new InvalidOperationException(
                $"Cannot cancel a purchase order in '{purchaseOrder.Status}' status");

        purchaseOrder.Status = "cancelled";
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
