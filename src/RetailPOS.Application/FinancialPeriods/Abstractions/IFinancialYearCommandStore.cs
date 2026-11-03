using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Abstractions;

public interface IFinancialYearCommandStore
{
    Task<FinancialYearReadModel> CreateAsync(
        long businessId,
        CreateFinancialYearCommand command,
        CancellationToken cancellationToken);

    Task<FinancialYearReadModel> UpdateAsync(
        long businessId,
        UpdateFinancialYearCommand command,
        CancellationToken cancellationToken);

    Task<FinancialYearReadModel> ActivateAsync(
        long businessId,
        ActivateFinancialYearCommand command,
        CancellationToken cancellationToken);
}
