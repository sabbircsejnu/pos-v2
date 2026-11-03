using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierPaymentCreationStore
{
    Task<bool> SupplierExistsAsync(long businessId, long supplierId, CancellationToken cancellationToken);
    Task<bool> PaymentAccountExistsAsync(long businessId, long paymentAccountId, CancellationToken cancellationToken);
    Task CreateDraftAsync(SupplierPayment payment, CancellationToken cancellationToken);
    Task<SupplierPayableDocument?> FindPayableDocumentAsync(
        long businessId,
        long supplierId,
        long payableDocumentId,
        CancellationToken cancellationToken);
    Task<Bill?> FindBillAsync(
        long businessId,
        long supplierId,
        long billId,
        CancellationToken cancellationToken);
    Task<decimal> GetPayableOutstandingAsync(long payableDocumentId, CancellationToken cancellationToken);
    Task CreateAllocationsAsync(
        IReadOnlyList<SupplierPaymentAllocation> allocations,
        CancellationToken cancellationToken);
    Task<SupplierPaymentReadModel> FindByIdAsync(long paymentId, CancellationToken cancellationToken);
}

public interface ISupplierPaymentCreationAuthorizer
{
    Task<SupplierPaymentCreationScope> AuthorizeAsync(
        long? outletId,
        CancellationToken cancellationToken);
}

public interface ISupplierPaymentWorkflowAuthorizer
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
    Task<long?> EnforceOutletAsync(long? outletId, CancellationToken cancellationToken);
}

public interface ISupplierPaymentCreationAuditWriter
{
    Task RecordCreatedAsync(
        SupplierPaymentCreationAuditRecord record,
        CancellationToken cancellationToken);
}

public interface ISupplierPaymentSubmissionStore
{
    Task<SupplierPaymentSubmissionSnapshot?> FindForSubmissionAsync(
        long paymentId,
        long businessId,
        CancellationToken cancellationToken);
    Task SubmitAsync(
        long paymentId,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken);
    Task<SupplierPaymentReadModel> FindByIdAsync(
        long paymentId,
        CancellationToken cancellationToken);
}

public interface ISupplierPaymentSubmissionAuditWriter
{
    Task RecordSubmittedAsync(
        SupplierPaymentSubmissionAuditRecord record,
        CancellationToken cancellationToken);
}

public sealed record SupplierPaymentCreationScope(long BusinessId, long? OutletId);

public sealed record SupplierPaymentSubmissionSnapshot(
    long Id,
    long? OutletId,
    string Status,
    long SupplierId,
    decimal Amount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? ReversalJournalEntryId);

public sealed record SupplierPaymentSubmissionAuditRecord(
    long PaymentId,
    long BusinessId,
    long? OutletId,
    long SupplierId,
    decimal Amount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? ReversalJournalEntryId,
    long ActorUserId);

public sealed record SupplierPaymentCreationAuditRecord(
    long PaymentId,
    long BusinessId,
    long? OutletId,
    long SupplierId,
    decimal Amount,
    string PaymentMethod,
    long PaymentAccountId,
    long? JournalEntryId,
    long? ReversalJournalEntryId,
    long ActorUserId,
    int AllocationCount,
    decimal AllocationTotal);
