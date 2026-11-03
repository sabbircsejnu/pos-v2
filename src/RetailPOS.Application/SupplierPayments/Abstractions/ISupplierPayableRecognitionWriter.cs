using RetailPOS.Core.Entities;

namespace RetailPOS.Application.SupplierPayments.Abstractions;

public interface ISupplierPayableRecognitionWriter
{
    Task RecordFromPostedGrnAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        SupplierPayableRecognitionJournal postedJournal,
        long? supplierPayableAccountId,
        int defaultPaymentTermsDays,
        long? actorUserId,
        CancellationToken cancellationToken);
}

public sealed record SupplierPayableRecognitionJournal(
    long Id,
    long BusinessId,
    string Status,
    DateTime PostingDate,
    string? ReferenceNumber,
    IReadOnlyList<SupplierPayableRecognitionJournalLine> Lines);

public sealed record SupplierPayableRecognitionJournalLine(
    long AccountId,
    decimal DebitAmount,
    decimal CreditAmount);
