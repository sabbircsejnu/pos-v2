using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankReconciliationReadStore
{
    Task<IReadOnlyList<BankReconciliationSessionReadModel>> ListSessionsAsync(
        long businessId,
        long? bankAccountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationCandidateReadModel>> FindStatementCandidatesAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationCandidateReadModel>> FindGlCandidatesAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken);

    Task<BankReconciliationCalculationReadModel> GetCalculationAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BankAccountReadModel>> FindActiveBankAccountsAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationMatchReadModel>> FindMatchesAsync(
        long sessionId,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StatementAdjustmentReadModel>> FindAdjustmentsAsync(
        long bankAccountId,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StatementExceptionReadModel>> FindExceptionsAsync(
        long bankAccountId,
        long businessId,
        CancellationToken cancellationToken);
}

public interface IBankReconciliationTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
