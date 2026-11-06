using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Authorization;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Security;

/// <summary>
/// Product Create/Edit and POS Checkout reference-data lookups: a user authorized for the workflow can read
/// the category list and the Business tax settings it needs, without gaining Category or Settings Management,
/// and the data stays confined to the caller's own Business.
/// </summary>
public sealed class ProductFormLookupAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long SuperAdminUserId = 9701;
    private readonly RetailPosApiFactory _factory;

    public ProductFormLookupAuthorizationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> ProductFormPermissions() =>
    [
        ["products.create"],
        ["products.edit"]
    ];

    [Theory]
    [MemberData(nameof(ProductFormPermissions))]
    public async Task ProductCreateOrEditUser_LoadsOwnBusinessCategoriesAndTaxSettings(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient($"products.view,{permission}");

        Assert.Equal(new long[] { 9411, 9412, 9413 }, await CategoryIdsAsync(client));

        using var tax = await client.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.OK, tax.StatusCode);
        using var taxDocument = JsonDocument.Parse(await tax.Content.ReadAsStringAsync());
        Assert.True(taxDocument.RootElement.GetProperty("data").TryGetProperty("defaultTaxRate", out _));
    }

    [Fact]
    public async Task ProductCreateOrEditUser_GainsNoCategoryOrSettingsManagement()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("products.view,products.create,products.edit");

        foreach (var url in new[] { "/api/categories/tree", "/api/categories/9411", "/api/categories/9411/children", "/api/settings", "/api/settings/company" })
        {
            using var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} returned {(int)response.StatusCode}");
        }

        using var createCategory = await client.PostAsJsonAsync("/api/categories", new { name = "New" });
        Assert.Equal(HttpStatusCode.Forbidden, createCategory.StatusCode);
        using var editCategory = await client.PutAsJsonAsync("/api/categories/9411", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Forbidden, editCategory.StatusCode);
        using var deleteCategory = await client.DeleteAsync("/api/categories/9411");
        Assert.Equal(HttpStatusCode.Forbidden, deleteCategory.StatusCode);
        using var updateTax = await client.PutAsJsonAsync("/api/settings/tax", new { taxEnabled = true, defaultTaxRate = 99m });
        Assert.Equal(HttpStatusCode.Forbidden, updateTax.StatusCode);
    }

    [Fact]
    public async Task PosCashier_ReadsTaxSettingsButNotTheManagementCategoryListOrTaxUpdate()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient("sales.create,sales.view,cash_sessions.view,cash_sessions.open,customers.view");

        using var tax = await cashier.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.OK, tax.StatusCode);
        // POS Checkout reads categories through /api/pos/categories (sales.create).
        using var posCategories = await cashier.GetAsync("/api/pos/categories");
        Assert.Equal(HttpStatusCode.OK, posCategories.StatusCode);
        using var managementCategories = await cashier.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Forbidden, managementCategories.StatusCode);
        using var updateTax = await cashier.PutAsJsonAsync("/api/settings/tax", new { taxEnabled = true, defaultTaxRate = 99m });
        Assert.Equal(HttpStatusCode.Forbidden, updateTax.StatusCode);
    }

    [Fact]
    public async Task UserWithoutWorkflowPermission_IsDeniedBothLookups()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("products.view,customers.view,inventory.view");

        using var categories = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Forbidden, categories.StatusCode);
        Assert.DoesNotContain("Homeware", await categories.Content.ReadAsStringAsync());
        using var tax = await client.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.Forbidden, tax.StatusCode);
    }

    [Fact]
    public async Task ProductUser_CannotReadAnotherBusinessThroughTheBusinessHeader()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("products.view,products.create,products.edit");
        client.DefaultRequestHeaders.Add("X-Business-Id", "2");

        using var categories = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Forbidden, categories.StatusCode);
        Assert.DoesNotContain("Foreign category", await categories.Content.ReadAsStringAsync());
        using var tax = await client.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.Forbidden, tax.StatusCode);
    }

    [Fact]
    public async Task BusinessOwnerWildcard_KeepsFullAccessIncludingTaxUpdate()
    {
        await ResetAndSeedAsync();
        using var owner = CreateClient("*");

        Assert.Equal(new long[] { 9411, 9412, 9413 }, await CategoryIdsAsync(owner));
        using var tree = await owner.GetAsync("/api/categories/tree");
        Assert.Equal(HttpStatusCode.OK, tree.StatusCode);
        using var tax = await owner.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.OK, tax.StatusCode);
        using var updateTax = await owner.PutAsJsonAsync(
            "/api/settings/tax", new { taxEnabled = true, defaultTaxRate = 5m, taxName = "VAT", pricesIncludeTax = false });
        Assert.Equal(HttpStatusCode.OK, updateTax.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_ReadsOnlyTheSelectedBusinessAndNeedsABusinessContext()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            await TestSuperAdmin.SeedAsync(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(), SuperAdminUserId);
        }

        using var selected = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, 1);
        Assert.Equal(new long[] { 9411, 9412, 9413 }, await CategoryIdsAsync(selected));
        using var tax = await selected.GetAsync("/api/settings/tax");
        Assert.Equal(HttpStatusCode.OK, tax.StatusCode);

        using var withoutBusiness = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, null);
        using var noBusinessCategories = await withoutBusiness.GetAsync("/api/categories");
        Assert.NotEqual(HttpStatusCode.OK, noBusinessCategories.StatusCode);
        Assert.DoesNotContain("Homeware", await noBusinessCategories.Content.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> TaxLookupPermissions() =>
        LookupAuthorizationPolicies.PermissionsFor(LookupAuthorizationPolicies.TaxSettingsRead)
            .Select(permission => new object[] { permission });

    [Theory]
    [MemberData(nameof(TaxLookupPermissions))]
    public async Task TaxSettingsRead_IsOpenToEveryListedWorkflowPermission(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync("/api/settings/tax");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static IEnumerable<object[]> CategoryLookupPermissions() =>
        LookupAuthorizationPolicies.PermissionsFor(LookupAuthorizationPolicies.CategoryList)
            .Select(permission => new object[] { permission });

    [Theory]
    [MemberData(nameof(CategoryLookupPermissions))]
    public async Task CategoryList_IsOpenToEveryListedWorkflowPermission(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        Assert.Equal(new long[] { 9411, 9412, 9413 }, await CategoryIdsAsync(client));
    }

    private static async Task<long[]> CategoryIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(category => category.GetProperty("id").GetInt64())
            .OrderBy(id => id)
            .ToArray();
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7401");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Product Manager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Product form business", IsActive = true },
            new Business { Id = 2, Name = "Foreign product form business", IsActive = true });
        db.Categories.AddRange(
            new Category { Id = 9411, BusinessId = 1, Name = "Beverages", DisplayOrder = 1 },
            new Category { Id = 9412, BusinessId = 1, Name = "Homeware", DisplayOrder = 2 },
            new Category { Id = 9413, BusinessId = 1, Name = "Retired category", DisplayOrder = 3, IsActive = false },
            new Category { Id = 9491, BusinessId = 2, Name = "Foreign category" });
        db.Users.Add(new User
        {
            Id = 7401,
            BusinessId = 1,
            Name = "Product manager",
            Email = "product-manager@example.com",
            PasswordHash = "x",
            IsActive = true
        });

        await db.SaveChangesAsync();
    }
}
