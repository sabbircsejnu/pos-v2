using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Customers;

/// <summary>FR-SEC-02: the legacy integer-balance endpoints are closed by default and never open to customers.view alone.</summary>
public sealed class LegacyLoyaltyEndpointGateTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public LegacyLoyaltyEndpointGateTests(RetailPosApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("add-points")]
    [InlineData("redeem-points")]
    public async Task ViewOnlyUsersCannotMutateLoyaltyPoints(string action)
    {
        await SeedAsync();
        using var client = CreateClient("customers.view");

        using var response = await client.PostAsync($"/api/customers/1/{action}?points=5", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(7, await BalanceAsync());
    }

    [Theory]
    [InlineData("add-points")]
    [InlineData("redeem-points")]
    public async Task EditorsCannotMutateLoyaltyPointsWhileTheLegacyGateIsClosed(string action)
    {
        await SeedAsync();
        using var client = CreateClient("customers.view,customers.edit");

        using var response = await client.PostAsync($"/api/customers/1/{action}?points=5", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("disabled", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, await BalanceAsync());
    }

    [Fact]
    public async Task LoyaltyCapabilities_ReportsTheLegacyMutationGateClosedByDefault()
    {
        await SeedAsync();
        using var client = CreateClient("customers.view");

        using var response = await client.GetAsync("/api/customers/loyalty-capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("data").GetProperty("legacyPointsMutationEnabled").GetBoolean());
    }

    [Fact]
    public async Task LoyaltyCapabilities_RequiresCustomerViewAccess()
    {
        await SeedAsync();
        using var client = CreateClient("sales.create");

        using var response = await client.GetAsync("/api/customers/loyalty-capabilities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);
        db.Customers.Add(new Customer { Id = 1, BusinessId = 1, Name = "Loyal Customer", LoyaltyPoints = 7 });
        await db.SaveChangesAsync();
    }

    private async Task<int> BalanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return (await db.Customers.SingleAsync(customer => customer.Id == 1)).LoyaltyPoints;
    }
}
