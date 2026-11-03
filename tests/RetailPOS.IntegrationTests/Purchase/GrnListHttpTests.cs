using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

public sealed class GrnListHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task GetPoVariance_PreservesAuthorizationTotalsAndMissingBehavior()
    {
        var store = new Mock<IGrnPoVarianceReadStore>();
        store.Setup(port => port.FindByPurchaseOrderIdAsync(20, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnPoVarianceReadModel(
                20,
                "approved",
                3,
                1,
                [new GrnPoVarianceItemReadModel(21, 1, "Product", "Variant", 5, 2, 10m, 18m)]));
        store.Setup(port => port.FindByPurchaseOrderIdAsync(999, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrnPoVarianceReadModel?)null);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnPoVarianceReadStore>();
                services.AddScoped<IGrnPoVarianceReadStore>(_ => store.Object);
                services.RemoveAll<IGrnTenantScope>();
                services.AddScoped<IGrnTenantScope>(_ => tenantScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        using var denied = await deniedClient.GetAsync("/api/grns/po/20/variance");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        using var response = await client.GetAsync("/api/grns/po/20/variance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("PO-000020", data.GetProperty("poOrderNumber").GetString());
        Assert.Equal(3, data.GetProperty("totalGrns").GetInt32());
        Assert.Equal(1, data.GetProperty("completedGrns").GetInt32());
        Assert.Equal(5, data.GetProperty("totalOrderedQty").GetInt32());
        Assert.Equal(2, data.GetProperty("totalReceivedQty").GetInt32());
        Assert.Equal(3, data.GetProperty("totalRemainingQty").GetInt32());
        Assert.False(data.GetProperty("isFullyReceived").GetBoolean());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(18m, item.GetProperty("totalReceivedCost").GetDecimal());
        Assert.Equal(3, item.GetProperty("remainingQty").GetInt32());

        using var missing = await client.GetAsync("/api/grns/po/999/variance");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        store.Verify(
            port => port.FindByPurchaseOrderIdAsync(20, 1, It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            port => port.FindByPurchaseOrderIdAsync(999, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetVariance_PreservesAuthorizationResponseAndMissingBehavior()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindVarianceByIdAsync(40, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnVarianceReadModel(
                40,
                20,
                "full",
                [new GrnVarianceItemReadModel(21, 1, "Product", "Variant", 5, 5)]));
        store.Setup(port => port.FindVarianceByIdAsync(999, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrnVarianceReadModel?)null);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnReadStore>();
                services.AddScoped<IGrnReadStore>(_ => store.Object);
                services.RemoveAll<IGrnTenantScope>();
                services.AddScoped<IGrnTenantScope>(_ => tenantScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        using var denied = await deniedClient.GetAsync("/api/grns/40/variance");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        using var response = await client.GetAsync("/api/grns/40/variance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("GRN-000040", data.GetProperty("grnNumber").GetString());
        Assert.Equal("PO-000020", data.GetProperty("poOrderNumber").GetString());
        Assert.True(data.GetProperty("isFullReceipt").GetBoolean());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(5, item.GetProperty("orderedQty").GetInt32());
        Assert.Equal(5, item.GetProperty("receivedQty").GetInt32());

        using var missing = await client.GetAsync("/api/grns/999/variance");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        store.Verify(
            port => port.FindVarianceByIdAsync(40, 1, It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            port => port.FindVarianceByIdAsync(999, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPendingPos_PreservesPermissionEnvelopeAndPurchaseOrderProjection()
    {
        var store = new Mock<IPendingGrnPurchaseOrderReadStore>();
        store.Setup(port => port.FindPendingAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PendingGrnPurchaseOrderReadModel(
                    23,
                    10,
                    "Supplier",
                    30,
                    "Warehouse",
                    new DateTime(2026, 3, 9),
                    84m,
                    "partial",
                    [
                        new PendingGrnPurchaseOrderItemReadModel(
                            24,
                            1,
                            "Product",
                            "P-1",
                            "Variant",
                            "Color: Blue",
                            7,
                            12m,
                            "primary-thumb.jpg")
                    ])
            ]);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPendingGrnPurchaseOrderReadStore>();
                services.AddScoped<IPendingGrnPurchaseOrderReadStore>(_ => store.Object);
                services.RemoveAll<IGrnTenantScope>();
                services.AddScoped<IGrnTenantScope>(_ => tenantScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        using var denied = await deniedClient.GetAsync("/api/grns/pending-pos");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        using var response = await client.GetAsync("/api/grns/pending-pos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var po = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(23, po.GetProperty("id").GetInt64());
        Assert.Equal("PO-000023", po.GetProperty("orderNumber").GetString());
        Assert.Equal("Supplier", po.GetProperty("supplierName").GetString());
        Assert.Equal("Warehouse", po.GetProperty("warehouseName").GetString());
        Assert.Equal(84m, po.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("partial", po.GetProperty("status").GetString());
        var item = Assert.Single(po.GetProperty("items").EnumerateArray());
        Assert.Equal("primary-thumb.jpg", item.GetProperty("primaryImageThumb").GetString());
        Assert.Equal(7, item.GetProperty("quantity").GetInt32());

        store.Verify(
            port => port.FindPendingAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Search_PreservesFiltersPermissionEnvelopeAndPaginationProjection()
    {
        var readModel = new GrnReadModel(
            40,
            20,
            10,
            "Supplier",
            30,
            "Warehouse",
            new DateTime(2026, 3, 10),
            "full",
            null,
            null,
            null,
            null,
            null,
            new DateTime(2026, 3, 10),
            [
                new GrnItemReadModel(
                    41,
                    21,
                    0,
                    string.Empty,
                    null,
                    string.Empty,
                    null,
                    0,
                    5,
                    0,
                    8m,
                    null)
            ]);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.SearchAsync(
                20,
                "FULL",
                new DateTime(2026, 3, 10),
                new DateTime(2026, 3, 12),
                2,
                5,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnSearchReadModel([readModel], 15, 2, 5));
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnReadStore>();
                services.AddScoped<IGrnReadStore>(_ => store.Object);
                services.RemoveAll<IGrnTenantScope>();
                services.AddScoped<IGrnTenantScope>(_ => tenantScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        using var denied = await deniedClient.PostAsJsonAsync(
            "/api/grns/search",
            new
            {
                poId = 20,
                status = "FULL",
                startDate = "2026-03-10",
                endDate = "2026-03-12",
                pageNumber = 2,
                pageSize = 5
            });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        using var response = await client.PostAsJsonAsync(
            "/api/grns/search",
            new
            {
                poId = 20,
                status = "FULL",
                startDate = "2026-03-10",
                endDate = "2026-03-12",
                pageNumber = 2,
                pageSize = 5
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(15, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("pageNumber").GetInt32());
        Assert.Equal(5, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, data.GetProperty("totalPages").GetInt32());
        Assert.True(data.GetProperty("hasPreviousPage").GetBoolean());
        Assert.True(data.GetProperty("hasNextPage").GetBoolean());
        var grn = Assert.Single(data.GetProperty("grns").EnumerateArray());
        Assert.Equal("GRN-000040", grn.GetProperty("grnNumber").GetString());
        Assert.Equal(5, grn.GetProperty("totalReceivedQty").GetInt32());
        Assert.Equal(40m, grn.GetProperty("totalReceivedCost").GetDecimal());
        Assert.Equal(1, grn.GetProperty("totalItems").GetInt32());

        store.Verify(port => port.SearchAsync(
            20,
            "FULL",
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 12),
            2,
            5,
            1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_PreservesFiltersPermissionEnvelopeAndListProjection()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindAllAsync(20, "FULL", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new GrnReadModel(
                    40,
                    20,
                    10,
                    "Supplier",
                    30,
                    "Warehouse",
                    new DateTime(2026, 3, 10),
                    "full",
                    null,
                    null,
                    null,
                    null,
                    null,
                    new DateTime(2026, 3, 10),
                    [])
            ]);
        var tenantScope = new Mock<IGrnTenantScope>();
        tenantScope.Setup(port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnReadStore>();
                services.AddScoped<IGrnReadStore>(_ => store.Object);
                services.RemoveAll<IGrnTenantScope>();
                services.AddScoped<IGrnTenantScope>(_ => tenantScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        var denied = await deniedClient.GetAsync("/api/grns?poId=20&status=FULL");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        var response = await client.GetAsync("/api/grns?poId=20&status=FULL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var data = Assert.Single(root.GetProperty("data").EnumerateArray());
        Assert.Equal(40, data.GetProperty("id").GetInt64());
        Assert.Equal("GRN-000040", data.GetProperty("grnNumber").GetString());
        Assert.Equal("PO-000020", data.GetProperty("poOrderNumber").GetString());
        Assert.Equal(0, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(0, data.GetProperty("totalReceivedQty").GetInt32());
        Assert.Equal(0m, data.GetProperty("totalReceivedCost").GetDecimal());

        store.Verify(
            port => port.FindAllAsync(20, "FULL", 1, It.IsAny<CancellationToken>()),
            Times.Once);
        tenantScope.Verify(
            port => port.GetBusinessIdAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static HttpClient CreateClient(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "WarehouseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        client.DefaultRequestHeaders.Add("X-Test-UserId", "77");
        return client;
    }
}
