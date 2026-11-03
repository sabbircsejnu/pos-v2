using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record ApprovePurchaseOrderCommand(long PurchaseOrderId)
    : ICommand<PurchaseOrderReadModel>;

public sealed class ApprovePurchaseOrderCommandHandler(
    IPurchaseOrderUpdateStore updateStore,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<ApprovePurchaseOrderCommand, PurchaseOrderReadModel>
{
    public async Task<PurchaseOrderReadModel> Handle(
        ApprovePurchaseOrderCommand request,
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
                $"Can only approve pending purchase orders. Current status: {purchaseOrder.Status}");

        await updateStore.SaveStatusUpdateAsync(
            purchaseOrder,
            "approved",
            cancellationToken);

        return await readStore.FindByIdAsync(
                request.PurchaseOrderId,
                businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Purchase order with ID {request.PurchaseOrderId} not found");
    }
}
