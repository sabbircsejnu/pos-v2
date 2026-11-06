using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BusinessAdministration;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

/// <summary>The platform footer columns on a schema built from the tracked migration chain (isolated Test stack only).</summary>
public sealed class BusinessReceiptFooterPostgresTests
{
    private const string PreviousMigration = "20261105105652_PosV2OutletInvoicePrintSettings";

    private static Task RunAsync(Func<PostgresTestDatabase, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            PostgresTestDatabaseStrategy.FreshMigration,
            operation);

    private static RetailPOSDbContext Context(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    [Fact]
    public Task Migration_LeavesExistingBusinessesWithoutAFooter_AndStoresRoundTrip() => RunAsync(async database =>
    {
        await using (var db = Context(database))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO businesses (id, name, is_active, created_at, updated_at) VALUES (1, 'Existing', true, now(), now())");
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }

        await using (var db = Context(database))
        {
            var existing = await db.Businesses.AsNoTracking().SingleAsync(item => item.Id == 1);
            Assert.False(existing.ShowPoweredByOnReceipt);
            Assert.Null(existing.PoweredByText);

            Assert.True(await new BusinessAdministrationWriteStore(db)
                .UpdateReceiptFooterAsync(1, true, "Powered by: Irani Mart Technology", CancellationToken.None));
            Assert.False(await new BusinessAdministrationWriteStore(db)
                .UpdateReceiptFooterAsync(404, true, "x", CancellationToken.None));

            db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main", Address = "Dhaka", InvoicePrefix = "DHK" });
            db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
            await db.SaveChangesAsync();
            db.Sales.Add(new Sale
            {
                Id = 101,
                SaleNumber = "DHK2611051",
                BusinessId = 1,
                OutletId = 11,
                CashierId = 5,
                SaleDate = new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc),
                TotalAmount = 100m,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        await using var read = Context(database);
        var summary = await new BusinessAdministrationReadStore(read).FindBusinessAsync(1, CancellationToken.None);
        Assert.NotNull(summary);
        Assert.True(summary.ShowPoweredByOnReceipt);
        Assert.Equal("Powered by: Irani Mart Technology", summary.PoweredByText);

        var sale = await new SaleReadStore(read, TestCompanySettings.Provider()).FindForReceiptAsync(101, CancellationToken.None);
        Assert.NotNull(sale);
        Assert.True(sale.ShowPoweredByOnReceipt);
        Assert.Equal("Powered by: Irani Mart Technology", sale.PoweredByText);
    });
}
