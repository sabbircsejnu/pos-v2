using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record SubmitPurchaseOrderForApprovalCommand(long PurchaseOrderId)
    : ICommand<PurchaseOrderReadModel>;

public sealed class SubmitPurchaseOrderForApprovalCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<SubmitPurchaseOrderForApprovalCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        SubmitPurchaseOrderForApprovalCommand request,
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

        var currentStatus = purchaseOrder.Status.ToLower();
        if (currentStatus != "draft" && currentStatus != "sent_back")
            throw new InvalidOperationException(
                $"Can only submit draft or sent-back purchase orders. Current status: {purchaseOrder.Status}");

        purchaseOrder.Status = "pending";
        purchaseOrder.RejectionReason = null;
        await updateStore.SaveUpdateAsync(purchaseOrder, cancellationToken);

        return await readStore.FindByIdAsync(
                request.PurchaseOrderId,
                businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
