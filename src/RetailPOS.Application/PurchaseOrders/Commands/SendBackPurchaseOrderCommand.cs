using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record SendBackPurchaseOrderCommand(long PurchaseOrderId, string Reason)
    : ICommand<PurchaseOrderReadModel>;

public sealed class SendBackPurchaseOrderCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<SendBackPurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        SendBackPurchaseOrderCommand request,
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
                $"Can only send back pending purchase orders. Current status: {purchaseOrder.Status}");

        purchaseOrder.Status = "sent_back";
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
