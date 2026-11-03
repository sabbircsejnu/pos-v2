namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface ICashFlowOwnerAccountsProvider
{
    Task<CashFlowOwnerAccounts> GetAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public sealed record CashFlowOwnerAccounts(
    long? OwnerCapitalAccountId,
    long? OwnerDrawingsAccountId);
