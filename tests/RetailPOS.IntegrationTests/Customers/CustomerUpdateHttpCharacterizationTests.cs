using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerUpdateHttpCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CustomerUpdateHttpCharacterizationTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UpdateCustomer_NormalizesEditableFieldsAndPreservesLoyaltyAndSales()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.PutAsJsonAsync("/api/customers/1", new
        {
            name = "  Updated Customer  ",
            phone = " 01712340101 ",
            email = " Updated@Example.Test "
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Customer updated successfully", root.GetProperty("message").GetString());
        var customer = root.GetProperty("data");
        Assert.Equal("Updated Customer", customer.GetProperty("name").GetString());
        Assert.Equal("01712340101", customer.GetProperty("phone").GetString());
        Assert.Equal("updated@example.test", customer.GetProperty("email").GetString());
        Assert.Equal(12, customer.GetProperty("loyaltyPoints").GetInt32());
        Assert.Equal(1, customer.GetProperty("totalPurchases").GetInt32());
    }

    [Fact]
    public async Task UpdateCustomer_PreservesMissingSystemAndDuplicateValidationBehavior()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var missing = await client.PutAsJsonAsync("/api/customers/999", new { name = "Missing" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingDocument = await JsonDocument.ParseAsync(await missing.Content.ReadAsStreamAsync());
        Assert.Equal("Customer with ID 999 not found", missingDocument.RootElement.GetProperty("message").GetString());

        using var systemCustomer = await client.PutAsJsonAsync("/api/customers/3", new { name = "System edit" });
        Assert.Equal(HttpStatusCode.BadRequest, systemCustomer.StatusCode);
        using var systemDocument = await JsonDocument.ParseAsync(await systemCustomer.Content.ReadAsStreamAsync());
        Assert.Equal("System customer cannot be edited", systemDocument.RootElement.GetProperty("message").GetString());

        using var duplicatePhone = await client.PutAsJsonAsync("/api/customers/1", new
        {
            name = "Updated",
            phone = "01712340102"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePhone.StatusCode);
        using var phoneDocument = await JsonDocument.ParseAsync(await duplicatePhone.Content.ReadAsStreamAsync());
        Assert.Equal(
            "A customer with phone '01712340102' already exists",
            phoneDocument.RootElement.GetProperty("message").GetString());
        Assert.Equal("CUSTOMER_PHONE_EXISTS", phoneDocument.RootElement.GetProperty("errorCode").GetString());

        using var duplicateEmail = await client.PutAsJsonAsync("/api/customers/1", new
        {
            name = "Updated",
            email = "SECOND@example.test"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateEmail.StatusCode);
        using var emailDocument = await JsonDocument.ParseAsync(await duplicateEmail.Content.ReadAsStreamAsync());
        Assert.Equal(
            "A customer with email 'SECOND@example.test' already exists",
            emailDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void CustomerUpdateRouteAndRequestBodyRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = swagger.Paths["/api/customers/{id}"].Operations[OperationType.Put];

        Assert.NotNull(operation.RequestBody);
        Assert.Contains("application/json", operation.RequestBody.Content.Keys);
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
        db.Customers.AddRange(
            new Customer
            {
                Id = 1,
                BusinessId = 1,
                Name = "First",
                Phone = "01712340101",
                PhoneNormalized = "+8801712340101",
                Email = "first@example.test",
                LoyaltyPoints = 12,
                Sales = [new Sale { Id = 101, CustomerId = 1 }]
            },
            new Customer
            {
                Id = 2,
                BusinessId = 1,
                Name = "Second",
                Phone = "01712340102",
                PhoneNormalized = "+8801712340102",
                Email = "second@example.test"
            },
            new Customer
            {
                Id = 3,
                BusinessId = 1,
                Name = "System",
                CustomerCode = Customer.WalkInCustomerCode,
                IsSystem = true,
                Phone = "01712340103",
                PhoneNormalized = "+8801712340103"
            });
        await db.SaveChangesAsync();
    }
}
