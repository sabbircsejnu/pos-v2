using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Queries;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.Settings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

/// <summary>
/// PostgreSQL coverage of the GRN list filters and location scope. Every GRN date follows the Company zone, here
/// Asia/Dhaka (+06:00), so 10 March covers 09 Mar 18:00Z to 10 Mar 18:00Z at every location, including outlet 202,
/// whose own America/New_York setting must have no effect on GRNs.
/// </summary>
public sealed class GrnReadScopeRelationalTests
{
    private const long OutletDhaka = 201;
    private const long OutletNewYork = 202;
    private const long WarehouseMain = 301;
    private const long WarehouseOther = 302;
    private const long ForeignOutlet = 209;

    private const long PoOutletDhaka = 21;
    private const long PoOutletNewYork = 22;
    private const long PoWarehouseMain = 23;
    private const long PoWarehouseOther = 24;
    private const long PoForeign = 25;

    private const long WarehouseAtDayStart = 1;
    private const long WarehouseBeforeDayStart = 2;
    private const long DhakaOutletLastSecond = 3;
    private const long DhakaOutletAtNextDay = 4;
    private const long OwnZoneOutletNextCompanyDay = 5;
    private const long OwnZoneOutletSameCompanyDay = 6;
    private const long OwnZoneOutletLaterCompanyDay = 7;
    private const long OtherWarehouseSameDay = 8;
    private const long ForeignSameDay = 9;

    private static readonly DateOnly Day = new(2026, 3, 10);
    private static readonly GrnReadScope Owner = new(
        1, true, [OutletDhaka, OutletNewYork], [WarehouseMain, WarehouseOther]);
    private static readonly GrnReadScope WarehouseOnly = new(1, false, [], [WarehouseMain]);

    static GrnReadScopeRelationalTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public Task DateBounds_AreInclusiveInTheCompanyZone_AndExcludeOtherBusinesses() => RunAsync(async db =>
    {
        var result = await SearchAsync(db, Owner, fromDate: Day, toDate: Day);

        Assert.Equal(
            [DhakaOutletLastSecond, OwnZoneOutletSameCompanyDay, OtherWarehouseSameDay, WarehouseAtDayStart],
            result.Grns.Select(grn => grn.Id));
        Assert.Equal(4, result.TotalCount);
    });

    [Fact]
    public Task DateBounds_AreIndependentlyOptional() => RunAsync(async db =>
    {
        var fromOnly = await SearchAsync(db, Owner, locationType: "warehouse", fromDate: Day);
        var toOnly = await SearchAsync(db, Owner, locationType: "warehouse", toDate: Day);

        Assert.Equal([OtherWarehouseSameDay, WarehouseAtDayStart], fromOnly.Grns.Select(grn => grn.Id));
        Assert.Equal(
            [OtherWarehouseSameDay, WarehouseAtDayStart, WarehouseBeforeDayStart],
            toOnly.Grns.Select(grn => grn.Id));
    });

    [Fact]
    public Task CombinedFilters_ApplyTogether() => RunAsync(async db =>
    {
        var result = await SearchAsync(
            db,
            Owner,
            status: "partial",
            locationType: "outlet",
            locationId: OutletDhaka,
            fromDate: Day,
            toDate: Day);

        Assert.Equal(DhakaOutletLastSecond, Assert.Single(result.Grns).Id);
        Assert.Equal(1, result.TotalCount);
    });

    [Fact]
    public Task EmptyResults_ReportZeroTotal() => RunAsync(async db =>
    {
        var result = await SearchAsync(
            db,
            Owner,
            status: "full",
            locationType: "outlet",
            locationId: OutletDhaka,
            fromDate: Day,
            toDate: Day);

        Assert.Empty(result.Grns);
        Assert.Equal(0, result.TotalCount);
    });

    [Fact]
    public Task WarehouseOnlyUser_SeesOnlyTheAssignedWarehouse_AndCountsMatch() => RunAsync(async db =>
    {
        var all = await SearchAsync(db, WarehouseOnly);
        var outlets = await SearchAsync(db, WarehouseOnly, locationType: "outlet");

        Assert.Equal([WarehouseAtDayStart, WarehouseBeforeDayStart], all.Grns.Select(grn => grn.Id));
        Assert.Equal(2, all.TotalCount);
        Assert.Empty(outlets.Grns);
        Assert.Equal(0, outlets.TotalCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            SearchAsync(db, WarehouseOnly, locationType: "warehouse", locationId: WarehouseOther));
    });

    [Fact]
    public Task Pagination_ReflectsTheFilteredTotal() => RunAsync(async db =>
    {
        var first = await SearchAsync(db, Owner, fromDate: Day, toDate: Day, pageNumber: 1, pageSize: 3);
        var second = await SearchAsync(db, Owner, fromDate: Day, toDate: Day, pageNumber: 2, pageSize: 3);

        Assert.Equal(4, first.TotalCount);
        Assert.Equal(3, first.Grns.Count);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(WarehouseAtDayStart, Assert.Single(second.Grns).Id);
    });

    [Fact]
    public Task BusinessIsolation_HoldsEvenWhenAScopeListsAForeignLocation() => RunAsync(async db =>
    {
        var store = new GrnReadStore(db);
        var forged = new GrnReadScope(1, false, [ForeignOutlet], []);

        Assert.Empty((await SearchAsync(db, forged)).Grns);
        Assert.Null(await store.FindByIdAsync(ForeignSameDay, forged, CancellationToken.None));
        Assert.Null(await store.FindByIdAsync(ForeignSameDay, Owner, CancellationToken.None));
        Assert.DoesNotContain(
            await store.FindAllAsync(null, null, Owner, CancellationToken.None),
            grn => grn.Id == ForeignSameDay);
    });

    [Fact]
    public Task IndividualReads_HideGrnsOutsideTheUsersLocations() => RunAsync(async db =>
    {
        var store = new GrnReadStore(db);
        var variance = new GrnPoVarianceReadStore(db);

        Assert.NotNull(await store.FindByIdAsync(WarehouseAtDayStart, WarehouseOnly, CancellationToken.None));
        Assert.Null(await store.FindByIdAsync(OtherWarehouseSameDay, WarehouseOnly, CancellationToken.None));
        Assert.Null(await store.FindByIdAsync(DhakaOutletLastSecond, WarehouseOnly, CancellationToken.None));

        Assert.NotNull(await store.FindVarianceByIdAsync(WarehouseAtDayStart, WarehouseOnly, CancellationToken.None));
        Assert.Null(await store.FindVarianceByIdAsync(OtherWarehouseSameDay, WarehouseOnly, CancellationToken.None));

        Assert.NotNull(await variance.FindByPurchaseOrderIdAsync(PoWarehouseMain, WarehouseOnly, CancellationToken.None));
        Assert.Null(await variance.FindByPurchaseOrderIdAsync(PoWarehouseOther, WarehouseOnly, CancellationToken.None));
        Assert.Null(await variance.FindByPurchaseOrderIdAsync(PoForeign, Owner, CancellationToken.None));

        var all = await store.FindAllAsync(null, null, WarehouseOnly, CancellationToken.None);
        Assert.Equal([WarehouseAtDayStart, WarehouseBeforeDayStart], all.Select(grn => grn.Id));
    });

    [Fact]
    public Task PendingPurchaseOrders_FollowTheScope() => RunAsync(async db =>
    {
        var store = new GrnReadStore(db);

        var warehouseOnly = await store.FindPendingAsync(WarehouseOnly, CancellationToken.None);
        var owner = await store.FindPendingAsync(Owner, CancellationToken.None);

        Assert.Equal(PoWarehouseMain, Assert.Single(warehouseOnly).Id);
        Assert.DoesNotContain(owner, order => order.Id == PoForeign);
    });

    [Fact]
    public Task FilterAndAccountingDate_AgreeForEveryGrn() => RunAsync(async db =>
    {
        var filtered = await SearchAsync(db, Owner, fromDate: Day, toDate: Day);
        var all = await SearchAsync(db, Owner);
        var filteredIds = filtered.Grns.Select(grn => grn.Id).ToHashSet();
        var day = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        Assert.Equal(8, all.TotalCount);
        foreach (var grn in all.Grns)
        {
            Assert.Equal(CompanyDates.DateFor(grn.ReceivedDate, "Asia/Dhaka") == day, filteredIds.Contains(grn.Id));
        }

    });

    [Fact]
    public Task DaylightSavingDay_FiltersByTheCompanyZone() => RunAsync(async db =>
    {
        // In a New York Company zone, 8 March 2026 lasts 23 hours: 05:00Z up to, but excluding, 04:00Z the next day.
        db.Grns.AddRange(
            Receipt(11, PoWarehouseOther, 3, 8, 4, 59, 59),
            Receipt(12, PoWarehouseOther, 3, 8, 5, 0, 0),
            Receipt(13, PoWarehouseOther, 3, 9, 3, 59, 59),
            Receipt(14, PoWarehouseOther, 3, 9, 4, 0, 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var dstDay = new DateOnly(2026, 3, 8);

        var result = await SearchAsync(
            db,
            Owner,
            locationType: "warehouse",
            locationId: WarehouseOther,
            fromDate: dstDay,
            toDate: dstDay,
            companyZone: "America/New_York");

        Assert.Equal([13L, 12L], result.Grns.Select(grn => grn.Id));
    });

    [Fact]
    public Task OutletAndPosTimeZones_DoNotAffectGrnFilters() => RunAsync(async db =>
    {
        db.BusinessPosSettings.Add(new BusinessPosSettings { BusinessId = 1, DefaultTimeZoneId = "Asia/Tokyo" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await SearchAsync(db, Owner, locationType: "outlet", fromDate: Day, toDate: Day);

        Assert.Equal([DhakaOutletLastSecond, OwnZoneOutletSameCompanyDay], result.Grns.Select(grn => grn.Id));
    });

    [Fact]
    public Task UserWithoutAssignments_SeesNothing() => RunAsync(async db =>
    {
        var none = new GrnReadScope(1, false, [], []);

        var result = await SearchAsync(db, none);

        Assert.Empty(result.Grns);
        Assert.Equal(0, result.TotalCount);
        Assert.Null(await new GrnReadStore(db).FindByIdAsync(WarehouseAtDayStart, none, CancellationToken.None));
    });

    private static Task<GrnSearchReadModel> SearchAsync(
        RetailPOSDbContext db,
        GrnReadScope scope,
        string? status = null,
        string? locationType = null,
        long? locationId = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int pageNumber = 1,
        int pageSize = 20,
        string companyZone = "Asia/Dhaka")
    {
        var provider = new Mock<IGrnReadScopeProvider>();
        provider.Setup(port => port.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        var settings = TestCompanySettings.Provider(companyZone);
        return new SearchGrnsQueryHandler(new GrnReadStore(db), provider.Object, settings).Handle(
            new SearchGrnsQuery(null, status, null, null, pageNumber, pageSize, locationType, locationId, fromDate, toDate),
            CancellationToken.None);
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
                await SeedAsync(db);
                await test(db);
            });
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business 1", IsActive = true },
            new Business { Id = 2, Name = "Business 2", IsActive = true });
        db.Outlets.AddRange(
            new Outlet { Id = OutletDhaka, BusinessId = 1, Name = "Dhaka outlet" },
            new Outlet { Id = OutletNewYork, BusinessId = 1, Name = "New York outlet", TimeZoneId = "America/New_York" },
            new Outlet { Id = ForeignOutlet, BusinessId = 2, Name = "Foreign outlet", TimeZoneId = "Europe/Paris" });
        db.Warehouses.AddRange(
            new Warehouse { Id = WarehouseMain, BusinessId = 1, Name = "Main warehouse" },
            new Warehouse { Id = WarehouseOther, BusinessId = 1, Name = "Other warehouse" });
        db.Suppliers.AddRange(
            new Supplier { Id = 10, BusinessId = 1, Name = "Supplier 1" },
            new Supplier { Id = 11, BusinessId = 2, Name = "Supplier 2" });
        db.PurchaseOrders.AddRange(
            Order(PoOutletDhaka, 10, outletId: OutletDhaka),
            Order(PoOutletNewYork, 10, outletId: OutletNewYork),
            Order(PoWarehouseMain, 10, warehouseId: WarehouseMain),
            Order(PoWarehouseOther, 10, warehouseId: WarehouseOther),
            Order(PoForeign, 11, outletId: ForeignOutlet));
        db.Grns.AddRange(
            Receipt(WarehouseAtDayStart, PoWarehouseMain, 3, 9, 18, 0, 0),
            Receipt(WarehouseBeforeDayStart, PoWarehouseMain, 3, 9, 17, 59, 59),
            Receipt(DhakaOutletLastSecond, PoOutletDhaka, 3, 10, 17, 59, 59, "partial"),
            Receipt(DhakaOutletAtNextDay, PoOutletDhaka, 3, 10, 18, 0, 0),
            Receipt(OwnZoneOutletNextCompanyDay, PoOutletNewYork, 3, 11, 3, 59, 59),
            Receipt(OwnZoneOutletSameCompanyDay, PoOutletNewYork, 3, 10, 3, 59, 59),
            Receipt(OwnZoneOutletLaterCompanyDay, PoOutletNewYork, 3, 11, 4, 0, 0),
            Receipt(OtherWarehouseSameDay, PoWarehouseOther, 3, 10, 0, 0, 0),
            Receipt(ForeignSameDay, PoForeign, 3, 10, 0, 0, 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static PurchaseOrder Order(long id, long supplierId, long? outletId = null, long? warehouseId = null) =>
        new()
        {
            Id = id,
            PoNumber = $"PO-{id:D6}",
            SupplierId = supplierId,
            OutletId = outletId,
            WarehouseId = warehouseId,
            OrderDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = "approved"
        };

    private static Grn Receipt(
        long id,
        long purchaseOrderId,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        string status = "full") =>
        new()
        {
            Id = id,
            PoId = purchaseOrderId,
            ReceivedDate = new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc),
            Status = status
        };
}
