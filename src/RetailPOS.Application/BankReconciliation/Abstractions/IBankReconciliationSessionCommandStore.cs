using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankReconciliationSessionCommandStore
{
    Task<BankReconciliationSessionReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        CreateBankReconciliationSessionInput input,
        CancellationToken cancellationToken);
}

public sealed record CreateBankReconciliationSessionInput(
    long BankAccountId,
    DateTime PeriodStart,
    DateTime PeriodEnd);
