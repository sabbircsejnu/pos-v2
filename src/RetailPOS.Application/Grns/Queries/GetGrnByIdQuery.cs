using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetGrnByIdQuery(long GrnId) : IQuery<GrnReadModel>;

public sealed class GetGrnByIdQueryHandler(
    IGrnReadStore store,
    IGrnReadScopeProvider scopeProvider)
    : IRequestHandler<GetGrnByIdQuery, GrnReadModel>
{
    public async Task<GrnReadModel> Handle(
        GetGrnByIdQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.FindByIdAsync(request.GrnId, scope, cancellationToken)
            ?? throw new KeyNotFoundException($"GRN with ID {request.GrnId} not found");
    }
}
