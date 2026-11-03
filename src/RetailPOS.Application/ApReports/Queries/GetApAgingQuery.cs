using MediatR;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Application.Abstractions.Messaging;

namespace RetailPOS.Application.ApReports.Queries;

public sealed record GetApAgingQuery(ApPayablesReportFilter Filter)
    : IQuery<ApAgingReadModel>;

public sealed class GetApAgingQueryHandler(
    IApPayablesReportReadStore store,
    IApReportTenantScope tenantScope)
    : IRequestHandler<GetApAgingQuery, ApAgingReadModel>
{
    public async Task<ApAgingReadModel> Handle(
        GetApAgingQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetAgingAsync(request.Filter, businessId, cancellationToken);
    }
}
