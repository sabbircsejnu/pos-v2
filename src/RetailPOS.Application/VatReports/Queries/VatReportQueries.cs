using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.VatReports.Abstractions;
using RetailPOS.Application.VatReports.ReadModels;

namespace RetailPOS.Application.VatReports.Queries;

public sealed record GetVatTransactionsQuery(VatReportFilter Filter)
    : IQuery<IReadOnlyList<VatTransactionReadModel>>;

public sealed class GetVatTransactionsQueryHandler(
    IVatReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetVatTransactionsQuery, IReadOnlyList<VatTransactionReadModel>>
{
    public async Task<IReadOnlyList<VatTransactionReadModel>> Handle(
        GetVatTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetTransactionsAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}

public sealed record GetVatSummaryQuery(VatReportFilter Filter)
    : IQuery<VatSummaryReadModel>;

public sealed class GetVatSummaryQueryHandler(
    IVatReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetVatSummaryQuery, VatSummaryReadModel>
{
    public async Task<VatSummaryReadModel> Handle(
        GetVatSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetSummaryAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}

public sealed record GetVatReconciliationQuery(VatReportFilter Filter)
    : IQuery<VatReconciliationReadModel>;

public sealed class GetVatReconciliationQueryHandler(
    IVatReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetVatReconciliationQuery, VatReconciliationReadModel>
{
    public async Task<VatReconciliationReadModel> Handle(
        GetVatReconciliationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetReconciliationAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}

public sealed record GetVatExceptionsQuery(VatReportFilter Filter)
    : IQuery<IReadOnlyList<VatExceptionReadModel>>;

public sealed class GetVatExceptionsQueryHandler(
    IVatReportReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetVatExceptionsQuery, IReadOnlyList<VatExceptionReadModel>>
{
    public async Task<IReadOnlyList<VatExceptionReadModel>> Handle(
        GetVatExceptionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetExceptionsAsync(
            request.Filter,
            businessId,
            cancellationToken);
    }
}
