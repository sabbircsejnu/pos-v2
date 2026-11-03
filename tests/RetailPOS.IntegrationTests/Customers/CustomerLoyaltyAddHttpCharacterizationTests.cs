using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerLoyaltyAddHttpCharacterizationTests : IClassFixture<LegacyLoyaltyEnabledApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CustomerLoyaltyAddHttpCharacterizationTests(LegacyLoyaltyEnabledApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AddLoyaltyPoints_AddsRequestedValueAndReturnsExistingEnvelope()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.PostAsync("/api/customers/1/add-points?points=5", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("5 loyalty points added", root.GetProperty("message").GetString());
        Assert.Equal(12, root.GetProperty("data").GetProperty("loyaltyPoints").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(12, (await db.Customers.SingleAsync(customer => customer.Id == 1)).LoyaltyPoints);
    }

    [Fact]
    public async Task LoyaltyCapabilities_ReportsTheLegacyMutationGateOpenWhenTheFeatureFlagIsOn()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/customers/loyalty-capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.GetProperty("data").GetProperty("legacyPointsMutationEnabled").GetBoolean());
    }

    [Fact]
    public async Task AddLoyaltyPoints_RejectsNonpositivePointsAndMissingCustomer()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var zero = await client.PostAsync("/api/customers/1/add-points?points=0", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        using var zeroDocument = await JsonDocument.ParseAsync(await zero.Content.ReadAsStreamAsync());
        Assert.Equal("Points must be greater than zero", zeroDocument.RootElement.GetProperty("message").GetString());

        using var negative = await client.PostAsync("/api/customers/1/add-points?points=-1", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        using var negativeDocument = await JsonDocument.ParseAsync(await negative.Content.ReadAsStreamAsync());
        Assert.Equal("Points must be greater than zero", negativeDocument.RootElement.GetProperty("message").GetString());

        using var missing = await client.PostAsync("/api/customers/999/add-points?points=1", content: null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingDocument = await JsonDocument.ParseAsync(await missing.Content.ReadAsStreamAsync());
        Assert.Equal("Customer with ID 999 not found", missingDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void AddLoyaltyPointsRouteAndPointsParameterRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = swagger.Paths["/api/customers/{id}/add-points"].Operations[OperationType.Post];

        Assert.Contains("points", operation.Parameters.Select(parameter => parameter.Name));
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "customers.view,customers.edit");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);
        db.Customers.Add(new Customer { Id = 1, BusinessId = 1, Name = "Loyal Customer", LoyaltyPoints = 7 });
        await db.SaveChangesAsync();
    }
}
