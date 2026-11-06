using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleTodaySummaryReadStore
{
    Task<IReadOnlyList<SaleSummaryLineReadModel>> GetAsync(
        SaleReadScope scope,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken cancellationToken);
}
