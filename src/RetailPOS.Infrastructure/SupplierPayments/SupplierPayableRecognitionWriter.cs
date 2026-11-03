using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.SupplierPayments;

public sealed class SupplierPayableRecognitionWriter(RetailPOSDbContext dbContext)
    : ISupplierPayableRecognitionWriter
{
    public async Task RecordFromPostedGrnAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        SupplierPayableRecognitionJournal postedJournal,
        long? supplierPayableAccountId,
        int defaultPaymentTermsDays,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                postedJournal.Status,
                JournalEntryStatuses.Posted,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A supplier payable document requires a posted purchase receipt journal.");

        var existing = dbContext.SupplierPayableDocuments.Local
            .FirstOrDefault(document => document.BusinessId == postedJournal.BusinessId
                && document.SourceJournalEntryId == postedJournal.Id)
            ?? await dbContext.SupplierPayableDocuments
                .FirstOrDefaultAsync(document => document.BusinessId == postedJournal.BusinessId
                    && document.SourceJournalEntryId == postedJournal.Id,
                    cancellationToken);
        if (existing is not null)
            return;

        var termsDays = purchaseOrder.Supplier.PaymentTermsDays ?? defaultPaymentTermsDays;
        var recognitionDate = postedJournal.PostingDate.Date;
        var dueDateBasis = grn.SupplierInvoiceDate?.Date ?? recognitionDate;
        var payableAmount = postedJournal.Lines
            .Where(line => line.AccountId == supplierPayableAccountId)
            .Sum(line => line.CreditAmount - line.DebitAmount);
        if (payableAmount <= 0m)
            throw new InvalidOperationException(
                "The posted receipt journal does not contain a supplier payable credit.");

        var now = DateTime.UtcNow;
        dbContext.SupplierPayableDocuments.Add(new SupplierPayableDocument
        {
            BusinessId = postedJournal.BusinessId,
            SupplierId = purchaseOrder.SupplierId,
            WarehouseId = purchaseOrder.WarehouseId,
            SourceDocumentType = "Grn",
            SourceDocumentId = grn.Id,
            SourceReference = postedJournal.ReferenceNumber ?? $"GRN-{grn.Id:D6}",
            SourceJournalEntryId = postedJournal.Id,
            OriginalPayableAmount = payableAmount,
            RecognitionDate = recognitionDate,
            SupplierInvoiceNumber = grn.SupplierInvoiceNumber,
            SupplierInvoiceDate = grn.SupplierInvoiceDate?.Date,
            PaymentTermsDays = termsDays,
            DueDate = dueDateBasis.AddDays(termsDays),
            Status = SupplierPayableDocumentStatuses.Open,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        });
    }
}
