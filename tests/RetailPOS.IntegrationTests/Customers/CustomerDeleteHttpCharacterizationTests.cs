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

public sealed class CustomerDeleteHttpCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CustomerDeleteHttpCharacterizationTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteCustomer_PreservesNotFoundSystemGuardResponseAndSaleLinkBehavior()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var missing = await client.DeleteAsync("/api/customers/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingDocument = await JsonDocument.ParseAsync(await missing.Content.ReadAsStreamAsync());
        Assert.Equal("Customer with ID 999 not found", missingDocument.RootElement.GetProperty("message").GetString());

        using var systemCustomer = await client.DeleteAsync("/api/customers/3");
        Assert.Equal(HttpStatusCode.BadRequest, systemCustomer.StatusCode);
        using var systemDocument = await JsonDocument.ParseAsync(await systemCustomer.Content.ReadAsStreamAsync());
        Assert.Equal("System customer cannot be deleted", systemDocument.RootElement.GetProperty("message").GetString());

        using var response = await client.DeleteAsync("/api/customers/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Customer deleted successfully", document.RootElement.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Customers.AnyAsync(customer => customer.Id == 1));
        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == 101)).CustomerId);
    }

    [Fact]
    public void CustomerDeleteRouteRemainsInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains(
            OperationType.Delete,
            swagger.Paths["/api/customers/{id}"].Operations.Keys);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "customers.view,customers.delete");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);
        db.Customers.AddRange(
            new Customer
            {
                Id = 1,
                BusinessId = 1,
                Name = "Customer with sale",
                Sales = [new Sale { Id = 101, CustomerId = 1 }]
            },
            new Customer
            {
                Id = 3,
                BusinessId = 1,
                Name = "System",
                CustomerCode = Customer.WalkInCustomerCode,
                IsSystem = true
            });
        await db.SaveChangesAsync();
    }
}
