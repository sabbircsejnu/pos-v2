using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockRequisitionWriteSqliteTests(
    SqliteRetailPosApiFactory factory)
    : IClassFixture<SqliteRetailPosApiFactory>
{
    [Fact]
    public async Task Update_WhenLineInsertFails_RollsBackHeaderAndOriginalLines()
    {
        await ResetAndSeedAsync();
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockRequisitions.Add(new StockRequisition
            {
                Id = 501,
                RequisitionNo = "REQ-TEST-501",
                RequestingLocationId = 11,
                RequestingLocationType = "outlet",
                SourceLocationId = 21,
                SourceLocationType = "warehouse",
                RequestedBy = 101,
                RequestDate = DateTime.UtcNow,
                Status = StockRequisition.StatusDraft,
                Notes = "before",
                CreatedAt = DateTime.UtcNow,
                Lines =
                [
                    new StockRequisitionLine
                    {
                        Id = 601,
                        VariantId = 301,
                        RequestedQuantity = 2,
                        FulfilledQuantity = 0,
                        Remarks = "original"
                    }
                ]
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_requisition_line_insert BEFORE INSERT ON stock_requisition_lines " +
                "BEGIN SELECT RAISE(ABORT, 'forced requisition update rollback'); END;");
        }

        using (var writeScope = factory.Services.CreateScope())
        {
            var store = writeScope.ServiceProvider.GetRequiredService<IStockRequisitionWriteStore>();
            await Assert.ThrowsAsync<DbUpdateException>(() => store.UpdateAsync(
                501,
                new StockRequisitionWriteInput(
                    11,
                    "outlet",
                    21,
                    "warehouse",
                    "must-rollback",
                    [new StockRequisitionWriteLineInput(301, 4, "replacement")]),
                1,
                101,
                CancellationToken.None));
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await verifyDb.StockRequisitions
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleAsync();
        Assert.Equal("before", requisition.Notes);
        var line = Assert.Single(requisition.Lines);
        Assert.Equal(301, line.VariantId);
        Assert.Equal(2, line.RequestedQuantity);
        Assert.Equal("original", line.Remarks);
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Requisition Write Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Requisition Category", Description = "Tests" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Requisition Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Requisition Warehouse", Address = "W" });
        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Requisition Product",
            ProductCode = "REQ-001",
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
            Name = "Requisition Variant",
            Sku = "REQ-001-DEF",
            Barcode = "REQ-BC-001",
            Attributes = "{}"
        });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Requisition User",
            Email = "requisition.user@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
}
