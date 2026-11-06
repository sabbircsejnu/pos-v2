using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Sales.Queries;

public sealed record SearchSalesQuery(SaleSearchCriteria Criteria)
    : IQuery<SaleSearchReadModel>;

public sealed class SearchSalesQueryHandler(
    ISaleReadStore store,
    ISaleReadScopeProvider scopeProvider,
    ICurrentCompanyTimeZone companyTimeZone)
    : IRequestHandler<SearchSalesQuery, SaleSearchReadModel>
{
    public async Task<SaleSearchReadModel> Handle(
        SearchSalesQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(request.Criteria.OutletId, cancellationToken);
        var criteria = request.Criteria;
        if (criteria.StartDate.HasValue || criteria.EndDate.HasValue)
        {
            // A picked date covers that whole day in the Company time zone.
            var zone = await companyTimeZone.GetTimeZoneIdAsync(cancellationToken);
            criteria = criteria with
            {
                StartDate = criteria.StartDate.HasValue ? CompanyDates.StartUtc(criteria.StartDate.Value, zone) : null,
                EndDate = criteria.EndDate.HasValue ? CompanyDates.EndInclusiveUtc(criteria.EndDate.Value, zone) : null
            };
        }

        return await store.SearchAsync(criteria, scope, cancellationToken);
    }
}
