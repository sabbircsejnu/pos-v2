using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Barcodes;

public sealed class BarcodeTemplateMutationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public BarcodeTemplateMutationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_PreservesNormalizationActorFieldsAndCreatedLocation()
    {
        await SeedAsync();
        using var client = CreateClient(1);
        var request = new CreateBarcodeTemplateDto
        {
            Name = "  Custom Label  ",
            TemplateType = " CUSTOM ",
            PaperType = " LABEL ",
            LabelWidthMm = 55,
            LabelHeightMm = 35,
            IsDefault = true,
            Fields =
            [
                new UpsertBarcodeTemplateFieldDto
                {
                    FieldKey = " BARCODE ",
                    SortOrder = 2,
                    FontWeight = " Bold ",
                    Align = " CENTER "
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/barcode-templates", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/barcode-templates/", response.Headers.Location!.AbsolutePath);
        var result = await ReadApiData<BarcodeTemplateDto>(response);
        Assert.NotNull(result);
        Assert.Equal("Custom Label", result!.Name);
        Assert.Equal("custom", result.TemplateType);
        Assert.Equal("label", result.PaperType);
        var field = Assert.Single(result.Fields);
        Assert.Equal("barcode", field.FieldKey);
        Assert.Equal("Bold", field.FontWeight);
        Assert.Equal("center", field.Align);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await db.BarcodeTemplates
            .Include(template => template.Fields)
            .SingleAsync(template => template.Id == result.Id);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal(7001, saved.CreatedBy);
        Assert.Equal(7001, saved.UpdatedBy);
        Assert.True(saved.IsDefault);
        Assert.False(await db.BarcodeTemplates
            .Where(template => template.Id == 10)
            .Select(template => template.IsDefault)
            .SingleAsync());
        Assert.Equal("center", Assert.Single(saved.Fields).Align);
    }

    [Fact]
    public async Task UpdateAndSetDefault_PreserveFieldReplacementAndBusinessDefault()
    {
        await SeedAsync();
        using var client = CreateClient(1);

        var updateResponse = await client.PutAsJsonAsync(
            "/api/barcode-templates/11",
            new UpdateBarcodeTemplateDto
            {
                Name = " Updated Label ",
                TemplateType = "Retail",
                PaperType = "Label",
                LabelWidthMm = 70,
                LabelHeightMm = 45,
                IsDefault = true,
                Fields =
                [
                    new UpsertBarcodeTemplateFieldDto
                    {
                        FieldKey = " product_name ",
                        SortOrder = 1
                    }
                ]
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadApiData<BarcodeTemplateDto>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal("Updated Label", updated!.Name);
        Assert.True(updated.IsDefault);
        Assert.Equal("product_name", Assert.Single(updated.Fields).FieldKey);

        var setDefaultResponse = await client.PostAsync("/api/barcode-templates/10/set-default", null);
        Assert.Equal(HttpStatusCode.OK, setDefaultResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.True(await db.BarcodeTemplates
            .Where(template => template.Id == 10)
            .Select(template => template.IsDefault)
            .SingleAsync());
        Assert.False(await db.BarcodeTemplates
            .Where(template => template.Id == 11)
            .Select(template => template.IsDefault)
            .SingleAsync());
        Assert.Equal(7001, await db.BarcodeTemplates
            .Where(template => template.Id == 10)
            .Select(template => template.UpdatedBy)
            .SingleAsync());
    }

    [Fact]
    public async Task Delete_PreservesTenantAuthorizationAndNotFoundBehavior()
    {
        await SeedAsync();
        using var client = CreateClient(1);

        var deleteResponse = await client.DeleteAsync("/api/barcode-templates/11");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var forbiddenResponse = await client.DeleteAsync("/api/barcode-templates/20");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var missingResponse = await client.DeleteAsync("/api/barcode-templates/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task CreateAndUpdate_PreserveValidationAndDuplicateNameErrors()
    {
        await SeedAsync();
        using var client = CreateClient(1);

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/barcode-templates",
            new CreateBarcodeTemplateDto { Name = " Default " });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        var invalidCreateResponse = await client.PostAsJsonAsync(
            "/api/barcode-templates",
            new CreateBarcodeTemplateDto { Name = "Invalid", LabelWidthMm = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreateResponse.StatusCode);

        var invalidUpdateResponse = await client.PutAsJsonAsync(
            "/api/barcode-templates/999",
            new UpdateBarcodeTemplateDto { Name = " ", LabelHeightMm = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResponse.StatusCode);
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
                Id = 10,
                BusinessId = 1,
                Name = "Default",
                IsDefault = true,
                Fields = [new BarcodeTemplateField { Id = 110, FieldKey = "barcode", SortOrder = 0 }]
            },
            new BarcodeTemplate
            {
                Id = 11,
                BusinessId = 1,
                Name = "Second",
                Fields = [new BarcodeTemplateField { Id = 111, FieldKey = "product_name", SortOrder = 0 }]
            },
            new BarcodeTemplate
            {
                Id = 20,
                BusinessId = 2,
                Name = "Other Business"
            });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClient(long businessId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "barcode.view,barcode.template_manage");
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
