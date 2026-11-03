using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;
using RetailPOS.Application.FinancialYearCloseRuns.ReadModels;

namespace RetailPOS.Application.FinancialYearCloseRuns.Queries;

public sealed record GetAccountingFinancialYearCloseRunQuery(long Id)
    : IRequest<AccountingFinancialYearCloseRunReadModel>;

public sealed class GetAccountingFinancialYearCloseRunQueryHandler(
    IAccountingFinancialYearCloseRunReadStore readStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<GetAccountingFinancialYearCloseRunQuery, AccountingFinancialYearCloseRunReadModel>
{
    public async Task<AccountingFinancialYearCloseRunReadModel> Handle(
        GetAccountingFinancialYearCloseRunQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetAsync(businessId, request.Id, cancellationToken);
    }
}

public sealed record ListAccountingFinancialYearCloseRunsQuery(long? FinancialYearId)
    : IRequest<IReadOnlyList<AccountingFinancialYearCloseRunReadModel>>;

public sealed class ListAccountingFinancialYearCloseRunsQueryHandler(
    IAccountingFinancialYearCloseRunReadStore readStore,
    IAccountingFinancialYearCloseTenantScope tenantScope)
    : IRequestHandler<ListAccountingFinancialYearCloseRunsQuery, IReadOnlyList<AccountingFinancialYearCloseRunReadModel>>
{
    public async Task<IReadOnlyList<AccountingFinancialYearCloseRunReadModel>> Handle(
        ListAccountingFinancialYearCloseRunsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListAsync(
            businessId,
            request.FinancialYearId,
            cancellationToken);
    }
}
