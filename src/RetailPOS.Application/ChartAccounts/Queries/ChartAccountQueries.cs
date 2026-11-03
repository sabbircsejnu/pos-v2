using MediatR;
using RetailPOS.Application.ChartAccounts.Abstractions;

namespace RetailPOS.Application.ChartAccounts.Queries;

public sealed record GetChartAccountsQuery(
    string? Query,
    string? AccountType,
    bool? IsActive) : IRequest<IReadOnlyList<ChartAccountReadModel>>;

public sealed class GetChartAccountsQueryHandler(
    IChartAccountReadStore readStore,
    IChartAccountReadScope scope)
    : IRequestHandler<GetChartAccountsQuery, IReadOnlyList<ChartAccountReadModel>>
{
    public Task<IReadOnlyList<ChartAccountReadModel>> Handle(
        GetChartAccountsQuery request,
        CancellationToken cancellationToken) =>
        readStore.GetAllAsync(
            scope.RequireBusinessId(),
            request.Query,
            request.AccountType,
            request.IsActive,
            cancellationToken);
}

public sealed record GetChartAccountByIdQuery(long Id) : IRequest<ChartAccountReadModel>;

public sealed class GetChartAccountByIdQueryHandler(
    IChartAccountReadStore readStore,
    IChartAccountReadScope scope)
    : IRequestHandler<GetChartAccountByIdQuery, ChartAccountReadModel>
{
    public async Task<ChartAccountReadModel> Handle(
        GetChartAccountByIdQuery request,
        CancellationToken cancellationToken)
    {
        return await readStore.GetByIdAsync(
                scope.RequireBusinessId(),
                request.Id,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Chart account with ID {request.Id} was not found.");
    }
}
