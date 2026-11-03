using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Queries;

public sealed record SearchOtherIncomeQuery(OtherIncomeSearchFilter Filter)
    : IRequest<OtherIncomeListReadModel>;

public sealed class SearchOtherIncomeQueryHandler(IOtherIncomeReadStore readStore)
    : IRequestHandler<SearchOtherIncomeQuery, OtherIncomeListReadModel>
{
    public Task<OtherIncomeListReadModel> Handle(
        SearchOtherIncomeQuery request,
        CancellationToken cancellationToken) =>
        readStore.SearchAsync(request.Filter, cancellationToken);
}

public sealed record GetOtherIncomeByIdQuery(long BusinessId, long EntryId)
    : IRequest<OtherIncomeEntryReadModel>;

public sealed class GetOtherIncomeByIdQueryHandler(IOtherIncomeReadStore readStore)
    : IRequestHandler<GetOtherIncomeByIdQuery, OtherIncomeEntryReadModel>
{
    public Task<OtherIncomeEntryReadModel> Handle(
        GetOtherIncomeByIdQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetByIdAsync(request.BusinessId, request.EntryId, cancellationToken);
}
