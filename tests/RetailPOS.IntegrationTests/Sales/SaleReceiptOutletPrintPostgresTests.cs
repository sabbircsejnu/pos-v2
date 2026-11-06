using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Outlets;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace RetailPOS.Tests.Sales;

/// <summary>The outlet invoice print columns on a schema built from the tracked migration chain (isolated Test stack only).</summary>
public sealed class SaleReceiptOutletPrintPostgresTests
{
    private const string Migration = "PosV2OutletInvoicePrintSettings";

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            PostgresTestDatabaseStrategy.FreshMigration,
            operation);

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    [Fact]
    public Task Migration_AddsNullableColumns_AndReadStoresReturnThem() => RunAsync(async database =>
    {
        await using (var db = Context(database))
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), id => id.EndsWith(Migration, StringComparison.Ordinal));

            db.Businesses.Add(new Business { Id = 1, Name = "One" });
            await db.SaveChangesAsync();
            db.BusinessPosSettings.Add(new BusinessPosSettings { BusinessId = 1, DefaultTimeZoneId = "Asia/Karachi" });
            db.Outlets.AddRange(
                new Outlet
                {
                    Id = 11,
                    BusinessId = 1,
                    Name = "Main",
                    Address = "Dhaka",
                    ContactNumber = "01700000000",
                    InvoicePrefix = "DHK",
                    InvoicePrintBranchName = "IRANI MART-1",
                    BinNumber = "0029807530203",
                    Mushak = "6.3",
                    ReturnExchangePolicy = "No refunds.\nExchange in 15 days.",
                    VatLawNotification = "S.R.O. No. 19\nVAT applies."
                },
                new Outlet { Id = 12, BusinessId = 1, Name = "Unconfigured", Address = "Sylhet", InvoicePrefix = "SYL", TimeZoneId = "Asia/Dhaka" });
            db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
            await db.SaveChangesAsync();
            db.Sales.AddRange(
                NewSale(101, 11, "DHK2611051"),
                NewSale(102, 12, "SYL2611051"));
            await db.SaveChangesAsync();
        }

        await using var read = Context(database);
        var outlet = await new OutletReadStore(read).GetByIdAsync(11, 1, CancellationToken.None);
        Assert.NotNull(outlet);
        Assert.Equal("IRANI MART-1", outlet.InvoicePrintBranchName);
        Assert.Equal("0029807530203", outlet.BinNumber);
        Assert.Equal("6.3", outlet.Mushak);
        Assert.Equal("No refunds.\nExchange in 15 days.", outlet.ReturnExchangePolicy);
        Assert.Equal("S.R.O. No. 19\nVAT applies.", outlet.VatLawNotification);

        var store = new SaleReadStore(read, TestCompanySettings.Provider("Asia/Karachi"));
        var configured = await store.FindForReceiptAsync(101, CancellationToken.None);
        Assert.NotNull(configured);
        Assert.Equal("IRANI MART-1", configured.OutletPrint?.BranchName);
        Assert.Equal("Asia/Karachi", configured.EffectiveTimeZoneId);

        var unconfigured = await store.FindForReceiptAsync(102, CancellationToken.None);
        Assert.NotNull(unconfigured);
        Assert.Null(unconfigured.OutletPrint?.BranchName);
        // The outlet's own Asia/Dhaka setting no longer applies: receipts follow the Company zone.
        Assert.Equal("Asia/Karachi", unconfigured.EffectiveTimeZoneId);
    });

    private static Sale NewSale(long id, long outletId, string number) => new()
    {
        Id = id,
        SaleNumber = number,
        BusinessId = 1,
        OutletId = outletId,
        CashierId = 5,
        SaleDate = new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc),
        TotalAmount = 1290m,
        PaymentMethod = "cash",
        Status = "completed"
    };
}
