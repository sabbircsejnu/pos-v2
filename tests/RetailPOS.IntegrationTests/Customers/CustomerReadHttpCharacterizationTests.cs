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

public sealed class CustomerReadHttpCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CustomerReadHttpCharacterizationTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ListCustomers_PreservesActiveFilterSearchPagingOrderingAndEnvelope()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/customers?query=match&pageNumber=2&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Operation successful", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(11, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        var customers = data.GetProperty("customers").EnumerateArray().ToArray();
        Assert.Equal(["Match 9", "Match 8"], customers.Select(customer => customer.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task QuickSearch_WhitespaceQueryIsRejectedAndMatchingQueryIsNameOrderedAndLimitedToTen()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var blankResponse = await client.GetAsync("/api/customers/search?q=%20%20");
        Assert.Equal(HttpStatusCode.BadRequest, blankResponse.StatusCode);

        using var searchResponse = await client.GetAsync("/api/customers/search?q=MATCH");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var searchDocument = await JsonDocument.ParseAsync(await searchResponse.Content.ReadAsStreamAsync());
        var customers = searchDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(10, customers.Length);
        Assert.Equal(
            ["Match 1", "Match 10", "Match 11", "Match 2", "Match 3", "Match 4", "Match 5", "Match 6", "Match 7", "Match 8"],
            customers.Select(customer => customer.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task CustomerDetail_IncludesSalesCountAndMissingCustomerUsesNotFoundEnvelope()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.GetAsync("/api/customers/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var customer = document.RootElement.GetProperty("data");
        Assert.Equal("Match 1", customer.GetProperty("name").GetString());
        Assert.Equal(2, customer.GetProperty("totalPurchases").GetInt32());

        using var missingResponse = await client.GetAsync("/api/customers/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = await JsonDocument.ParseAsync(await missingResponse.Content.ReadAsStreamAsync());
        Assert.False(missingDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Customer with ID 999 not found", missingDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void CustomerReadRoutesAndQueryParametersRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains(OperationType.Get, swagger.Paths["/api/customers"].Operations.Keys);
        Assert.Contains(OperationType.Get, swagger.Paths["/api/customers/search"].Operations.Keys);
        Assert.Contains(OperationType.Get, swagger.Paths["/api/customers/{id}"].Operations.Keys);
        var listParameters = swagger.Paths["/api/customers"].Operations[OperationType.Get]
            .Parameters.Select(parameter => parameter.Name).ToArray();
        Assert.Contains("Query", listParameters);
        Assert.Contains("PageNumber", listParameters);
        Assert.Contains("PageSize", listParameters);
        Assert.Contains(
            "q",
            swagger.Paths["/api/customers/search"].Operations[OperationType.Get]
                .Parameters.Select(parameter => parameter.Name));
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "customers.view");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);

        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Customers.AddRange(Enumerable.Range(1, 11).Select(index => new Customer
        {
            Id = index,
            BusinessId = 1,
            Name = $"Match {index}",
            Phone = $"555-010{index:D2}",
            Email = $"match{index}@example.test",
            CreatedAt = createdAt.AddDays(index),
            IsActive = true,
            Sales = index == 1
                ? [new Sale { Id = 101, CustomerId = 1 }, new Sale { Id = 102, CustomerId = 1 }]
                : []
        }));
        db.Customers.Add(new Customer
        {
            Id = 12,
            BusinessId = 1,
            Name = "Match inactive",
            CreatedAt = createdAt.AddDays(12),
            IsActive = false
        });
        await db.SaveChangesAsync();
    }
}
