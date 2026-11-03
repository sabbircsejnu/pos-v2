using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Barcodes;

public sealed class BarcodeTemplateReadHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public BarcodeTemplateReadHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetById_PreservesTemplateFieldOrderingAndBusinessAuthorization()
    {
        await SeedAsync();
        using var client = CreateClient(1);

        var response = await client.GetAsync("/api/barcode-templates/401");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadApiData<BarcodeTemplateDto>(response);
        Assert.NotNull(result);
        Assert.Equal("Retail Label", result!.Name);
        var fields = result.Fields;
        Assert.Collection(
            fields,
            field => Assert.Equal("product_name", field.FieldKey),
            field => Assert.Equal("barcode", field.FieldKey));

        using var otherBusinessClient = CreateClient(2);
        var forbiddenResponse = await otherBusinessClient.GetAsync("/api/barcode-templates/401");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var missingResponse = await client.GetAsync("/api/barcode-templates/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task GetAll_PreservesInactiveFilterAndDefaultThenNameOrdering()
    {
        await SeedAsync();
        using var client = CreateClient(1);

        var activeResponse = await client.GetAsync("/api/barcode-templates");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeRows = await ReadApiData<List<BarcodeTemplateDto>>(activeResponse);
        Assert.NotNull(activeRows);
        Assert.Equal(401, Assert.Single(activeRows!).Id);

        var allResponse = await client.GetAsync("/api/barcode-templates?includeInactive=true");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allRows = await ReadApiData<List<BarcodeTemplateDto>>(allResponse);
        Assert.NotNull(allRows);
        Assert.Collection(
            allRows!,
            row => Assert.Equal(401, row.Id),
            row => Assert.Equal(403, row.Id));
    }

    [Fact]
    public async Task GetAll_WhenNoTemplatesExist_CreatesDefaultsAndReturnsDefaultFirst()
    {
        await ResetAndSeedBusinessAsync();
        using var client = CreateClient(1);

        var response = await client.GetAsync("/api/barcode-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await ReadApiData<List<BarcodeTemplateDto>>(response);
        Assert.NotNull(rows);
        Assert.Collection(
            rows!,
            row =>
            {
                Assert.Equal("Retail Price Label", row.Name);
                Assert.True(row.IsDefault);
                Assert.Equal("product_name", row.Fields[0].FieldKey);
                Assert.Equal(8, row.Fields.Count);
            },
            row => Assert.Equal("Detailed Variant Label", row.Name),
            row => Assert.Equal("Small Barcode Label", row.Name),
            row => Assert.Equal("Warehouse Label", row.Name));

        var repeatedResponse = await client.GetAsync("/api/barcode-templates");
        var repeatedRows = await ReadApiData<List<BarcodeTemplateDto>>(repeatedResponse);
        Assert.NotNull(repeatedRows);
        Assert.Equal(4, repeatedRows!.Count);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(4, await db.BarcodeTemplates.CountAsync(template => template.BusinessId == 1));
        Assert.Equal(32, await db.BarcodeTemplateFields.CountAsync());
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Template One" },
            new Business { Id = 2, Name = "Template Two" });
        db.BarcodeTemplates.AddRange(
            new BarcodeTemplate
            {
                Id = 401,
                BusinessId = 1,
                Name = "Retail Label",
                IsDefault = true,
                Fields =
                [
                    new BarcodeTemplateField
                    {
                        Id = 411,
                        FieldKey = "barcode",
                        SortOrder = 2
                    },
                    new BarcodeTemplateField
                    {
                        Id = 412,
                        FieldKey = "product_name",
                        SortOrder = 1
                    }
                ]
            },
            new BarcodeTemplate
            {
                Id = 402,
                BusinessId = 2,
                Name = "Other Label"
            },
            new BarcodeTemplate
            {
                Id = 403,
                BusinessId = 1,
                Name = "Inactive Label",
                IsActive = false
            });
        await db.SaveChangesAsync();
    }

    private async Task ResetAndSeedBusinessAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Template One" },
            new Business { Id = 2, Name = "Template Two" });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClient(long businessId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "barcode.view");
        return client;
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response)
        where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T>
        where T : class
    {
        public T? Data { get; set; }
    }
}
