using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Queries;

public sealed record GetPurchaseSummaryQuery(DateTime StartDate, DateTime EndDate)
    : IQuery<PurchaseSummaryReadModel>;

public sealed class GetPurchaseSummaryQueryHandler(
    IPurchaseReportReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<GetPurchaseSummaryQuery, PurchaseSummaryReadModel>
{
    public async Task<PurchaseSummaryReadModel> Handle(
        GetPurchaseSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetSummaryAsync(
            request.StartDate,
            request.EndDate,
            businessId,
            cancellationToken);
    }
}

public sealed record GetPurchasesBySupplierQuery(DateTime StartDate, DateTime EndDate)
    : IQuery<IReadOnlyList<PurchaseBySupplierReadModel>>;

public sealed class GetPurchasesBySupplierQueryHandler(
    IPurchaseReportReadStore readStore,
    IPurchaseOrderTenantScope tenantScope)
    : IRequestHandler<
        GetPurchasesBySupplierQuery,
        IReadOnlyList<PurchaseBySupplierReadModel>>
{
    public async Task<IReadOnlyList<PurchaseBySupplierReadModel>> Handle(
        GetPurchasesBySupplierQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetBySupplierAsync(
            request.StartDate,
            request.EndDate,
            businessId,
            cancellationToken);
    }
}
