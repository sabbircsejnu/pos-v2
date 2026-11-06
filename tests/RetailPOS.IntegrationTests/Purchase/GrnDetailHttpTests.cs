using System.Net;
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

public sealed class GrnDetailHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    private static readonly GrnReadScope Scope = new(1, false, [], [30]);

    [Fact]
    public async Task GetById_PreservesPermissionEnvelopeProjectionAndBusinessScope()
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
            "Dock receipt",
            "INV-40",
            new DateTime(2026, 3, 9),
            77,
            "Receiver",
            new DateTime(2026, 3, 10),
            [
                new GrnItemReadModel(
                    41,
                    21,
                    1,
                    "Product",
                    "P-1",
                    "Variant",
                    "Color=Blue",
                    5,
                    4,
                    10m,
                    8m,
                    "Received intact")
            ]);
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.FindByIdAsync(40, Scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(readModel);
        store.Setup(port => port.FindByIdAsync(41, Scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrnReadModel?)null);
        var readScope = new Mock<IGrnReadScopeProvider>();
        readScope.Setup(port => port.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Scope);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnReadStore>();
                services.AddScoped<IGrnReadStore>(_ => store.Object);
                services.RemoveAll<IGrnReadScopeProvider>();
                services.AddScoped<IGrnReadScopeProvider>(_ => readScope.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "purchase.view");
        var denied = await deniedClient.GetAsync("/api/grns/40");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var client = CreateClient(app.CreateClient(), "grn.view");
        var response = await client.GetAsync("/api/grns/40");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var data = root.GetProperty("data");
        Assert.Equal(40, data.GetProperty("id").GetInt64());
        Assert.Equal("GRN-000040", data.GetProperty("grnNumber").GetString());
        Assert.Equal(20, data.GetProperty("poId").GetInt64());
        Assert.Equal("PO-000020", data.GetProperty("poOrderNumber").GetString());
        Assert.Equal("Supplier", data.GetProperty("supplierName").GetString());
        Assert.Equal("Warehouse", data.GetProperty("warehouseName").GetString());
        Assert.Equal("INV-40", data.GetProperty("supplierInvoiceNumber").GetString());
        Assert.Equal(1, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(4, data.GetProperty("totalReceivedQty").GetInt32());
        Assert.Equal(32m, data.GetProperty("totalReceivedCost").GetDecimal());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(21, item.GetProperty("poItemId").GetInt64());
        Assert.Equal("P-1", item.GetProperty("productCode").GetString());
        Assert.Equal("Color=Blue", item.GetProperty("variantAttributes").GetString());
        Assert.Equal(5, item.GetProperty("orderedQty").GetInt32());
        Assert.Equal(4, item.GetProperty("receivedQty").GetInt32());
        Assert.Equal(8m, item.GetProperty("unitCost").GetDecimal());

        var missing = await client.GetAsync("/api/grns/41");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        store.Verify(port => port.FindByIdAsync(40, Scope, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(port => port.FindByIdAsync(41, Scope, It.IsAny<CancellationToken>()), Times.Once);
        readScope.Verify(port => port.GetAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
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
