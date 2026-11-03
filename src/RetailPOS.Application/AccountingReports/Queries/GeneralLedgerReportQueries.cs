using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetGeneralLedgerQuery(GeneralLedgerReportFilter Filter)
    : IQuery<GeneralLedgerReportReadModel>;

public sealed class GetGeneralLedgerQueryHandler(
    IGeneralLedgerReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetGeneralLedgerQuery, GeneralLedgerReportReadModel>
{
    public async Task<GeneralLedgerReportReadModel> Handle(
        GetGeneralLedgerQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetGeneralLedgerAsync(request.Filter, businessId, cancellationToken);
    }
}

public sealed record GetTrialBalanceQuery(GeneralLedgerReportFilter Filter)
    : IQuery<TrialBalanceReportReadModel>;

public sealed class GetTrialBalanceQueryHandler(
    IGeneralLedgerReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetTrialBalanceQuery, TrialBalanceReportReadModel>
{
    public async Task<TrialBalanceReportReadModel> Handle(
        GetTrialBalanceQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetTrialBalanceAsync(request.Filter, businessId, cancellationToken);
    }
}

public sealed record GetProfitLossQuery(GeneralLedgerReportFilter Filter)
    : IQuery<ProfitLossReportReadModel>;

public sealed class GetProfitLossQueryHandler(
    IGeneralLedgerReadStore store,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetProfitLossQuery, ProfitLossReportReadModel>
{
    public async Task<ProfitLossReportReadModel> Handle(
        GetProfitLossQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetProfitLossAsync(request.Filter, businessId, cancellationToken);
    }
}
