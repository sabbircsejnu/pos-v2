using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Infrastructure.Settings;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Settings;

public sealed class SystemSettingsHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public SystemSettingsHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void SettingsStore_IsOneSingletonAndSaleNumberingUsesTheDatabaseAllocator()
    {
        var concrete = _factory.Services.GetRequiredService<JsonSystemSettingsStore>();
        Assert.Same(concrete, _factory.Services.GetRequiredService<ISystemSettingsStore>());

        using var scope = _factory.Services.CreateScope();
        Assert.IsType<RetailPOS.Infrastructure.Sales.SaleInvoiceNumberAllocator>(
            scope.ServiceProvider.GetRequiredService<ISaleInvoiceNumberAllocator>());
    }

    [Fact]
    public async Task SettingsRoutes_PreservePermissionsValidationMessagesAndOpenApi()
    {
        using var viewer = CreateClient("settings.view");
        using var editor = CreateClient("settings.edit");
        using var noPermissions = CreateClient(string.Empty);

        using var allSettings = await viewer.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, allSettings.StatusCode);
        using var companyWithoutPermission = await noPermissions.GetAsync("/api/settings/company");
        Assert.Equal(HttpStatusCode.Forbidden, companyWithoutPermission.StatusCode);
        using var formatting = await noPermissions.GetAsync("/api/settings/formatting");
        Assert.Equal(HttpStatusCode.OK, formatting.StatusCode);
        using (var body = await JsonDocument.ParseAsync(await formatting.Content.ReadAsStreamAsync()))
        {
            var data = body.RootElement.GetProperty("data");
            Assert.Equal(["dateFormat", "timeZone"], data.EnumerateObject().Select(property => property.Name).Order());
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("dateFormat").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("timeZone").GetString()));
        }
        using var currencyWithoutPermission = await noPermissions.GetAsync("/api/settings/currency");
        Assert.Equal(HttpStatusCode.OK, currencyWithoutPermission.StatusCode);

        using var invalidCurrency = await editor.PutAsJsonAsync("/api/settings/currency", new CurrencySettings
        {
            CurrencyCode = " eur ",
            CurrencySymbol = "€",
            DecimalPlaces = 9,
            SymbolPosition = "before"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrency.StatusCode);
        using (var invalidBody = await JsonDocument.ParseAsync(await invalidCurrency.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                "Decimal places must be between 0 and 4",
                invalidBody.RootElement.GetProperty("message").GetString());
        }

        using var validCurrency = await editor.PutAsJsonAsync("/api/settings/currency", new CurrencySettings
        {
            CurrencyCode = " eur ",
            CurrencySymbol = "€",
            DecimalPlaces = 2,
            SymbolPosition = " AFTER "
        });
        Assert.Equal(HttpStatusCode.OK, validCurrency.StatusCode);
        using (var validBody = await JsonDocument.ParseAsync(await validCurrency.Content.ReadAsStreamAsync()))
        {
            Assert.True(validBody.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Currency settings updated", validBody.RootElement.GetProperty("message").GetString());
            Assert.Equal("EUR", validBody.RootElement.GetProperty("data")
                .GetProperty("currency").GetProperty("currencyCode").GetString());
            Assert.Equal("EUR", validBody.RootElement.GetProperty("data")
                .GetProperty("company").GetProperty("currency").GetString());
        }

        using var inventoryDenied = await noPermissions.GetAsync("/api/settings/inventory");
        Assert.Equal(HttpStatusCode.Forbidden, inventoryDenied.StatusCode);
        using var inventoryAllowed = CreateClient("stock_adjustments.create");
        using var inventoryResponse = await inventoryAllowed.GetAsync("/api/settings/inventory");
        Assert.Equal(HttpStatusCode.OK, inventoryResponse.StatusCode);

        using var invalidInvoiceNumber = await editor.PutAsJsonAsync(
            "/api/settings/invoice-number",
            new InvoiceNumberSettings { Prefix = " ", NextNumber = 0, Padding = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidInvoiceNumber.StatusCode);
    }

    [Fact]
    public void SettingsRoutesRemainInOpenApi()
    {
        var document = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        AssertOperation(document, "/api/Settings", OperationType.Get);
        AssertOperation(document, "/api/Settings/company", OperationType.Get);
        AssertOperation(document, "/api/Settings/company", OperationType.Put);
        AssertOperation(document, "/api/Settings/formatting", OperationType.Get);
        AssertOperation(document, "/api/Settings/currency", OperationType.Get);
        AssertOperation(document, "/api/Settings/currency", OperationType.Put);
        AssertOperation(document, "/api/Settings/tax", OperationType.Get);
        AssertOperation(document, "/api/Settings/tax", OperationType.Put);
        AssertOperation(document, "/api/Settings/receipt", OperationType.Get);
        AssertOperation(document, "/api/Settings/receipt", OperationType.Put);
        AssertOperation(document, "/api/Settings/invoice-number", OperationType.Get);
        AssertOperation(document, "/api/Settings/invoice-number", OperationType.Put);
        AssertOperation(document, "/api/Settings/inventory", OperationType.Get);
        AssertOperation(document, "/api/Settings/inventory", OperationType.Put);
    }

    [Fact]
    public async Task InventoryUpdate_RequiresSettingsEditPermission()
    {
        using var noPermissions = CreateClient(string.Empty);
        using var response = await noPermissions.PutAsJsonAsync(
            "/api/settings/inventory",
            new InventorySettings { AllowNegativeStock = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static void AssertOperation(
        OpenApiDocument document,
        string path,
        OperationType operation)
    {
        var entry = document.Paths.Single(item =>
            string.Equals(item.Key, path, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(operation, entry.Value.Operations.Keys);
    }
}
