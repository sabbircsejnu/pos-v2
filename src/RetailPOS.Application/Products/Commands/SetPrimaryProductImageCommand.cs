using MediatR;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record SetPrimaryProductImageCommand(long ProductId, long ImageId) : IRequest<long>;

public sealed class SetPrimaryProductImageCommandHandler(IProductMediaStore store)
    : IRequestHandler<SetPrimaryProductImageCommand, long>
{
    public Task<long> Handle(SetPrimaryProductImageCommand request, CancellationToken cancellationToken) =>
        store.SetPrimaryAsync(request.ProductId, request.ImageId, cancellationToken);
}
