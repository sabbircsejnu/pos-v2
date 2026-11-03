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

namespace RetailPOS.IntegrationTests.Variations;

public sealed class VariationHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task VariationAndOptionRoutes_PreserveCatalogCrudOptionsAndProductUseGuards()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            "variations.view,variations.create,variations.edit,variations.delete");

        using var listResponse = await client.GetAsync("/api/variations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        var active = list.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["Color", "Used"], active.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(["Red", "Blue"],
            active[0].GetProperty("options").EnumerateArray()
                .Select(option => option.GetProperty("name").GetString()));
        Assert.False(active[0].GetProperty("options").EnumerateArray().First()
            .GetProperty("isActive").GetBoolean());

        using var includeInactiveResponse = await client.GetAsync("/api/variations?includeInactive=true");
        using var includeInactive = await ReadDocumentAsync(includeInactiveResponse);
        Assert.Equal(3, includeInactive.RootElement.GetProperty("data").GetArrayLength());

        using var detailResponse = await client.GetAsync("/api/variations/1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var missingDetail = await client.GetAsync("/api/variations/999");
        Assert.Equal(HttpStatusCode.InternalServerError, missingDetail.StatusCode);

        using var createResponse = await client.PostAsJsonAsync("/api/variations", new
        {
            name = "Material",
            displayOrder = 4,
            isActive = true,
            autoSelectAllOptions = true,
            options = new[]
            {
                new { name = "Wool", priceAdjustment = -2.50m, displayOrder = 1, isActive = false },
                new { name = "Cotton", priceAdjustment = 1.50m, displayOrder = 2, isActive = true }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        using var created = await ReadDocumentAsync(createResponse);
        var createdData = created.RootElement.GetProperty("data");
        var createdId = createdData.GetProperty("id").GetInt64();
        Assert.Equal("Variation created successfully", created.RootElement.GetProperty("message").GetString());
        Assert.True(createdData.GetProperty("autoSelectAllOptions").GetBoolean());
        Assert.Equal(["Wool", "Cotton"],
            createdData.GetProperty("options").EnumerateArray()
                .Select(option => option.GetProperty("name").GetString()));

        using var duplicateResponse = await client.PostAsJsonAsync("/api/variations", new
        {
            name = "Color",
            displayOrder = 0,
            options = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.InternalServerError, duplicateResponse.StatusCode);

        using var updateResponse = await client.PutAsJsonAsync($"/api/variations/{createdId}", new
        {
            name = "Updated Material",
            displayOrder = 5,
            isActive = false,
            autoSelectAllOptions = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = await ReadDocumentAsync(updateResponse);
        Assert.Equal("Variation updated successfully", updated.RootElement.GetProperty("message").GetString());
        Assert.Equal("Updated Material", updated.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var blockedDelete = await client.DeleteAsync("/api/variations/3");
        Assert.Equal(HttpStatusCode.Conflict, blockedDelete.StatusCode);
        using var blockedDeleteBody = await ReadDocumentAsync(blockedDelete);
        Assert.Equal(
            "Cannot delete variation 'Used' because it is used by 1 product(s).",
            blockedDeleteBody.RootElement.GetProperty("message").GetString());
        Assert.Equal(500, blockedDeleteBody.RootElement.GetProperty("data")[0]
            .GetProperty("id").GetInt64());
        Assert.Equal("Used Product",
            blockedDeleteBody.RootElement.GetProperty("data")[0].GetProperty("name").GetString());

        using var createOptionResponse = await client.PostAsJsonAsync("/api/variations/1/options", new
        {
            name = "Green",
            priceAdjustment = 0.75m,
            displayOrder = 3,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.Created, createOptionResponse.StatusCode);
        using var createdOption = await ReadDocumentAsync(createOptionResponse);
        var optionId = createdOption.RootElement.GetProperty("data").GetProperty("id").GetInt64();
        Assert.Equal("Color", createdOption.RootElement.GetProperty("data")
            .GetProperty("variationName").GetString());

        using var updateOptionResponse = await client.PutAsJsonAsync($"/api/variations/options/{optionId}", new
        {
            name = "Green Updated",
            priceAdjustment = 1.25m,
            displayOrder = 4,
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateOptionResponse.StatusCode);
        using var updatedOption = await ReadDocumentAsync(updateOptionResponse);
        Assert.Equal("Green Updated",
            updatedOption.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var missingOptionUpdate = await client.PutAsJsonAsync("/api/variations/options/999", new
        {
            name = "Missing",
            priceAdjustment = 0,
            displayOrder = 0,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.InternalServerError, missingOptionUpdate.StatusCode);

        using var deleteOptionResponse = await client.DeleteAsync("/api/variations/options/102");
        Assert.Equal(HttpStatusCode.OK, deleteOptionResponse.StatusCode);
        using var deleteMissingOptionResponse = await client.DeleteAsync("/api/variations/options/999");
        Assert.Equal(HttpStatusCode.OK, deleteMissingOptionResponse.StatusCode);

        using var deleteVariationResponse = await client.DeleteAsync("/api/variations/2");
        Assert.Equal(HttpStatusCode.OK, deleteVariationResponse.StatusCode);
        using var missingVariationDelete = await client.DeleteAsync("/api/variations/999");
        Assert.Equal(HttpStatusCode.NotFound, missingVariationDelete.StatusCode);
        using var missingVariationBody = await ReadDocumentAsync(missingVariationDelete);
        Assert.Equal("Variation not found", missingVariationBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task VariationRoutes_PreserveActionPermissionsAndOpenApiOperations()
    {
        using var client = CreateClient("variations.view");
        using var unauthorizedCreate = await client.PostAsJsonAsync("/api/variations", new
        {
            name = "Unauthorized",
            displayOrder = 0,
            options = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedCreate.StatusCode);

        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Variations", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(OperationType.Get, paths["/api/Variations"].Operations.Keys);
        Assert.Contains(OperationType.Post, paths["/api/Variations"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Variations/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Put, paths["/api/Variations/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Delete, paths["/api/Variations/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Post, paths["/api/Variations/{variationId}/options"].Operations.Keys);
        Assert.Contains(OperationType.Put, paths["/api/Variations/options/{optionId}"].Operations.Keys);
        Assert.Contains(OperationType.Delete, paths["/api/Variations/options/{optionId}"].Operations.Keys);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var color = new Variation
        {
            Id = 1,
            Name = "Color",
            DisplayOrder = 1,
            IsActive = true,
            Options =
            [
                new VariationOption { Id = 101, Name = "Blue", DisplayOrder = 2, IsActive = true },
                new VariationOption { Id = 102, Name = "Red", DisplayOrder = 1, IsActive = false }
            ]
        };
        var inactive = new Variation { Id = 2, Name = "Inactive", DisplayOrder = 3, IsActive = false };
        var used = new Variation { Id = 3, Name = "Used", DisplayOrder = 2, IsActive = true };
        var product = new Product { Id = 500, BusinessId = 1, Name = "Used Product" };
        db.Variations.AddRange(color, inactive, used);
        db.Products.Add(product);
        db.ProductVariations.Add(new ProductVariation
        {
            Id = 600,
            Product = product,
            Variation = used
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
