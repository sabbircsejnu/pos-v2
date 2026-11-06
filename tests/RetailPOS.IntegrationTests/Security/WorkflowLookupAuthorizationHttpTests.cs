using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Security;

/// <summary>
/// Workflow reference-data lookups: POS Checkout reads the inventory settings and a minimal customer picker with
/// sales.create; Product Edit reads its own product record, images, variant tab and the variation catalogue with
/// products.edit. None of these grant the corresponding management permission or cross the Business boundary.
/// </summary>
public sealed class WorkflowLookupAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const string Cashier = "sales.create,sales.view,cash_sessions.view,cash_sessions.open";
    private const long SuperAdminUserId = 9702;
    private readonly RetailPosApiFactory _factory;

    public WorkflowLookupAuthorizationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    // ── Inventory settings (POS low-stock threshold) ─────────────────────────

    [Fact]
    public async Task Cashier_ReadsInventorySettingsButCannotUpdateThem()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        using var read = await cashier.GetAsync("/api/settings/inventory");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("data").TryGetProperty("lowStockThreshold", out _));

        using var update = await cashier.PutAsJsonAsync("/api/settings/inventory", new { lowStockThreshold = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Theory]
    [InlineData("settings.view")]
    [InlineData("inventory.view")]
    [InlineData("stock_adjustments.view")]
    [InlineData("stock_adjustments.create")]
    public async Task InventorySettings_StillOpenToThePreviouslyAllowedPermissions(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync("/api/settings/inventory");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InventorySettings_DeniedWithoutAWorkflowPermission()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("customers.view,products.view");

        using var response = await client.GetAsync("/api/settings/inventory");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Variation catalogue (Product Edit variant section) ───────────────────

    [Fact]
    public async Task ProductEditor_ReadsVariationsButCannotManageThem()
    {
        await ResetAndSeedAsync();
        using var editor = CreateClient("products.edit");

        using var list = await editor.GetAsync("/api/variations");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("Size", await list.Content.ReadAsStringAsync());
        using var single = await editor.GetAsync("/api/variations/9601");
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);

        using var create = await editor.PostAsJsonAsync("/api/variations", new { name = "Material" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var edit = await editor.PutAsJsonAsync("/api/variations/9601", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        using var delete = await editor.DeleteAsync("/api/variations/9601");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Variations_DeniedToCashiersAndCreateOnlyUsers()
    {
        await ResetAndSeedAsync();
        foreach (var permissions in new[] { Cashier, "products.view,products.create" })
        {
            using var client = CreateClient(permissions);
            using var response = await client.GetAsync("/api/variations");
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{permissions} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task VariationManager_StillNeedsViewPlusTheSpecificManagementPermission()
    {
        await ResetAndSeedAsync();
        using var createOnly = CreateClient("variations.create");
        using var create = await createOnly.PostAsJsonAsync("/api/variations", new { name = "Material" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using var viewer = CreateClient("variations.view");
        using var list = await viewer.GetAsync("/api/variations");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ── Product record (edit by ID) ──────────────────────────────────────────

    [Fact]
    public async Task EditOnlyUser_LoadsAndSavesOwnProductWithCostRedactedButGainsNoCatalogueAccess()
    {
        await ResetAndSeedAsync();
        using var editor = CreateClient("products.edit");

        using var read = await editor.GetAsync("/api/products/9611");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using (var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("Workflow shirt", data.GetProperty("name").GetString());
            Assert.True(
                !data.TryGetProperty("costPrice", out var cost) || cost.ValueKind == JsonValueKind.Null,
                "Cost must stay redacted without products.view_cost.");
        }

        foreach (var url in new[] { "/api/products/9611/images", "/api/products/9611/variations", "/api/products/9611/variations/combinations" })
        {
            using var response = await editor.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}");
        }

        using var sku = await editor.PostAsJsonAsync("/api/products/generate-sku", "Workflow shirt");
        Assert.Equal(HttpStatusCode.OK, sku.StatusCode);

        using var save = await editor.PutAsJsonAsync("/api/products/9611", new { name = "Workflow shirt v2", categoryId = 9621, basePrice = 15m });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        foreach (var url in new[] { "/api/products", "/api/products/sku/WF-SHIRT", "/api/products/barcode/WF-BC", "/api/products/category/9621" })
        {
            using var response = await editor.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} returned {(int)response.StatusCode}");
        }

        using var delete = await editor.DeleteAsync("/api/products/9611");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        using var create = await editor.PostAsJsonAsync("/api/products", new { name = "New", categoryId = 9621, basePrice = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task EditOnlyUser_CannotReadAnotherBusinessProduct()
    {
        await ResetAndSeedAsync();
        using var editor = CreateClient("products.edit");

        using var foreign = await editor.GetAsync("/api/products/9691");
        Assert.NotEqual(HttpStatusCode.OK, foreign.StatusCode);
        Assert.DoesNotContain("Foreign product", await foreign.Content.ReadAsStringAsync());

        using var foreignSave = await editor.PutAsJsonAsync("/api/products/9691", new { name = "Hijacked", categoryId = 9621, basePrice = 1m });
        Assert.NotEqual(HttpStatusCode.OK, foreignSave.StatusCode);

        using var otherBusinessHeader = CreateClient("products.edit");
        otherBusinessHeader.DefaultRequestHeaders.Add("X-Business-Id", "2");
        using var headerRead = await otherBusinessHeader.GetAsync("/api/products/9691");
        Assert.Equal(HttpStatusCode.Forbidden, headerRead.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal("Foreign product", db.Products.Single(product => product.Id == 9691).Name);
    }

    [Fact]
    public async Task CreateOnlyUser_CreatesAndGeneratesCodesButCannotReadOrEditRecords()
    {
        await ResetAndSeedAsync();
        using var creator = CreateClient("products.create");

        using var sku = await creator.PostAsJsonAsync("/api/products/generate-sku", "New mug");
        Assert.Equal(HttpStatusCode.OK, sku.StatusCode);
        using var create = await creator.PostAsJsonAsync("/api/products", new { name = "New mug", categoryId = 9621, basePrice = 4m });
        Assert.NotEqual(HttpStatusCode.Forbidden, create.StatusCode);

        using var read = await creator.GetAsync("/api/products/9611");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using var save = await creator.PutAsJsonAsync("/api/products/9611", new { name = "Renamed", categoryId = 9621, basePrice = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [Fact]
    public async Task ProductRecordAndCodes_DeniedWithoutProductPermissions()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        foreach (var url in new[] { "/api/products/9611", "/api/products/9611/images", "/api/products/9611/variations" })
        {
            using var response = await cashier.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} returned {(int)response.StatusCode}");
        }

        using var sku = await cashier.PostAsJsonAsync("/api/products/generate-sku", "x");
        Assert.Equal(HttpStatusCode.Forbidden, sku.StatusCode);
    }

    [Fact]
    public async Task ViewOnlyUser_KeepsReadAccessButStillCannotEdit()
    {
        await ResetAndSeedAsync();
        using var viewer = CreateClient("products.view");

        using var read = await viewer.GetAsync("/api/products/9611");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var list = await viewer.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var save = await viewer.PutAsJsonAsync("/api/products/9611", new { name = "Renamed", categoryId = 9621, basePrice = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        using var variationTabWrite = await viewer.PostAsJsonAsync("/api/products/9611/variations/combinations/generate-all", new { });
        Assert.Equal(HttpStatusCode.Forbidden, variationTabWrite.StatusCode);
    }

    // ── POS customer picker ──────────────────────────────────────────────────

    [Fact]
    public async Task Cashier_SearchesOwnBusinessActiveCustomersWithMinimalFields()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        using var response = await cashier.GetAsync("/api/pos/customers/search?q=ayesha");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var customer = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(9631, customer.GetProperty("id").GetInt64());
        Assert.Equal("Ayesha Rahman", customer.GetProperty("name").GetString());
        Assert.Equal("01711000001", customer.GetProperty("phone").GetString());

        Assert.Equal(
            new[] { "id", "name", "phone" },
            customer.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.DoesNotContain("ayesha@example.com", body);
        Assert.DoesNotContain("Inactive Ayesha", body);
        Assert.DoesNotContain("Foreign Ayesha", body);

        using var blank = await cashier.GetAsync("/api/pos/customers/search?q=");
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        using var blankDocument = JsonDocument.Parse(await blank.Content.ReadAsStringAsync());
        Assert.Empty(blankDocument.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Cashier_StillCannotUseCustomerManagement()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        foreach (var url in new[] { "/api/customers", "/api/customers/search?q=ayesha", "/api/customers/9631" })
        {
            using var response = await cashier.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task PosCustomerSearch_RequiresSalesCreateAndTheCallersBusiness()
    {
        await ResetAndSeedAsync();
        using var customerViewer = CreateClient("customers.view");
        using var denied = await customerViewer.GetAsync("/api/pos/customers/search?q=ayesha");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var otherBusiness = CreateClient(Cashier);
        otherBusiness.DefaultRequestHeaders.Add("X-Business-Id", "2");
        using var crossBusiness = await otherBusiness.GetAsync("/api/pos/customers/search?q=ayesha");
        Assert.Equal(HttpStatusCode.Forbidden, crossBusiness.StatusCode);
        Assert.DoesNotContain("Foreign Ayesha", await crossBusiness.Content.ReadAsStringAsync());
    }

    // ── Product images: products.edit is mandatory ───────────────────────────

    private static MultipartFormDataContent ImageForm(string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "File", "not-really.png");
        return form;
    }

    [Fact]
    public async Task ImageUpload_IsAuthorizedWithProductsEdit()
    {
        await ResetAndSeedAsync();
        using var editor = CreateClient("products.edit");

        // Authorization passes and the request reaches the upload use case's file-type validation (no file is stored).
        using var upload = await editor.PostAsync("/api/products/9611/images", ImageForm("image/gif"));
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("Only JPG, PNG, and WEBP", await upload.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ImageChanges_AreDeniedToCreateOnlyAndViewOnlyUsers()
    {
        await ResetAndSeedAsync();
        foreach (var permissions in new[] { "products.create", "products.view,products.create" })
        {
            using var client = CreateClient(permissions);
            using var upload = await client.PostAsync("/api/products/9611/images", ImageForm());
            Assert.True(upload.StatusCode == HttpStatusCode.Forbidden, $"upload with {permissions} returned {(int)upload.StatusCode}");
            using var primary = await client.PutAsync("/api/products/9611/images/1/primary", null);
            Assert.True(primary.StatusCode == HttpStatusCode.Forbidden, $"primary with {permissions} returned {(int)primary.StatusCode}");
            using var delete = await client.DeleteAsync("/api/products/9611/images/1");
            Assert.True(delete.StatusCode == HttpStatusCode.Forbidden, $"delete with {permissions} returned {(int)delete.StatusCode}");
        }
    }

    // ── POS customer search: name and phone only ─────────────────────────────

    [Fact]
    public async Task PosCustomerSearch_MatchesNameAndPhoneButNotEmail()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        Assert.Equal(new long[] { 9631 }, await PosCustomerIdsAsync(cashier, "Rahman"));
        Assert.Equal(new long[] { 9631 }, await PosCustomerIdsAsync(cashier, "01711000001"));
        Assert.Equal(new long[] { 9631 }, await PosCustomerIdsAsync(cashier, "+880 1711-000001"));
        Assert.Empty(await PosCustomerIdsAsync(cashier, "ayesha@example"));
        Assert.Empty(await PosCustomerIdsAsync(cashier, "example.com"));

        // Customer Management search is unchanged: it still matches email for customers.view.
        using var manager = CreateClient("customers.view");
        using var management = await manager.GetAsync("/api/customers/search?q=ayesha@example");
        Assert.Equal(HttpStatusCode.OK, management.StatusCode);
        Assert.Contains("Ayesha Rahman", await management.Content.ReadAsStringAsync());
    }

    private static async Task<long[]> PosCustomerIdsAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/pos/customers/search?q={Uri.EscapeDataString(query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()).ToArray();
    }

    // ── POS Quick Customer ───────────────────────────────────────────────────

    [Fact]
    public async Task Cashier_CreatesQuickCustomerInOwnBusinessWithMinimalResponseAndNoLoyalty()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        // A client-supplied businessId/email/loyalty is not part of the contract and is ignored.
        using var response = await cashier.PostAsJsonAsync("/api/pos/customers", new
        {
            name = "  Rahim Uddin ",
            phone = "01911000009",
            businessId = 2,
            email = "rahim@example.com",
            loyaltyPoints = 500
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(new[] { "id", "name", "phone" }, data.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal("Rahim Uddin", data.GetProperty("name").GetString());
        Assert.Equal("01911000009", data.GetProperty("phone").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var row = db.Customers.Single(customer => customer.Id == data.GetProperty("id").GetInt64());
        Assert.Equal(1, row.BusinessId);
        Assert.Equal("+8801911000009", row.PhoneNormalized);
        Assert.Null(row.Email);
        Assert.Equal(0, row.LoyaltyPoints);
        Assert.True(row.IsActive);
        Assert.False(row.IsSystem);
    }

    [Fact]
    public async Task QuickCustomer_RequiresSalesCreateNotCustomerManagement()
    {
        await ResetAndSeedAsync();
        foreach (var permissions in new[] { "customers.create", "customers.view,customers.create,customers.edit", "products.edit" })
        {
            using var client = CreateClient(permissions);
            using var response = await client.PostAsJsonAsync("/api/pos/customers", new { name = "Denied", phone = "01911000010" });
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{permissions} returned {(int)response.StatusCode}");
        }

        // The cashier still has no Customer Management create or edit.
        using var cashier = CreateClient(Cashier);
        using var manage = await cashier.PostAsJsonAsync("/api/customers", new { name = "Managed", phone = "01911000011" });
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
        using var edit = await cashier.PutAsJsonAsync("/api/customers/9631", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        // Customer Management create is unchanged for customers.create.
        using var manager = CreateClient("customers.view,customers.create");
        using var managed = await manager.PostAsJsonAsync("/api/customers", new { name = "Managed", phone = "01911000012" });
        Assert.Equal(HttpStatusCode.Created, managed.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(db.Customers.Any(customer => customer.Name == "Denied"));
    }

    [Theory]
    [InlineData("", "01911000013")]
    [InlineData("Rahim", "")]
    [InlineData("Rahim", "12")]
    [InlineData("Rahim", "01211000013")]
    public async Task QuickCustomer_RejectsMissingOrInvalidNameOrPhone(string name, string phone)
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        using var response = await cashier.PostAsJsonAsync("/api/pos/customers", new { name, phone });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(3, scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().Customers.Count());
    }

    [Fact]
    public async Task QuickCustomer_DuplicatePhoneInBusinessReturnsTheExistingCustomerSafely()
    {
        await ResetAndSeedAsync();
        using var cashier = CreateClient(Cashier);

        using var response = await cashier.PostAsJsonAsync("/api/pos/customers", new { name = "Second Ayesha", phone = "+880 1711-000001" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("CUSTOMER_PHONE_EXISTS", document.RootElement.GetProperty("errorCode").GetString());
        var existing = document.RootElement.GetProperty("data");
        Assert.Equal(9631, existing.GetProperty("id").GetInt64());
        Assert.Equal("Ayesha Rahman", existing.GetProperty("name").GetString());
        Assert.DoesNotContain("ayesha@example.com", body);
        Assert.DoesNotContain("loyalty", body, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(db.Customers.Any(customer => customer.Name == "Second Ayesha"));
    }

    [Fact]
    public async Task QuickCustomer_IsIsolatedPerBusiness()
    {
        await ResetAndSeedAsync();

        // Business 2 owns 01711000003; Business 1 may register the same number for its own customer.
        using var cashier = CreateClient(Cashier);
        using var created = await cashier.PostAsJsonAsync("/api/pos/customers", new { name = "Local Owner", phone = "01711000003" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.DoesNotContain("Foreign Ayesha", await created.Content.ReadAsStringAsync());

        // Selecting another Business through the header is refused for a Business user.
        using var otherBusiness = CreateClient(Cashier);
        otherBusiness.DefaultRequestHeaders.Add("X-Business-Id", "2");
        using var crossBusiness = await otherBusiness.PostAsJsonAsync("/api/pos/customers", new { name = "Cross", phone = "01911000014" });
        Assert.Equal(HttpStatusCode.Forbidden, crossBusiness.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, db.Customers.Single(customer => customer.Name == "Local Owner").BusinessId);
        Assert.False(db.Customers.Any(customer => customer.Name == "Cross"));
        Assert.Equal("Foreign Ayesha", db.Customers.Single(customer => customer.Id == 9691).Name);
    }

    [Fact]
    public async Task SuperAdmin_QuickCustomerLandsInTheSelectedBusinessOnly()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            await TestSuperAdmin.SeedAsync(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(), SuperAdminUserId);
        }

        using var withoutBusiness = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, null);
        using var noBusiness = await withoutBusiness.PostAsJsonAsync("/api/pos/customers", new { name = "Nowhere", phone = "01911000015" });
        Assert.NotEqual(HttpStatusCode.Created, noBusiness.StatusCode);

        using var selected = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, 2);
        using var created = await selected.PostAsJsonAsync("/api/pos/customers", new { name = "Admin Added", phone = "01911000016" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(2, db.Customers.Single(customer => customer.Name == "Admin Added").BusinessId);
        Assert.False(db.Customers.Any(customer => customer.Name == "Nowhere"));
    }

    // ── Wildcard and Super Admin ─────────────────────────────────────────────

    [Fact]
    public async Task BusinessOwnerWildcard_KeepsFullAccess()
    {
        await ResetAndSeedAsync();
        using var owner = CreateClient("*");

        foreach (var url in new[]
                 {
                     "/api/settings/inventory", "/api/variations", "/api/products", "/api/products/9611",
                     "/api/products/9611/images", "/api/pos/customers/search?q=ayesha", "/api/customers/search?q=ayesha"
                 })
        {
            using var response = await owner.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}");
        }

        using var create = await owner.PostAsJsonAsync("/api/variations", new { name = "Material" });
        Assert.NotEqual(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_SeesOnlyTheSelectedBusiness()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            await TestSuperAdmin.SeedAsync(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(), SuperAdminUserId);
        }

        using var selected = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, 1);
        using var own = await selected.GetAsync("/api/products/9611");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var foreign = await selected.GetAsync("/api/products/9691");
        Assert.NotEqual(HttpStatusCode.OK, foreign.StatusCode);

        using var customers = await selected.GetAsync("/api/pos/customers/search?q=ayesha");
        Assert.Equal(HttpStatusCode.OK, customers.StatusCode);
        var body = await customers.Content.ReadAsStringAsync();
        Assert.Contains("Ayesha Rahman", body);
        Assert.DoesNotContain("Foreign Ayesha", body);

        using var withoutBusiness = TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, null);
        using var noBusiness = await withoutBusiness.GetAsync("/api/pos/customers/search?q=ayesha");
        Assert.NotEqual(HttpStatusCode.OK, noBusiness.StatusCode);
        Assert.DoesNotContain("Ayesha", await noBusiness.Content.ReadAsStringAsync());
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7601");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Workflow User");
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
            new Business { Id = 1, Name = "Workflow business", IsActive = true },
            new Business { Id = 2, Name = "Foreign workflow business", IsActive = true });
        db.Categories.AddRange(
            new Category { Id = 9621, BusinessId = 1, Name = "Apparel" },
            new Category { Id = 9692, BusinessId = 2, Name = "Foreign category" });
        db.Users.Add(new User
        {
            Id = 7601,
            BusinessId = 1,
            Name = "Workflow user",
            Email = "workflow-user@example.com",
            PasswordHash = "x",
            IsActive = true
        });
        db.Products.AddRange(
            new Product { Id = 9611, BusinessId = 1, Name = "Workflow shirt", Sku = "WF-SHIRT", Barcode = "WF-BC", CategoryId = 9621, BasePrice = 10m, CostPrice = 4m, Status = ProductStatus.Active },
            new Product { Id = 9691, BusinessId = 2, Name = "Foreign product", CategoryId = 9692, BasePrice = 99m, CostPrice = 50m, Status = ProductStatus.Active });
        db.Variations.Add(new Variation
        {
            Id = 9601,
            Name = "Size",
            DisplayOrder = 1,
            Options = [new VariationOption { Id = 9602, VariationId = 9601, Name = "M", DisplayOrder = 1 }]
        });
        db.Customers.AddRange(
            new Customer { Id = 9631, BusinessId = 1, Name = "Ayesha Rahman", Phone = "01711000001", PhoneNormalized = "+8801711000001", Email = "ayesha@example.com", LoyaltyPoints = 120 },
            new Customer { Id = 9632, BusinessId = 1, Name = "Inactive Ayesha", Phone = "01711000002", PhoneNormalized = "+8801711000002", IsActive = false },
            new Customer { Id = 9691, BusinessId = 2, Name = "Foreign Ayesha", Phone = "01711000003", PhoneNormalized = "+8801711000003", Email = "foreign@example.com" });

        await db.SaveChangesAsync();
    }
}
