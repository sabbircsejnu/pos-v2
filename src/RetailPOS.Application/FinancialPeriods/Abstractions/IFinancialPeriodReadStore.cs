using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Abstractions;

public interface IFinancialPeriodReadStore
{
    Task<IReadOnlyList<FinancialYearReadModel>> GetFinancialYearsAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<bool> FinancialYearExistsAsync(
        long businessId,
        long financialYearId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingPeriodReadModel>> GetPeriodsAsync(
        long businessId,
        long financialYearId,
        CancellationToken cancellationToken);
}
