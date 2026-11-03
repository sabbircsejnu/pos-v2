using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record DeletePurchaseOrderCommand(long PurchaseOrderId) : ICommand<bool>;

public sealed class DeletePurchaseOrderCommandHandler(
    IPurchaseOrderDeletionStore deletionStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<DeletePurchaseOrderCommand, bool>
{
    public async Task<bool> Handle(
        DeletePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (!await deletionStore.CanDeleteAsync(
                request.PurchaseOrderId,
                businessId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot delete purchase order: either it's not in draft/pending status or has associated GRNs");
        }

        return await deletionStore.DeleteAsync(
            request.PurchaseOrderId,
            businessId,
            cancellationToken);
    }
}
