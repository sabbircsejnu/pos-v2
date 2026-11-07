using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// U-13 (Option A): <c>reports.inventory</c> grants the inventory reports with quantities, movements, dates and units;
/// every cost-derived figure, in rows, summaries and exports, also needs <c>products.view_cost</c>.
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private const string InventoryReports = "/api/reports/inventory";

    private static readonly HashSet<string> CostFieldNames = new(StringComparer.Ordinal)
    {
        "unitCost", "stockValue", "inventoryValue", "transferValue", "transactionValue", "estimatedValue", "value",
        "totalValue", "totalStockValue", "totalInventoryValue", "totalTransactionValue", "totalNegativeValue",
        "estimatedCostImpact", "percentOfTotal"
    };

    /// <summary>The pcs variant is received at this cost, so it is easy to look for in an export.</summary>
    private const decimal CostVisibilityPcsCost = 41.37m;

    private static string[] CostReportPaths(long pcsProduct) =>
    [
        $"{InventoryReports}/stock-levels",
        $"{InventoryReports}/valuation",
        $"{InventoryReports}/slow-moving?days=90",
        $"{InventoryReports}/current-stock?pageSize=500",
        $"{InventoryReports}/product-ledger?productId={pcsProduct}&pageSize=500",
        $"{InventoryReports}/stock-valuation?pageSize=500",
        $"{InventoryReports}/outlet-wise-stock?pageSize=500",
        $"{InventoryReports}/out-of-stock?pageSize=500",
        $"{InventoryReports}/negative-stock?pageSize=500",
        $"{InventoryReports}/stock-transfers-report?pageSize=500"
    ];

    private static string[] QuantityOnlyReportPaths =>
    [
        $"{InventoryReports}/stock-movement?pageSize=500",
        $"{InventoryReports}/low-stock?pageSize=500",
        $"{InventoryReports}/stock-adjustments?pageSize=500"
    ];

    private static string[] ExportPaths(long pcsProduct) =>
    [
        $"{InventoryReports}/current-stock/export",
        $"{InventoryReports}/product-ledger/export?productId={pcsProduct}",
        $"{InventoryReports}/stock-movement/export",
        $"{InventoryReports}/stock-valuation/export",
        $"{InventoryReports}/outlet-wise-stock/export",
        $"{InventoryReports}/low-stock/export",
        $"{InventoryReports}/out-of-stock/export",
        $"{InventoryReports}/negative-stock/export",
        $"{InventoryReports}/stock-adjustments/export",
        $"{InventoryReports}/stock-transfers-report/export"
    ];

    /// <summary>Exports that carry a cost column for a caller who may see cost.</summary>
    private static readonly string[] ExportsWithCostColumns =
        ["current-stock", "product-ledger", "stock-valuation", "outlet-wise-stock", "out-of-stock", "negative-stock", "stock-transfers-report"];

    // stock_transfers.view_all_locations only widens which transfers are listed; it carries no cost.
    private HttpClient ReportsClient(string permissions) =>
        _fixture.Client(UomPostgresFixture.OwnerId, "InventoryAnalyst", permissions + ",stock_transfers.view_all_locations");

    /// <summary>A legacy pcs variant and a measured ml variant with receipts, a sale, an adjustment, a transfer, plus a negative and a zero row.</summary>
    private async Task<(long Pcs, long Ml)> CostVisibilityDataAsync()
    {
        var owner = Owner();
        var pcs = await _fixture.CreateVariantAsync("CVP");
        // The product ledger values movements at the product's cost price.
        await _fixture.WithDbAsync(async db =>
        {
            var product = await db.Products.SingleAsync(row => row.Id == pcs);
            product.CostPrice = CostVisibilityPcsCost;
            await db.SaveChangesAsync();
        });
        await ReceivePcsAsync(owner, pcs, 10m, CostVisibilityPcsCost);

        var (ml, config) = await StockedAttarAsync(owner, "CVM");
        await SellAsync(owner, new SaleLine(ml, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        await Send(owner, HttpMethod.Post, "/api/stock-adjustments/batch", new
        {
            action = "SubmitAndApprove",
            locationId = UomPostgresFixture.Outlet,
            locationType = "outlet",
            reason = "Damaged",
            items = new[] { new { variantId = ml, enteredQuantity = -0.5m, unitOptionId = (long?)null, quantityChange = 0m } }
        });

        var transfer = await Send(owner, HttpMethod.Post, "/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = UomPostgresFixture.Outlet,
            fromLocationType = "outlet",
            toLocationId = UomPostgresFixture.OutletTwo,
            toLocationType = "outlet",
            transferDate = DateTime.UtcNow,
            items = new[] { new { variantId = pcs, quantity = 2m } }
        });
        await Send(owner, HttpMethod.Post, $"/api/stock-transfers/{transfer.GetProperty("id").GetInt64()}/submit-and-dispatch", new { });

        var negative = await _fixture.CreateVariantAsync("CVN");
        var empty = await _fixture.CreateVariantAsync("CVO");
        await _fixture.WithDbAsync(async db =>
        {
            db.Inventories.AddRange(
                new RetailPOS.Core.Entities.Inventory { VariantId = negative, LocationId = UomPostgresFixture.Outlet, LocationType = "outlet", Quantity = -2 },
                new RetailPOS.Core.Entities.Inventory { VariantId = empty, LocationId = UomPostgresFixture.Outlet, LocationType = "outlet", Quantity = 0 });
            await db.SaveChangesAsync();
        });
        return (pcs, ml);
    }

    [Fact]
    public async Task CostVisibility_ReportsInventoryAlone_ReturnsEveryReportWithCostAndValueNull()
    {
        var (pcs, ml) = await CostVisibilityDataAsync();
        var reportsOnly = ReportsClient("reports.inventory");
        var withCost = ReportsClient("reports.inventory,products.view_cost");

        foreach (var path in CostReportPaths(pcs))
        {
            var redacted = await ReportJsonAsync(reportsOnly, path);
            var redactedFields = CostFields(redacted);
            Assert.True(redactedFields.Count > 0, $"{path} returned no cost field to check");
            Assert.All(redactedFields, field => Assert.True(
                field.Value.ValueKind == JsonValueKind.Null, $"{path}: {field.Name} = {field.Value}"));

            var visible = CostFields(await ReportJsonAsync(withCost, path));
            Assert.All(visible, field => Assert.True(
                field.Value.ValueKind == JsonValueKind.Number, $"{path} with view_cost: {field.Name} = {field.Value}"));
        }

        foreach (var path in QuantityOnlyReportPaths)
            Assert.Empty(CostFields(await ReportJsonAsync(reportsOnly, path)));

        // Quantities and units are the same for both callers and match the stock, for pcs and for ml.
        var pcsOnHand = await OutletQuantityAsync(pcs);
        var mlOnHand = await OutletQuantityAsync(ml);
        Assert.Equal(487.5m, mlOnHand);
        foreach (var client in new[] { reportsOnly, withCost })
        {
            var current = await ReportJsonAsync(client, $"{InventoryReports}/current-stock?outletId={UomPostgresFixture.Outlet}&pageSize=500");
            Assert.Equal(pcsOnHand, Row(current, "variantId", pcs).GetProperty("availableQuantity").GetDecimal());
            Assert.Equal(mlOnHand, Row(current, "variantId", ml).GetProperty("availableQuantity").GetDecimal());

            var valuation = await ReportJsonAsync(client, $"{InventoryReports}/stock-valuation?outletId={UomPostgresFixture.Outlet}&pageSize=500");
            Assert.Equal(pcsOnHand, Row(valuation, "variantId", pcs).GetProperty("quantity").GetDecimal());
            Assert.Equal(mlOnHand, Row(valuation, "variantId", ml).GetProperty("quantity").GetDecimal());
            Assert.Contains(valuation.GetProperty("summary").GetProperty("byCategory").EnumerateArray(),
                category => category.GetProperty("categoryName").GetString() == "Attar"
                    && category.GetProperty("quantity").GetDecimal() > 0);

            var ledger = await ReportJsonAsync(client, $"{InventoryReports}/product-ledger?productId={pcs}&pageSize=500");
            Assert.Equal(10m, ledger.GetProperty("summary").GetProperty("totalStockIn").GetDecimal());
            Assert.Equal(2m, ledger.GetProperty("summary").GetProperty("totalStockOut").GetDecimal());
            Assert.All(ledger.GetProperty("items").EnumerateArray(), row =>
            {
                Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("referenceNumber").GetString()));
                Assert.NotEqual(default, row.GetProperty("transactionDate").GetDateTime());
            });

            var transfers = await ReportJsonAsync(client, $"{InventoryReports}/stock-transfers-report?pageSize=500");
            Assert.Equal(2m, Row(transfers, "productCode", $"CVP-{pcs}").GetProperty("quantity").GetDecimal());
        }

        var adjustments = await ReportJsonAsync(reportsOnly, $"{InventoryReports}/stock-adjustments?pageSize=500");
        Assert.Contains(adjustments.GetProperty("items").EnumerateArray(),
            row => row.GetProperty("productCode").GetString() == $"CVM-{ml}" && row.GetProperty("quantityChange").GetDecimal() == -0.5m);
    }

    [Fact]
    public async Task CostVisibility_Exports_LeaveCostColumnsOutWithoutViewCost_AndKeepThemWithIt()
    {
        var (pcs, _) = await CostVisibilityDataAsync();
        var reportsOnly = ReportsClient("reports.inventory");
        var withCost = ReportsClient("reports.inventory,products.view_cost");
        var owner = Owner();

        foreach (var path in ExportPaths(pcs))
        {
            var report = path[(InventoryReports.Length + 1)..].Split('/')[0];
            var hasCostColumns = ExportsWithCostColumns.Contains(report);

            var csv = await ExportTextAsync(reportsOnly, path, "csv");
            AssertNoCostText(csv, path);
            var excel = await ExportCellsAsync(reportsOnly, path);
            Assert.NotEmpty(excel.Texts);
            Assert.All(excel.Texts, text => AssertNoCostText(text, $"{path} excel"));
            Assert.DoesNotContain(excel.Numbers, number => number == CostVisibilityPcsCost || number == CostVisibilityPcsCost * 10m);

            if (!hasCostColumns)
                continue;
            foreach (var client in new[] { withCost, owner })
            {
                var fullCsv = await ExportTextAsync(client, path, "csv");
                Assert.True(fullCsv.Contains("Cost", StringComparison.Ordinal), $"{path}: no cost column for an authorized user");
                var fullExcel = await ExportCellsAsync(client, path);
                Assert.Contains(fullExcel.Texts, text => text.Contains("Cost", StringComparison.Ordinal));
            }
        }

        // The pcs variant's own cost appears for the authorized user only.
        var ledgerPath = $"{InventoryReports}/product-ledger/export?productId={pcs}";
        Assert.Contains("41.37", await ExportTextAsync(withCost, ledgerPath, "csv"));
        Assert.Contains(CostVisibilityPcsCost, (await ExportCellsAsync(withCost, ledgerPath)).Numbers);
        Assert.DoesNotContain("41.37", await ExportTextAsync(reportsOnly, ledgerPath, "csv"));
    }

    [Fact]
    public async Task CostVisibility_BusinessOwnerWildcard_SeesCost_AndViewCostAloneIsForbidden()
    {
        var (pcs, _) = await CostVisibilityDataAsync();
        var owner = Owner();
        foreach (var path in CostReportPaths(pcs))
        {
            var fields = CostFields(await ReportJsonAsync(owner, path));
            Assert.True(fields.Count > 0, path);
            Assert.All(fields, field => Assert.True(field.Value.ValueKind == JsonValueKind.Number, $"{path}: {field.Name}"));
        }

        var ledger = await ReportJsonAsync(owner, $"{InventoryReports}/product-ledger?productId={pcs}&pageSize=500");
        var receipt = ledger.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("stockIn").GetDecimal() == 10m);
        Assert.Equal(CostVisibilityPcsCost, receipt.GetProperty("unitCost").GetDecimal());
        Assert.Equal(CostVisibilityPcsCost * 10m, receipt.GetProperty("transactionValue").GetDecimal());

        var costOnly = _fixture.Client(UomPostgresFixture.OwnerId, "InventoryAnalyst", "products.view_cost");
        foreach (var path in CostReportPaths(pcs).Concat(QuantityOnlyReportPaths).Concat(ExportPaths(pcs)))
        {
            using var response = await costOnly.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task CostVisibility_LocationAndBusinessScopeStillApply()
    {
        var (pcs, _) = await CostVisibilityDataAsync();
        var outlet = UomPostgresFixture.Outlet;

        // Assigned-only user with no assignment: another outlet is refused, with or without view_cost.
        var restricted = _fixture.Client(UomPostgresFixture.RestrictedUserId, "Manager", "reports.inventory,products.view_cost");
        foreach (var path in new[]
        {
            $"{InventoryReports}/current-stock?outletId={outlet}",
            $"{InventoryReports}/current-stock/export?outletId={outlet}&format=csv",
            $"{InventoryReports}/product-ledger?productId={pcs}&outletId={outlet}",
            $"{InventoryReports}/stock-valuation?outletId={outlet}",
            $"{InventoryReports}/stock-valuation/export?outletId={outlet}&format=excel",
            $"{InventoryReports}/outlet-wise-stock?outletId={outlet}",
            $"{InventoryReports}/negative-stock?outletId={outlet}"
        })
        {
            using var response = await restricted.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Another business sees nothing of this one.
        var foreign = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        using (var otherOutlet = await foreign.GetAsync($"{InventoryReports}/current-stock?outletId={outlet}"))
            Assert.Equal(HttpStatusCode.Forbidden, otherOutlet.StatusCode);
        using (var otherLedger = await foreign.GetAsync($"{InventoryReports}/product-ledger?productId={pcs}"))
            Assert.Equal(HttpStatusCode.NotFound, otherLedger.StatusCode);
        foreach (var path in new[]
        {
            $"{InventoryReports}/current-stock?pageSize=500",
            $"{InventoryReports}/stock-valuation?pageSize=500",
            $"{InventoryReports}/outlet-wise-stock?pageSize=500",
            $"{InventoryReports}/stock-levels",
            $"{InventoryReports}/valuation"
        })
        {
            using var response = await foreign.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode} {body}");
            Assert.DoesNotContain($"CVP-{pcs}", body);
            Assert.DoesNotContain("Attar", body);
        }
    }

    private async Task<decimal> OutletQuantityAsync(long variant)
    {
        decimal quantity = 0;
        await _fixture.WithDbAsync(async db => quantity = await db.Inventories.AsNoTracking()
            .Where(row => row.VariantId == variant && row.LocationId == UomPostgresFixture.Outlet && row.LocationType == "outlet")
            .Select(row => row.Quantity)
            .SingleAsync());
        return quantity;
    }

    private static JsonElement Row(JsonElement report, string property, long value) =>
        report.GetProperty("items").EnumerateArray().Single(row => row.GetProperty(property).GetInt64() == value);

    private static JsonElement Row(JsonElement report, string property, string value) =>
        report.GetProperty("items").EnumerateArray().First(row => row.GetProperty(property).GetString() == value);

    /// <summary>The report body: the <c>data</c> of an ApiResponse envelope, or the whole body for the unwrapped reports.</summary>
    private static async Task<JsonElement> ReportJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var isEnvelope = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("success", out _)
            && root.TryGetProperty("data", out _);
        return (isEnvelope ? root.GetProperty("data") : root).Clone();
    }

    private static List<(string Name, JsonElement Value)> CostFields(JsonElement element)
    {
        var found = new List<(string, JsonElement)>();
        void Walk(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        if (CostFieldNames.Contains(property.Name))
                            found.Add((property.Name, property.Value));
                        Walk(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray())
                        Walk(item);
                    break;
            }
        }
        Walk(element);
        return found;
    }

    private static async Task<string> ExportTextAsync(HttpClient client, string path, string format)
    {
        using var response = await client.GetAsync($"{path}{(path.Contains('?') ? '&' : '?')}format={format}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} {format}: {response.StatusCode} {body}");
        return body;
    }

    private static async Task<(List<string> Texts, List<decimal> Numbers)> ExportCellsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"{path}{(path.Contains('?') ? '&' : '?')}format=excel");
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} excel: {response.StatusCode}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        await using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XLWorkbook(stream);
        var texts = new List<string>();
        var numbers = new List<decimal>();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var cell in sheet.CellsUsed())
            {
                if (cell.Value.IsNumber)
                    numbers.Add((decimal)cell.Value.GetNumber());
                else if (cell.Value.IsText)
                    texts.Add(cell.Value.GetText());
            }
        }
        return (texts, numbers);
    }

    /// <summary>No cost or value header, summary line or cost figure of the pcs variant.</summary>
    private static void AssertNoCostText(string text, string context)
    {
        foreach (var fragment in new[] { "Unit Cost", "Value", "Est. Cost", "% of Total", "41.37", "413.70" })
            Assert.False(text.Contains(fragment, StringComparison.Ordinal), $"{context} contains \"{fragment}\"");
    }
}
