using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public class StockTransferVisibilityHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public StockTransferVisibilityHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UserWithoutViewAll_SeesOnlyTransfersInvolvingDefaultLocation()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(body?.Data);

        var ids = body.Data!.StockTransfers.Select(t => t.Id).OrderBy(x => x).ToList();
        Assert.Equal([501L, 502L], ids);
        Assert.Equal(2, body.Data.TotalCount);
    }

    [Fact]
    public async Task UserWithoutViewAll_CannotOpenUnrelatedTransferDetails()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view", "OutletManager");

        var response = await client.GetAsync("/api/stock-transfers/503");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("You are not authorized to view this stock transfer.", body?.Message);
    }

    [Fact]
    public async Task UserWithViewAll_CanSeeAllTransfersAndDetails()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");

        var searchResponse = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(searchBody?.Data);
        Assert.Equal(3, searchBody.Data!.TotalCount);

        var detailResponse = await client.GetAsync("/api/stock-transfers/503");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    }

    [Fact]
    public async Task ViewAllLocationPermission_StillRestrictsListAndSearchToEffectiveBusiness()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = 2, Name = "Other Visibility Business" });
            db.Outlets.AddRange(
                new Outlet { Id = 31, BusinessId = 2, Name = "Other Outlet 1", Address = "X" },
                new Outlet { Id = 32, BusinessId = 2, Name = "Other Outlet 2", Address = "Y" });
            db.StockTransfers.Add(new StockTransfer
            {
                Id = 504,
                TransferNo = "TRF-504-OTHER",
                TransferType = StockTransfer.TransferTypeDirect,
                FromLocationId = 31,
                FromLocationType = "outlet",
                ToLocationId = 32,
                ToLocationType = "outlet",
                TransferDate = DateTime.UtcNow,
                Status = StockTransfer.StatusSubmitted,
                CreatedAt = DateTime.UtcNow,
                Items =
                [
                    new StockTransferItem
                    {
                        Id = 604,
                        VariantId = 301,
                        Quantity = 1,
                        RequestedQuantity = 1,
                        TransferQuantity = 1,
                        UnitCost = 10m
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(
            1001,
            "stock_transfers.view,stock_transfers.view_all_locations",
            "OutletManager");

        var allResponse = await client.GetAsync("/api/stock-transfers");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allBody = await allResponse.Content.ReadFromJsonAsync<ApiResponse<List<StockTransferDto>>>();
        Assert.Equal([501L, 502L, 503L], allBody?.Data?.Select(transfer => transfer.Id).OrderBy(id => id));

        var searchResponse = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            pageNumber = 1,
            pageSize = 50
        });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.Equal(3, searchBody?.Data?.TotalCount);
        Assert.DoesNotContain(
            searchBody?.Data?.StockTransfers ?? [],
            transfer => transfer.Id == 504);

        var detailResponse = await client.GetAsync("/api/stock-transfers/504");
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
        Assert.Contains("Stock transfer with ID 504 not found", detailBody);

        var pdfResponse = await client.GetAsync("/api/documents/stock-transfer/504/pdf");
        var pdfBody = await pdfResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, pdfResponse.StatusCode);
        Assert.Contains("Stock transfer with ID 504 not found", pdfBody);
        Assert.DoesNotContain("Document type 'stock-transfer' is not implemented yet.", pdfBody);
    }

    [Fact]
    public async Task MissingDefaultLocation_ReturnsConfigurationError()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1002, "stock_transfers.view", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            pageNumber = 1,
            pageSize = 50
        });
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Your default inventory location is not configured.", body?.Message);
    }

    [Fact]
    public async Task ViewAllPermission_DoesNotGrantWorkflowActionPermissions()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");

        var response = await client.PostAsync("/api/stock-transfers/501/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReportVisibility_UsesSameRuleForCounts()
    {
        await ResetAndSeedAsync();

        using var scopedClient = CreateClient(1001, "stock_transfers.view,reports.inventory", "OutletManager");
        var scopedResponse = await scopedClient.GetAsync("/api/reports/inventory/stock-transfers-report?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, scopedResponse.StatusCode);
        var scopedBody = await scopedResponse.Content.ReadFromJsonAsync<StockTransferReportDto>();
        Assert.NotNull(scopedBody);
        Assert.Equal(2, scopedBody!.TotalCount);

        using var globalClient = CreateClient(1001, "stock_transfers.view,reports.inventory,stock_transfers.view_all_locations", "OutletManager");
        var globalResponse = await globalClient.GetAsync("/api/reports/inventory/stock-transfers-report?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, globalResponse.StatusCode);
        var globalBody = await globalResponse.Content.ReadFromJsonAsync<StockTransferReportDto>();
        Assert.NotNull(globalBody);
        Assert.Equal(3, globalBody!.TotalCount);
    }

    [Fact]
    public async Task StockTransferReport_CharacterizesLegacyRowsSummaryFiltersAndExports()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            1001,
            "stock_transfers.view,reports.inventory",
            "OutletManager");

        using var reportResponse = await client.GetAsync(
            "/api/reports/inventory/stock-transfers-report?sortBy=transferDate&sortDir=asc&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<StockTransferReportDto>();
        Assert.NotNull(report);
        Assert.Equal(2, report.TotalCount);
        Assert.Equal(2, report.Summary.TotalTransfers);
        Assert.Equal(2, report.Summary.TotalLines);
        Assert.Equal(5, report.Summary.TotalQuantity);
        Assert.Equal(350m, report.Summary.TotalValue);
        Assert.Equal(0, report.Summary.PendingCount);
        Assert.Equal(0, report.Summary.CompletedCount);
        var row = Assert.Single(report.Items);
        Assert.Equal(501, row.TransferId);
        Assert.Equal("Outlet 1", row.FromLocationName);
        Assert.Equal("outlet", row.FromLocationType);
        Assert.Equal("Outlet 2", row.ToLocationName);
        Assert.Equal("outlet", row.ToLocationType);
        Assert.Equal("submitted", row.Status);
        Assert.Equal("VIS-001", row.ProductCode);
        Assert.Equal("VIS-BC-001", row.Barcode);
        Assert.Equal("VIS-001-DEF", row.Sku);
        Assert.Equal("Visibility Product", row.ProductName);
        Assert.Equal("Visibility Category", row.CategoryName);
        Assert.Equal(3, row.Quantity);
        Assert.Equal(70m, row.UnitCost);
        Assert.Equal(210m, row.TransferValue);
        Assert.Equal(string.Empty, row.CreatedBy);

        using var filteredResponse = await client.GetAsync(
            "/api/reports/inventory/stock-transfers-report?status=submitted&categoryId=201&search=VIS-001");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filtered = await filteredResponse.Content.ReadFromJsonAsync<StockTransferReportDto>();
        Assert.NotNull(filtered);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(1, filtered.Summary.TotalTransfers);
        Assert.Equal(3, filtered.Summary.TotalQuantity);
        Assert.Equal(210m, filtered.Summary.TotalValue);

        using var csvResponse = await client.GetAsync(
            "/api/reports/inventory/stock-transfers-report/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        Assert.StartsWith(
            "Transfer ID,Date,From Location,From Type,To Location,To Type,Status,Product Code,Barcode,SKU,Product Name,Category,Quantity,Unit Cost,Transfer Value,Created By",
            csv,
            StringComparison.Ordinal);
        Assert.Contains("501,", csv, StringComparison.Ordinal);
        Assert.Contains("VIS-001-DEF", csv, StringComparison.Ordinal);

        using var excelResponse = await client.GetAsync(
            "/api/reports/inventory/stock-transfers-report/export?format=excel");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StockTransferReportReadStore_EnforcesBusinessAndVisibilityScope()
    {
        await ResetAndSeedAsync();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = 2, Name = "Other Business" });
            db.Outlets.AddRange(
                new Outlet { Id = 31, BusinessId = 2, Name = "Other Outlet 1", Address = "X" },
                new Outlet { Id = 32, BusinessId = 2, Name = "Other Outlet 2", Address = "Y" });
            db.StockTransfers.Add(new StockTransfer
            {
                Id = 504,
                TransferNo = "TRF-504-OTHER",
                TransferType = StockTransfer.TransferTypeDirect,
                FromLocationId = 31,
                FromLocationType = "outlet",
                ToLocationId = 32,
                ToLocationType = "outlet",
                TransferDate = DateTime.UtcNow,
                Status = StockTransfer.StatusSubmitted,
                CreatedAt = DateTime.UtcNow,
                Items =
                [
                    new StockTransferItem
                    {
                        Id = 604,
                        VariantId = 301,
                        Quantity = 1,
                        RequestedQuantity = 1,
                        TransferQuantity = 1,
                        UnitCost = 10m
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var serviceScope = _factory.Services.CreateScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<IStockTransferReportReadStore>();
        var scope = new StockTransferReadScope(1, true, null, null);
        var filter = new StockTransferReportFilter(
            null,
            null,
            null,
            string.Empty,
            string.Empty,
            null,
            null,
            "transferDate",
            "desc",
            1,
            50);
        var allBusinessTransfers = await store.GetAsync(scope, filter, CancellationToken.None);
        Assert.Equal(3, allBusinessTransfers.TotalCount);
        Assert.DoesNotContain(allBusinessTransfers.Items, row => row.TransferId == 504);

        var foreignOutletFilter = await store.GetAsync(
            scope,
            filter with { FromOutletId = 31 },
            CancellationToken.None);
        Assert.Empty(foreignOutletFilter.Items);
        Assert.Equal(0, foreignOutletFilter.Summary.TotalTransfers);

        var defaultLocationTransfers = await store.GetAsync(
            new StockTransferReadScope(1, false, "outlet", 11),
            filter,
            CancellationToken.None);
        Assert.Equal(2, defaultLocationTransfers.TotalCount);
        Assert.All(defaultLocationTransfers.Items, row =>
            Assert.Contains(
                row.TransferId,
                new long[] { 501, 502 }));
    }

    [Fact]
    public async Task StockTransferPdf_DirectAccess_UnrelatedTransferIsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view", "OutletManager");

        var response = await client.GetAsync("/api/documents/stock-transfer/503/pdf");
        var body = await response.Content.ReadFromJsonAsync<SimpleMessageDto>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("You are not authorized to view this stock transfer.", body?.Message);
    }

    [Fact]
    public async Task StockTransferPdf_MissingDefaultLocation_ReturnsConfigurationError()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1002, "stock_transfers.view", "OutletManager");

        var response = await client.GetAsync("/api/documents/stock-transfer/501/pdf");
        var body = await response.Content.ReadFromJsonAsync<SimpleMessageDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Your default inventory location is not configured.", body?.Message);
    }

    [Fact]
    public async Task Search_ByTransferNumber_ReturnsExpectedTransfer()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            searchTerm = "TRF-502",
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(1, body!.Data!.TotalCount);
        Assert.Single(body.Data.StockTransfers);
        Assert.Equal(502L, body.Data.StockTransfers[0].Id);
    }

    [Fact]
    public async Task Search_ByTransferType_ReturnsOnlyMatchingType()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            transferType = StockTransfer.TransferTypeRequisition,
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(1, body!.Data!.TotalCount);
        Assert.Single(body.Data.StockTransfers);
        Assert.Equal(502L, body.Data.StockTransfers[0].Id);
    }

    [Fact]
    public async Task Search_ByDateRange_ReturnsOnlyTransfersWithinWindow()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");

        var start = DateTime.UtcNow.Date.AddDays(-1);
        var end = DateTime.UtcNow.Date;

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            dateFrom = start,
            dateTo = end,
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(body?.Data);

        var ids = body!.Data!.StockTransfers.Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal([502L, 503L], ids);
        Assert.Equal(2, body.Data.TotalCount);
    }

    [Fact]
    public async Task Search_ByVariant_StillHonorsVisibilityRestrictions()
    {
        await ResetAndSeedAsync();

        using var scopedClient = CreateClient(1001, "stock_transfers.view", "OutletManager");
        var scopedResponse = await scopedClient.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            productVariantId = 302,
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, scopedResponse.StatusCode);
        var scopedBody = await scopedResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(scopedBody?.Data);
        Assert.Empty(scopedBody!.Data!.StockTransfers);
        Assert.Equal(0, scopedBody.Data.TotalCount);

        using var globalClient = CreateClient(1001, "stock_transfers.view,stock_transfers.view_all_locations", "OutletManager");
        var globalResponse = await globalClient.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            productVariantId = 302,
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, globalResponse.StatusCode);
        var globalBody = await globalResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(globalBody?.Data);
        Assert.Single(globalBody!.Data!.StockTransfers);
        Assert.Equal(503L, globalBody.Data.StockTransfers[0].Id);
        Assert.Equal(1, globalBody.Data.TotalCount);
    }

    [Fact]
    public async Task Search_WithUnauthorizedLocationPair_ReturnsNoRowsForRestrictedUser()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view", "OutletManager");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            fromLocationType = "warehouse",
            fromLocationId = 21,
            toLocationType = "outlet",
            toLocationId = 13,
            pageNumber = 1,
            pageSize = 50
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(body?.Data);
        Assert.Empty(body!.Data!.StockTransfers);
        Assert.Equal(0, body.Data.TotalCount);
    }

    [Fact]
    public async Task Search_FilteredResults_MaintainConsistentCountAcrossPages()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(1001, "stock_transfers.view", "OutletManager");

        var firstPage = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            status = "",
            pageNumber = 1,
            pageSize = 1
        });

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        var firstBody = await firstPage.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(firstBody?.Data);
        Assert.Equal(2, firstBody!.Data!.TotalCount);
        Assert.Single(firstBody.Data.StockTransfers);

        var secondPage = await client.PostAsJsonAsync("/api/stock-transfers/search", new
        {
            status = "",
            pageNumber = 2,
            pageSize = 1
        });

        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        var secondBody = await secondPage.Content.ReadFromJsonAsync<ApiResponse<StockTransferListDto>>();
        Assert.NotNull(secondBody?.Data);
        Assert.Equal(2, secondBody!.Data!.TotalCount);
        Assert.Single(secondBody.Data.StockTransfers);

        var pageIds = new[]
        {
            firstBody.Data.StockTransfers[0].Id,
            secondBody.Data.StockTransfers[0].Id
        }.OrderBy(x => x).ToArray();

        Assert.Equal(new[] { 501L, 502L }, pageIds);
    }

    private HttpClient CreateClient(long userId, string permissions, string role, long businessId = 1)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Visibility Test Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Visibility Category" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet 1", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Outlet 2", Address = "B" },
            new Outlet { Id = 13, BusinessId = 1, Name = "Outlet 3", Address = "C" });

        db.Warehouses.AddRange(
            new Warehouse { Id = 21, BusinessId = 1, Name = "Main Warehouse", Address = "W1" },
            new Warehouse { Id = 22, BusinessId = 1, Name = "Overflow Warehouse", Address = "W2" });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Visibility Product",
            ProductCode = "VIS-001",
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
            Name = "Visibility Variant",
            Sku = "VIS-001-DEF",
            Barcode = "VIS-BC-001",
            Attributes = "{}"
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 302,
            BusinessId = 1,
            ProductId = 300,
            Name = "Visibility Variant Secondary",
            Sku = "VIS-001-ALT",
            Barcode = "VIS-BC-002",
            Attributes = "{}"
        });

        db.Users.AddRange(
            new User
            {
                Id = 1001,
                BusinessId = 1,
                Name = "Outlet 1 User",
                Email = "o1.user@test.local",
                PasswordHash = "x",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 1002,
                BusinessId = 1,
                Name = "No Default User",
                Email = "nodefault.user@test.local",
                PasswordHash = "x",
                OutletId = null,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            });

        var baseDate = DateTime.UtcNow.Date.AddHours(12);
        db.StockTransfers.AddRange(
            new StockTransfer
            {
                Id = 501,
                TransferNo = "TRF-501",
                TransferType = StockTransfer.TransferTypeDirect,
                FromLocationId = 11,
                FromLocationType = "outlet",
                ToLocationId = 12,
                ToLocationType = "outlet",
                TransferDate = baseDate.AddDays(-2),
                Status = StockTransfer.StatusSubmitted,
                CreatedAt = baseDate.AddDays(-2),
                Items = new List<StockTransferItem>
                {
                    new()
                    {
                        Id = 601,
                        VariantId = 301,
                        Quantity = 3,
                        RequestedQuantity = 3,
                        TransferQuantity = 3,
                        UnitCost = 12m
                    }
                }
            },
            new StockTransfer
            {
                Id = 502,
                TransferNo = "TRF-502",
                TransferType = StockTransfer.TransferTypeRequisition,
                FromLocationId = 12,
                FromLocationType = "outlet",
                ToLocationId = 11,
                ToLocationType = "outlet",
                TransferDate = baseDate.AddDays(-1),
                Status = StockTransfer.StatusApproved,
                CreatedAt = baseDate.AddDays(-1),
                Items = new List<StockTransferItem>
                {
                    new()
                    {
                        Id = 602,
                        VariantId = 301,
                        Quantity = 2,
                        RequestedQuantity = 2,
                        TransferQuantity = 2,
                        UnitCost = 11m
                    }
                }
            },
            new StockTransfer
            {
                Id = 503,
                TransferNo = "TRF-503",
                TransferType = StockTransfer.TransferTypeReturn,
                FromLocationId = 21,
                FromLocationType = "warehouse",
                ToLocationId = 13,
                ToLocationType = "outlet",
                TransferDate = baseDate,
                Status = StockTransfer.StatusInTransit,
                CreatedAt = baseDate,
                Items = new List<StockTransferItem>
                {
                    new()
                    {
                        Id = 603,
                        VariantId = 302,
                        Quantity = 1,
                        RequestedQuantity = 1,
                        TransferQuantity = 1,
                        UnitCost = 10m
                    }
                }
            });

        await db.SaveChangesAsync();
    }

    private sealed class SimpleMessageDto
    {
        public string? Message { get; set; }
    }
}
