using MediatR;
using RetailPOS.Application.Bills.Abstractions;

namespace RetailPOS.Application.Bills.Queries;

public sealed record SearchBillsQuery(
    string? Status,
    long? SupplierId,
    int PageNumber,
    int PageSize) : IRequest<BillSearchReadModel>;

public sealed class SearchBillsQueryHandler(
    IBillReadStore readStore,
    IBillReadScopeProvider scopeProvider)
    : IRequestHandler<SearchBillsQuery, BillSearchReadModel>
{
    public async Task<BillSearchReadModel> Handle(
        SearchBillsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.SearchAsync(
            request.Status,
            request.SupplierId,
            request.PageNumber,
            request.PageSize,
            businessId,
            cancellationToken);
    }
}

public sealed record GetBillByIdQuery(long BillId) : IRequest<BillReadModel>;

public sealed class GetBillByIdQueryHandler(
    IBillReadStore readStore,
    IBillReadScopeProvider scopeProvider)
    : IRequestHandler<GetBillByIdQuery, BillReadModel>
{
    public async Task<BillReadModel> Handle(
        GetBillByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetByIdAsync(request.BillId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bill with ID {request.BillId} not found");
    }
}

public sealed record GetBillSummaryQuery : IRequest<BillSummaryReadModel>;

public sealed class GetBillSummaryQueryHandler(
    IBillReadStore readStore,
    IBillReadScopeProvider scopeProvider)
    : IRequestHandler<GetBillSummaryQuery, BillSummaryReadModel>
{
    public async Task<BillSummaryReadModel> Handle(
        GetBillSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetSummaryAsync(businessId, cancellationToken);
    }
}
