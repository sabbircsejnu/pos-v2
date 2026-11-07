using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class InventoryReportReadHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public InventoryReportReadHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProductLedgerReadStore_RejectsExplicitLocationOutsideTrustedScope()
    {
        await SeedAsync();
        await SeedProductLedgerAsync();

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IProductLedgerReportReadStore>();
        var report = await store.GetAsync(
            new InventoryReportReadScope(4101, [5101], []),
            new ProductLedgerReportFilter(7101, null, 5201, null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Empty(report.Items);
        Assert.Equal(0, report.Summary.OpeningStock);
        Assert.Equal(0, report.Summary.TotalStockIn);
        Assert.Equal(0, report.Summary.TotalStockOut);
    }

    [Fact]
    public async Task StockTransactionReport_PreservesScopedBalancesAndReferenceContracts()
    {
        await SeedAsync();
        await SeedCurrentStockHistoryAsync();
        await SeedProductLedgerAsync();

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/stock-transactions?productId=7101&outletId=5101&locationType=outlet&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-18T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("data");
        Assert.Equal(7101, report.GetProperty("productId").GetInt64());
        Assert.Equal("Report product", report.GetProperty("productName").GetString());
        Assert.Equal("product", report.GetProperty("level").GetString());
        Assert.Equal(5101, report.GetProperty("outletId").GetInt64());
        Assert.Equal("outlet", report.GetProperty("locationType").GetString());
        var summary = report.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("openingStock").GetInt32());
        Assert.Equal(2, summary.GetProperty("stockIn").GetInt32());
        Assert.Equal(1, summary.GetProperty("stockOut").GetInt32());
        Assert.Equal(6, summary.GetProperty("closingStock").GetInt32());
        Assert.Equal(2, summary.GetProperty("currentStock").GetInt32());

        var rows = report.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(6003, rows[0].GetProperty("id").GetInt64());
        Assert.Equal("S-REPORT-1", rows[0].GetProperty("referenceNo").GetString());
        Assert.Equal(4, rows[0].GetProperty("runningBalance").GetInt32());
        Assert.Equal(6005, rows[1].GetProperty("id").GetInt64());
        Assert.Equal("ADJ-6101", rows[1].GetProperty("referenceNo").GetString());
        Assert.Equal(6, rows[1].GetProperty("runningBalance").GetInt32());
    }

    [Fact]
    public async Task StockTransactionReadStore_RejectsExplicitLocationOutsideTrustedScope()
    {
        await SeedAsync();
        await SeedCurrentStockHistoryAsync();
        await SeedProductLedgerAsync();

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStockTransactionReportReadStore>();
        var report = await store.GetAsync(
            new InventoryReportReadScope(4101, [5101], [5301]),
            new StockTransactionReportFilter(7101, null, 5201, "outlet", null, null),
            CancellationToken.None);

        Assert.Empty(report.Rows);
        Assert.Equal(0, report.Summary.OpeningStock);
        Assert.Equal(0, report.Summary.StockIn);
        Assert.Equal(0, report.Summary.StockOut);
        Assert.Equal(0, report.Summary.CurrentStock);
    }

    [Fact]
    public async Task StockMovementReport_PreservesPaginationSummarySortingAndExportContracts()
    {
        await SeedAsync();
        await SeedCurrentStockHistoryAsync();
        await SeedProductLedgerAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockLedgers.AddRange(
                new StockLedger
                {
                    Id = 6006,
                    VariantId = 8102,
                    LocationId = 5101,
                    LocationType = "outlet",
                    TransactionType = StockLedgerTransactionType.Grn,
                    QtyIn = 4,
                    BalanceAfter = 4,
                    ReferenceType = StockLedgerReferenceType.Grn,
                    ReferenceId = 5601,
                    CreatedAt = new DateTime(2026, 9, 18, 23, 59, 59, DateTimeKind.Utc)
                },
                new StockLedger
                {
                    Id = 6007,
                    VariantId = 8102,
                    LocationId = 5101,
                    LocationType = "outlet",
                    TransactionType = StockLedgerTransactionType.Sale,
                    QtyOut = 1,
                    BalanceAfter = 3,
                    ReferenceType = StockLedgerReferenceType.Sale,
                    ReferenceId = 5801,
                    CreatedAt = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc)
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var reportResponse = await client.GetAsync(
            "/api/reports/inventory/stock-movement?outletId=5101&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-18T00%3A00%3A00Z&sortBy=sku&sortDir=asc&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        using var document = JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("data");
        Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("totalOpeningStock").GetInt32());
        Assert.Equal(6, summary.GetProperty("totalStockIn").GetInt32());
        Assert.Equal(1, summary.GetProperty("totalStockOut").GetInt32());
        Assert.Equal(10, summary.GetProperty("totalClosingStock").GetInt32());
        Assert.Equal(5, summary.GetProperty("netMovement").GetInt32());
        var row = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal("FOREIGN-1", row.GetProperty("sku").GetString());
        Assert.Equal(0, row.GetProperty("openingStock").GetInt32());
        Assert.Equal(4, row.GetProperty("stockIn").GetInt32());
        Assert.Equal(4, row.GetProperty("closingStock").GetInt32());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/stock-movement/export?outletId=5101&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-18T00%3A00%3A00Z&sortBy=sku&sortDir=asc&format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Stock Movement Report",
            csv,
            StringComparison.Ordinal);
        Assert.Contains("Product Code,SKU,Product Name,Category,Location,Location Type,Opening Stock,Stock In,Stock Out,Closing Stock,Net Movement", csv, StringComparison.Ordinal);
        Assert.Contains("TOTAL,,2 rows,,,,5,6,1,10,5", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/stock-movement/export?outletId=5101&format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/stock-movement?outletId=5101&categoryId=6101&search=report-1&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-18T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filteredReport = filteredDocument.RootElement.GetProperty("data");
        Assert.Equal(1, filteredReport.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "REPORT-1",
            Assert.Single(filteredReport.GetProperty("items").EnumerateArray())
                .GetProperty("sku").GetString());
    }

    [Fact]
    public async Task StockMovementReadStore_RejectsForeignOutletAndScopesUnfilteredRows()
    {
        await SeedAsync();
        await SeedCurrentStockHistoryAsync();
        await SeedProductLedgerAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockLedgers.Add(new StockLedger
            {
                Id = 6008,
                VariantId = 8101,
                LocationId = 5301,
                LocationType = "warehouse",
                TransactionType = StockLedgerTransactionType.Grn,
                QtyIn = 2,
                BalanceAfter = 2,
                ReferenceType = StockLedgerReferenceType.Grn,
                ReferenceId = 5601,
                CreatedAt = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStockMovementReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], [5301]);
        var foreignFilter = new StockMovementReportFilter(
            5201,
            null,
            null,
            null,
            null,
            null,
            "productName",
            "asc",
            1,
            50);
        var foreignReport = await store.GetAsync(trustedScope, foreignFilter, CancellationToken.None);
        Assert.Empty(foreignReport.Items);

        var scopedReport = await store.GetAsync(
            trustedScope,
            foreignFilter with { OutletId = null },
            CancellationToken.None);
        Assert.DoesNotContain(scopedReport.Items, row => row.LocationId == 5201);

        var warehouseReport = await store.GetAsync(
            trustedScope,
            foreignFilter with { OutletId = null, WarehouseId = 5301 },
            CancellationToken.None);
        Assert.All(warehouseReport.Items, row =>
        {
            Assert.Equal(5301, row.LocationId);
            Assert.Equal("warehouse", row.LocationType);
        });
        Assert.Single(warehouseReport.Items);
    }

    [Fact]
    public async Task StockValuationReport_PreservesProjectionAndScopesBusinessData()
    {
        await SeedAsync();

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/stock-valuation?page=1&pageSize=1&sortBy=inventoryValue&sortDir=desc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("data");
        Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalProducts").GetInt32());
        Assert.Equal(3, summary.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(42m, summary.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(2, summary.GetProperty("byCategory").GetArrayLength());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, topRow.GetProperty("locationId").GetInt64());
        Assert.Equal(22m, topRow.GetProperty("inventoryValue").GetDecimal());
        Assert.Equal(52.38m, topRow.GetProperty("percentOfTotal").GetDecimal());

        using var csvResponse = await client.GetAsync("/api/reports/inventory/stock-valuation/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains("Total Inventory Value:,42.00", csv, StringComparison.Ordinal);
        Assert.Contains("TOTAL,,,2 rows,,,,3,,42.00,100%", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/stock-valuation/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/stock-valuation?outletId=5101&categoryId=6101&search=report-1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, filtered.GetProperty("summary").GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(
            "REPORT-1",
            Assert.Single(filtered.GetProperty("items").EnumerateArray())
                .GetProperty("sku").GetString());
    }

    [Fact]
    public async Task StockValuationReadStore_RejectsForeignLocationAndScopesOutletAndWarehouseRows()
    {
        await SeedAsync();
        await SeedCurrentStockHistoryAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8101,
                LocationId = 5301,
                LocationType = "warehouse",
                Quantity = 3
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStockValuationReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], [5301]);
        var filter = new StockValuationReportFilter(
            null,
            null,
            null,
            null,
            "inventoryValue",
            "desc",
            1,
            50);
        var report = await store.GetAsync(trustedScope, filter, CancellationToken.None);
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(6, report.Summary.TotalQuantity);
        Assert.Equal(75m, report.Summary.TotalInventoryValue);
        Assert.DoesNotContain(report.Items, row => row.LocationId == 5201);
        Assert.Contains(report.Items, row => row.LocationId == 5301 && row.LocationType == "warehouse");

        var foreignReport = await store.GetAsync(
            trustedScope,
            filter with { OutletId = 5201 },
            CancellationToken.None);
        Assert.Empty(foreignReport.Items);
    }

    [Fact]
    public async Task OutletWiseStockReport_PreservesAggregationAndExportContractsWithinAuthorizedScope()
    {
        await SeedAsync();

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/outlet-wise-stock?page=1&pageSize=1&sortBy=outletName&sortDir=asc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement.GetProperty("data");
        Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("totalOutlets").GetInt32());
        Assert.Equal(2, summary.GetProperty("totalSkus").GetInt32());
        Assert.Equal(3, summary.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(42m, summary.GetProperty("totalStockValue").GetDecimal());
        Assert.Equal(1, summary.GetProperty("byOutlet").GetArrayLength());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal("Report outlet", topRow.GetProperty("outletName").GetString());
        Assert.Equal(5101, topRow.GetProperty("outletId").GetInt64());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/outlet-wise-stock/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains("Total Outlets:,1", csv, StringComparison.Ordinal);
        Assert.Contains("Total Stock Value:,42.00", csv, StringComparison.Ordinal);
        Assert.Contains("TOTAL,,,,2 rows,,3,,42.00", csv, StringComparison.Ordinal);
        Assert.Contains("Report outlet,2,3,42.00", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/outlet-wise-stock/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/outlet-wise-stock?outletId=5101&categoryId=6101&search=REPORT-1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, filtered.GetProperty("summary").GetProperty("totalStockValue").GetDecimal());
        Assert.Equal(
            "REPORT-1",
            Assert.Single(filtered.GetProperty("items").EnumerateArray())
                .GetProperty("sku").GetString());
    }

    [Fact]
    public async Task OutletWiseStockReadStore_RestrictsUnfilteredAndExplicitOutletsToTrustedScope()
    {
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutletWiseStockReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], []);
        var allTrustedRows = await store.GetAsync(
                trustedScope,
                new OutletWiseStockFilter(null, null, null, "outletName", "asc", 1, 0),
                CancellationToken.None);
        Assert.Equal(2, allTrustedRows.TotalCount);
        Assert.All(allTrustedRows.Items, row => Assert.Equal(5101, row.OutletId));

        var foreignOutlet = await store.GetAsync(
                trustedScope,
                new OutletWiseStockFilter(5201, null, null, "outletName", "asc", 1, 50),
                CancellationToken.None);
        Assert.Empty(foreignOutlet.Items);
        Assert.Equal(0, foreignOutlet.Summary.TotalStockValue);
    }

    [Fact]
    public async Task LowStockReport_PreservesThresholdSummaryAndExportContractsWithinAuthorizedScope()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = 1,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/low-stock?page=1&pageSize=1&sortBy=urgencyLevel&sortDir=desc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement;
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("totalSkus").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalDeficitQty").GetInt32());
        Assert.Equal(0, summary.GetProperty("criticalCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("lowCount").GetInt32());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, topRow.GetProperty("locationId").GetInt64());
        Assert.Equal("Below Reorder", topRow.GetProperty("urgencyLevel").GetString());
        Assert.Equal(0, topRow.GetProperty("suggestedOrderQty").GetInt32());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/low-stock/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Current Stock,Reorder Level,Suggested Order Qty,Urgency",
            csv,
            StringComparison.Ordinal);
        Assert.Contains("\"REPORT-1\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FOREIGN-1\"", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/low-stock/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/low-stock?outletId=5101&categoryId=6101&search=REPORT-1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement;
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        var filteredRow = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, filteredRow.GetProperty("locationId").GetInt64());
        Assert.Equal("Below Reorder", filteredRow.GetProperty("urgencyLevel").GetString());
        Assert.Equal(0, filteredRow.GetProperty("suggestedOrderQty").GetInt32());
    }

    [Fact]
    public async Task LowStockReadStore_RestrictsUnfilteredAndExplicitLocationsToTrustedScope()
    {
        await SeedAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = 1,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILowStockReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], []);
        var allTrustedRows = await store.GetAsync(
            trustedScope,
            new LowStockReportFilter(null, null, null, null, "urgencyLevel", "desc", 1, 0),
            CancellationToken.None);
        Assert.Single(allTrustedRows.Items);
        Assert.All(allTrustedRows.Items, row => Assert.Equal(5101, row.LocationId));

        var foreignOutlet = await store.GetAsync(
            trustedScope,
            new LowStockReportFilter(5201, null, null, null, "urgencyLevel", "desc", 1, 50),
            CancellationToken.None);
        Assert.Empty(foreignOutlet.Items);
        Assert.Equal(0, foreignOutlet.Summary.TotalSkus);
    }

    [Fact]
    public async Task OutOfStockReport_PreservesZeroQuantitySummaryAndExportContractsWithinAuthorizedScope()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var authorizedInventory = await db.Inventories.SingleAsync(inventory => inventory.Id == 9103);
            authorizedInventory.Quantity = 0;
            authorizedInventory.LowStockThreshold = 2;
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = 0,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/out-of-stock?page=1&pageSize=1&sortBy=location&sortDir=asc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement;
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("totalSkus").GetInt32());
        Assert.Equal(1, summary.GetProperty("totalLocations").GetInt32());
        Assert.Equal(40m, summary.GetProperty("estimatedCostImpact").GetDecimal());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, topRow.GetProperty("locationId").GetInt64());
        Assert.Equal(2, topRow.GetProperty("reorderLevel").GetInt32());
        Assert.Equal(20m, topRow.GetProperty("unitCost").GetDecimal());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/out-of-stock/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Reorder Level,Unit Cost",
            csv,
            StringComparison.Ordinal);
        Assert.Equal(2, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("\"FOREIGN-1\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign outlet", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/out-of-stock/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/out-of-stock?outletId=5101&categoryId=6102&search=FOREIGN-1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement;
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            40m,
            filtered.GetProperty("summary").GetProperty("estimatedCostImpact").GetDecimal());
        var filteredRow = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, filteredRow.GetProperty("locationId").GetInt64());
    }

    [Fact]
    public async Task OutOfStockReadStore_RestrictsUnfilteredAndExplicitLocationsToTrustedScope()
    {
        await SeedAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var authorizedInventory = await db.Inventories.SingleAsync(inventory => inventory.Id == 9103);
            authorizedInventory.Quantity = 0;
            authorizedInventory.LowStockThreshold = 2;
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = 0,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutOfStockReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], []);
        var allTrustedRows = await store.GetAsync(
            trustedScope,
            new OutOfStockReportFilter(null, null, null, null, "location", "asc", 1, 0),
            CancellationToken.None);
        Assert.Single(allTrustedRows.Items);
        Assert.All(allTrustedRows.Items, row => Assert.Equal(5101, row.LocationId));

        var foreignOutlet = await store.GetAsync(
            trustedScope,
            new OutOfStockReportFilter(5201, null, null, null, "location", "asc", 1, 50),
            CancellationToken.None);
        Assert.Empty(foreignOutlet.Items);
        Assert.Equal(0m, foreignOutlet.Summary.EstimatedCostImpact);
    }

    [Fact]
    public async Task NegativeStockReport_PreservesNegativeQuantityAndExportContractsWithinAuthorizedScope()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var authorizedInventory = await db.Inventories.SingleAsync(inventory => inventory.Id == 9103);
            authorizedInventory.Quantity = -2;
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = -3,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/negative-stock?page=1&pageSize=1&sortBy=currentStock&sortDir=asc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement;
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("totalSkus").GetInt32());
        Assert.Equal(-2, summary.GetProperty("totalNegativeQty").GetInt32());
        Assert.Equal(-40m, summary.GetProperty("totalNegativeValue").GetDecimal());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, topRow.GetProperty("locationId").GetInt64());
        Assert.Equal(-2, topRow.GetProperty("currentStock").GetInt32());
        Assert.Equal(20m, topRow.GetProperty("unitCost").GetDecimal());
        Assert.Equal(-40m, topRow.GetProperty("stockValue").GetDecimal());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/negative-stock/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Current Stock,Unit Cost,Stock Value",
            csv,
            StringComparison.Ordinal);
        Assert.Equal(2, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("Foreign outlet", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/negative-stock/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/negative-stock?outletId=5101&categoryId=6102&search=FOREIGN-1");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement;
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            -40m,
            filtered.GetProperty("summary").GetProperty("totalNegativeValue").GetDecimal());
        var filteredRow = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(5101, filteredRow.GetProperty("locationId").GetInt64());
        Assert.Equal(-2, filteredRow.GetProperty("currentStock").GetInt32());
    }

    [Fact]
    public async Task NegativeStockReadStore_RestrictsUnfilteredAndExplicitLocationsToTrustedScope()
    {
        await SeedAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var authorizedInventory = await db.Inventories.SingleAsync(inventory => inventory.Id == 9103);
            authorizedInventory.Quantity = -2;
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 9104,
                VariantId = 8102,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = -3,
                LowStockThreshold = 2
            });
            await db.SaveChangesAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INegativeStockReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], []);
        var allTrustedRows = await store.GetAsync(
            trustedScope,
            new NegativeStockReportFilter(null, null, null, null, "currentStock", "asc", 1, 0),
            CancellationToken.None);
        Assert.Single(allTrustedRows.Items);
        Assert.All(allTrustedRows.Items, row => Assert.Equal(5101, row.LocationId));

        var foreignOutlet = await store.GetAsync(
            trustedScope,
            new NegativeStockReportFilter(5201, null, null, null, "currentStock", "asc", 1, 50),
            CancellationToken.None);
        Assert.Empty(foreignOutlet.Items);
        Assert.Equal(0m, foreignOutlet.Summary.TotalNegativeValue);
    }

    [Fact]
    public async Task StockAdjustmentReport_PreservesLineExpansionAndExportContractsWithinTrustedScope()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockAdjustments.AddRange(
                new StockAdjustment
                {
                    Id = 6201,
                    AdjustmentNumber = "ADJ-RPT-1",
                    LocationId = 5101,
                    LocationType = "outlet",
                    AdjustedBy = 4102,
                    AdjustmentDate = new DateTime(2026, 9, 16, 23, 59, 59, DateTimeKind.Utc),
                    Lines =
                    [
                        new StockAdjustmentLine
                        {
                            Id = 9201,
                            VariantId = 8101,
                            QuantityChange = 3,
                            Reason = "Cycle count"
                        },
                        new StockAdjustmentLine
                        {
                            Id = 9202,
                            VariantId = 8102,
                            QuantityChange = -2,
                            Reason = "Damage"
                        }
                    ]
                },
                new StockAdjustment
                {
                    Id = 6202,
                    AdjustmentNumber = "ADJ-RPT-2",
                    LocationId = 5201,
                    LocationType = "outlet",
                    AdjustedBy = 4102,
                    AdjustmentDate = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc),
                    Lines =
                    [
                        new StockAdjustmentLine
                        {
                            Id = 9203,
                            VariantId = 8102,
                            QuantityChange = -7,
                            Reason = "Foreign loss"
                        }
                    ]
                },
                new StockAdjustment
                {
                    Id = 6203,
                    AdjustmentNumber = "ADJ-RPT-3",
                    LocationId = 5101,
                    LocationType = "outlet",
                    AdjustedBy = 4102,
                    AdjustmentDate = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc),
                    Lines =
                    [
                        new StockAdjustmentLine
                        {
                            Id = 9204,
                            VariantId = 8101,
                            QuantityChange = 9,
                            Reason = "Next day"
                        }
                    ]
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/inventory/stock-adjustments?dateFrom=2026-09-16T00%3A00%3A00Z&dateTo=2026-09-16T00%3A00%3A00Z&page=1&pageSize=1&sortBy=location&sortDir=asc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var report = document.RootElement;
        Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
        var summary = report.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalAdjustments").GetInt32());
        Assert.Equal(3, summary.GetProperty("totalAdditions").GetInt32());
        Assert.Equal(2, summary.GetProperty("totalReductions").GetInt32());
        Assert.Equal(1, summary.GetProperty("netQuantityChange").GetInt32());
        var topRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(6201, topRow.GetProperty("id").GetInt64());
        Assert.Equal(5101, topRow.GetProperty("locationId").GetInt64());
        Assert.Equal("Addition", topRow.GetProperty("adjustmentType").GetString());
        Assert.Equal("Cycle count", topRow.GetProperty("reason").GetString());
        Assert.Equal("Report owner", topRow.GetProperty("adjustedBy").GetString());

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/stock-adjustments/export?dateFrom=2026-09-16T00%3A00%3A00Z&dateTo=2026-09-16T00%3A00%3A00Z&format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Date,Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Type,Quantity Change,Reason,Adjusted By",
            csv,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign loss", csv, StringComparison.Ordinal);
        Assert.Contains("Cycle count", csv, StringComparison.Ordinal);
        Assert.Contains("Damage", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Next day", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/stock-adjustments/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/stock-adjustments?outletId=5101&categoryId=6101&search=Cycle");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        var filtered = filteredDocument.RootElement;
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, filtered.GetProperty("summary").GetProperty("netQuantityChange").GetInt32());
        var filteredRows = filtered.GetProperty("items").EnumerateArray().ToArray();
        var filteredRow = Assert.Single(
            filteredRows,
            row => row.GetProperty("reason").GetString() == "Cycle count");
        Assert.Equal(6201, filteredRow.GetProperty("id").GetInt64());
        Assert.Equal(3, filteredRow.GetProperty("quantityChange").GetInt32());
        Assert.Equal("Addition", filteredRow.GetProperty("adjustmentType").GetString());
        Assert.Contains(filteredRows, row => row.GetProperty("reason").GetString() == "Damage");
    }

    [Fact]
    public async Task StockAdjustmentReadStore_EnforcesTrustedScopeForUnfilteredAndExplicitLocations()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockAdjustments.AddRange(
                new StockAdjustment
                {
                    Id = 6301,
                    AdjustmentNumber = "ADJ-SCOPE-1",
                    LocationId = 5101,
                    LocationType = "outlet",
                    AdjustedBy = 4102,
                    AdjustmentDate = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc),
                    Lines =
                    [
                        new StockAdjustmentLine
                        {
                            Id = 9301,
                            VariantId = 8101,
                            QuantityChange = 4,
                            Reason = "Trusted adjustment"
                        }
                    ]
                },
                new StockAdjustment
                {
                    Id = 6302,
                    AdjustmentNumber = "ADJ-SCOPE-2",
                    LocationId = 5201,
                    LocationType = "outlet",
                    AdjustedBy = 4102,
                    AdjustmentDate = new DateTime(2026, 9, 16, 13, 0, 0, DateTimeKind.Utc),
                    Lines =
                    [
                        new StockAdjustmentLine
                        {
                            Id = 9302,
                            VariantId = 8102,
                            QuantityChange = -6,
                            Reason = "Foreign adjustment"
                        }
                    ]
                });
            await db.SaveChangesAsync();
        }

        using var serviceScope = _factory.Services.CreateScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<IStockAdjustmentReportReadStore>();
        var trustedScope = new InventoryReportReadScope(4101, [5101], []);
        var unfiltered = await store.GetAsync(
            trustedScope,
            new StockAdjustmentReportFilter(null, null, null, null, null, null, "adjustmentDate", "asc", 1, 50),
            CancellationToken.None);
        Assert.Single(unfiltered.Items);
        Assert.Equal(6301, unfiltered.Items[0].Id);
        Assert.Equal(5101, unfiltered.Items[0].LocationId);

        var explicitForeign = await store.GetAsync(
            trustedScope,
            new StockAdjustmentReportFilter(5201, null, null, null, null, null, "adjustmentDate", "asc", 1, 50),
            CancellationToken.None);
        Assert.Empty(explicitForeign.Items);
        Assert.Equal(0, explicitForeign.Summary.TotalAdjustments);
    }

    [Fact]
    public async Task StockLevelValuationAndSlowMoving_PreserveProjectionAndFilterContracts()
    {
        await SeedAsync();

        using var client = CreateBusinessOwnerClient();
        using var stockResponse = await client.GetAsync(
            "/api/reports/inventory/stock-levels?locationId=5101&locationType=outlet&lowStockOnly=true");
        Assert.True(
            stockResponse.StatusCode == HttpStatusCode.OK,
            await stockResponse.Content.ReadAsStringAsync());
        using var stockDocument = JsonDocument.Parse(await stockResponse.Content.ReadAsStringAsync());
        var stock = Assert.Single(stockDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(8101, stock.GetProperty("variantId").GetInt64());
        Assert.Equal("Report product", stock.GetProperty("productName").GetString());
        Assert.Equal("REPORT-1", stock.GetProperty("sku").GetString());
        Assert.Equal("Report category", stock.GetProperty("categoryName").GetString());
        Assert.Equal(5101, stock.GetProperty("locationId").GetInt64());
        Assert.Equal("Report outlet", stock.GetProperty("locationName").GetString());
        Assert.Equal(2, stock.GetProperty("quantity").GetInt32());
        Assert.Equal(2, stock.GetProperty("reorderLevel").GetInt32());
        Assert.True(stock.GetProperty("isLowStock").GetBoolean());
        Assert.Equal(22m, stock.GetProperty("estimatedValue").GetDecimal());

        using var valuationResponse = await client.GetAsync("/api/reports/inventory/valuation");
        Assert.Equal(HttpStatusCode.OK, valuationResponse.StatusCode);
        using var valuationDocument = JsonDocument.Parse(await valuationResponse.Content.ReadAsStringAsync());
        var valuation = valuationDocument.RootElement.GetProperty("data");
        Assert.Equal(42m, valuation.GetProperty("totalValue").GetDecimal());
        Assert.Equal(3, valuation.GetProperty("totalItems").GetInt32());
        Assert.Equal(
            new[] { "Report category", "Foreign category" },
            valuation.GetProperty("byCategory").EnumerateArray()
                .Select(category => category.GetProperty("categoryName").GetString()));

        using var slowResponse = await client.GetAsync("/api/reports/inventory/slow-moving?days=30");
        Assert.True(slowResponse.StatusCode == HttpStatusCode.OK, await slowResponse.Content.ReadAsStringAsync());
        using var slowDocument = JsonDocument.Parse(await slowResponse.Content.ReadAsStringAsync());
        var slowItems = slowDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, slowItems.Length);
        var slowVariant = Assert.Single(slowItems, item => item.GetProperty("variantId").GetInt64() == 8101);
        Assert.Equal(2, slowVariant.GetProperty("currentStock").GetInt32());
        Assert.Equal(int.MaxValue, slowVariant.GetProperty("daysSinceLastSale").GetInt32());
        Assert.Equal(22m, slowVariant.GetProperty("estimatedValue").GetDecimal());

        using var staffClient = CreateBusinessOwnerClient("Staff");
        using var staffValuationResponse = await staffClient.GetAsync("/api/reports/inventory/valuation");
        Assert.Equal(HttpStatusCode.OK, staffValuationResponse.StatusCode);

        await SeedCurrentStockHistoryAsync();
        using var currentStockResponse = await client.GetAsync(
            "/api/reports/inventory/current-stock?page=1&pageSize=1&sortBy=stockValue&sortDir=desc");
        Assert.Equal(HttpStatusCode.OK, currentStockResponse.StatusCode);
        using var currentStockDocument = JsonDocument.Parse(await currentStockResponse.Content.ReadAsStringAsync());
        var currentStock = currentStockDocument.RootElement.GetProperty("data");
        Assert.Equal(2, currentStock.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, currentStock.GetProperty("summary").GetProperty("totalProducts").GetInt32());
        Assert.Equal(3, currentStock.GetProperty("summary").GetProperty("totalQuantity").GetInt32());
        Assert.Equal(42m, currentStock.GetProperty("summary").GetProperty("totalStockValue").GetDecimal());
        Assert.Equal(
            8101,
            Assert.Single(currentStock.GetProperty("items").EnumerateArray())
                .GetProperty("variantId").GetInt64());
        var currentStockRow = currentStock.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(5101, currentStockRow.GetProperty("locationId").GetInt64());
        Assert.Equal("Report product", currentStockRow.GetProperty("productName").GetString());
        Assert.Equal(2, currentStockRow.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(22m, currentStockRow.GetProperty("stockValue").GetDecimal());
        Assert.Equal("low-stock", currentStockRow.GetProperty("stockStatus").GetString());
        Assert.Equal(
            new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            currentStockRow.GetProperty("lastPurchaseDate").GetDateTime());
        Assert.Equal(
            new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            currentStockRow.GetProperty("lastSaleDate").GetDateTime());

        using var filteredStockResponse = await client.GetAsync(
            "/api/reports/inventory/current-stock?stockStatus=low-stock&search=REPORT-1");
        Assert.Equal(HttpStatusCode.OK, filteredStockResponse.StatusCode);
        using var filteredStockDocument = JsonDocument.Parse(
            await filteredStockResponse.Content.ReadAsStringAsync());
        var filteredStock = filteredStockDocument.RootElement.GetProperty("data");
        Assert.Equal(1, filteredStock.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            8101,
            Assert.Single(filteredStock.GetProperty("items").EnumerateArray())
                .GetProperty("variantId").GetInt64());

        using var unauthorizedOutletResponse = await client.GetAsync(
            "/api/reports/inventory/current-stock?outletId=5201");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedOutletResponse.StatusCode);

        using var csvResponse = await client.GetAsync("/api/reports/inventory/current-stock/export?format=csv");
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Product Code,Barcode,Product Name,Category,Location,Location Type,Available Qty,Reserved Qty,Reorder Level,Unit Cost,Stock Value,Last Purchase Date,Last Sale Date,Stock Status",
            csv,
            StringComparison.Ordinal);
        Assert.Contains("TOTAL,,2 products,,,,3,,,,42.00", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync("/api/reports/inventory/current-stock/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);

        await SeedProductLedgerAsync();
        using var ledgerResponse = await client.GetAsync(
            "/api/reports/inventory/product-ledger?productId=7101&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-19T00%3A00%3A00Z&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);
        using var ledgerDocument = JsonDocument.Parse(await ledgerResponse.Content.ReadAsStringAsync());
        var ledger = ledgerDocument.RootElement.GetProperty("data");
        Assert.Equal(2, ledger.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, ledger.GetProperty("summary").GetProperty("openingStock").GetInt32());
        Assert.Equal(2, ledger.GetProperty("summary").GetProperty("totalStockIn").GetInt32());
        Assert.Equal(1, ledger.GetProperty("summary").GetProperty("totalStockOut").GetInt32());
        Assert.Equal(6, ledger.GetProperty("summary").GetProperty("closingStock").GetInt32());
        Assert.Equal(33m, ledger.GetProperty("summary").GetProperty("totalTransactionValue").GetDecimal());
        var ledgerRow = Assert.Single(ledger.GetProperty("items").EnumerateArray());
        Assert.Equal("sale", ledgerRow.GetProperty("transactionType").GetString());
        Assert.Equal("S-REPORT-1", ledgerRow.GetProperty("referenceNumber").GetString());
        Assert.Equal(5, ledgerRow.GetProperty("openingQuantity").GetInt32());
        Assert.Equal(4, ledgerRow.GetProperty("closingQuantity").GetInt32());

        using var ledgerCsvResponse = await client.GetAsync(
            "/api/reports/inventory/product-ledger/export?productId=7101&dateFrom=2026-09-15T00%3A00%3A00Z&dateTo=2026-09-19T00%3A00%3A00Z&format=csv");
        Assert.Equal("text/csv", ledgerCsvResponse.Content.Headers.ContentType?.MediaType);
        var ledgerCsv = await ledgerCsvResponse.Content.ReadAsStringAsync();
        Assert.Contains("2 transactions", ledgerCsv, StringComparison.Ordinal);
        Assert.Contains("5,2,1,6,,33.00", ledgerCsv, StringComparison.Ordinal);

        using var ledgerExcelResponse = await client.GetAsync(
            "/api/reports/inventory/product-ledger/export?productId=7101&format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ledgerExcelResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, ledgerExcelResponse.StatusCode);
    }

    private HttpClient CreateBusinessOwnerClient(string role = "BusinessOwner")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "reports.inventory,products.view_cost");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "4101");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "4102");
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
            new Business { Id = 4101, Name = "Report business" },
            new Business { Id = 4201, Name = "Foreign business" });
        db.Categories.AddRange(
            new Category { Id = 6101, BusinessId = 4101, Name = "Report category", Description = "" },
            new Category { Id = 6102, BusinessId = 4201, Name = "Foreign category", Description = "" });
        db.Products.AddRange(
            new Product
            {
                Id = 7101,
                BusinessId = 4101,
                Name = "Report product",
                CategoryId = 6101,
                CostPrice = 10m
            },
            new Product
            {
                Id = 7102,
                BusinessId = 4201,
                Name = "Foreign product",
                CategoryId = 6102,
                CostPrice = 20m
            });
        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 8101,
                BusinessId = 4101,
                ProductId = 7101,
                Name = "Default",
                Sku = "REPORT-1",
                CostAdjustment = 1m
            },
            new ProductVariant
            {
                Id = 8102,
                BusinessId = 4201,
                ProductId = 7102,
                Name = "Default",
                Sku = "FOREIGN-1"
            });
        db.Outlets.AddRange(
            new Outlet { Id = 5101, BusinessId = 4101, Name = "Report outlet" },
            new Outlet { Id = 5201, BusinessId = 4201, Name = "Foreign outlet" });
        db.Users.Add(new User
        {
            Id = 4102,
            BusinessId = 4101,
            OutletId = 5101,
            Name = "Report owner",
            Email = "report-owner@example.test",
            PasswordHash = "test",
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9101,
                VariantId = 8101,
                LocationId = 5101,
                LocationType = "outlet",
                Quantity = 2,
                LowStockThreshold = 2
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9102,
                VariantId = 8101,
                LocationId = 5201,
                LocationType = "outlet",
                Quantity = 5,
                LowStockThreshold = 2
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9103,
                VariantId = 8102,
                LocationId = 5101,
                LocationType = "outlet",
                Quantity = 1,
                LowStockThreshold = 0
            });
        await db.SaveChangesAsync();
    }

    private async Task SeedCurrentStockHistoryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Warehouses.AddRange(
            new Warehouse { Id = 5301, BusinessId = 4101, Name = "Report warehouse" },
            new Warehouse { Id = 5302, BusinessId = 4201, Name = "Foreign warehouse" });
        db.PurchaseOrders.AddRange(
            new PurchaseOrder { Id = 5401, SupplierId = 1, WarehouseId = 5301, OrderDate = new DateTime(2026, 9, 1) },
            new PurchaseOrder { Id = 5402, SupplierId = 2, WarehouseId = 5302, OrderDate = new DateTime(2026, 10, 1) });
        db.PurchaseOrderItems.AddRange(
            new PurchaseOrderItem { Id = 5501, PoId = 5401, VariantId = 8101, Quantity = 1, UnitPrice = 10m },
            new PurchaseOrderItem { Id = 5502, PoId = 5402, VariantId = 8101, Quantity = 1, UnitPrice = 10m });
        db.Grns.AddRange(
            new Grn { Id = 5601, PoId = 5401, ReceivedDate = new DateTime(2026, 9, 15) },
            new Grn { Id = 5602, PoId = 5402, ReceivedDate = new DateTime(2026, 10, 2) });
        db.GrnItems.AddRange(
            new GrnItem { Id = 5701, GrnId = 5601, PoItemId = 5501, ReceivedQty = 1, UnitCost = 10m },
            new GrnItem { Id = 5702, GrnId = 5602, PoItemId = 5502, ReceivedQty = 1, UnitCost = 10m });
        db.Sales.AddRange(
            new Sale
            {
                Id = 5801,
                SaleNumber = "S-REPORT-1",
                OutletId = 5101,
                SaleDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
                PaymentMethod = "cash",
                CashierId = 4102
            },
            new Sale
            {
                Id = 5802,
                SaleNumber = "S-FOREIGN-1",
                OutletId = 5201,
                SaleDate = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc),
                PaymentMethod = "cash",
                CashierId = 4102
            });
        db.SaleItems.AddRange(
            new SaleItem { Id = 5901, SaleId = 5801, VariantId = 8101, Quantity = 1, UnitPrice = 11m, Subtotal = 11m },
            new SaleItem { Id = 5902, SaleId = 5802, VariantId = 8101, Quantity = 1, UnitPrice = 11m, Subtotal = 11m });
        await db.SaveChangesAsync();
    }

    private async Task SeedProductLedgerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.StockLedgers.AddRange(
            new StockLedger
            {
                Id = 6001, VariantId = 8101, LocationId = 5101, LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Grn, QtyIn = 5, BalanceAfter = 5,
                ReferenceType = StockLedgerReferenceType.Grn, ReferenceId = 5601,
                CreatedAt = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
            },
            new StockLedger
            {
                Id = 6002, VariantId = 8101, LocationId = 5201, LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Grn, QtyIn = 7, BalanceAfter = 7,
                ReferenceType = StockLedgerReferenceType.Grn, ReferenceId = 5602,
                CreatedAt = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc)
            },
            new StockLedger
            {
                Id = 6003, VariantId = 8101, LocationId = 5101, LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Sale, QtyOut = 1, BalanceAfter = 4,
                ReferenceType = StockLedgerReferenceType.Sale, ReferenceId = 5801,
                CreatedAt = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc)
            },
            new StockLedger
            {
                Id = 6004, VariantId = 8101, LocationId = 5201, LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Sale, QtyOut = 1, BalanceAfter = 6,
                ReferenceType = StockLedgerReferenceType.Sale, ReferenceId = 5802,
                CreatedAt = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc)
            },
            new StockLedger
            {
                Id = 6005, VariantId = 8101, LocationId = 5101, LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Adjustment, QtyIn = 2, BalanceAfter = 6,
                ReferenceType = StockLedgerReferenceType.StockAdjustment, ReferenceId = 6101,
                CreatedAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();
    }
}
