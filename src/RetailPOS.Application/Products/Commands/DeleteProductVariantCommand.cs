using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record DeleteProductVariantCommand(long VariantId) : ICommand;

public sealed class DeleteProductVariantCommandHandler(IProductVariantMaintenanceStore store)
    : IRequestHandler<DeleteProductVariantCommand>
{
    public async Task Handle(DeleteProductVariantCommand request, CancellationToken cancellationToken)
    {
        var data = await store.GetDeleteDataAsync(request.VariantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Variant with ID {request.VariantId} not found");

        if (data.HasSales)
        {
            throw new InvalidOperationException("Cannot delete a variant that has associated sales records.");
        }
        if (data.HasPurchaseOrderItems)
        {
            throw new InvalidOperationException("Cannot delete a variant that has associated purchase order items.");
        }
        if (data.HasStockTransferItems)
        {
            throw new InvalidOperationException("Cannot delete a variant that has associated stock transfer items.");
        }
        if (data.HasStockAdjustmentLines)
        {
            throw new InvalidOperationException("Cannot delete a variant that has associated stock adjustments.");
        }

        await store.DeleteAsync(request.VariantId, cancellationToken);
    }
}
