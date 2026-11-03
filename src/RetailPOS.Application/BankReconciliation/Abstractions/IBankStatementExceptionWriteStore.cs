namespace RetailPOS.Application.BankReconciliation.Abstractions;

public interface IBankStatementExceptionWriteStore
{
    Task CreateAsync(
        long statementLineId,
        long businessId,
        string classification,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ResolveAsync(
        long exceptionId,
        long businessId,
        string resolutionNotes,
        long actorUserId,
        CancellationToken cancellationToken);
}
