using MediatR;
using RetailPOS.Application.OpeningBalances.Abstractions;

namespace RetailPOS.Application.OpeningBalances.Queries;

public sealed record GetOpeningBalanceStatusQuery(long BusinessId)
    : IRequest<OpeningBalanceStatus>;

public sealed class GetOpeningBalanceStatusQueryHandler(
    IOpeningBalanceQueryStore queryStore)
    : IRequestHandler<GetOpeningBalanceStatusQuery, OpeningBalanceStatus>
{
    public Task<OpeningBalanceStatus> Handle(
        GetOpeningBalanceStatusQuery request,
        CancellationToken cancellationToken) =>
        queryStore.GetStatusAsync(request.BusinessId, cancellationToken);
}
