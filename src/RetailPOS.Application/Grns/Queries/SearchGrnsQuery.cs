using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record SearchGrnsQuery(
    long? PurchaseOrderId,
    string? Status,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize) : IQuery<GrnSearchReadModel>;

public sealed class SearchGrnsQueryHandler(
    IGrnReadStore store,
    IGrnTenantScope tenantScope)
    : IRequestHandler<SearchGrnsQuery, GrnSearchReadModel>
{
    public async Task<GrnSearchReadModel> Handle(
        SearchGrnsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.SearchAsync(
            request.PurchaseOrderId,
            request.Status,
            request.StartDate,
            request.EndDate,
            request.PageNumber,
            request.PageSize,
            businessId,
            cancellationToken);
    }
}
