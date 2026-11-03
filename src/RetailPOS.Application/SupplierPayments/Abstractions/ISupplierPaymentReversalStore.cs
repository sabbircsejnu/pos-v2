using RetailPOS.Application.SupplierPayments.ReadModels;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierPaymentReversalStore
{
    Task<SupplierPaymentReversalScope?> FindForReversalAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierPaymentReadModel> ReverseAsync(
        long paymentId,
        long businessId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<SupplierPaymentReadModel> FindByIdAsync(
        long paymentId,
        CancellationToken cancellationToken);
}

public interface ISupplierPaymentReversalJournalPoster
{
    Task<long> ReverseJournalAsync(
        long businessId,
        long journalEntryId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken);
}

public sealed record SupplierPaymentReversalScope(long? OutletId);

public sealed record SupplierPaymentReversalAuditRecord(
    long PaymentId,
    long BusinessId,
    long? OutletId,
    long SupplierId,
    decimal Amount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? ReversalJournalEntryId,
    string ReversalReason,
    DateTime ReversedAt,
    string AllocationSummary,
    long ActorUserId);

public interface ISupplierPaymentReversalAuditWriter
{
    Task RecordReversedAsync(
        SupplierPaymentReversalAuditRecord record,
        CancellationToken cancellationToken);
}
