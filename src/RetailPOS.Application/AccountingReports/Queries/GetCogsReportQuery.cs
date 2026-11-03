using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetCogsReportQuery(CogsReportFilter Filter)
    : IQuery<CogsReportReadModel>;

public sealed class GetCogsReportQueryHandler(
    ICogsReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetCogsReportQuery, CogsReportReadModel>
{
    public async Task<CogsReportReadModel> Handle(
        GetCogsReportQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}
