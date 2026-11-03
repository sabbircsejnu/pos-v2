using MediatR;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Application.Abstractions.Messaging;

namespace RetailPOS.Application.ApReports.Queries;

public sealed record GetApSubledgerQuery(ApPayablesReportFilter Filter)
    : IQuery<ApSubledgerReadModel>;

public sealed class GetApSubledgerQueryHandler(
    IApPayablesReportReadStore store,
    IApReportTenantScope tenantScope)
    : IRequestHandler<GetApSubledgerQuery, ApSubledgerReadModel>
{
    public async Task<ApSubledgerReadModel> Handle(
        GetApSubledgerQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetSubledgerAsync(request.Filter, businessId, cancellationToken);
    }
}
