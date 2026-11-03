using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using Moq;
using Swashbuckle.AspNetCore.Swagger;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public class SalesCreateTerminalHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SalesCreateTerminalHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenNoActiveTerminalExistsForOutlet()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("No active terminal is configured", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenTerminalDoesNotBelongToOutlet()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: 9999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("is not active for outlet", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_Succeeds_AndPersistsTerminal_WhenValidTerminalProvided()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var response = await client.PostAsJsonAsync("/api/sales", BuildRequest(terminalId: 8001));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiEnvelope<SaleDto>(response);
        Assert.NotNull(created);
        Assert.Equal(8001, created!.Data?.TerminalId);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenBackdatedSalesDateWithoutPermission()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient();

        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = DateTime.UtcNow.AddDays(-1);

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("backdated", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_ReturnsBadRequest_WhenSalesDateIsFuture()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient(permissions: "sales.view,sales.create,sales.backdate");

        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = DateTime.UtcNow.AddHours(1);

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.NotNull(error);
        Assert.Contains("future", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_SucceedsWithBackdatedSalesDate_WhenPermissionGranted()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using var client = CreateSalesClient(permissions: "sales.view,sales.create,sales.backdate");

        var backdated = DateTime.UtcNow.AddDays(-2).Date;
        var request = BuildRequest(terminalId: 8001);
        request.SalesDate = backdated;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiEnvelope<SaleDto>(response);
        Assert.NotNull(created?.Data);
        Assert.Equal(backdated, created!.Data!.SaleDate.Date);
        Assert.True(created.Data.CreatedAt.Date >= backdated);
    }

    [Fact]
    public async Task TodaySummary_PreservesTotalsPaymentBreakdownAndOutletAuthorization()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            var today = DateTime.UtcNow.Date;
            db.Sales.AddRange(
                new Sale
                {
                    Id = 1001,
                    SaleNumber = "SUMMARY-1001",
                    OutletId = 5001,
                    CashierId = 7001,
                    SaleDate = today.AddHours(1),
                    TotalAmount = 100m,
                    Discount = 10m,
                    Tax = 5m,
                    PaymentMethod = "cash",
                    Status = "completed"
                },
                new Sale
                {
                    Id = 1002,
                    SaleNumber = "SUMMARY-1002",
                    OutletId = 5001,
                    CashierId = 7001,
                    SaleDate = today.AddHours(2),
                    TotalAmount = 50m,
                    Discount = 2m,
                    Tax = 3m,
                    PaymentMethod = "card",
                    Status = "completed"
                },
                new Sale
                {
                    Id = 1003,
                    SaleNumber = "SUMMARY-1003",
                    OutletId = 5001,
                    CashierId = 7001,
                    SaleDate = today.AddHours(3),
                    TotalAmount = 500m,
                    Discount = 50m,
                    Tax = 25m,
                    PaymentMethod = "cash",
                    Status = "voided"
                },
                new Sale
                {
                    Id = 1004,
                    SaleNumber = "SUMMARY-1004",
                    OutletId = 5001,
                    CashierId = 7001,
                    SaleDate = today.AddDays(-1),
                    TotalAmount = 700m,
                    Discount = 70m,
                    Tax = 35m,
                    PaymentMethod = "cash",
                    Status = "completed"
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/today/summary?outletId=5001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalSales").GetInt32());
        Assert.Equal(150m, data.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(12m, data.GetProperty("totalDiscount").GetDecimal());
        Assert.Equal(8m, data.GetProperty("totalTax").GetDecimal());
        Assert.Equal(100m, data.GetProperty("paymentBreakdown").GetProperty("cash").GetDecimal());
        Assert.Equal(50m, data.GetProperty("paymentBreakdown").GetProperty("card").GetDecimal());

        using var defaultOutletResponse = await client.GetAsync("/api/sales/today/summary");
        Assert.Equal(HttpStatusCode.OK, defaultOutletResponse.StatusCode);
        using var defaultOutletDocument = JsonDocument.Parse(
            await defaultOutletResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            2,
            defaultOutletDocument.RootElement.GetProperty("data").GetProperty("totalSales").GetInt32());

        using var unauthorizedResponse = await client.GetAsync(
            "/api/sales/today/summary?outletId=5002");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public async Task GetSaleById_PreservesSaleDtoAndAuthorizesOutletBeforeReturningIt()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = 2, Name = "Other Business", IsActive = true });
            db.Outlets.AddRange(
                new Outlet { Id = 5002, BusinessId = 1, Name = "Restricted outlet", Address = "Dhaka" },
                new Outlet { Id = 5003, BusinessId = 2, Name = "Other business outlet", Address = "Dhaka" });
            db.Sales.AddRange(
                new Sale
                {
                    Id = 1201,
                    SaleNumber = "SALE-1201",
                    OutletId = 5001,
                    CustomerId = 6001,
                    CashierId = 7001,
                    SaleDate = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
                    TotalAmount = 20m,
                    Discount = 2m,
                    Tax = 1m,
                    PaymentMethod = "cash",
                    Status = "completed",
                    Items =
                    [
                        new SaleItem
                        {
                            Id = 2201,
                            VariantId = 3201,
                            Quantity = 2,
                            UnitPrice = 10m,
                            Subtotal = 20m,
                            DiscountAmount = 2m,
                            AppliedRuleName = "Sale rule"
                        }
                    ],
                    Payments =
                    [
                        new SalePayment
                        {
                            Id = 2301,
                            Method = "cash",
                            Amount = 19m,
                            Tendered = 20m
                        }
                    ]
                },
                new Sale
                {
                    Id = 1202,
                    SaleNumber = "RESTRICTED-1202",
                    OutletId = 5002,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "completed"
                },
                new Sale
                {
                    Id = 1203,
                    SaleNumber = "OTHER-BUSINESS-1203",
                    OutletId = 5003,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "completed"
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1201");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sale = document.RootElement.GetProperty("data");
        Assert.Equal(
            new[]
            {
                "id", "saleNumber", "outletId", "outletName", "terminalId", "terminalName",
                "cashSessionId", "businessDate", "saleDateTime", "customerId", "customerName",
                "cashierId", "cashierName", "salespersonUserId", "salespersonName", "saleDate", "totalAmount", "discount", "tax",
                "netTotal", "paymentMethod", "status", "createdAt", "items", "payments"
            }.OrderBy(name => name),
            sale.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("SALE-1201", sale.GetProperty("saleNumber").GetString());
        Assert.Equal(5001, sale.GetProperty("outletId").GetInt64());
        Assert.Equal("Main Outlet", sale.GetProperty("outletName").GetString());
        Assert.Equal(6001, sale.GetProperty("customerId").GetInt64());
        Assert.Equal(7001, sale.GetProperty("cashierId").GetInt64());
        Assert.Equal("Cashier One", sale.GetProperty("cashierName").GetString());
        Assert.Equal(20m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2m, sale.GetProperty("discount").GetDecimal());
        Assert.Equal(1m, sale.GetProperty("tax").GetDecimal());
        Assert.Equal(19m, sale.GetProperty("netTotal").GetDecimal());
        Assert.Equal("cash", sale.GetProperty("paymentMethod").GetString());
        Assert.Equal("completed", sale.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, sale.GetProperty("terminalName").ValueKind);
        var item = Assert.Single(sale.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(10m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(20m, item.GetProperty("subtotal").GetDecimal());
        Assert.Equal(2m, item.GetProperty("discountAmount").GetDecimal());
        Assert.Equal("Test Product", item.GetProperty("productName").GetString());
        Assert.Equal("SKU-3201", item.GetProperty("variantSku").GetString());
        Assert.Equal("Sale rule", item.GetProperty("appliedRuleName").GetString());
        var payment = Assert.Single(sale.GetProperty("payments").EnumerateArray());
        Assert.Equal("cash", payment.GetProperty("method").GetString());
        Assert.Equal(19m, payment.GetProperty("amount").GetDecimal());
        Assert.Equal(20m, payment.GetProperty("tendered").GetDecimal());
        Assert.Equal(1m, payment.GetProperty("change").GetDecimal());

        using var restricted = await client.GetAsync("/api/sales/1202");
        Assert.Equal(HttpStatusCode.Forbidden, restricted.StatusCode);
        Assert.DoesNotContain("RESTRICTED-1202", await restricted.Content.ReadAsStringAsync());
        using var crossBusiness = await client.GetAsync("/api/sales/1203");
        Assert.Equal(HttpStatusCode.Forbidden, crossBusiness.StatusCode);
        Assert.DoesNotContain("OTHER-BUSINESS-1203", await crossBusiness.Content.ReadAsStringAsync());

        using var missing = await client.GetAsync("/api/sales/9999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var missingError = await ReadApiEnvelope<ApiErrorBody>(missing);
        Assert.Equal("Sale with ID 9999 not found", missingError?.Message);
    }

    [Fact]
    public void GetSaleById_RouteRemainsInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Get, route.Value!.Operations.Keys);
    }

    [Fact]
    public async Task VoidSale_PreservesHttpErrorAndOpenApiContract()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.Add(new Sale
            {
                Id = 1901,
                SaleNumber = "VOID-1901",
                OutletId = 5001,
                CashierId = 7001,
                CustomerId = 6001,
                Status = "refunded",
                TotalAmount = 0m,
                PaymentMethod = "cash"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient(permissions: "sales.view,sales.void");
        using var response = await client.PostAsJsonAsync(
            "/api/sales/1901/void",
            new VoidSaleDto { Reason = "valid request" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.Equal("Cannot void a refunded sale", error?.Message);

        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}/void", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Post, route.Value!.Operations.Keys);
        Assert.NotNull(route.Value.Operations[OperationType.Post].RequestBody);
        Assert.Contains(
            route.Value.Operations[OperationType.Post].Parameters,
            parameter => parameter.Name == "id" && parameter.In == ParameterLocation.Path);
    }

    [Fact]
    public async Task RefundSale_PreservesHttpErrorAndOpenApiContract()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.Add(new Sale
            {
                Id = 1902,
                SaleNumber = "REFUND-1902",
                OutletId = 5001,
                CashierId = 7001,
                CustomerId = 6001,
                Status = "voided",
                TotalAmount = 0m,
                PaymentMethod = "cash"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient(permissions: "sales.view,sales.refund");
        using var response = await client.PostAsJsonAsync(
            "/api/sales/1902/refund",
            new RefundSaleDto
            {
                Items = [new RefundItemDto { VariantId = 3201, Quantity = 1 }],
                Reason = "valid request"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(response);
        Assert.Equal("Cannot refund a voided sale", error?.Message);

        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");
        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}/refund", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Post, route.Value!.Operations.Keys);
        Assert.NotNull(route.Value.Operations[OperationType.Post].RequestBody);
        Assert.Contains(
            route.Value.Operations[OperationType.Post].Parameters,
            parameter => parameter.Name == "id" && parameter.In == ParameterLocation.Path);
    }

    [Fact]
    public void HeldSaleRoutes_PreserveOpenApiVerbsAndPayloads()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var collectionRoute = swagger.Paths.Single(path =>
            string.Equals(path.Key, "/api/held-sales", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Post, collectionRoute.Value.Operations.Keys);
        Assert.NotNull(collectionRoute.Value.Operations[OperationType.Post].RequestBody);

        var itemRoute = swagger.Paths.Single(path =>
            string.Equals(path.Key, "/api/held-sales/{id}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Get, itemRoute.Value.Operations.Keys);
        Assert.Contains(OperationType.Delete, itemRoute.Value.Operations.Keys);
        Assert.Contains(
            itemRoute.Value.Operations[OperationType.Delete].Parameters,
            parameter => parameter.Name == "id" && parameter.In == ParameterLocation.Path);

        Assert.Contains(
            swagger.Paths,
            path => string.Equals(path.Key, "/api/held-sales/outlet/{outletId}", StringComparison.OrdinalIgnoreCase) &&
                path.Value.Operations.ContainsKey(OperationType.Get));
        Assert.Contains(
            swagger.Paths,
            path => string.Equals(path.Key, "/api/held-sales/cashier/{cashierId}", StringComparison.OrdinalIgnoreCase) &&
                path.Value.Operations.ContainsKey(OperationType.Get));
    }

    [Fact]
    public void CreateSale_RouteAndRequestBodyRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Post, route.Value!.Operations.Keys);
        Assert.NotNull(route.Value.Operations[OperationType.Post].RequestBody);
    }

    [Fact]
    public async Task GetAll_PreservesFiltersPaginationOrderingAndLegacyProjection()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        var firstDate = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Users.Add(new User
            {
                Id = 7002,
                BusinessId = 1,
                Name = "Second Cashier",
                Email = "cashier2@example.com",
                PasswordHash = "x",
                OutletId = 5001,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            });
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.AddRange(
                CreateSearchSale(1301, "SEARCH-1301", 5001, 7001, 6001, firstDate, "completed"),
                CreateSearchSale(1302, "SEARCH-1302", 5001, 7001, 6001, firstDate.AddDays(1), "completed"),
                CreateSearchSale(1303, "SEARCH-1303", 5001, 7001, 6001, firstDate.AddDays(2), "completed"),
                CreateSearchSale(1304, "SEARCH-1304", 5001, 7002, 6001, firstDate.AddDays(3), "completed"),
                CreateSearchSale(1305, "SEARCH-1305", 5001, 7001, 6001, firstDate.AddDays(4), "voided"),
                CreateSearchSale(1306, "OTHER-OUTLET-1306", 5002, 7001, 6001, firstDate.AddDays(5), "completed"));
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        var filterUrl = "/api/sales?outletId=5001&cashierId=7001&customerId=6001" +
            "&startDate=2026-10-01T00%3A00%3A00Z&endDate=2026-10-03T23%3A59%3A59Z" +
            "&status=COMPLETED&pageNumber=1&pageSize=2";
        using var filtered = await client.GetAsync(filterUrl);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using var filteredDocument = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        var list = filteredDocument.RootElement.GetProperty("data");
        Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, list.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, list.GetProperty("pageSize").GetInt32());
        var sales = list.GetProperty("sales").EnumerateArray().ToArray();
        Assert.Equal(2, sales.Length);
        Assert.Equal("SEARCH-1303", sales[0].GetProperty("saleNumber").GetString());
        Assert.Equal("SEARCH-1302", sales[1].GetProperty("saleNumber").GetString());
        Assert.Equal(string.Empty, sales[0].GetProperty("items")[0].GetProperty("productName").GetString());
        Assert.Equal(string.Empty, sales[0].GetProperty("items")[0].GetProperty("variantSku").GetString());
        Assert.Empty(sales[0].GetProperty("payments").EnumerateArray());

        using var secondPage = await client.GetAsync(
            "/api/sales?outletId=5001&cashierId=7001&customerId=6001" +
            "&startDate=2026-10-01T00%3A00%3A00Z&endDate=2026-10-03T23%3A59%3A59Z" +
            "&status=completed&pageNumber=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);
        using var secondPageDocument = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync());
        var secondPageData = secondPageDocument.RootElement.GetProperty("data");
        Assert.Equal(3, secondPageData.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, secondPageData.GetProperty("pageNumber").GetInt32());
        Assert.Equal("SEARCH-1302", secondPageData.GetProperty("sales")[0].GetProperty("saleNumber").GetString());

        using var defaultOutlet = await client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.OK, defaultOutlet.StatusCode);
        using var defaultDocument = JsonDocument.Parse(await defaultOutlet.Content.ReadAsStringAsync());
        Assert.All(defaultDocument.RootElement.GetProperty("data").GetProperty("sales").EnumerateArray(),
            sale => Assert.Equal(5001, sale.GetProperty("outletId").GetInt64()));

        using var unauthorized = await client.GetAsync("/api/sales?outletId=5002");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
    }

    [Fact]
    public void GetAll_RouteAndQueryParametersRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Get, route.Value!.Operations.Keys);
        Assert.Equal(
            new[]
            {
                "OutletId", "CashierId", "CustomerId", "StartDate",
                "EndDate", "Status", "PageNumber", "PageSize"
            }.OrderBy(name => name),
            route.Value.Operations[OperationType.Get].Parameters!
                .Select(parameter => parameter.Name)
                .OrderBy(name => name));
    }

    [Fact]
    public async Task SearchPost_MatchesGetSearchProjectionAndRetainsOutletAuthorization()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.AddRange(
                CreateSearchSale(
                    1401, "POST-SEARCH-1401", 5001, 7001, 6001,
                    new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), "completed"),
                CreateSearchSale(
                    1402, "POST-SEARCH-1402", 5001, 7001, 6001,
                    new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc), "completed"));
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var getResponse = await client.GetAsync(
            "/api/sales?outletId=5001&cashierId=7001&customerId=6001&pageNumber=1&pageSize=2");
        using var postResponse = await client.PostAsJsonAsync(
            "/api/sales/search",
            new SaleSearchDto
            {
                OutletId = 5001,
                CashierId = 7001,
                CustomerId = 6001,
                PageNumber = 1,
                PageSize = 2
            });

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        using var getDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        using var postDocument = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            getDocument.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32(),
            postDocument.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(
            getDocument.RootElement.GetProperty("data").GetProperty("sales").EnumerateArray()
                .Select(sale => sale.GetProperty("saleNumber").GetString()).ToArray(),
            postDocument.RootElement.GetProperty("data").GetProperty("sales").EnumerateArray()
                .Select(sale => sale.GetProperty("saleNumber").GetString()).ToArray());
        Assert.Equal(
            getDocument.RootElement.GetProperty("data").GetProperty("sales")[0]
                .GetProperty("items").GetArrayLength(),
            postDocument.RootElement.GetProperty("data").GetProperty("sales")[0]
                .GetProperty("items").GetArrayLength());

        using var unauthorized = await client.PostAsJsonAsync(
            "/api/sales/search",
            new SaleSearchDto { OutletId = 5002 });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
    }

    [Fact]
    public void SearchPost_RouteAndBodyRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/search", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Post, route.Value!.Operations.Keys);
        Assert.NotNull(route.Value.Operations[OperationType.Post].RequestBody);
    }

    [Fact]
    public async Task GetReceipt_ReturnsSaleAndRecordsPrintHistoryOnlyAfterOutletAuthorization()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.AddRange(
                new Sale
                {
                    Id = 1501,
                    SaleNumber = "RECEIPT-1501",
                    OutletId = 5001,
                    TerminalId = 8001,
                    CustomerId = 6001,
                    CashierId = 7001,
                    SaleDate = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
                    TotalAmount = 20m,
                    Discount = 2m,
                    Tax = 1m,
                    PaymentMethod = "cash",
                    Status = "completed",
                    Items =
                    [
                        new SaleItem
                        {
                            Id = 2501,
                            VariantId = 3201,
                            Quantity = 2,
                            UnitPrice = 10m,
                            Subtotal = 20m
                        }
                    ],
                    Payments =
                    [
                        new SalePayment
                        {
                            Id = 2601,
                            Method = "cash",
                            Amount = 19m,
                            Tendered = 20m
                        }
                    ]
                },
                new Sale
                {
                    Id = 1502,
                    SaleNumber = "RESTRICTED-RECEIPT-1502",
                    OutletId = 5002,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "completed"
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1501/receipt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var receiptDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var receipt = receiptDocument.RootElement.GetProperty("data");
        Assert.Equal("RECEIPT-1501", receipt.GetProperty("saleNumber").GetString());
        Assert.Equal(5001, receipt.GetProperty("outletId").GetInt64());
        Assert.Equal(19m, receipt.GetProperty("netTotal").GetDecimal());
        Assert.Single(receipt.GetProperty("items").EnumerateArray());
        Assert.Equal(1m, receipt.GetProperty("payments")[0].GetProperty("change").GetDecimal());

        using var unauthorized = await client.GetAsync("/api/sales/1502/receipt");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        Assert.DoesNotContain(
            "RESTRICTED-RECEIPT-1502",
            await unauthorized.Content.ReadAsStringAsync());
        using var missing = await client.GetAsync("/api/sales/9999/receipt");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var history = Assert.Single(db.ReceiptPrintHistories);
            Assert.Equal(1501, history.SaleId);
            Assert.Equal(8001, history.TerminalId);
            Assert.Equal(7001, history.PrintedByUserId);
            Assert.Equal("print", history.ActionType);
            Assert.InRange(history.PrintedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        }
    }

    [Fact]
    public void GetReceipt_RouteRemainsInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}/receipt", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Get, route.Value!.Operations.Keys);
    }

    [Fact]
    public async Task ReceiptPrintAttempt_PreservesResponseHistoryAndAuditContract()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.Add(new Sale
            {
                Id = 1551,
                SaleNumber = "ATTEMPT-1551",
                OutletId = 5001,
                TerminalId = 8001,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.PostAsJsonAsync(
            "/api/sales/1551/receipt-print-attempt",
            new ReceiptPrintAttemptDto
            {
                Action = "SavedAsPdf",
                OutputMode = "thermal",
                Message = "Receipt saved"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope<object>(response);
        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal("Receipt attempt recorded.", envelope.Message);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var history = Assert.Single(db.ReceiptPrintHistories);
            Assert.Equal(1551, history.SaleId);
            Assert.Equal(8001, history.TerminalId);
            Assert.Equal(7001, history.PrintedByUserId);
            Assert.Equal("saved_pdf", history.ActionType);

            var audit = Assert.Single(db.AuditEvents);
            Assert.Equal("ReceiptSavedAsPdf", audit.ActionType);
            Assert.Equal("Success", audit.Status);
            using var metadata = JsonDocument.Parse(audit.Metadata!);
            Assert.Equal("saved_pdf", metadata.RootElement.GetProperty("action").GetString());
            Assert.Equal("thermal", metadata.RootElement.GetProperty("outputMode").GetString());
            Assert.Equal("Receipt saved", metadata.RootElement.GetProperty("message").GetString());
        }
    }

    [Fact]
    public void ReceiptPrintAttempt_RouteAndRequestBodyRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(
                path.Key,
                "/api/sales/{id}/receipt-print-attempt",
                StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        Assert.Contains(OperationType.Post, route.Value!.Operations.Keys);
        Assert.NotNull(route.Value.Operations[OperationType.Post].RequestBody);
    }

    [Fact]
    public async Task ReceiptPrintAttempt_DeniesUnauthorizedOutletBeforeWritingHistoryOrAudit()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.Add(new Sale
            {
                Id = 1552,
                SaleNumber = "RESTRICTED-ATTEMPT-1552",
                OutletId = 5002,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.PostAsJsonAsync(
            "/api/sales/1552/receipt-print-attempt",
            new ReceiptPrintAttemptDto { Action = "printed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(db.ReceiptPrintHistories);
            Assert.DoesNotContain(db.AuditEvents, audit => audit.ActionType == "ReceiptPrinted");
        }
    }

    [Fact]
    public async Task ReceiptPrintAttemptAuditWriter_SuppressesAuditFailureAfterHistoryWrite()
    {
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var writer = new SaleReceiptAttemptAuditWriter(
            auditService.Object,
            NullLogger<SaleReceiptAttemptAuditWriter>.Instance);

        await writer.RecordBestEffortAsync(
            new RetailPOS.Application.Sales.Abstractions.SaleReceiptAttemptAuditRecord(
                "ReceiptPrinted",
                "Receipt printed for SALE-1551",
                "Success",
                null,
                1551,
                "SALE-1551",
                5001,
                8001,
                "thermal",
                "printed",
                null),
            CancellationToken.None);

        auditService.Verify(
            service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reprint_PreservesFinalizedSaleEligibilityResponseAndHistory()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.AddRange(
                new Sale
                {
                    Id = 1561,
                    SaleNumber = "REPRINT-1561",
                    OutletId = 5001,
                    TerminalId = 8001,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "completed"
                },
                new Sale
                {
                    Id = 1562,
                    SaleNumber = "DRAFT-1562",
                    OutletId = 5001,
                    TerminalId = 8001,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "draft"
                },
                new Sale
                {
                    Id = 1563,
                    SaleNumber = "RESTRICTED-REPRINT-1563",
                    OutletId = 5002,
                    CashierId = 7001,
                    SaleDate = DateTime.UtcNow,
                    PaymentMethod = "cash",
                    Status = "completed"
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.PostAsync("/api/sales/1561/reprint", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope<SaleDto>(response);
        Assert.NotNull(envelope?.Data);
        Assert.Equal("REPRINT-1561", envelope!.Data!.SaleNumber);
        Assert.Equal("completed", envelope.Data.Status);
        Assert.Equal("Receipt reprint payload generated", envelope.Message);

        using var invalid = await client.PostAsync("/api/sales/1562/reprint", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await ReadApiEnvelope<ApiErrorBody>(invalid);
        Assert.Equal("Only finalized sales can be reprinted", error?.Message);

        using var missing = await client.PostAsync("/api/sales/9999/reprint", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var unauthorized = await client.PostAsync("/api/sales/1563/reprint", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var history = Assert.Single(db.ReceiptPrintHistories);
            Assert.Equal(1561, history.SaleId);
            Assert.Equal(8001, history.TerminalId);
            Assert.Equal(7001, history.PrintedByUserId);
            Assert.Equal("reprint", history.ActionType);
            Assert.DoesNotContain(db.ReceiptPrintHistories, row => row.SaleId == 1563);
        }
    }

    [Fact]
    public void Reprint_RouteRemainsPostWithoutRequestBodyInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}/reprint", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        var operation = route.Value!.Operations[OperationType.Post];
        Assert.Null(operation.RequestBody);
        Assert.Contains(operation.Parameters, parameter => parameter.In == ParameterLocation.Path);
    }

    [Fact]
    public async Task ReceiptDocument_PreservesProjectionModeAndGeneratedHistory()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.Add(new Sale
            {
                Id = 1571,
                SaleNumber = "DOCUMENT-1571",
                OutletId = 5001,
                TerminalId = 8001,
                CustomerId = 6001,
                CashierId = 7001,
                SaleDate = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
                TotalAmount = 20m,
                Discount = 2m,
                Tax = 1m,
                PaymentMethod = "cash",
                Status = "completed",
                Items =
                [
                    new SaleItem
                    {
                        Id = 2571,
                        VariantId = 3201,
                        Quantity = 2,
                        UnitPrice = 10m,
                        DiscountAmount = 2m,
                        Subtotal = 18m
                    }
                ],
                Payments =
                [
                    new SalePayment
                    {
                        Id = 2671,
                        Method = "cash",
                        Amount = 19m,
                        Tendered = 20m
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1571/receipt-document?mode=A4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("A4", data.GetProperty("outputMode").GetString());
        Assert.True(data.GetProperty("header").GetProperty("showLogo").GetBoolean());
        Assert.Equal("Dhaka", data.GetProperty("header").GetProperty("outletAddress").GetString());
        Assert.Equal("DOCUMENT-1571", data.GetProperty("saleInfo").GetProperty("invoiceNumber").GetString());
        Assert.Equal("Cashier One", data.GetProperty("saleInfo").GetProperty("cashier").GetString());
        Assert.Equal("Counter-1", data.GetProperty("saleInfo").GetProperty("terminal").GetString());
        Assert.Equal("Test Product", data.GetProperty("items")[0].GetProperty("productName").GetString());
        Assert.Equal("Default", data.GetProperty("items")[0].GetProperty("variantLabel").GetString());
        Assert.Equal(10m, data.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(18m, data.GetProperty("items")[0].GetProperty("lineTotal").GetDecimal());
        Assert.Equal(2m, data.GetProperty("summary").GetProperty("quantity").GetDecimal());
        Assert.Equal(20m, data.GetProperty("summary").GetProperty("subtotal").GetDecimal());
        Assert.Equal(4m, data.GetProperty("summary").GetProperty("discount").GetDecimal());
        Assert.Equal(1m, data.GetProperty("summary").GetProperty("tax").GetDecimal());
        Assert.Equal(20m, data.GetProperty("summary").GetProperty("grandTotal").GetDecimal());
        Assert.Equal(19m, data.GetProperty("summary").GetProperty("paidAmount").GetDecimal());
        Assert.Equal(1m, data.GetProperty("summary").GetProperty("change").GetDecimal());
        Assert.Equal("DOCUMENT-1571", data.GetProperty("footer").GetProperty("codeValue").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var history = Assert.Single(db.ReceiptPrintHistories);
            Assert.Equal(1571, history.SaleId);
            Assert.Equal("generated", history.ActionType);
            Assert.Contains(db.AuditEvents, audit => audit.ActionType == "ReceiptGenerated");
        }
    }

    [Fact]
    public void ReceiptDocument_RouteAndModeQueryRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(
                path.Key,
                "/api/sales/{id}/receipt-document",
                StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        var operation = route.Value!.Operations[OperationType.Get];
        Assert.Contains(operation.Parameters, parameter => parameter.In == ParameterLocation.Path);
        Assert.Contains(operation.Parameters, parameter =>
            parameter.In == ParameterLocation.Query &&
            string.Equals(parameter.Name, "mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReceiptDocument_DeniesUnauthorizedOutletBeforeRecordingReceiptEffects()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.Add(new Sale
            {
                Id = 1572,
                SaleNumber = "RESTRICTED-DOCUMENT-1572",
                OutletId = 5002,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1572/receipt-document");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(db.ReceiptPrintHistories);
            Assert.DoesNotContain(db.AuditEvents, audit => audit.ActionType == "ReceiptGenerated");
        }
    }

    [Fact]
    public async Task ReceiptGeneratedAuditWriter_SuppressesAuditFailure()
    {
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var writer = new SaleReceiptActionAuditWriter(
            auditService.Object,
            NullLogger<SaleReceiptActionAuditWriter>.Instance);

        await writer.RecordBestEffortAsync(
            new RetailPOS.Application.Sales.Abstractions.SaleReceiptActionAuditRecord(
                "ReceiptGenerated",
                "Receipt generated for DOCUMENT-1571",
                1571,
                "DOCUMENT-1571",
                5001,
                8001,
                "A4",
                "generated"),
            CancellationToken.None);

        auditService.Verify(
            service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReceiptPdf_BaselinePreservesDownloadAndSavedPdfHistory()
    {
        await ResetAndSeedAsync(includeActiveTerminal: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Sales.Add(new Sale
            {
                Id = 1581,
                SaleNumber = "PDF-1581",
                OutletId = 5001,
                TerminalId = 8001,
                CustomerId = 6001,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                TotalAmount = 20m,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1581/receipt-pdf?mode=A4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("receipt-PDF-1581-", response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var history = Assert.Single(db.ReceiptPrintHistories);
            Assert.Equal(1581, history.SaleId);
            Assert.Equal("saved_pdf", history.ActionType);
            Assert.Contains(db.AuditEvents, audit => audit.ActionType == "ReceiptSavedAsPdf");
        }
    }

    [Fact]
    public async Task ReceiptPdf_BaselineDeniesUnauthorizedOutletWithoutReceiptHistory()
    {
        await ResetAndSeedAsync(includeActiveTerminal: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet
            {
                Id = 5002,
                BusinessId = 1,
                Name = "Restricted outlet",
                Address = "Dhaka"
            });
            db.Sales.Add(new Sale
            {
                Id = 1582,
                SaleNumber = "RESTRICTED-PDF-1582",
                OutletId = 5002,
                CashierId = 7001,
                SaleDate = DateTime.UtcNow,
                PaymentMethod = "cash",
                Status = "completed"
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateSalesClient();
        using var response = await client.GetAsync("/api/sales/1582/receipt-pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(db.ReceiptPrintHistories);
            Assert.DoesNotContain(db.AuditEvents, audit => audit.ActionType == "ReceiptSavedAsPdf");
        }
    }

    [Fact]
    public void ReceiptPdf_RouteAndModeQueryRemainInOpenApi()
    {
        var swagger = _factory.Services
            .GetRequiredService<ISwaggerProvider>()
            .GetSwagger("v1");

        var route = swagger.Paths.SingleOrDefault(path =>
            string.Equals(path.Key, "/api/sales/{id}/receipt-pdf", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(route.Value);
        var operation = route.Value!.Operations[OperationType.Get];
        Assert.Contains(operation.Parameters, parameter => parameter.In == ParameterLocation.Path);
        Assert.Contains(operation.Parameters, parameter =>
            parameter.In == ParameterLocation.Query &&
            string.Equals(parameter.Name, "mode", StringComparison.OrdinalIgnoreCase));
    }

    private static Sale CreateSearchSale(
        long id,
        string saleNumber,
        long outletId,
        long cashierId,
        long? customerId,
        DateTime saleDate,
        string status) =>
        new()
        {
            Id = id,
            SaleNumber = saleNumber,
            OutletId = outletId,
            CashierId = cashierId,
            CustomerId = customerId,
            SaleDate = saleDate,
            TotalAmount = 25m,
            Discount = 2m,
            Tax = 1m,
            PaymentMethod = "cash",
            Status = status,
            Items =
            [
                new SaleItem
                {
                    Id = id + 10000,
                    VariantId = 3201,
                    Quantity = 1,
                    UnitPrice = 25m,
                    Subtotal = 25m
                }
            ]
        };

    private HttpClient CreateSalesClient(string permissions = "sales.view,sales.create")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync(bool includeActiveTerminal)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Test Business",
            IsActive = true
        });

        db.Outlets.Add(new Outlet
        {
            Id = 5001,
            BusinessId = 1,
            Name = "Main Outlet",
            InvoicePrefix = "MAIN",
            Address = "Dhaka"
        });

        db.Users.Add(new User
        {
            Id = 7001,
            BusinessId = 1,
            Name = "Cashier One",
            Email = "cashier@example.com",
            PasswordHash = "x",
            OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true
        });

        db.Customers.Add(new Customer
        {
            Id = 6001,
            BusinessId = 1,
            Name = Customer.WalkInCustomerName,
            CustomerCode = Customer.WalkInCustomerCode,
            IsSystem = true,
            IsActive = true
        });

        db.Categories.Add(new Category
        {
            Id = 3001,
            BusinessId = 1,
            Name = "General",
            Description = "Test Category"
        });

        db.Products.Add(new Product
        {
            Id = 3101,
            BusinessId = 1,
            Name = "Test Product",
            ProductCode = "P-3101",
            CategoryId = 3001,
            BasePrice = 100m,
            CostPrice = 70m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 3201,
            BusinessId = 1,
            ProductId = 3101,
            Name = "Default",
            Sku = "SKU-3201",
            Barcode = "BC-3201",
            Attributes = "{}",
            PriceAdjustment = 0m
        });

        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            Id = 9001,
            VariantId = 3201,
            LocationType = "outlet",
            LocationId = 5001,
            Quantity = 50,
            LowStockThreshold = 5
        });

        if (includeActiveTerminal)
        {
            db.PosTerminals.Add(new PosTerminal
            {
                Id = 8001,
                OutletId = 5001,
                Name = "Counter-1",
                Code = "T-1",
                IsActive = true,
                IsDefault = true
            });

            db.CashSessions.Add(new CashSession
            {
                Id = 8101,
                BusinessId = 1,
                OutletId = 5001,
                TerminalId = 8001,
                BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow,
                OpeningCashAmount = 1000m,
                Status = "open",
                OpenedByUserId = 7001,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static CreateSaleDto BuildRequest(long? terminalId)
    {
        return new CreateSaleDto
        {
            OutletId = 5001,
            TerminalId = terminalId,
            CashierId = 7001,
            PaymentMethod = "cash",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            // 2 x 100.00 at the default 10% VAT: the server recomputes tax and rejects a different asserted value.
            Tax = 20m,
            CashTendered = 220m,
            Items =
            [
                new CreateSaleItemDto
                {
                    VariantId = 3201,
                    Quantity = 2,
                    UnitPrice = 100m,
                    DiscountAmount = 0m
                }
            ]
        };
    }

    private static async Task<ApiEnvelope<T>?> ReadApiEnvelope<T>(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class ApiErrorBody
    {
        public string Message { get; set; } = string.Empty;
    }
}
