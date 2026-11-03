using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetGrnsQuery(long? PurchaseOrderId, string? Status)
    : IQuery<IReadOnlyList<GrnReadModel>>;

public sealed class GetGrnsQueryHandler(
    IGrnReadStore store,
    IGrnTenantScope tenantScope)
    : IRequestHandler<GetGrnsQuery, IReadOnlyList<GrnReadModel>>
{
    public async Task<IReadOnlyList<GrnReadModel>> Handle(
        GetGrnsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.FindAllAsync(
            request.PurchaseOrderId,
            request.Status,
            businessId,
            cancellationToken);
    }
}
