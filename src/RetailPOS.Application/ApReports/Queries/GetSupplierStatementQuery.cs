using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;

namespace RetailPOS.Application.ApReports.Queries;

public sealed record GetSupplierStatementQuery(ApPayablesReportFilter Filter)
    : IQuery<SupplierStatementReadModel>;

public sealed class GetSupplierStatementQueryHandler(
    IApPayablesReportReadStore store,
    IApReportTenantScope tenantScope)
    : IRequestHandler<GetSupplierStatementQuery, SupplierStatementReadModel>
{
    public async Task<SupplierStatementReadModel> Handle(
        GetSupplierStatementQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetSupplierStatementAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}
