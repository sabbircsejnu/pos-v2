using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.SalesReports;
using RetailPOS.Application.SalesReports.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Infrastructure.SalesReports;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// PostgreSQL coverage of sales dates in the Company zone, Asia/Dhaka (+06:00): 1 April runs from 18:00Z on 31 March up
/// to, but excluding, 18:00Z on 1 April. Outlet 201 has its own New York setting, which must have no effect.
/// </summary>
public sealed class SalesCompanyZoneRelationalTests
{
    private const long OutletId = 201;
    private const long BeforeApril = 1;
    private const long FirstInstantOfApril = 2;
    private const long LastSecondOfAprilFirst = 3;
    private const long AprilSecond = 4;

    private static readonly DateTime AprilFirst = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Unspecified);

    static SalesCompanyZoneRelationalTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public Task DailyTrend_GroupsByTheCompanyDate() => RunAsync(async db =>
    {
        var handler = new GetDailySalesTrendQueryHandler(Scope(), new SalesReportReadStore(db), Zone());

        var trend = await handler.Handle(
            new GetDailySalesTrendQuery(new DateTime(2026, 3, 31), new DateTime(2026, 4, 2), null),
            CancellationToken.None);

        Assert.Equal(
            [
                (new DateTime(2026, 3, 31), 1, 10m),
                (new DateTime(2026, 4, 1), 2, 50m),
                (new DateTime(2026, 4, 2), 1, 40m)
            ],
            trend.Select(day => (day.Date, day.TransactionCount, day.Revenue)));
    });

    [Fact]
    public Task SalesReport_ASingleDayRange_CoversTheWholeCompanyDay() => RunAsync(async db =>
    {
        var handler = new GetSalesReportSummaryQueryHandler(Scope(), new SalesReportReadStore(db), Zone());

        var summary = await handler.Handle(
            new GetSalesReportSummaryQuery(AprilFirst, AprilFirst, null, null),
            CancellationToken.None);

        Assert.Equal(2, summary.TotalTransactions);
        Assert.Equal(50m, summary.TotalRevenue);
    });

    [Fact]
    public Task SaleList_ASingleDayRange_CoversTheWholeCompanyDay() => RunAsync(async db =>
    {
        var handler = new SearchSalesQueryHandler(
            new SaleReadStore(db, TestCompanySettings.Provider()),
            SaleScope(),
            Zone());

        var result = await handler.Handle(
            new SearchSalesQuery(new SaleSearchCriteria(null, null, null, AprilFirst, AprilFirst, null, 1, 10)),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            [LastSecondOfAprilFirst, FirstInstantOfApril],
            result.Sales.Select(sale => sale.Id));
    });

    private static ISalesReportReadScopeProvider Scope()
    {
        var scope = new Mock<ISalesReportReadScopeProvider>();
        scope.Setup(port => port.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesReportReadScope([OutletId]));
        return scope.Object;
    }

    private static ISaleReadScopeProvider SaleScope()
    {
        var scope = new Mock<ISaleReadScopeProvider>();
        scope.Setup(port => port.GetAsync(It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaleReadScope(1, [OutletId]));
        return scope.Object;
    }

    private static ICurrentCompanyTimeZone Zone()
    {
        var zone = new Mock<ICurrentCompanyTimeZone>();
        zone.Setup(port => port.GetTimeZoneIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Dhaka");
        return zone.Object;
    }

    private static async Task RunAsync(Func<RetailPOSDbContext, Task> test)
    {
        await PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.Require(InfrastructureTestSettings.PostgresKey),
            PostgresTestDatabasePrefix.GrnCompletion,
            async database =>
            {
                var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .Options;
                await using var db = await PostgresTestDatabase.InitializeResourceAsync(
                    new RetailPOSDbContext(options),
                    context => context.Database.MigrateAsync());
                db.Businesses.Add(new Business { Id = 1, Name = "Business", IsActive = true });
                db.Outlets.Add(new Outlet
                {
                    Id = OutletId,
                    BusinessId = 1,
                    Name = "Outlet",
                    Address = "Dhaka",
                    TimeZoneId = "America/New_York"
                });
                db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
                db.Sales.AddRange(
                    Sale(BeforeApril, new DateTime(2026, 3, 31, 17, 59, 59, DateTimeKind.Utc), 10m),
                    Sale(FirstInstantOfApril, new DateTime(2026, 3, 31, 18, 0, 0, DateTimeKind.Utc), 20m),
                    Sale(LastSecondOfAprilFirst, new DateTime(2026, 4, 1, 17, 59, 59, DateTimeKind.Utc), 30m),
                    Sale(AprilSecond, new DateTime(2026, 4, 1, 18, 0, 0, DateTimeKind.Utc), 40m));
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                await test(db);
            });
    }

    private static Sale Sale(long id, DateTime at, decimal total) => new()
    {
        Id = id,
        BusinessId = 1,
        SaleNumber = $"S-{id}",
        OutletId = OutletId,
        CashierId = 5,
        SaleDate = at,
        TotalAmount = total,
        PaymentMethod = "cash",
        Status = "completed"
    };
}
