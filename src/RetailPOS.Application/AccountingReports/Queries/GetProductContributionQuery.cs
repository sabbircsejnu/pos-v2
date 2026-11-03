using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Queries;

public sealed record GetProductContributionQuery(ProfitabilityFilter Filter)
    : IQuery<ProductContributionReadModel>;

public sealed class GetProductContributionQueryHandler(
    IProductContributionReadStore readStore,
    IAccountingReportTenantScope tenantScope)
    : IRequestHandler<GetProductContributionQuery, ProductContributionReadModel>
{
    public async Task<ProductContributionReadModel> Handle(
        GetProductContributionQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var (dateFrom, dateTo) =
            GetBusinessProfitabilityQueryHandler.RequireDates(request.Filter);
        if (request.Filter.LocationId.HasValue)
            GetBusinessProfitabilityQueryHandler.ValidateLocationType(
                request.Filter.LocationType);
        return await readStore.GetAsync(
            request.Filter with { DateFrom = dateFrom, DateTo = dateTo },
            businessId,
            cancellationToken);
    }
}

public sealed record GetComparativeProductContributionQuery(ProfitabilityFilter Filter)
    : IQuery<ComparativeProductContributionReadModel>;

public sealed class GetComparativeProductContributionQueryHandler(ISender sender)
    : IRequestHandler<
        GetComparativeProductContributionQuery,
        ComparativeProductContributionReadModel>
{
    public async Task<ComparativeProductContributionReadModel> Handle(
        GetComparativeProductContributionQuery request,
        CancellationToken cancellationToken)
    {
        GetComparativeBusinessProfitabilityQueryHandler.RequireComparativeDates(
            request.Filter,
            "Product contribution");
        var current = await sender.Send(
            new GetProductContributionQuery(request.Filter),
            cancellationToken);
        var comparison = await sender.Send(
            new GetProductContributionQuery(
                GetComparativeBusinessProfitabilityQueryHandler.ComparisonFilter(
                    request.Filter) with
                {
                    ProductId = request.Filter.ProductId,
                    CategoryId = request.Filter.CategoryId
                }),
            cancellationToken);
        return new ComparativeProductContributionReadModel(current, comparison);
    }
}
