using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record AssignProductVariationsCommand(
    long ProductId,
    IReadOnlyList<long> VariationIds,
    IReadOnlyDictionary<long, List<long>> SelectedOptionsByVariation) : ICommand;

public sealed class AssignProductVariationsCommandHandler(
    IProductVariationCommandStore store)
    : IRequestHandler<AssignProductVariationsCommand>
{
    public Task Handle(
        AssignProductVariationsCommand request,
        CancellationToken cancellationToken) =>
        store.AssignAsync(
            request.ProductId,
            request.VariationIds,
            request.SelectedOptionsByVariation,
            cancellationToken);
}
