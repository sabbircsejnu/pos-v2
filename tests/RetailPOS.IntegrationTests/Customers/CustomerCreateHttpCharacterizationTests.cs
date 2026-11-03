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

public sealed class CustomerCreateHttpCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CustomerCreateHttpCharacterizationTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateCustomer_NormalizesInputAndReturnsCreatedLocationAndEnvelope()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "  Ada Lovelace  ",
            phone = " 01712-340142 ",
            email = " Ada@Example.Test "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri("http://localhost/api/customers/11"), response.Headers.Location);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Customer created successfully", root.GetProperty("message").GetString());
        var customer = root.GetProperty("data");
        Assert.Equal("Ada Lovelace", customer.GetProperty("name").GetString());
        Assert.Equal("01712-340142", customer.GetProperty("phone").GetString());
        Assert.Equal("ada@example.test", customer.GetProperty("email").GetString());
        Assert.Equal(0, customer.GetProperty("loyaltyPoints").GetInt32());
        Assert.Equal(0, customer.GetProperty("totalPurchases").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await db.Customers.SingleAsync(customer => customer.Id == 11);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsSystem);
        Assert.Null(saved.CustomerCode);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal("+8801712340142", saved.PhoneNormalized);
    }

    [Fact]
    public async Task CreateCustomer_RejectsMissingNameAndDuplicatePhoneOrEmail()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var missingName = await client.PostAsJsonAsync("/api/customers", new { name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
        using var missingNameDocument = await JsonDocument.ParseAsync(await missingName.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Name is required",
            missingNameDocument.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString());

        using var duplicatePhone = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Phone Duplicate",
            phone = "01712345678"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePhone.StatusCode);
        using var duplicatePhoneDocument = await JsonDocument.ParseAsync(await duplicatePhone.Content.ReadAsStreamAsync());
        Assert.Equal(
            "A customer with phone '01712345678' already exists",
            duplicatePhoneDocument.RootElement.GetProperty("message").GetString());
        Assert.Equal("CUSTOMER_PHONE_EXISTS", duplicatePhoneDocument.RootElement.GetProperty("errorCode").GetString());

        using var duplicateEmail = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Email Duplicate",
            email = "KNOWN@example.test"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateEmail.StatusCode);
        using var duplicateEmailDocument = await JsonDocument.ParseAsync(await duplicateEmail.Content.ReadAsStreamAsync());
        Assert.Equal(
            "A customer with email 'KNOWN@example.test' already exists",
            duplicateEmailDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void CustomerCreateRouteAndRequestBodyRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = swagger.Paths["/api/customers"].Operations[OperationType.Post];

        Assert.NotNull(operation.RequestBody);
        Assert.Contains("application/json", operation.RequestBody.Content.Keys);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "customers.view,customers.create");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);
        db.Customers.Add(new Customer
        {
            Id = 10,
            BusinessId = 1,
            Name = "Existing Customer",
            Phone = "01712345678",
            PhoneNormalized = "+8801712345678",
            Email = "known@example.test"
        });
        await db.SaveChangesAsync();
    }
}
