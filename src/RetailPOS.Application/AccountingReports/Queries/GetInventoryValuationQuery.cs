using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetInventoryValuationQuery(InventoryValuationFilter Filter)
    : IQuery<InventoryValuationReadModel>;

public sealed class GetInventoryValuationQueryHandler(
    IInventoryValuationReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetInventoryValuationQuery, InventoryValuationReadModel>
{
    public async Task<InventoryValuationReadModel> Handle(
        GetInventoryValuationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}
