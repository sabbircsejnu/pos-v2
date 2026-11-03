using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.SupplierPayments;

namespace RetailPOS.Tests.Accounting;

public class SupplierPayableRecognitionWriterTests
{
    [Fact]
    public async Task RecordFromPostedGrnAsync_CreatesIdempotentPayableWithInvoiceDateTermSnapshot()
    {
        await using var db = CreateDb();
        var supplier = new Supplier { Id = 10, Name = "Supplier", PaymentTermsDays = 45 };
        var purchaseOrder = new PurchaseOrder { Id = 20, SupplierId = 10, WarehouseId = 30, Supplier = supplier };
        var grn = new Grn { Id = 40, PoId = 20, ReceivedDate = new DateTime(2026, 2, 10), SupplierInvoiceNumber = "INV-100", SupplierInvoiceDate = new DateTime(2026, 2, 1) };
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(supplier);
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "Warehouse", Address = "A" });
        await db.SaveChangesAsync();
        var journal = PostedJournal(100, 1, 200, 125m, new DateTime(2026, 2, 10));
        var writer = new SupplierPayableRecognitionWriter(db);

        await writer.RecordFromPostedGrnAsync(
            grn,
            purchaseOrder,
            journal,
            200,
            30,
            7,
            CancellationToken.None);
        await writer.RecordFromPostedGrnAsync(
            grn,
            purchaseOrder,
            journal,
            200,
            30,
            7,
            CancellationToken.None);

        var document = Assert.Single(db.SupplierPayableDocuments.Local);
        Assert.Equal(125m, document.OriginalPayableAmount);
        Assert.Equal(45, document.PaymentTermsDays);
        Assert.Equal(new DateTime(2026, 3, 18), document.DueDate);
        Assert.Equal("INV-100", document.SupplierInvoiceNumber);
        Assert.Equal(100, document.SourceJournalEntryId);
        Assert.Equal(7, document.CreatedBy);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.SupplierPayableDocuments.CountAsync());
    }

    [Fact]
    public async Task RecordFromPostedGrnAsync_UsesReceiptDateAndBusinessDefaultWhenInvoiceAndSupplierTermsAreMissing()
    {
        await using var db = CreateDb();
        var supplier = new Supplier { Id = 10, Name = "Supplier" };
        var purchaseOrder = new PurchaseOrder { Id = 20, SupplierId = 10, WarehouseId = 30, Supplier = supplier };
        var grn = new Grn { Id = 40, PoId = 20, ReceivedDate = new DateTime(2026, 2, 10) };
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(supplier);
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "Warehouse", Address = "A" });
        await db.SaveChangesAsync();
        await new SupplierPayableRecognitionWriter(db).RecordFromPostedGrnAsync(
            grn,
            purchaseOrder,
            PostedJournal(100, 1, 200, 125m, grn.ReceivedDate),
            200,
            30,
            7,
            CancellationToken.None);

        var document = Assert.Single(db.SupplierPayableDocuments.Local);
        Assert.Equal(30, document.PaymentTermsDays);
        Assert.Equal(new DateTime(2026, 3, 12), document.DueDate);
    }

    private static SupplierPayableRecognitionJournal PostedJournal(
        long id,
        long businessId,
        long payableAccountId,
        decimal amount,
        DateTime date) =>
        new(
            id,
            businessId,
            JournalEntryStatuses.Posted,
            date,
            "GRN-000040",
            [new SupplierPayableRecognitionJournalLine(payableAccountId, 0m, amount)]);

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase($"SupplierPayable_{Guid.NewGuid()}").Options);
}