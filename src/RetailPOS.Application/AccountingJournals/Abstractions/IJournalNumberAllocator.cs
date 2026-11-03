namespace RetailPOS.Application.AccountingJournals.Abstractions;

public interface IJournalNumberAllocator
{
    Task<string> AllocateNextJournalNumberAsync(
        long businessId,
        long? financialYearId,
        DateTime postingDate,
        long? actorUserId = null,
        CancellationToken cancellationToken = default);
}
