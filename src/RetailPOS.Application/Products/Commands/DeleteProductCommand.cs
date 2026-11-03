using MediatR;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record DeleteProductCommand(long ProductId) : IRequest;

public sealed class DeleteProductCommandHandler(
    IProductReadStore readStore,
    IProductDeleteStore deleteStore)
    : IRequestHandler<DeleteProductCommand>
{
    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await readStore.FindByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            throw new KeyNotFoundException($"Product with ID {request.ProductId} not found");
        }

        if (product.TotalStock > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete product with existing inventory (Stock: {product.TotalStock})");
        }

        await deleteStore.DeleteAsync(request.ProductId, cancellationToken);
    }
}
