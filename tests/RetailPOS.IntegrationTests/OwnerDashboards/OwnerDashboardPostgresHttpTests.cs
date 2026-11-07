using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.OwnerDashboards;

/// <summary>Real PostgreSQL, real HTTP pipeline: money, tenancy, the two dashboard permissions and business-wide scope.</summary>
public sealed class OwnerDashboardPostgresHttpTests : IClassFixture<OwnerDashboardPostgresFixture>
{
    private const string SalesPermission = "dashboard.sales_finance.view";
    private const string InventoryPermission = "dashboard.inventory_intelligence.view";
    private const string Sales = "from=2026-10-10&to=2026-10-12";
    private const string Analysis = "from=2026-10-01&to=2026-10-30";
    private const string Base = "/api/v1/owner-dashboard";
    private readonly OwnerDashboardPostgresFixture _fixture;

    public OwnerDashboardPostgresHttpTests(OwnerDashboardPostgresFixture fixture) => _fixture = fixture;

    private HttpClient Owner() => _fixture.Client(OwnerDashboardSeed.OwnerId, "BusinessOwner", "*");

    /// <summary>Assigned to outlet A only, with no other permission than the ones given here.</summary>
    private HttpClient AssignedUser(string permissions) =>
        _fixture.Client(OwnerDashboardSeed.ManagerId, "Manager", permissions);

    /// <summary>No Step 3 scope, no Step 5 assignment and no default outlet.</summary>
    private HttpClient UnassignedUser(string permissions) =>
        _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", permissions);

    private static async Task<JsonElement> DataAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return response.StatusCode;
    }

    private static decimal? Metric(JsonElement data, string name)
    {
        var value = data.GetProperty(name).GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    }

    private static string MetricStatus(JsonElement data, string name) =>
        data.GetProperty(name).GetProperty("status").GetString()!;

    // ---- The two permissions ------------------------------------------------------------------------------------

    public static TheoryData<string> SalesEndpoints => new()
    {
        $"{Base}/sales/summary?{Sales}",
        $"{Base}/sales/showrooms?{Sales}",
        $"{Base}/sales/cash-flow?{Sales}",
        $"{Base}/sales/trend?{Sales}"
    };

    public static TheoryData<string> InventoryEndpoints => new()
    {
        $"{Base}/inventory/summary?{Analysis}",
        $"{Base}/inventory/locations?{Analysis}",
        $"{Base}/inventory/best-sellers?{Analysis}",
        $"{Base}/inventory/slow-movers?{Analysis}",
        $"{Base}/inventory/transfer-opportunities?{Analysis}",
        $"{Base}/inventory/products/{OwnerDashboardSeed.ProductOne}/insights?{Analysis}"
    };

    [Theory]
    [MemberData(nameof(SalesEndpoints))]
    public async Task SalesPermissionOnly_OpensSalesEndpoints(string path)
    {
        using var client = UnassignedUser(SalesPermission);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, path));
    }

    [Theory]
    [MemberData(nameof(InventoryEndpoints))]
    public async Task SalesPermissionOnly_IsForbiddenFromInventoryEndpoints(string path)
    {
        using var client = UnassignedUser(SalesPermission);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(client, path));
    }

    [Theory]
    [MemberData(nameof(InventoryEndpoints))]
    public async Task InventoryPermissionOnly_OpensInventoryEndpoints(string path)
    {
        using var client = UnassignedUser(InventoryPermission);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, path));
    }

    [Theory]
    [MemberData(nameof(SalesEndpoints))]
    public async Task InventoryPermissionOnly_IsForbiddenFromSalesEndpoints(string path)
    {
        using var client = UnassignedUser(InventoryPermission);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(client, path));
    }

    [Theory]
    [MemberData(nameof(SalesEndpoints))]
    [MemberData(nameof(InventoryEndpoints))]
    public async Task BothPermissions_OpenEveryEndpoint(string path)
    {
        using var client = UnassignedUser($"{SalesPermission},{InventoryPermission}");

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, path));
    }

    [Theory]
    [MemberData(nameof(SalesEndpoints))]
    [MemberData(nameof(InventoryEndpoints))]
    public async Task NeitherPermission_IsForbidden_EvenWithOtherModulesViewPermissions(string path)
    {
        // The report and cost permissions that used to open these screens no longer do.
        using var client = AssignedUser(
            "reports.sales,reports.inventory,reports.financial,products.view_cost,sales.view,inventory.view,stock_transfers.create");

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(client, path));
    }

    [Theory]
    [InlineData(SalesPermission, HttpStatusCode.OK)]
    [InlineData(InventoryPermission, HttpStatusCode.OK)]
    [InlineData("products.view", HttpStatusCode.Forbidden)]
    public async Task Meta_OpensWithEitherDashboardPermission(string permission, HttpStatusCode expected)
    {
        using var client = UnassignedUser(permission);

        Assert.Equal(expected, await StatusAsync(client, $"{Base}/meta"));
    }

    // ---- Business-wide visibility, one reporting scope --------------------------------------------------------------

    [Fact]
    public async Task UserWithoutAssignments_SeesTheSameFiguresAsTheOwner()
    {
        using var owner = Owner();
        using var unassigned = UnassignedUser(SalesPermission);

        var expected = await DataAsync(owner, $"{Base}/sales/summary?{Sales}");
        var actual = await DataAsync(unassigned, $"{Base}/sales/summary?{Sales}");

        foreach (var name in new[] { "netSales", "purchase", "expense", "profit" })
            Assert.Equal(Metric(expected, name), Metric(actual, name));
        Assert.Equal(500m, Metric(actual, "profit"));
    }

    [Fact]
    public async Task UserAssignedToOneOutlet_IsNotLimitedToIt()
    {
        using var client = AssignedUser($"{SalesPermission},{InventoryPermission}");

        var summary = await DataAsync(client, $"{Base}/sales/summary?{Sales}");
        Assert.Equal(1800m, Metric(summary, "netSales"));

        var otherOutlet = await DataAsync(client, $"{Base}/sales/summary?{Sales}&outletId={OwnerDashboardSeed.OutletB}");
        Assert.Equal(300m, Metric(otherOutlet, "netSales"));

        var meta = await DataAsync(client, $"{Base}/meta");
        Assert.Equal(2, meta.GetProperty("showrooms").GetArrayLength());
        Assert.Equal(1, meta.GetProperty("warehouses").GetArrayLength());

        var inventory = await DataAsync(client, $"{Base}/inventory/summary?{Analysis}");
        Assert.Equal(83, inventory.GetProperty("totalStockQuantity").GetInt64());
        Assert.Equal(3280m, Metric(inventory, "totalStockValue"));
    }

    [Fact]
    public async Task AllLocations_Profit_CarriesEveryExpense_AndReconcilesToEachShowroom()
    {
        using var client = UnassignedUser(SalesPermission);

        var all = await DataAsync(client, $"{Base}/sales/summary?{Sales}");
        var outletA = await DataAsync(client, $"{Base}/sales/summary?{Sales}&outletId={OwnerDashboardSeed.OutletA}");
        var outletB = await DataAsync(client, $"{Base}/sales/summary?{Sales}&outletId={OwnerDashboardSeed.OutletB}");

        // Whole Business: 1800 sales - 1080 COGS - 220 expense (A 140, business-level 50, warehouse 30).
        Assert.Equal(1800m, Metric(all, "netSales"));
        Assert.Equal(220m, Metric(all, "expense"));
        Assert.Equal(500m, Metric(all, "profit"));
        Assert.Equal("estimated", MetricStatus(all, "profit"));

        // Outlet A: 1500 sales - 900 COGS - 140 expense.
        Assert.Equal(1500m, Metric(outletA, "netSales"));
        Assert.Equal(140m, Metric(outletA, "expense"));
        Assert.Equal(460m, Metric(outletA, "profit"));

        // Outlet B: 300 sales - 180 COGS, no expense of its own.
        Assert.Equal(300m, Metric(outletB, "netSales"));
        Assert.Equal(0m, Metric(outletB, "expense"));
        Assert.Equal(120m, Metric(outletB, "profit"));

        // Showrooms plus the costs only the whole Business carries (business-level 50, warehouse 30).
        Assert.Equal(Metric(all, "profit"), Metric(outletA, "profit") + Metric(outletB, "profit") - 80m);
    }

    [Fact]
    public async Task SelectedShowroom_NarrowsCashFlowAlike()
    {
        using var client = UnassignedUser(SalesPermission);

        var all = await DataAsync(client, $"{Base}/sales/cash-flow?{Sales}");
        var outletB = await DataAsync(client, $"{Base}/sales/cash-flow?{Sales}&outletId={OwnerDashboardSeed.OutletB}");

        Assert.Equal(2070m, all.GetProperty("totalCashReceived").GetDecimal());
        Assert.Equal(195m, all.GetProperty("totalExpensePayments").GetDecimal());
        Assert.Equal(345m, outletB.GetProperty("totalCashReceived").GetDecimal());
        Assert.Equal(0m, outletB.GetProperty("totalExpensePayments").GetDecimal());
    }

    [Fact]
    public async Task DateRange_IsEnforcedOnEveryFinancialComponent()
    {
        using var client = UnassignedUser(SalesPermission);

        var outside = await DataAsync(client, $"{Base}/sales/summary?from=2026-09-01&to=2026-09-02");

        Assert.Equal(0m, Metric(outside, "netSales"));
        Assert.Equal(0m, Metric(outside, "purchase"));
        Assert.Equal(0m, Metric(outside, "expense"));
        Assert.Equal(0m, Metric(outside, "profit"));
    }

    [Fact]
    public async Task SingleLocation_InventoryReporting_FiltersToThatLocation()
    {
        using var client = UnassignedUser(InventoryPermission);

        var outlet = await DataAsync(
            client, $"{Base}/inventory/summary?{Analysis}&locationId={OwnerDashboardSeed.OutletA}&locationType=outlet");
        var warehouse = await DataAsync(
            client, $"{Base}/inventory/summary?{Analysis}&locationId={OwnerDashboardSeed.Warehouse}&locationType=warehouse");

        Assert.Equal(11, outlet.GetProperty("totalStockQuantity").GetInt64());
        Assert.Equal(300m, Metric(outlet, "totalStockValue"));
        Assert.Equal(20, warehouse.GetProperty("totalStockQuantity").GetInt64());
        Assert.Equal(1000m, Metric(warehouse, "totalStockValue"));
    }

    // ---- Accounting treatment ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SalesSummary_NetsVatVoidsAndRefunds_AndExcludesOtherBusinessesAndDrafts()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/sales/summary?{Sales}");

        // A: 1000 + 100 + 200 (void) + 500 (refunded), less reversals 200 and 100. B: 300.
        Assert.Equal(1800m, Metric(data, "netSales"));
        Assert.Null(Metric(data, "target"));
        Assert.Equal("not_configured", MetricStatus(data, "target"));
        Assert.Equal("not_configured", MetricStatus(data, "shortfall"));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("achievementPercent").ValueKind);
    }

    [Fact]
    public async Task Discounts_AndVatInclusivePrices_LeaveNetRevenueExclVat()
    {
        using var owner = Owner();

        // 1150 VAT-inclusive, less 115 discount: 1035 payable, of which 135 is VAT.
        var data = await DataAsync(owner, $"{Base}/sales/summary?from=2026-11-03&to=2026-11-03");

        Assert.Equal(900m, Metric(data, "netSales"));
        Assert.Equal(300m, Metric(data, "profit")); // 900 - 10 x 60.
    }

    [Fact]
    public async Task Cogs_RoundsToCurrencyPerSale_AsTheJournalDoes()
    {
        using var owner = Owner();

        // Two sales of 3 x 33.3333: each journal posts 100.00, so COGS is 200.00, not 199.9998.
        var data = await DataAsync(owner, $"{Base}/sales/summary?from=2026-11-02&to=2026-11-02");

        Assert.Equal(300m, Metric(data, "netSales"));
        Assert.Equal(100m, Metric(data, "profit"));
    }

    [Fact]
    public async Task SalesSummary_SeparatesPurchaseAndExpense_AndProfitNeverUsesPurchases()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/sales/summary?{Sales}");

        Assert.Equal(1000m, Metric(data, "purchase"));
        Assert.Equal(500m, Metric(data, "profit"));
    }

    [Fact]
    public async Task SalesSummary_WithAMissingLineCost_ReportsProfitUnavailable()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/sales/summary?from=2026-10-20&to=2026-10-20");

        Assert.Equal(70m, Metric(data, "netSales"));
        Assert.Null(Metric(data, "profit"));
        Assert.Equal("unavailable", MetricStatus(data, "profit"));
    }

    [Fact]
    public async Task SalesAfterMidnight_BelongToTheCashSessionBusinessDate()
    {
        using var owner = Owner();

        var sessionDay = await DataAsync(owner, $"{Base}/sales/trend?from=2026-10-11&to=2026-10-11");
        Assert.Equal(100m, sessionDay.GetProperty("points")[0].GetProperty("netSales").GetDecimal());

        var calendarDay = await DataAsync(owner, $"{Base}/sales/trend?from=2026-10-13&to=2026-10-13");
        Assert.Equal(0m, calendarDay.GetProperty("points")[0].GetProperty("netSales").GetDecimal());
    }

    [Fact]
    public async Task Reversal_ReducesThePeriodOfItsAccountingDate()
    {
        using var owner = Owner();
        var trend = await DataAsync(owner, $"{Base}/sales/trend?{Sales}");
        var points = trend.GetProperty("points");

        Assert.Equal(1800m, points[0].GetProperty("netSales").GetDecimal()); // 10th: 1000 + 200 - 200 + 500 + 300.
        Assert.Equal(100m, points[1].GetProperty("netSales").GetDecimal());
        Assert.Equal(-100m, points[2].GetProperty("netSales").GetDecimal()); // 12th: the refund alone.
        Assert.Equal("not_configured", trend.GetProperty("targetStatus").GetString());
    }

    [Fact]
    public async Task ShowroomTotals_ReconcileToSummary_AndWarehousesAreNotShowrooms()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/sales/showrooms?{Sales}");
        var rows = data.GetProperty("showrooms").EnumerateArray().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1800m, rows.Sum(row => row.GetProperty("netSales").GetDecimal()));
        Assert.All(rows, row => Assert.Equal("not_configured", row.GetProperty("targetStatus").GetString()));
    }

    [Fact]
    public async Task CashFlow_UsesTenderAndActualDisbursement()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/sales/cash-flow?{Sales}");

        // Payments 1150 + 115 + 230 + 575 + 345 = 2415, less returned 230 + 115.
        Assert.Equal(2070m, data.GetProperty("totalCashReceived").GetDecimal());

        // Gross 115 + 50 + 30 paid directly; the undisbursed drawer expense has moved no cash.
        Assert.Equal(195m, data.GetProperty("totalExpensePayments").GetDecimal());
        Assert.Equal(1875m, data.GetProperty("difference").GetDecimal());
    }

    // ---- Business isolation -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("sales/summary?from=2026-10-10&to=2026-10-12&outletId=6211")]
    [InlineData("sales/showrooms?from=2026-10-10&to=2026-10-12&outletId=6211")]
    [InlineData("sales/cash-flow?from=2026-10-10&to=2026-10-12&outletId=6211")]
    [InlineData("sales/trend?from=2026-10-10&to=2026-10-12&outletId=6211")]
    [InlineData("inventory/summary?from=2026-10-01&to=2026-10-30&locationId=6211&locationType=outlet")]
    [InlineData("inventory/best-sellers?from=2026-10-01&to=2026-10-30&locationId=6211&locationType=outlet")]
    [InlineData("inventory/transfer-opportunities?from=2026-10-01&to=2026-10-30&sourceLocationId=6211")]
    public async Task AnotherBusinessesLocation_IsForbidden_EvenForTheOwner(string path)
    {
        using var owner = Owner();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(owner, $"{Base}/{path}"));
    }

    [Fact]
    public async Task EachBusiness_SeesOnlyItsOwnData()
    {
        using var foreign = _fixture.Client(
            OwnerDashboardSeed.ForeignUserId, "BusinessOwner", "*", OwnerDashboardSeed.ForeignBusiness);

        var summary = await DataAsync(foreign, $"{Base}/sales/summary?{Sales}");
        Assert.Equal(999m, Metric(summary, "netSales"));

        var meta = await DataAsync(foreign, $"{Base}/meta");
        var showroom = Assert.Single(meta.GetProperty("showrooms").EnumerateArray().ToList());
        Assert.Equal(OwnerDashboardSeed.ForeignOutlet, showroom.GetProperty("id").GetInt64());
        Assert.Empty(meta.GetProperty("warehouses").EnumerateArray());

        var inventory = await DataAsync(foreign, $"{Base}/inventory/summary?{Analysis}");
        Assert.Equal(77, inventory.GetProperty("totalStockQuantity").GetInt64());

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await StatusAsync(foreign, $"{Base}/sales/summary?{Sales}&outletId={OwnerDashboardSeed.OutletA}"));
    }

    [Fact]
    public async Task BusinessIdInTheQuery_CannotSwitchBusiness()
    {
        using var client = UnassignedUser(SalesPermission);

        var data = await DataAsync(client, $"{Base}/sales/summary?{Sales}&businessId={OwnerDashboardSeed.ForeignBusiness}");

        Assert.Equal(1800m, Metric(data, "netSales"));
    }

    [Fact]
    public async Task BusinessHeader_ForAnotherBusiness_NeverReturnsItsData()
    {
        using var client = UnassignedUser(SalesPermission);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/sales/summary?{Sales}");
        request.Headers.Add("X-Business-Id", OwnerDashboardSeed.ForeignBusiness.ToString());

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("999", body);
    }

    [Fact]
    public async Task ForeignProduct_IsNotFound_EvenWithTheSameMainCode()
    {
        using var owner = Owner();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await StatusAsync(owner, $"{Base}/inventory/products/{OwnerDashboardSeed.ForeignProduct}/insights?{Analysis}"));

        var insights = await DataAsync(owner, $"{Base}/inventory/products/{OwnerDashboardSeed.ProductOne}/insights?{Analysis}");
        Assert.Equal("MAIN-1", insights.GetProperty("mainCode").GetString());
        Assert.Equal(5, insights.GetProperty("locations").GetArrayLength()); // Two variants at A and B, one at the warehouse.
    }

    // ---- Requests -----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("from=2026-10-12&to=2026-10-10")]
    [InlineData("from=2024-01-01&to=2026-10-10")]
    public async Task InvalidRanges_AreRejected(string range)
    {
        using var owner = Owner();

        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(owner, $"{Base}/sales/summary?{range}"));
    }

    // ---- Inventory figures --------------------------------------------------------------------------------------------

    [Fact]
    public async Task InventorySummary_ValuesAtWeightedAverage_WithoutDoubleCounting()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/inventory/summary?{Analysis}");

        // A/V1 300, B/V1 1680, warehouse/V1 1000, B/V3 300. A/V2 has no cost history, so it is not valued.
        Assert.Equal(3280m, Metric(data, "totalStockValue"));
        Assert.Equal("partial", MetricStatus(data, "totalStockValue"));
        Assert.Equal(1, data.GetProperty("unvaluedStockRows").GetInt32());
        Assert.Equal(83, data.GetProperty("totalStockQuantity").GetInt64());
        Assert.Equal(2, data.GetProperty("activeStockedProducts").GetInt32());
        Assert.Equal(3, data.GetProperty("stockedVariants").GetInt32());
        Assert.Equal(1, data.GetProperty("outOfStockVariants").GetInt32());
        Assert.Equal(1980m, Metric(data, "slowMovingStockValue"));
        Assert.Equal(1, data.GetProperty("transferOpportunities").GetInt32());
    }

    [Fact]
    public async Task StockByLocation_SumsToTheSummaryValue()
    {
        using var owner = Owner();
        var summary = await DataAsync(owner, $"{Base}/inventory/summary?{Analysis}");
        var byLocation = await DataAsync(owner, $"{Base}/inventory/locations?{Analysis}");
        var rows = byLocation.GetProperty("locations").EnumerateArray().ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(Metric(summary, "totalStockValue"), rows.Sum(row => row.GetProperty("stockValue").GetDecimal()));
        Assert.Equal(
            summary.GetProperty("totalStockQuantity").GetInt64(),
            rows.Sum(row => row.GetProperty("quantity").GetInt64()));
    }

    [Fact]
    public async Task BestSellers_RankByNetUnits_AndFindByMainCode()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/inventory/best-sellers?{Analysis}&search=MAIN-1");
        var first = data.GetProperty("items")[0];

        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal("MAIN-1", first.GetProperty("mainCode").GetString());
        Assert.Equal("MAIN-1-BLK-XL", first.GetProperty("sku").GetString());
        Assert.Equal(19, first.GetProperty("netUnitsSold").GetInt64()); // A: 15 + 1 late-month. B: 3.
        Assert.Equal(68, first.GetProperty("currentAvailableStock").GetInt64());
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task BestSellers_RejectUnknownSortColumns()
    {
        using var owner = Owner();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            await StatusAsync(owner, $"{Base}/inventory/best-sellers?{Analysis}&sortBy=passwordHash"));
    }

    [Fact]
    public async Task SlowMovers_ClassifyByInStockVelocity_AndSkipWarehouses()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/inventory/slow-movers?{Analysis}");
        var rows = data.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.GetProperty("sku").GetString() == "MAIN-1-BLK-XL"
            && row.GetProperty("classification").GetString() == "low_sales"
            && row.GetProperty("locationId").GetInt64() == OwnerDashboardSeed.OutletB);
        Assert.Contains(rows, row => row.GetProperty("sku").GetString() == "SECOND-1"
            && row.GetProperty("classification").GetString() == "never_sold");

        // A/V2 has stock but no ledger history: reported as a count, never classified as slow.
        Assert.Equal(1, data.GetProperty("insufficientEvidenceCount").GetInt32());

        var zero = await DataAsync(owner, $"{Base}/inventory/slow-movers?{Analysis}&classification=zero_sales");
        Assert.Equal(1, zero.GetProperty("rows").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Payloads_NeverCarryCustomerOrCashierFields()
    {
        using var owner = Owner();
        using var response = await owner.GetAsync($"{Base}/inventory/best-sellers?{Analysis}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("customer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cashier", body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Stock transfers ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TransferOpportunity_MatchesExactVariant_WithSafeQuantity_AndMovesNoStock()
    {
        using var owner = Owner();
        var data = await DataAsync(owner, $"{Base}/inventory/transfer-opportunities?{Analysis}");
        var row = Assert.Single(data.GetProperty("rows").GetProperty("items").EnumerateArray().ToList());

        Assert.Equal(OwnerDashboardSeed.OutletB, row.GetProperty("source").GetProperty("locationId").GetInt64());
        Assert.Equal(OwnerDashboardSeed.OutletA, row.GetProperty("destination").GetProperty("locationId").GetInt64());
        Assert.Equal("MAIN-1-BLK-XL", row.GetProperty("sku").GetString());
        Assert.Equal(5, row.GetProperty("suggestedQty").GetInt32()); // ceil(0.5333 x 21) = 12, less 6 on hand, less 1 inbound.
        Assert.True(row.GetProperty("canInitiate").GetBoolean());
        Assert.Contains(
            "reduced_by_inbound",
            row.GetProperty("reasonCodes").EnumerateArray().Select(reason => reason.GetString()));

        var key = row.GetProperty("recommendationKey").GetString();
        var detail = await DataAsync(owner, $"{Base}/inventory/transfer-opportunities/{key}?{Analysis}");
        Assert.Equal(5, detail.GetProperty("suggestedQty").GetInt32());

        Assert.Equal(
            HttpStatusCode.NotFound,
            await StatusAsync(owner, $"{Base}/inventory/transfer-opportunities/1-2-3?{Analysis}"));

        Assert.Equal(42, await _fixture.ReadOnHandAsync(OwnerDashboardSeed.VariantOne, OwnerDashboardSeed.OutletB));
    }

    [Fact]
    public async Task InventoryPermissionOnly_SeesRecommendations_ButCannotInitiate()
    {
        using var client = UnassignedUser(InventoryPermission);

        var data = await DataAsync(client, $"{Base}/inventory/transfer-opportunities?{Analysis}");
        var row = Assert.Single(data.GetProperty("rows").GetProperty("items").EnumerateArray().ToList());
        Assert.False(row.GetProperty("canInitiate").GetBoolean());

        var key = row.GetProperty("recommendationKey").GetString();
        var detail = await DataAsync(client, $"{Base}/inventory/transfer-opportunities/{key}?{Analysis}");
        Assert.False(detail.GetProperty("canInitiate").GetBoolean());
    }

    [Fact]
    public async Task CreatePermission_WithoutAssignmentToTheSource_CannotInitiate()
    {
        // Assigned to outlet A, while the recommendation's source is outlet B.
        using var client = AssignedUser($"{InventoryPermission},stock_transfers.create");

        var data = await DataAsync(client, $"{Base}/inventory/transfer-opportunities?{Analysis}");
        var row = Assert.Single(data.GetProperty("rows").GetProperty("items").EnumerateArray().ToList());

        Assert.Equal(OwnerDashboardSeed.OutletB, row.GetProperty("source").GetProperty("locationId").GetInt64());
        Assert.False(row.GetProperty("canInitiate").GetBoolean());
    }

    [Fact]
    public async Task DashboardPermission_CannotCreateARealTransfer()
    {
        using var client = UnassignedUser($"{SalesPermission},{InventoryPermission}");

        using var response = await client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = OwnerDashboardSeed.OutletB,
            fromLocationType = "outlet",
            toLocationId = OwnerDashboardSeed.OutletA,
            toLocationType = "outlet",
            transferDate = "2026-11-06",
            items = new[] { new { variantId = OwnerDashboardSeed.VariantOne, quantity = 1, requestedQuantity = 1, transferQuantity = 1, unitCost = 0 } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(42, await _fixture.ReadOnHandAsync(OwnerDashboardSeed.VariantOne, OwnerDashboardSeed.OutletB));
    }
}

internal static class OwnerDashboardSeed
{
    public const long Business = 6101;
    public const long ForeignBusiness = 6201;
    public const long OutletA = 6111;
    public const long OutletB = 6112;
    public const long Warehouse = 6113;
    public const long ForeignOutlet = 6211;
    public const long OwnerId = 6120;
    public const long ManagerId = 6121;
    public const long UnassignedUserId = 6122;
    public const long ForeignUserId = 6220;
    public const long ProductOne = 6301;
    public const long ProductTwo = 6302;
    public const long ForeignProduct = 6303;
    public const long VariantOne = 6401;
    public const long VariantTwo = 6402;
    public const long VariantThree = 6403;
    public const long ForeignVariant = 6404;

    public static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = Business, Name = "Owner dashboard business" },
            new Business { Id = ForeignBusiness, Name = "Other business" });
        db.Outlets.AddRange(
            new Outlet { Id = OutletA, BusinessId = Business, Name = "Outlet A" },
            new Outlet { Id = OutletB, BusinessId = Business, Name = "Outlet B" },
            new Outlet { Id = ForeignOutlet, BusinessId = ForeignBusiness, Name = "Foreign outlet" });
        db.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = Business, Name = "Central warehouse" });
        db.Users.AddRange(
            User(OwnerId, Business, RetailPOS.Core.Entities.User.InventoryAccessAll),
            User(ManagerId, Business, RetailPOS.Core.Entities.User.InventoryAccessSpecific),
            User(UnassignedUserId, Business, RetailPOS.Core.Entities.User.InventoryAccessAssignedOnly),
            User(ForeignUserId, ForeignBusiness, RetailPOS.Core.Entities.User.InventoryAccessAll));
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 6501, UserId = ManagerId, OutletId = OutletA, BusinessId = Business, IsPrimary = true, IsActive = true
        });
        db.Categories.AddRange(
            new Category { Id = 6601, BusinessId = Business, Name = "Borka", Description = "" },
            new Category { Id = 6602, BusinessId = ForeignBusiness, Name = "Foreign", Description = "" });
        db.Products.AddRange(
            new Product { Id = ProductOne, BusinessId = Business, Name = "Premium Borka", ProductCode = "MAIN-1", CategoryId = 6601 },
            new Product { Id = ProductTwo, BusinessId = Business, Name = "Second Item", ProductCode = "MAIN-2", CategoryId = 6601 },
            new Product { Id = ForeignProduct, BusinessId = ForeignBusiness, Name = "Foreign Borka", ProductCode = "MAIN-1-F", CategoryId = 6602 });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = VariantOne, BusinessId = Business, ProductId = ProductOne, Name = "Black / XL", Sku = "MAIN-1-BLK-XL" },
            new ProductVariant { Id = VariantTwo, BusinessId = Business, ProductId = ProductOne, Name = "Black / L", Sku = "MAIN-1-BLK-L" },
            new ProductVariant { Id = VariantThree, BusinessId = Business, ProductId = ProductTwo, Name = "One size", Sku = "SECOND-1" },
            new ProductVariant { Id = ForeignVariant, BusinessId = ForeignBusiness, ProductId = ForeignProduct, Name = "Foreign", Sku = "FOREIGN-1" });
        await db.SaveChangesAsync();

        // Outlet A sales. VAT-exclusive pricing at 15%, cost 60 a unit.
        db.Sales.AddRange(
            Sale(7001, OutletA, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc), "completed", 10, 1000m, 150m, 60m),
            // 01:00 on the 12th in Dhaka, inside the session opened on the 11th.
            Sale(7002, OutletA, new DateTime(2026, 10, 11), new DateTime(2026, 10, 11, 19, 0, 0, DateTimeKind.Utc), "completed", 1, 100m, 15m, 60m),
            Sale(7003, OutletA, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc), "voided", 2, 200m, 30m, 60m),
            Sale(7004, OutletA, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc), "refunded", 5, 500m, 75m, 60m),
            Sale(7005, OutletA, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), "pending", 9, 900m, 135m, 60m),
            // Line without cost evidence, in its own day.
            Sale(7006, OutletA, new DateTime(2026, 10, 20), new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc), "completed", 1, 70m, 0m, null),
            Sale(7007, OutletB, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc), "completed", 3, 300m, 45m, 60m),
            // Outside every other test's window: two sales whose 3 x 33.3333 cost rounds to 100.00 each.
            Sale(7008, OutletB, new DateTime(2026, 11, 2), new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc), "completed", 3, 150m, 0m, 33.3333m),
            Sale(7009, OutletB, new DateTime(2026, 11, 2), new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc), "completed", 3, 150m, 0m, 33.3333m),
            // 1150 VAT-inclusive less a 115 discount: 900 net plus 135 VAT.
            Sale(7010, OutletB, new DateTime(2026, 11, 3), new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc), "completed", 10, 900m, 135m, 60m, discount: 115m),
            Sale(7099, ForeignOutlet, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc), "completed", 1, 999m, 0m, 60m, ForeignVariant, ForeignBusiness, ForeignUserId));
        await db.SaveChangesAsync();

        db.SaleFinancialPostings.AddRange(
            Reversal(7003, OutletA, "void", new DateTime(2026, 10, 10, 7, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 10), 2, 200m, 30m, 120m),
            Reversal(7004, OutletA, "refund", new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 12), 1, 100m, 15m, 60m));

        // Stock. V1 at A: 21 in at 50, 15 out. V1 at B: 45 in at 40, 3 out. Warehouse: 20 at 50. V3 at B: 10 at 30.
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantOne, LocationId = OutletA, LocationType = "outlet", Quantity = 6 },
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantTwo, LocationId = OutletA, LocationType = "outlet", Quantity = 5 },
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantOne, LocationId = OutletB, LocationType = "outlet", Quantity = 42 },
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantTwo, LocationId = OutletB, LocationType = "outlet", Quantity = 0 },
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantThree, LocationId = OutletB, LocationType = "outlet", Quantity = 10 },
            new RetailPOS.Core.Entities.Inventory { VariantId = VariantOne, LocationId = Warehouse, LocationType = "warehouse", Quantity = 20 },
            new RetailPOS.Core.Entities.Inventory { VariantId = ForeignVariant, LocationId = ForeignOutlet, LocationType = "outlet", Quantity = 77 });
        db.StockLedgers.AddRange(
            Ledger(VariantOne, OutletA, "outlet", new DateTime(2026, 9, 1), 22, 0, 22, 50m, 1),
            Ledger(VariantOne, OutletA, "outlet", new DateTime(2026, 10, 12), 0, 16, 6, 50m, 2),
            Ledger(VariantOne, OutletB, "outlet", new DateTime(2026, 9, 1), 45, 0, 45, 40m, 1),
            Ledger(VariantOne, OutletB, "outlet", new DateTime(2026, 10, 10), 0, 3, 42, 40m, 2),
            Ledger(VariantThree, OutletB, "outlet", new DateTime(2026, 9, 1), 10, 0, 10, 30m, 1),
            Ledger(VariantOne, Warehouse, "warehouse", new DateTime(2026, 9, 1), 20, 0, 20, 50m, 1),
            Ledger(ForeignVariant, ForeignOutlet, "outlet", new DateTime(2026, 9, 1), 77, 0, 77, 10m, 1));

        // Purchases: one completed receipt counts; an uncompleted one does not.
        db.Suppliers.Add(new Supplier { Id = 6701, BusinessId = Business, Name = "Supplier" });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 6711,
            PoNumber = "PO-OD-1",
            SupplierId = 6701,
            WarehouseId = Warehouse,
            OrderDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 9999m,
            Status = "completed",
            Items = [new PurchaseOrderItem { Id = 6712, VariantId = VariantOne, Quantity = 40, UnitPrice = 50m }]
        });
        await db.SaveChangesAsync();
        db.Grns.AddRange(
            new Grn
            {
                Id = 6721,
                PoId = 6711,
                ReceivedDate = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                Items = [new GrnItem { Id = 6722, PoItemId = 6712, ReceivedQty = 20, UnitCost = 50m }]
            },
            new Grn
            {
                Id = 6723,
                PoId = 6711,
                ReceivedDate = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
                Status = "partial",
                Items = [new GrnItem { Id = 6724, PoItemId = 6712, ReceivedQty = 20, UnitCost = 50m }]
            });

        // Expenses.
        db.Expenses.AddRange(
            Expense(6801, OutletA, ExpenseStatuses.Approved, 115m, 100m, ExpensePaymentSources.Bank),
            Expense(6802, null, ExpenseStatuses.Approved, 50m, null, ExpensePaymentSources.Bank),
            Expense(6803, OutletA, ExpenseStatuses.Draft, 500m, null, ExpensePaymentSources.Bank),
            Expense(6805, null, ExpenseStatuses.Approved, 30m, null, ExpensePaymentSources.Bank, Warehouse));
        var undisbursed = Expense(6804, OutletA, ExpenseStatuses.Approved, 40m, null, ExpensePaymentSources.PosDrawer);
        db.Expenses.Add(undisbursed);
        await db.SaveChangesAsync();

        // One unit already on its way to outlet A lowers its need.
        db.StockTransfers.Add(new StockTransfer
        {
            Id = 6901,
            TransferNo = "TRF-OD-1",
            FromLocationId = Warehouse,
            FromLocationType = "warehouse",
            ToLocationId = OutletA,
            ToLocationType = "outlet",
            TransferDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = StockTransfer.StatusInTransit,
            Items = [new StockTransferItem { Id = 6902, VariantId = VariantOne, Quantity = 1, RequestedQuantity = 1, TransferQuantity = 1 }]
        });
        await db.SaveChangesAsync();
    }

    private static User User(long id, long businessId, string scope) => new()
    {
        Id = id,
        BusinessId = businessId,
        InventoryLocationAccessScope = scope,
        Name = $"User {id}",
        Email = $"owner-dashboard-{id}@example.test",
        PasswordHash = "test-only",
        IsActive = true
    };

    private static Sale Sale(
        long id,
        long outletId,
        DateTime businessDate,
        DateTime saleTime,
        string status,
        int quantity,
        decimal net,
        decimal vat,
        decimal? unitCost,
        long variantId = VariantOne,
        long businessId = Business,
        long cashierId = OwnerId,
        decimal discount = 0m)
    {
        var item = new SaleItem
        {
            Id = id + 10000,
            VariantId = variantId,
            Quantity = quantity,
            UnitPrice = net / quantity,
            Subtotal = net,
            TaxableAmount = net,
            VatAmount = vat
        };
        if (unitCost.HasValue)
            item.CaptureHistoricalUnitCost(unitCost.Value);

        return new Sale
        {
            Id = id,
            SaleNumber = $"OD-{id}",
            OutletId = outletId,
            BusinessId = businessId,
            CashierId = cashierId,
            BusinessDate = DateTime.SpecifyKind(businessDate, DateTimeKind.Utc),
            SaleDate = saleTime,
            SaleDateTime = saleTime,
            Status = status,
            PaymentMethod = "cash",
            TotalAmount = net + vat,
            Tax = vat,
            Discount = discount,
            Items = [item],
            Payments = [new SalePayment { Id = id + 20000, Method = "cash", Amount = net + vat }]
        };
    }

    private static SaleFinancialPosting Reversal(
        long saleId,
        long outletId,
        string type,
        DateTime atUtc,
        DateOnly accountingDate,
        int quantity,
        decimal net,
        decimal vat,
        decimal cost)
    {
        var snapshot = new SaleReversalJournalSnapshot(
            SaleFinancialIntentFactory.SnapshotVersion,
            saleId,
            $"OD-{saleId}",
            Business,
            outletId,
            null,
            null,
            type,
            OwnerId,
            atUtc,
            accountingDate,
            atUtc,
            net,
            vat,
            cost,
            false,
            [new SaleReversalLineSnapshot(saleId + 10000, VariantOne, quantity, quantity, net, vat, cost)],
            [new SaleReversalPaymentSnapshot(saleId + 20000, "cash", net + vat)],
            accountingDate,
            "Asia/Dhaka",
            net + vat);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new SaleFinancialPosting
        {
            BusinessId = Business,
            SaleId = saleId,
            Kind = SaleFinancialPostingKinds.Reversal,
            ReversalType = type,
            SourceKey = SaleFinancialIntentFactory.ReversalSourceKey(saleId),
            SnapshotJson = json,
            SnapshotHash = SaleFinancialIntentFactory.Hash(json),
            OutletId = outletId,
            ActorUserId = OwnerId,
            BusinessDate = DateTime.SpecifyKind(accountingDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            Status = SaleFinancialPostingStatuses.Pending,
            NextAttemptAtUtc = atUtc,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc
        };
    }

    private static StockLedger Ledger(
        long variantId,
        long locationId,
        string locationType,
        DateTime date,
        int qtyIn,
        int qtyOut,
        int balance,
        decimal unitCost,
        long sequence) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        LocationType = locationType,
        TransactionType = qtyIn > 0 ? StockLedgerTransactionType.Grn : StockLedgerTransactionType.Sale,
        QtyIn = qtyIn,
        QtyOut = qtyOut,
        BalanceAfter = balance,
        UnitCost = unitCost,
        MovementValue = (qtyIn + qtyOut) * unitCost,
        IsCostValuationReliable = true,
        BusinessDate = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        MovementSequence = sequence,
        ReferenceType = StockLedgerReferenceType.Sale,
        ReferenceId = sequence,
        CreatedAt = DateTime.SpecifyKind(date, DateTimeKind.Utc)
    };

    private static Expense Expense(
        long id,
        long? outletId,
        string status,
        decimal amount,
        decimal? taxable,
        string source,
        long? warehouseId = null) => new()
    {
        Id = id,
        BusinessId = Business,
        Category = "Rent",
        Amount = amount,
        TaxableAmount = taxable,
        OutletId = outletId,
        WarehouseId = warehouseId,
        Status = status,
        PaymentSource = source,
        ExpenseDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)
    };
}

public sealed class OwnerDashboardPostgresFixture : IAsyncLifetime
{
    private PostgresTestDatabase? _database;
    private MobileSalesDashboardPostgresApiFactory? _factory;

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(InfrastructureTestSettings.RequireIsolatedPostgres())
        {
            Pooling = false
        };
        _database = await PostgresTestDatabase.CreateAsync(builder.ConnectionString, PostgresTestDatabasePrefix.OwnerDashboard);
        try
        {
            _factory = new MobileSalesDashboardPostgresApiFactory(_database.ConnectionString);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.MigrateAsync();
            await OwnerDashboardSeed.SeedAsync(db);
        }
        catch
        {
            if (_factory is not null)
                await _factory.DisposeAsync();
            await _database.DisposeAsync();
            _database = null;
            throw;
        }
    }

    public HttpClient Client(long userId, string role, string permissions, long businessId = OwnerDashboardSeed.Business)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    /// <summary>Direct access for setup a test cannot do over HTTP, such as a target that started in the past.</summary>
    public async Task WithDbAsync(Func<RetailPOSDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>());
    }

    public async Task<decimal> ReadOnHandAsync(long variantId, long outletId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.Inventories
            .AsNoTracking()
            .Where(inventory => inventory.VariantId == variantId
                && inventory.LocationId == outletId
                && inventory.LocationType == "outlet")
            .SumAsync(inventory => inventory.Quantity);
    }

    private MobileSalesDashboardPostgresApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }
}
