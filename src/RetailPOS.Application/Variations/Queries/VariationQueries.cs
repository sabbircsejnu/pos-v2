using MediatR;
using RetailPOS.Application.Variations.Abstractions;

namespace RetailPOS.Application.Variations.Queries;

public sealed record GetVariationsQuery(bool IncludeInactive) : IRequest<IReadOnlyList<VariationReadModel>>;

public sealed class GetVariationsQueryHandler(IVariationReadStore readStore)
    : IRequestHandler<GetVariationsQuery, IReadOnlyList<VariationReadModel>>
{
    public Task<IReadOnlyList<VariationReadModel>> Handle(
        GetVariationsQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetAllAsync(request.IncludeInactive, cancellationToken);
}

public sealed record GetVariationByIdQuery(long VariationId) : IRequest<VariationReadModel>;

public sealed class GetVariationByIdQueryHandler(IVariationReadStore readStore)
    : IRequestHandler<GetVariationByIdQuery, VariationReadModel>
{
    public async Task<VariationReadModel> Handle(
        GetVariationByIdQuery request,
        CancellationToken cancellationToken) =>
        await readStore.GetByIdAsync(request.VariationId, cancellationToken)
        ?? throw new Exception("Variation not found");
}
