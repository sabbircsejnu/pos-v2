using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierAdvanceUtilizationStore
{
    Task<SupplierAdvanceUtilizationAdvanceSnapshot?> FindPostedAdvanceAsync(
        long advanceId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierAdvanceUtilizationPayableSnapshot?> FindPayableAsync(
        long payableDocumentId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierAdvanceUtilizationReversalScope?> FindForReversalAsync(
        long utilizationId,
        long businessId,
        CancellationToken cancellationToken);

    Task ApplyAsync(
        long advanceId,
        long payableDocumentId,
        long businessId,
        decimal amount,
        DateTime effectiveDate,
        string referenceNumber,
        string? notes,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ReverseAsync(
        long utilizationId,
        long businessId,
        DateTime postingDate,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken);
}

public interface ISupplierAdvanceUtilizationJournalPoster
{
    Task<SupplierAdvanceUtilizationPostingAccounts> ResolvePostingAccountsAsync(
        CancellationToken cancellationToken);

    Task<long> PostAsync(
        SupplierAdvanceUtilizationJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken);
}

public interface ISupplierAdvanceUtilizationAuditWriter
{
    Task RecordAppliedAsync(
        SupplierAdvanceUtilizationAuditRecord record,
        CancellationToken cancellationToken);

    Task RecordReversedAsync(
        SupplierAdvanceUtilizationReversalAuditRecord record,
        CancellationToken cancellationToken);
}

public sealed record SupplierAdvanceUtilizationReversalScope(
    long AdvanceId,
    long PayableDocumentId,
    long JournalEntryId,
    long? OutletId);

public sealed record SupplierAdvanceUtilizationAdvanceSnapshot(
    long Id,
    long? OutletId,
    long SupplierId);

public sealed record SupplierAdvanceUtilizationPayableSnapshot(
    long Id,
    long SupplierId);

public sealed record SupplierAdvanceUtilizationPostingAccounts(
    long SupplierPayableAccountId,
    long SupplierAdvanceAccountId);

public sealed record SupplierAdvanceUtilizationJournalRequest(
    long BusinessId,
    long AdvanceId,
    long PayableDocumentId,
    DateTime EffectiveDate,
    string ReferenceNumber,
    string? Notes,
    long SupplierPayableAccountId,
    long SupplierAdvanceAccountId,
    decimal Amount);

public sealed record SupplierAdvanceUtilizationAuditRecord(
    long UtilizationId,
    long BusinessId,
    long AdvanceId,
    long PayableDocumentId,
    decimal Amount,
    long JournalEntryId,
    long ActorUserId,
    string? Reason);

public sealed record SupplierAdvanceUtilizationReversalAuditRecord(
    long UtilizationId,
    long BusinessId,
    long AdvanceId,
    long PayableDocumentId,
    decimal Amount,
    long JournalEntryId,
    long ReversalJournalEntryId,
    string Reason,
    long ActorUserId);
