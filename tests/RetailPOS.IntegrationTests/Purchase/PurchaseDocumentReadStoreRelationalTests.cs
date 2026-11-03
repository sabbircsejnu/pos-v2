using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Tests.Purchase;

public sealed class PurchaseDocumentReadStoreRelationalTests
{
    [Fact]
    public async Task PdfReadStores_PreserveEveryProjectionAndTrustedScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        PurchaseDocumentSeed.Add(db);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var orders = new PurchaseOrderReadStore(db);
        var grns = new GrnReadStore(db);
        var order = Assert.IsType<RetailPOS.Application.PurchaseOrders.ReadModels.PurchaseOrderReadModel>(
            await orders.FindByIdAsync(20, 1, CancellationToken.None));
        Assert.Equal("PO-TEST-20", order.PoNumber);
        Assert.Equal("Supplier 1", order.SupplierName);
        Assert.Equal("Warehouse 1", order.WarehouseName);
        Assert.Equal("Buyer", order.CreatedByName);
        Assert.Equal(40, order.LatestGrnId);
        Assert.Equal("PO notes", order.Notes);
        Assert.Equal("SKU-1", Assert.Single(order.Items).Sku);
        Assert.Equal("Color=Blue", order.Items[0].VariantAttributes);
        Assert.Null(await orders.FindByIdAsync(21, 1, CancellationToken.None));
        Assert.NotNull(await orders.FindByIdAsync(21, null, CancellationToken.None));
        Assert.Null(await orders.FindByIdAsync(999, null, CancellationToken.None));

        var grn = Assert.IsType<RetailPOS.Application.Grns.ReadModels.GrnReadModel>(
            await grns.FindByIdAsync(40, 1, CancellationToken.None));
        Assert.Equal(20, grn.PurchaseOrderId);
        Assert.Equal("Supplier 1", grn.SupplierName);
        Assert.Equal("Warehouse 1", grn.WarehouseName);
        Assert.Equal("Buyer", grn.CreatedByName);
        Assert.Equal(7, grn.CreatedBy);
        Assert.Equal("INV-40", grn.SupplierInvoiceNumber);
        Assert.Equal("Receipt notes", grn.Notes);
        var item = Assert.Single(grn.Items);
        Assert.Equal(5, item.OrderedQuantity);
        Assert.Equal(4, item.ReceivedQuantity);
        Assert.Equal(10m, item.UnitPrice);
        Assert.Equal(8m, item.UnitCost);
        Assert.Equal("Item notes", item.Notes);
        Assert.Null(await grns.FindByIdAsync(41, 1, CancellationToken.None));
        Assert.NotNull(await grns.FindByIdAsync(41, null, CancellationToken.None));
        Assert.Null(await grns.FindByIdAsync(999, null, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orders.FindByIdAsync(20, 1, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            grns.FindByIdAsync(40, 1, cancellation.Token));
    }
}
