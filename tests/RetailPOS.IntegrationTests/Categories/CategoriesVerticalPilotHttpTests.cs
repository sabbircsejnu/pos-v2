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

namespace RetailPOS.Tests.Categories;

public sealed class CategoriesVerticalPilotHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public CategoriesVerticalPilotHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAllCategories_ReturnsOrderedFlatListWithComputedFields()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view");

        using var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("count").GetInt32());
        Assert.Equal("Categories retrieved successfully", root.GetProperty("message").GetString());

        var categories = root.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["Root A", "Root B", "Child"], categories.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(1, categories[0].GetProperty("childCount").GetInt32());
        Assert.Equal(1, categories[0].GetProperty("productCount").GetInt32());
        Assert.Equal(0, categories[0].GetProperty("level").GetInt32());
        Assert.Equal(1, categories[2].GetProperty("level").GetInt32());
        Assert.Equal(0, categories[2].GetProperty("childCount").GetInt32());
        Assert.Equal("Root A", categories[2].GetProperty("parentCategoryName").GetString());
    }

    [Fact]
    public async Task GetAllCategories_WithoutViewPermission_ReturnsForbidden()
    {
        using var client = CreateClient("categories.create");

        using var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CategoryTreeDetailAndChildren_PreserveHierarchyAndComputedFields()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view");

        using var treeResponse = await client.GetAsync("/api/categories/tree");
        Assert.Equal(HttpStatusCode.OK, treeResponse.StatusCode);
        using var treeDocument = await ReadDocumentAsync(treeResponse);
        var treeRoot = treeDocument.RootElement;
        Assert.Equal(2, treeRoot.GetProperty("count").GetInt32());
        var roots = treeRoot.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["Root A", "Root B"], roots.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(1, roots[0].GetProperty("productCount").GetInt32());
        var treeChild = Assert.Single(roots[0].GetProperty("children").EnumerateArray());
        Assert.Equal("Child", treeChild.GetProperty("name").GetString());
        Assert.Equal(1, treeChild.GetProperty("level").GetInt32());

        using var detailResponse = await client.GetAsync("/api/categories/3");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailDocument = await ReadDocumentAsync(detailResponse);
        var detail = detailDocument.RootElement.GetProperty("data");
        Assert.Equal("Child", detail.GetProperty("name").GetString());
        Assert.Equal("Root A", detail.GetProperty("parentCategoryName").GetString());
        Assert.Equal(1, detail.GetProperty("level").GetInt32());
        Assert.Equal(0, detail.GetProperty("childCount").GetInt32());

        using var childrenResponse = await client.GetAsync("/api/categories/1/children");
        Assert.Equal(HttpStatusCode.OK, childrenResponse.StatusCode);
        using var childrenDocument = await ReadDocumentAsync(childrenResponse);
        Assert.Equal(1, childrenDocument.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(
            "Child",
            Assert.Single(childrenDocument.RootElement.GetProperty("data").EnumerateArray())
                .GetProperty("name").GetString());

        using var missingChildrenResponse = await client.GetAsync("/api/categories/999/children");
        Assert.Equal(HttpStatusCode.OK, missingChildrenResponse.StatusCode);
        using var missingChildrenDocument = await ReadDocumentAsync(missingChildrenResponse);
        Assert.Equal(0, missingChildrenDocument.RootElement.GetProperty("count").GetInt32());

        using var missingDetailResponse = await client.GetAsync("/api/categories/999");
        Assert.Equal(HttpStatusCode.NotFound, missingDetailResponse.StatusCode);
        using var missingDetailDocument = await ReadDocumentAsync(missingDetailResponse);
        Assert.Equal("Category with ID 999 not found", missingDetailDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void CategoryRoutesRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var collectionPath = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Categories", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Get, collectionPath.Value.Operations.Keys);
        Assert.Contains(OperationType.Post, collectionPath.Value.Operations.Keys);

        var categoryPath = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Categories/{id}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Get, categoryPath.Value.Operations.Keys);
        Assert.Contains(OperationType.Put, categoryPath.Value.Operations.Keys);
        Assert.Contains(OperationType.Delete, categoryPath.Value.Operations.Keys);

        var movePath = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Categories/{id}/move", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Put, movePath.Value.Operations.Keys);
    }

    [Fact]
    public async Task CreateCategory_PersistsRequestedValuesAndReturnsComputedFields()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new
        {
            name = "New category",
            description = "Created by pilot characterization",
            parentCategoryId = 1,
            imageUrl = "https://example.test/category.png",
            displayOrder = 7,
            isActive = false
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var root = document.RootElement;
        Assert.Equal("Category created successfully", root.GetProperty("message").GetString());
        var category = root.GetProperty("data");
        Assert.Equal("New category", category.GetProperty("name").GetString());
        Assert.Equal("Created by pilot characterization", category.GetProperty("description").GetString());
        Assert.Equal(1, category.GetProperty("parentCategoryId").GetInt64());
        Assert.Equal("Root A", category.GetProperty("parentCategoryName").GetString());
        Assert.Equal("https://example.test/category.png", category.GetProperty("imageUrl").GetString());
        Assert.Equal(7, category.GetProperty("displayOrder").GetInt32());
        Assert.False(category.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, category.GetProperty("level").GetInt32());
        Assert.Equal(0, category.GetProperty("childCount").GetInt32());
        Assert.Equal(0, category.GetProperty("productCount").GetInt32());
        Assert.NotEqual(default, category.GetProperty("createdAt").GetDateTime());
        Assert.NotEqual(default, category.GetProperty("updatedAt").GetDateTime());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await db.Categories.SingleAsync(item => item.Name == "New category");
        Assert.Equal(1, saved.ParentCategoryId);
        Assert.Equal(7, saved.DisplayOrder);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task CreateCategory_OmittedOptionalValues_UsesExistingDefaults()
    {
        using var client = CreateClient("categories.view,categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Defaulted" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var category = document.RootElement.GetProperty("data");
        Assert.Equal(0, category.GetProperty("displayOrder").GetInt32());
        Assert.True(category.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, category.GetProperty("level").GetInt32());
        Assert.Equal(0, category.GetProperty("childCount").GetInt32());
        Assert.Equal(0, category.GetProperty("productCount").GetInt32());
    }

    [Theory]
    [InlineData("", "Category name is required")]
    [InlineData("x", "Category name must be between 2 and 100 characters")]
    public async Task CreateCategory_InvalidName_ReturnsTheExistingValidationProblemShape(
        string name,
        string expectedMessage)
    {
        using var client = CreateClient("categories.view,categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var root = document.RootElement;
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", root.GetProperty("type").GetString());
        Assert.Equal("One or more validation errors occurred.", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameIsCaseInsensitiveAndReturnsBadRequest()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "root a" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        Assert.Equal(
            "Category with name 'root a' already exists",
            document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateCategory_WithMissingParent_ReturnsNotFound()
    {
        using var client = CreateClient("categories.view,categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new
        {
            name = "Orphan category",
            parentCategoryId = 999
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        Assert.Equal("Parent category with ID 999 not found", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateCategory_WithoutCreatePermission_ReturnsForbidden()
    {
        using var client = CreateClient("categories.view");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Denied" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_WithoutViewPermission_ReturnsForbidden()
    {
        using var client = CreateClient("categories.create");

        using var response = await client.PostAsJsonAsync("/api/categories", new { name = "Denied" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_PreservesValuesAndComputedResponse()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.edit");

        using var response = await client.PutAsJsonAsync("/api/categories/3", new
        {
            name = "Updated child",
            description = "Updated description",
            parentCategoryId = 2,
            imageUrl = "https://example.test/updated.png",
            displayOrder = 8,
            isActive = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var root = document.RootElement;
        Assert.Equal("Category updated successfully", root.GetProperty("message").GetString());
        var category = root.GetProperty("data");
        Assert.Equal("Updated child", category.GetProperty("name").GetString());
        Assert.Equal("Updated description", category.GetProperty("description").GetString());
        Assert.Equal(2, category.GetProperty("parentCategoryId").GetInt64());
        Assert.Equal("Root B", category.GetProperty("parentCategoryName").GetString());
        Assert.Equal(8, category.GetProperty("displayOrder").GetInt32());
        Assert.False(category.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, category.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task UpdateCategory_RejectsDuplicateNamesInvalidParentsAndMissingCategories()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.edit");

        using var duplicateName = await client.PutAsJsonAsync("/api/categories/3", new
        {
            name = "Root B",
            parentCategoryId = 1,
            displayOrder = 1,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateName.StatusCode);
        using var duplicateDocument = await ReadDocumentAsync(duplicateName);
        Assert.Equal(
            "Category with name 'Root B' already exists",
            duplicateDocument.RootElement.GetProperty("error").GetString());

        using var selfParent = await client.PutAsJsonAsync("/api/categories/3", new
        {
            name = "Child",
            parentCategoryId = 3,
            displayOrder = 2,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, selfParent.StatusCode);
        using var selfParentDocument = await ReadDocumentAsync(selfParent);
        Assert.Equal(
            "Category cannot be its own parent",
            selfParentDocument.RootElement.GetProperty("error").GetString());

        using var descendantParent = await client.PutAsJsonAsync("/api/categories/1", new
        {
            name = "Root A",
            parentCategoryId = 3,
            displayOrder = 1,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, descendantParent.StatusCode);
        using var descendantDocument = await ReadDocumentAsync(descendantParent);
        Assert.Equal(
            "Cannot set a descendant category as parent (circular reference)",
            descendantDocument.RootElement.GetProperty("error").GetString());

        using var missingCategory = await client.PutAsJsonAsync("/api/categories/999", new
        {
            name = "Missing",
            displayOrder = 1,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.NotFound, missingCategory.StatusCode);
        using var missingDocument = await ReadDocumentAsync(missingCategory);
        Assert.Equal(
            "Category with ID 999 not found",
            missingDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DeleteCategory_EnforcesChildAndProductGuardsBeforeDeletion()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.delete");

        using var hasChildren = await client.DeleteAsync("/api/categories/1");
        Assert.Equal(HttpStatusCode.BadRequest, hasChildren.StatusCode);
        using var hasChildrenDocument = await ReadDocumentAsync(hasChildren);
        Assert.Equal(
            "Cannot delete category that has child categories. Delete or move children first.",
            hasChildrenDocument.RootElement.GetProperty("error").GetString());

        using var deleteChild = await client.DeleteAsync("/api/categories/3");
        Assert.Equal(HttpStatusCode.OK, deleteChild.StatusCode);

        using var hasProducts = await client.DeleteAsync("/api/categories/1");
        Assert.Equal(HttpStatusCode.BadRequest, hasProducts.StatusCode);
        using var hasProductsDocument = await ReadDocumentAsync(hasProducts);
        Assert.Equal(
            "Cannot delete category that has products. Reassign or delete products first.",
            hasProductsDocument.RootElement.GetProperty("error").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Products.Remove(await db.Products.SingleAsync(product => product.Id == 1));
            await db.SaveChangesAsync();
        }

        using var deleteRoot = await client.DeleteAsync("/api/categories/1");
        Assert.Equal(HttpStatusCode.OK, deleteRoot.StatusCode);
        using var deleteRootDocument = await ReadDocumentAsync(deleteRoot);
        Assert.Equal("Category deleted successfully", deleteRootDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MoveCategory_RejectsDescendantsAndMovesValidChild()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("categories.view,categories.edit");

        using var circularMove = await client.PutAsJsonAsync(
            "/api/categories/1/move",
            new { newParentCategoryId = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, circularMove.StatusCode);
        using var circularDocument = await ReadDocumentAsync(circularMove);
        Assert.Equal(
            "Cannot move category to one of its descendants (circular reference)",
            circularDocument.RootElement.GetProperty("error").GetString());

        using var missingParent = await client.PutAsJsonAsync(
            "/api/categories/3/move",
            new { newParentCategoryId = 999 });
        Assert.Equal(HttpStatusCode.NotFound, missingParent.StatusCode);
        using var missingParentDocument = await ReadDocumentAsync(missingParent);
        Assert.Equal(
            "Parent category with ID 999 not found",
            missingParentDocument.RootElement.GetProperty("error").GetString());

        using var moveChild = await client.PutAsJsonAsync(
            "/api/categories/3/move",
            new { newParentCategoryId = 2, newDisplayOrder = 5 });
        Assert.Equal(HttpStatusCode.OK, moveChild.StatusCode);
        using var moveDocument = await ReadDocumentAsync(moveChild);
        var moved = moveDocument.RootElement.GetProperty("data");
        Assert.Equal(2, moved.GetProperty("parentCategoryId").GetInt64());
        Assert.Equal(5, moved.GetProperty("displayOrder").GetInt32());
        Assert.Equal(1, moved.GetProperty("level").GetInt32());
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

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Categories.AddRange(
            new Category { Id = 1, BusinessId = 1, Name = "Root A", DisplayOrder = 1 },
            new Category { Id = 2, BusinessId = 1, Name = "Root B", DisplayOrder = 1 },
            new Category { Id = 3, BusinessId = 1, Name = "Child", ParentCategoryId = 1, DisplayOrder = 2 });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "Product A", CategoryId = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
