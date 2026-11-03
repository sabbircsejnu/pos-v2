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

public sealed class GrnCompletionHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task Complete_PreservesPermissionActorAndResponseContract()
    {
        var store = new Mock<IGrnCompletionStore>();
        store.Setup(port => port.FindScopeAsync(40, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCompletionScopeReadModel(1, 30, "warehouse", "full"));
        store.Setup(port => port.CompleteAsync(
                40,
                77,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnReadModel(
                40,
                20,
                10,
                "Supplier",
                30,
                "Warehouse",
                new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                "completed",
                "Dock receipt",
                "INV-40",
                new DateTime(2026, 3, 10),
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
                        5,
                        10m,
                        8m,
                        "Received intact")
                ]));
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(port => port.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnCompletionStore>();
                services.AddScoped<IGrnCompletionStore>(_ => store.Object);
                services.RemoveAll<IGrnCompletionAuthorizer>();
                services.AddScoped<IGrnCompletionAuthorizer>(_ => authorizer.Object);
            }));

        using var deniedClient = CreateClient(app.CreateClient(), "grn.view", userId: "77");
        var denied = await deniedClient.PostAsync("/api/grns/40/complete", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var authorizedClient = CreateClient(
            app.CreateClient(),
            "grn.view,grn.receive",
            userId: "77");
        var response = await authorizedClient.PostAsync(
            "/api/grns/40/complete",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(
            "GRN completed and stock updated successfully",
            root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(40, data.GetProperty("id").GetInt64());
        Assert.Equal("GRN-000040", data.GetProperty("grnNumber").GetString());
        Assert.Equal("completed", data.GetProperty("status").GetString());
        Assert.Equal("PO-000020", data.GetProperty("poOrderNumber").GetString());
        Assert.Equal(1, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(5, data.GetProperty("totalReceivedQty").GetInt32());
        Assert.Equal(40m, data.GetProperty("totalReceivedCost").GetDecimal());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("Product", item.GetProperty("productName").GetString());
        Assert.Equal(8m, item.GetProperty("unitCost").GetDecimal());

        store.Verify(port => port.FindScopeAsync(40, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(port => port.CompleteAsync(
            40,
            77,
            1,
            It.IsAny<CancellationToken>()), Times.Once);
        authorizer.Verify(port => port.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static HttpClient CreateClient(HttpClient client, string permissions, string? userId)
    {
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "WarehouseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        return client;
    }
}
