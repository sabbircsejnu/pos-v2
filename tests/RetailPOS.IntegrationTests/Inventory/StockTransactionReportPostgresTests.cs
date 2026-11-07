using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.InventoryReports;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

/// <summary>
/// The report's window against real timestamptz columns, under the Npgsql legacy timestamp mode the API runs in.
/// </summary>
public sealed class StockTransactionReportPostgresTests
{
    static StockTransactionReportPostgresTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task UtcWindow_SelectsExactInstantsWhateverTheHostTimeZone()
    {
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? throw new InvalidOperationException("RETAILPOS_TEST_POSTGRES must target the Test stack.");
        await PostgresTestDatabase.RunAsync(
            connectionString,
            PostgresTestDatabasePrefix.StockTransferDispatch,
            async database =>
            {
                var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .Options;
                await using var db = new RetailPOSDbContext(options);
                await db.Database.MigrateAsync();
                await SeedAsync(db);

                // 5 November in Dhaka.
                var report = await new StockTransactionReportReadStore(db).GetAsync(
                    new InventoryReportReadScope(1, [11], []),
                    new StockTransactionReportFilter(
                        31,
                        301,
                        11,
                        "outlet",
                        new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc).AddTicks(-1)),
                    CancellationToken.None);

                Assert.Equal(1, report.Summary.OpeningStock);
                Assert.Equal(6, report.Summary.StockIn);
                Assert.Equal(0, report.Summary.StockOut);
                Assert.Equal(7, report.Summary.ClosingStock);
                Assert.Equal(15, report.Summary.CurrentStock);
                Assert.Equal(new[] { 3m, 7m }, report.Rows.Select(row => row.RunningBalance));
            });
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Report business" });
        db.Categories.Add(new Category { Id = 21, BusinessId = 1, Name = "Report category", Description = "" });
        db.Products.Add(new Product { Id = 31, BusinessId = 1, Name = "Report product", CategoryId = 21 });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 31,
            Name = "Default",
            Sku = "REPORT-301"
        });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Report outlet" });
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 15
        });
        db.StockLedgers.AddRange(
            Receipt(1, 1, new DateTime(2026, 11, 4, 17, 59, 59, 999, DateTimeKind.Utc)),
            Receipt(2, 2, new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc)),
            Receipt(3, 4, new DateTime(2026, 11, 5, 17, 59, 59, 999, DateTimeKind.Utc)),
            Receipt(4, 8, new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
    }

    private static StockLedger Receipt(long sequence, int quantity, DateTime createdAt) =>
        new()
        {
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            TransactionType = StockLedgerTransactionType.Grn,
            QtyIn = quantity,
            MovementSequence = sequence,
            BusinessDate = createdAt.Date,
            ReferenceType = StockLedgerReferenceType.Grn,
            ReferenceId = sequence,
            CreatedAt = createdAt
        };
}
