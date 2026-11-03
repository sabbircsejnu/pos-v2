using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.ReadModels;

namespace RetailPOS.Application.ApReports.Queries;

public sealed record GetApReconciliationQuery(ApPayablesReportFilter Filter)
    : IQuery<ApReconciliationReadModel>;

public sealed class GetApReconciliationQueryHandler(
    IApPayablesReportReadStore store,
    IApReportTenantScope tenantScope)
    : IRequestHandler<GetApReconciliationQuery, ApReconciliationReadModel>
{
    public async Task<ApReconciliationReadModel> Handle(
        GetApReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetPayableReconciliationAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}

public sealed record GetSupplierAdvanceReconciliationQuery(ApPayablesReportFilter Filter)
    : IQuery<ApReconciliationReadModel>;

public sealed class GetSupplierAdvanceReconciliationQueryHandler(
    IApPayablesReportReadStore store,
    IApReportTenantScope tenantScope)
    : IRequestHandler<GetSupplierAdvanceReconciliationQuery, ApReconciliationReadModel>
{
    public async Task<ApReconciliationReadModel> Handle(
        GetSupplierAdvanceReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await store.GetAdvanceReconciliationAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}
