using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public class StockTransferUpdateSqliteTests : IClassFixture<SqliteRetailPosApiFactory>
{
    private readonly SqliteRetailPosApiFactory _factory;

    public StockTransferUpdateSqliteTests(SqliteRetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TransferUpdate_WhenReplacementSaveFails_RollsBackHeaderAndOriginalLines()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            notes = "original-draft",
            items = new[]
            {
                new { variantId = 301L, quantity = 1, unitCost = 10m, remarks = "original-line" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        long transferId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            transferId = (await db.StockTransfers.SingleAsync()).Id;
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_transfer_item_insert BEFORE INSERT ON stock_transfer_items " +
                "BEGIN SELECT RAISE(ABORT, 'forced update rollback'); END;");
        }

        var updateResponse = await client.PutAsJsonAsync($"/api/stock-transfers/{transferId}", new
        {
            transferType = "return",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date.AddDays(-1),
            notes = "must-rollback",
            items = new[]
            {
                new { variantId = 301L, quantity = 3, unitCost = 99m, remarks = "must-rollback" }
            }
        });

        Assert.Equal(HttpStatusCode.InternalServerError, updateResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await verifyDb.StockTransfers
            .Include(transfer => transfer.Items)
            .SingleAsync(transfer => transfer.Id == transferId);
        Assert.Equal("direct", persisted.TransferType);
        Assert.Equal("original-draft", persisted.Notes);
        Assert.Equal(StockTransfer.StatusDraft, persisted.Status);
        var persistedItem = Assert.Single(persisted.Items);
        Assert.Equal("original-line", persistedItem.Remarks);
        Assert.Equal(1, persistedItem.Quantity);
        Assert.Equal(10m, persistedItem.UnitCost);
        Assert.Equal(50, (await verifyDb.Inventories.SingleAsync()).Quantity);
        Assert.Empty(verifyDb.StockLedgers);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "stock_transfers.view,stock_transfers.create,stock_transfers.edit");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Stock Transfer Update Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Update Category", Description = "Update tests" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Update Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Update Warehouse", Address = "W" });
        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Update Product",
            ProductCode = "UPD-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 70m,
            HasVariants = true,
            Status = ProductStatus.Active
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Update Variant",
            Sku = "UPD-001-DEF",
            Barcode = "UPD-BC-001",
            Attributes = "{}"
        });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Update User",
            Email = "update.user@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventories (id, variant_id, location_id, location_type, quantity, low_stock_threshold, batch_number, expiry_date, xmin) " +
            "VALUES (401, 301, 11, 'outlet', 50, 10, NULL, NULL, 1);");
    }
}
