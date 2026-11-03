using MediatR;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record DeleteProductImageCommand(long ProductId, long ImageId) : IRequest;

public sealed class DeleteProductImageCommandHandler(
    IProductMediaStore store,
    IProductImageFileStore files) : IRequestHandler<DeleteProductImageCommand>
{
    public async Task Handle(DeleteProductImageCommand request, CancellationToken cancellationToken)
    {
        var deletion = await store.DeleteAsync(request.ProductId, request.ImageId, cancellationToken);
        await files.TryDeleteAsync(
            new ProductImageFilePaths(deletion.OriginalPath, deletion.MediumPath, deletion.ThumbPath),
            cancellationToken);

        if (deletion.WasPrimary)
            await store.PromoteNextPrimaryAsync(request.ProductId, cancellationToken);
    }
}
