using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.SupplierPayments.Abstractions;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPaymentReversalJournalPoster(
    IManualJournalDraftStore journalStore)
    : ISupplierPaymentReversalJournalPoster
{
    public Task<long> ReverseJournalAsync(
        long businessId,
        long journalEntryId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken) =>
        journalStore.ReverseAsync(
            businessId,
            actorUserId,
            journalEntryId,
            new ManualJournalReversalInput(
                reason,
                postingDate.Date,
                postingDate.Date),
            cancellationToken);
}
