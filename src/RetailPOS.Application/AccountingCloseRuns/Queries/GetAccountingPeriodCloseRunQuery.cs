using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Application.AccountingCloseRuns.ReadModels;

namespace RetailPOS.Application.AccountingCloseRuns.Queries;

public sealed record GetAccountingPeriodCloseRunQuery(long Id)
    : IRequest<AccountingPeriodCloseRunReadModel>;

public sealed class GetAccountingPeriodCloseRunQueryHandler(
    IAccountingPeriodCloseRunReadStore readStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<GetAccountingPeriodCloseRunQuery, AccountingPeriodCloseRunReadModel>
{
    public async Task<AccountingPeriodCloseRunReadModel> Handle(
        GetAccountingPeriodCloseRunQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetAsync(businessId, request.Id, cancellationToken);
    }
}

public sealed record ListAccountingPeriodCloseRunsQuery(long? AccountingPeriodId)
    : IRequest<IReadOnlyList<AccountingPeriodCloseRunReadModel>>;

public sealed class ListAccountingPeriodCloseRunsQueryHandler(
    IAccountingPeriodCloseRunReadStore readStore,
    IAccountingPeriodCloseTenantScope tenantScope)
    : IRequestHandler<ListAccountingPeriodCloseRunsQuery, IReadOnlyList<AccountingPeriodCloseRunReadModel>>
{
    public async Task<IReadOnlyList<AccountingPeriodCloseRunReadModel>> Handle(
        ListAccountingPeriodCloseRunsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListAsync(
            businessId,
            request.AccountingPeriodId,
            cancellationToken);
    }
}
