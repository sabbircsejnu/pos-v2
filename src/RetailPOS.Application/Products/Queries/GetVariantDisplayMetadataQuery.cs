using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetVariantDisplayMetadataQuery
    : IQuery<IReadOnlyList<VariantDisplayVariationReadModel>>;

public sealed class GetVariantDisplayMetadataQueryHandler(
    IVariantDisplayMetadataReadStore store)
    : IRequestHandler<GetVariantDisplayMetadataQuery, IReadOnlyList<VariantDisplayVariationReadModel>>
{
    public Task<IReadOnlyList<VariantDisplayVariationReadModel>> Handle(
        GetVariantDisplayMetadataQuery request,
        CancellationToken cancellationToken) =>
        store.GetAsync(cancellationToken);
}
