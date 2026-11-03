using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Abstractions;

public interface IAccountingPeriodCommandStore
{
    Task<AccountingPeriodReadModel> CreateAsync(
        long businessId,
        CreateAccountingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<AccountingPeriodReadModel> UpdateAsync(
        long businessId,
        UpdateAccountingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<AccountingPeriodReadModel> UpdateStatusAsync(
        long businessId,
        UpdateAccountingPeriodStatusCommand command,
        CancellationToken cancellationToken);
}
