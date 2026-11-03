using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankAccountCommandStore
{
    Task<BankAccountReadModel> CreateAsync(
        long businessId,
        long? actorUserId,
        CreateBankAccountInput input,
        CancellationToken cancellationToken);
}

public sealed record CreateBankAccountInput(
    string DisplayName,
    long ChartAccountId,
    long? OutletId,
    string? AccountReference,
    bool IsActive,
    DateTime? ReconciliationStartDate,
    decimal? OpeningStatementBalance);
