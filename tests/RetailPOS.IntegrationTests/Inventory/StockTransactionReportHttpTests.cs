using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

/// <summary>
/// The Stock Transaction Report over HTTP: balances, date windows in the Company time zone,
/// movement classification, the transfer lifecycle, and business and location scope.
/// </summary>
public sealed class StockTransactionReportHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long BusinessId = 4301;
    private const long ForeignBusinessId = 4302;
    private const long OwnerId = 4311;
    private const long WarehouseUserId = 4312;
    private const long OutletManagerId = 4313;
    private const long ProductId = 7301;
    private const long ForeignProductId = 7302;
    private const long CoffeeVariantId = 8301;
    private const long BorkaVariantId = 8302;
    private const long ClassificationVariantId = 8303;
    private const long BoundaryVariantId = 8304;
    private const long LifecycleVariantId = 8305;
    private const long WarehouseVariantId = 8306;
    private const long ForeignVariantId = 8399;
    private const long Outlet = 5301;
    private const long UnassignedOutlet = 5302;
    private const long ForeignOutlet = 5399;
    private const long Warehouse = 5351;
    private const long UnassignedWarehouse = 5352;
    private const long ForeignWarehouse = 5398;

    private readonly RetailPosApiFactory _factory;

    public StockTransactionReportHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReceiptBeforeTheRange_IsOpeningStock_NotStockIn()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={CoffeeVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-11-01&dateTo=2026-11-06");

        AssertSummary(report, opening: 75, stockIn: 0, stockOut: 6, closing: 69, current: 69);
        var rows = Rows(report);
        Assert.Equal(new[] { 70, 69 }, rows.Select(row => row.GetProperty("runningBalance").GetInt32()));
        Assert.All(rows, row => Assert.Equal("sale", row.GetProperty("transactionType").GetString()));
    }

    [Fact]
    public async Task ReceiptAfterLocalMidnight_BelongsToTheCompanyDate_NotTheUtcDate()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");
        var scope = $"productId={ProductId}&variantId={BorkaVariantId}&outletId={Outlet}&locationType=outlet";

        // 2026-11-05T19:27Z is 01:27 on 6 November in Dhaka.
        var receivedDay = await GetReportAsync(client, $"{scope}&dateFrom=2026-11-06&dateTo=2026-11-06");
        var dayBefore = await GetReportAsync(client, $"{scope}&dateFrom=2026-11-05&dateTo=2026-11-05");

        AssertSummary(receivedDay, opening: 0, stockIn: 4, stockOut: 0, closing: 4, current: 4);
        var row = Assert.Single(Rows(receivedDay));
        Assert.Equal("transfer_in", row.GetProperty("transactionType").GetString());
        Assert.Equal(4, row.GetProperty("runningBalance").GetInt32());
        AssertSummary(dayBefore, opening: 0, stockIn: 0, stockOut: 0, closing: 0, current: 4);
        Assert.Empty(Rows(dayBefore));
    }

    [Fact]
    public async Task DateWindow_EchoesTheRequestedCalendarDates()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={BorkaVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-11-06&dateTo=2026-11-07");

        Assert.StartsWith("2026-11-06", report.GetProperty("dateFrom").GetString());
        Assert.StartsWith("2026-11-07", report.GetProperty("dateTo").GetString());
    }

    [Fact]
    public async Task DateBoundaries_IncludeTheWholeCompanyDayAndNothingElse()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={BoundaryVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-11-05&dateTo=2026-11-05");

        // Last instant of 4 Nov Dhaka: opening. First and last instants of 5 Nov: in range. First of 6 Nov: out.
        AssertSummary(report, opening: 1, stockIn: 6, stockOut: 0, closing: 7, current: 15);
        Assert.Equal(new[] { 3, 7 }, Rows(report).Select(row => row.GetProperty("runningBalance").GetInt32()));
    }

    [Fact]
    public async Task EveryMovementType_IsClassifiedByItsActualEffect_AndReconciles()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={ClassificationVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-10-01&dateTo=2026-10-31");

        AssertSummary(report, opening: 10, stockIn: 31, stockOut: 15, closing: 26, current: 26);
        var rows = Rows(report);
        Assert.Equal(
            new[]
            {
                StockLedgerTransactionType.Grn,
                StockLedgerTransactionType.TransferIn,
                StockLedgerTransactionType.Adjustment,
                StockLedgerTransactionType.Return,
                StockLedgerTransactionType.ReturnTransferIn,
                StockLedgerTransactionType.Sale,
                StockLedgerTransactionType.TransferOut,
                StockLedgerTransactionType.Adjustment,
                StockLedgerTransactionType.ReturnTransferOut,
                StockLedgerTransactionType.TransferRejection
            },
            rows.Select(row => row.GetProperty("transactionType").GetString()));
        Assert.Equal(
            new[] { 11, 13, 17, 25, 41, 40, 38, 35, 26, 26 },
            rows.Select(row => row.GetProperty("runningBalance").GetInt32()));
        Assert.Equal(31, rows.Sum(row => row.GetProperty("quantityIn").GetInt32()));
        Assert.Equal(15, rows.Sum(row => row.GetProperty("quantityOut").GetInt32()));
    }

    [Fact]
    public async Task EqualTimestamps_OrderDeterministicallyById()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={ClassificationVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-10-01&dateTo=2026-10-31");

        var ids = Rows(report).Select(row => row.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(ids.OrderBy(id => id), ids);
    }

    [Fact]
    public async Task ReferenceNumbers_AreTheDocumentNumbersUsersSeeElsewhere()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={ClassificationVariantId}&outletId={Outlet}&locationType=outlet&dateFrom=2026-10-01&dateTo=2026-10-31");

        var references = Rows(report)
            .ToDictionary(
                row => row.GetProperty("id").GetInt64(),
                row => row.GetProperty("referenceNo").GetString());
        Assert.Equal("TRF-2026-00077", references[6112]);
        Assert.Equal("ADJ-2026-00088", references[6113]);
    }

    [Fact]
    public async Task TransferLifecycle_ReportsEachSideOnce_AndMatchesInventory()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");
        var source = $"productId={ProductId}&variantId={LifecycleVariantId}&outletId={Warehouse}&locationType=warehouse";
        var destination = $"productId={ProductId}&variantId={LifecycleVariantId}&outletId={Outlet}&locationType=outlet";

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStockTransferDispatchStore>()
                .SubmitAndDispatchAsync(9401, BusinessId, OwnerId, autoApprove: true, CancellationToken.None);
        }

        // In transit: the source has gone down; the destination has not moved.
        AssertSummary(await GetReportAsync(client, source), opening: 0, stockIn: 10, stockOut: 10, closing: 0, current: 0);
        AssertSummary(await GetReportAsync(client, destination), opening: 0, stockIn: 0, stockOut: 0, closing: 0, current: 0);

        var receive = new StockTransferReceiveInput(
            [new StockTransferReceiveItemInput(LifecycleVariantId, 7, 3, null)],
            null);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IStockTransferReceiveStore>()
                .ReceiveAsync(9401, BusinessId, receive, OwnerId, CancellationToken.None);
        }

        // Only accepted units reach the destination.
        var received = await GetReportAsync(client, destination);
        AssertSummary(received, opening: 0, stockIn: 7, stockOut: 0, closing: 7, current: 7);
        var rows = Rows(received);
        Assert.Equal(
            new[] { StockLedgerTransactionType.TransferIn, StockLedgerTransactionType.TransferRejection },
            rows.Select(row => row.GetProperty("transactionType").GetString()));
        Assert.All(rows, row => Assert.Equal("TRF-2026-09401", row.GetProperty("referenceNo").GetString()));

        // The rejected units are in return transit: the source does not get them back until it receives the return.
        AssertSummary(await GetReportAsync(client, source), opening: 0, stockIn: 10, stockOut: 10, closing: 0, current: 0);

        // A replayed receive cannot move stock or write a second movement.
        using (var scope = _factory.Services.CreateScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IStockTransferReceiveStore>()
                    .ReceiveAsync(9401, BusinessId, receive, OwnerId, CancellationToken.None));
        }

        AssertSummary(await GetReportAsync(client, destination), opening: 0, stockIn: 7, stockOut: 0, closing: 7, current: 7);
    }

    [Fact]
    public async Task ForeignProductOrVariant_IsNotFound_AndNeverNamed()
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        using var product = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ForeignProductId}");
        using var variant = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&variantId={ForeignVariantId}");

        Assert.Equal(HttpStatusCode.NotFound, product.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, variant.StatusCode);
        Assert.DoesNotContain("Foreign product", await product.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ForeignOutlet, "outlet")]
    [InlineData(ForeignWarehouse, "warehouse")]
    public async Task ForeignLocation_IsForbiddenEvenForTheBusinessOwner(long locationId, string locationType)
    {
        await SeedAsync();
        using var client = CreateClient(OwnerId, "BusinessOwner");

        using var response = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&outletId={locationId}&locationType={locationType}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OutletManager_SeesTheAssignedOutletOnly()
    {
        await SeedAsync();
        using var client = CreateClient(OutletManagerId, "OutletManager");

        var report = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={CoffeeVariantId}&outletId={Outlet}&locationType=outlet");
        using var unassigned = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&outletId={UnassignedOutlet}&locationType=outlet");
        using var warehouse = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&outletId={Warehouse}&locationType=warehouse");

        AssertSummary(report, opening: 0, stockIn: 75, stockOut: 6, closing: 69, current: 69);
        Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, warehouse.StatusCode);
    }

    [Fact]
    public async Task WarehouseOnlyUser_GetsTheirWarehouse_AndCannotReadOutlets()
    {
        await SeedAsync();
        using var client = CreateClient(WarehouseUserId, "WarehouseManager");

        var defaulted = await GetReportAsync(
            client,
            $"productId={ProductId}&variantId={WarehouseVariantId}");
        using var outlet = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&outletId={Outlet}&locationType=outlet");
        using var otherWarehouse = await client.GetAsync(
            $"/api/reports/stock-transactions?productId={ProductId}&outletId={UnassignedWarehouse}&locationType=warehouse");

        AssertSummary(defaulted, opening: 0, stockIn: 40, stockOut: 0, closing: 40, current: 40);
        Assert.All(Rows(defaulted), row => Assert.Equal(Warehouse, row.GetProperty("locationId").GetInt64()));
        Assert.Equal(HttpStatusCode.Forbidden, outlet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherWarehouse.StatusCode);
    }

    private static void AssertSummary(
        JsonElement report,
        int opening,
        int stockIn,
        int stockOut,
        int closing,
        int current)
    {
        var summary = report.GetProperty("summary");
        Assert.Equal(opening, summary.GetProperty("openingStock").GetInt32());
        Assert.Equal(stockIn, summary.GetProperty("stockIn").GetInt32());
        Assert.Equal(stockOut, summary.GetProperty("stockOut").GetInt32());
        Assert.Equal(closing, summary.GetProperty("closingStock").GetInt32());
        Assert.Equal(current, summary.GetProperty("currentStock").GetInt32());
        Assert.Equal(opening + stockIn - stockOut, closing);
    }

    private static JsonElement[] Rows(JsonElement report) =>
        report.GetProperty("rows").EnumerateArray().ToArray();

    private static async Task<JsonElement> GetReportAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/reports/stock-transactions?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query} returned {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private HttpClient CreateClient(long userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "reports.inventory");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", role);
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Report business" },
            new Business { Id = ForeignBusinessId, Name = "Foreign business" });
        db.BusinessPosSettings.Add(new BusinessPosSettings
        {
            BusinessId = BusinessId,
            DefaultTimeZoneId = "Asia/Dhaka"
        });
        db.Categories.AddRange(
            new Category { Id = 6301, BusinessId = BusinessId, Name = "Report category", Description = "" },
            new Category { Id = 6302, BusinessId = ForeignBusinessId, Name = "Foreign category", Description = "" });
        db.Products.AddRange(
            new Product { Id = ProductId, BusinessId = BusinessId, Name = "Report product", CategoryId = 6301 },
            new Product { Id = ForeignProductId, BusinessId = ForeignBusinessId, Name = "Foreign product", CategoryId = 6302 });
        db.ProductVariants.AddRange(
            Variant(CoffeeVariantId, BusinessId, ProductId),
            Variant(BorkaVariantId, BusinessId, ProductId),
            Variant(ClassificationVariantId, BusinessId, ProductId),
            Variant(BoundaryVariantId, BusinessId, ProductId),
            Variant(LifecycleVariantId, BusinessId, ProductId),
            Variant(WarehouseVariantId, BusinessId, ProductId),
            Variant(ForeignVariantId, ForeignBusinessId, ForeignProductId));
        db.Outlets.AddRange(
            new Outlet { Id = Outlet, BusinessId = BusinessId, Name = "Assigned outlet" },
            new Outlet { Id = UnassignedOutlet, BusinessId = BusinessId, Name = "Unassigned outlet" },
            new Outlet { Id = ForeignOutlet, BusinessId = ForeignBusinessId, Name = "Foreign outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = Warehouse, BusinessId = BusinessId, Name = "Assigned warehouse" },
            new Warehouse { Id = UnassignedWarehouse, BusinessId = BusinessId, Name = "Unassigned warehouse" },
            new Warehouse { Id = ForeignWarehouse, BusinessId = ForeignBusinessId, Name = "Foreign warehouse" });
        db.Users.AddRange(
            User(OwnerId, "report-owner", RetailPOS.Core.Entities.User.InventoryAccessAll, Outlet),
            User(WarehouseUserId, "warehouse-user", RetailPOS.Core.Entities.User.InventoryAccessSpecific, null),
            User(OutletManagerId, "outlet-manager", RetailPOS.Core.Entities.User.InventoryAccessSpecific, Outlet));
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 4391,
            UserId = OutletManagerId,
            OutletId = Outlet,
            BusinessId = BusinessId,
            IsPrimary = true
        });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            Id = 4392,
            UserId = WarehouseUserId,
            WarehouseId = Warehouse,
            BusinessId = BusinessId,
            IsPrimary = true
        });

        db.Inventories.AddRange(
            Stock(9301, CoffeeVariantId, Outlet, "outlet", 69),
            Stock(9302, BorkaVariantId, Outlet, "outlet", 4),
            Stock(9303, ClassificationVariantId, Outlet, "outlet", 26),
            Stock(9304, BoundaryVariantId, Outlet, "outlet", 15),
            Stock(9305, LifecycleVariantId, Warehouse, "warehouse", 10),
            Stock(9306, WarehouseVariantId, Warehouse, "warehouse", 40),
            Stock(9307, WarehouseVariantId, UnassignedWarehouse, "warehouse", 900));

        var octoberStart = new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);
        db.StockLedgers.AddRange(
            // The reported Basundhara example: a GRN before the range, then two sales inside it.
            Movement(6001, CoffeeVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 75, 0,
                StockLedgerReferenceType.Grn, 3, Utc(2026, 10, 23, 10, 47)),
            Movement(6002, CoffeeVariantId, Outlet, "outlet", StockLedgerTransactionType.Sale, 0, 5,
                StockLedgerReferenceType.Sale, 6, Utc(2026, 11, 5, 11, 32)),
            Movement(6003, CoffeeVariantId, Outlet, "outlet", StockLedgerTransactionType.Sale, 0, 1,
                StockLedgerReferenceType.Sale, 7, Utc(2026, 11, 5, 11, 44)),

            Movement(6011, BorkaVariantId, Outlet, "outlet", StockLedgerTransactionType.TransferIn, 4, 0,
                StockLedgerReferenceType.StockTransfer, 3, Utc(2026, 11, 5, 19, 27)),

            // Dhaka is UTC+6, so 5 November there runs from 18:00Z on the 4th to 18:00Z on the 5th.
            Movement(6021, BoundaryVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 1, 0,
                StockLedgerReferenceType.Grn, 21, new DateTime(2026, 11, 4, 17, 59, 59, 999, DateTimeKind.Utc)),
            Movement(6022, BoundaryVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 2, 0,
                StockLedgerReferenceType.Grn, 22, Utc(2026, 11, 4, 18, 0)),
            Movement(6023, BoundaryVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 4, 0,
                StockLedgerReferenceType.Grn, 23, new DateTime(2026, 11, 5, 17, 59, 59, 999, DateTimeKind.Utc)),
            Movement(6024, BoundaryVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 8, 0,
                StockLedgerReferenceType.Grn, 24, Utc(2026, 11, 5, 18, 0)),

            Movement(6100, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 10, 0,
                StockLedgerReferenceType.Grn, 30, Utc(2026, 9, 1, 6, 0)),
            Movement(6111, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Grn, 1, 0,
                StockLedgerReferenceType.Grn, 31, octoberStart),
            Movement(6112, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.TransferIn, 2, 0,
                StockLedgerReferenceType.StockTransfer, 9477, octoberStart),
            Movement(6113, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Adjustment, 4, 0,
                StockLedgerReferenceType.StockAdjustment, 9488, octoberStart),
            Movement(6114, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Return, 8, 0,
                StockLedgerReferenceType.Sale, 33, octoberStart.AddHours(1)),
            Movement(6115, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.ReturnTransferIn, 16, 0,
                StockLedgerReferenceType.StockTransfer, 34, octoberStart.AddHours(2)),
            Movement(6116, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Sale, 0, 1,
                StockLedgerReferenceType.Sale, 35, octoberStart.AddHours(3)),
            Movement(6117, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.TransferOut, 0, 2,
                StockLedgerReferenceType.StockTransfer, 36, octoberStart.AddHours(4)),
            Movement(6118, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.Adjustment, 0, 3,
                StockLedgerReferenceType.StockAdjustment, 37, octoberStart.AddHours(5)),
            Movement(6119, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.ReturnTransferOut, 0, 9,
                StockLedgerReferenceType.StockTransfer, 38, octoberStart.AddHours(6)),
            Movement(6120, ClassificationVariantId, Outlet, "outlet", StockLedgerTransactionType.TransferRejection, 0, 0,
                StockLedgerReferenceType.StockTransfer, 39, octoberStart.AddHours(7)),

            Movement(6201, LifecycleVariantId, Warehouse, "warehouse", StockLedgerTransactionType.Grn, 10, 0,
                StockLedgerReferenceType.Grn, 40, Utc(2026, 10, 1, 6, 0)),

            Movement(6301, WarehouseVariantId, Warehouse, "warehouse", StockLedgerTransactionType.Grn, 40, 0,
                StockLedgerReferenceType.Grn, 41, Utc(2026, 10, 1, 6, 0)),
            Movement(6302, WarehouseVariantId, UnassignedWarehouse, "warehouse", StockLedgerTransactionType.Grn, 900, 0,
                StockLedgerReferenceType.Grn, 42, Utc(2026, 10, 1, 6, 0)));

        db.StockTransfers.AddRange(
            new StockTransfer
            {
                Id = 9477,
                TransferNo = "TRF-2026-00077",
                FromLocationId = Warehouse,
                FromLocationType = "warehouse",
                ToLocationId = Outlet,
                ToLocationType = "outlet",
                Status = StockTransfer.StatusReceived
            },
            new StockTransfer
            {
                Id = 9401,
                TransferNo = "TRF-2026-09401",
                FromLocationId = Warehouse,
                FromLocationType = "warehouse",
                ToLocationId = Outlet,
                ToLocationType = "outlet",
                Status = StockTransfer.StatusDraft,
                CreatedBy = OwnerId,
                Items =
                [
                    new StockTransferItem
                    {
                        VariantId = LifecycleVariantId,
                        Quantity = 10,
                        RequestedQuantity = 10,
                        TransferQuantity = 10,
                        UnitCost = 5m
                    }
                ]
            });
        db.StockAdjustments.Add(new StockAdjustment
        {
            Id = 9488,
            AdjustmentNumber = "ADJ-2026-00088",
            LocationId = Outlet,
            LocationType = "outlet",
            Status = StockAdjustment.StatusApproved,
            AdjustedBy = OwnerId
        });

        await db.SaveChangesAsync();
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ProductVariant Variant(long id, long businessId, long productId) =>
        new() { Id = id, BusinessId = businessId, ProductId = productId, Name = $"Variant {id}", Sku = $"SKU-{id}" };

    private static User User(long id, string name, string scope, long? outletId) =>
        new()
        {
            Id = id,
            BusinessId = BusinessId,
            OutletId = outletId,
            Name = name,
            Email = $"{name}@example.test",
            PasswordHash = "test-only",
            InventoryLocationAccessScope = scope,
            IsActive = true
        };

    private static RetailPOS.Core.Entities.Inventory Stock(
        long id,
        long variantId,
        long locationId,
        string locationType,
        int quantity) =>
        new()
        {
            Id = id,
            VariantId = variantId,
            LocationId = locationId,
            LocationType = locationType,
            Quantity = quantity
        };

    private static StockLedger Movement(
        long id,
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int quantityIn,
        int quantityOut,
        string referenceType,
        long referenceId,
        DateTime createdAt) =>
        new()
        {
            Id = id,
            VariantId = variantId,
            LocationId = locationId,
            LocationType = locationType,
            TransactionType = transactionType,
            QtyIn = quantityIn,
            QtyOut = quantityOut,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            CreatedAt = createdAt
        };
}
