using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IProductContributionReadStore
{
    Task<ProductContributionReadModel> GetAsync(
        ProfitabilityFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
