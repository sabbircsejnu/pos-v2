using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Security;

public sealed class StockLocationAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public StockLocationAuthorizationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StockHint_AllowsAssignedOutletAndPreservesQuantityProjection()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("sales.create");

        using var response = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9901&outletId=5001&qty=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(9901, data.GetProperty("variantId").GetInt64());
        Assert.Equal(5001, data.GetProperty("outletId").GetInt64());
        Assert.Equal(9, data.GetProperty("availableQty").GetInt32());
        Assert.True(data.GetProperty("sufficient").GetBoolean());
    }

    [Theory]
    [InlineData(5003)]
    [InlineData(6001)]
    public async Task StockHint_RejectsOutletOutsideUserAssignments(long outletId)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("sales.create");

        using var response = await client.GetAsync(
            $"/api/pos/stock-hint?variantId=9901&outletId={outletId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("availableQty", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StockHint_RequiresPositiveOutletId()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("sales.create");

        using var missing = await client.GetAsync("/api/pos/stock-hint?variantId=9901");
        using var invalid = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9901&outletId=0");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task StockHint_AllowsExplicitSuperAdminGlobalLocationAccessInTheSelectedBusiness()
    {
        await ResetAndSeedAsync();
        using var client = CreateSuperAdminClient(selectedBusinessId: 1, permission: "sales.create");

        using var response = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9901&outletId=5003");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(8, document.RootElement.GetProperty("data")
            .GetProperty("availableQty").GetInt32());
    }

    [Fact]
    public async Task StockHint_SuperAdminCannotReachAnotherBusinessesOutletOrOmitTheBusiness()
    {
        await ResetAndSeedAsync();
        using var business1 = CreateSuperAdminClient(selectedBusinessId: 1, permission: "sales.create");
        using var noContext = CreateSuperAdminClient(selectedBusinessId: null, permission: "sales.create");

        using var foreignOutlet = await business1.GetAsync("/api/pos/stock-hint?variantId=9901&outletId=6001");
        using var withoutBusiness = await noContext.GetAsync("/api/pos/stock-hint?variantId=9901&outletId=5003");

        Assert.Equal(HttpStatusCode.Forbidden, foreignOutlet.StatusCode);
        Assert.DoesNotContain("availableQty", await foreignOutlet.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, withoutBusiness.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotSelectAnInactiveOrUnknownBusinessNorBeImpersonatedByARegularUser()
    {
        await ResetAndSeedAsync();
        using var unknown = CreateSuperAdminClient(selectedBusinessId: 999, permission: "sales.create");
        using var regularUser = CreateClient("sales.create");
        regularUser.DefaultRequestHeaders.Add("X-Business-Id", "2");

        using var unknownResponse = await unknown.GetAsync("/api/pos/stock-hint?variantId=9901&outletId=5003");
        using var regularResponse = await regularUser.GetAsync("/api/pos/stock-hint?variantId=9901&outletId=5001");

        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, regularResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view")]
    [InlineData("/api/products/variants/search", "inventory.view")]
    [InlineData("/api/barcodes/variants/search", "barcode.view")]
    public async Task VariantSearch_ReturnsNullableStockWhenLocationIsOmitted(
        string route,
        string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync($"{route}?query=Classic");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variant = document.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == 9901);
        Assert.True(variant.TryGetProperty("stockQuantity", out var stockQuantity));
        Assert.Equal(JsonValueKind.Null, stockQuantity.ValueKind);
        Assert.Equal(9001, variant.GetProperty("productId").GetInt64());
        Assert.Equal("BRC-RED-S", variant.GetProperty("barcode").GetString());
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view")]
    [InlineData("/api/products/variants/search", "inventory.view")]
    [InlineData("/api/barcodes/variants/search", "barcode.view")]
    public async Task VariantSearch_AuthorizesExplicitAssignedOutletAndReturnsItsStock(
        string route,
        string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync(
            $"{route}?query=Classic&locationId=5001&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variant = document.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == 9901);
        Assert.Equal(9, variant.GetProperty("stockQuantity").GetInt32());
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view", 5003)]
    [InlineData("/api/products/variants/search", "products.view", 6001)]
    [InlineData("/api/products/variants/search", "inventory.view", 5003)]
    [InlineData("/api/products/variants/search", "inventory.view", 6001)]
    [InlineData("/api/barcodes/variants/search", "barcode.view", 5003)]
    [InlineData("/api/barcodes/variants/search", "barcode.view", 6001)]
    public async Task VariantSearch_RejectsUnassignedSameOrForeignBusinessOutlet(
        string route,
        string permission,
        long outletId)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync(
            $"{route}?query=Classic&locationId={outletId}&locationType=outlet");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("stockQuantity", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view", "?locationId=5001&locationType=shop")]
    [InlineData("/api/products/variants/search", "products.view", "?locationId=5001")]
    [InlineData("/api/products/variants/search", "products.view", "?locationType=outlet")]
    [InlineData("/api/products/variants/search", "products.view", "?locationType=%20")]
    [InlineData("/api/products/variants/search", "products.view", "?locationId=0&locationType=outlet")]
    [InlineData("/api/barcodes/variants/search", "barcode.view", "?locationId=5001&locationType=shop")]
    [InlineData("/api/barcodes/variants/search", "barcode.view", "?locationId=5001")]
    [InlineData("/api/barcodes/variants/search", "barcode.view", "?locationType=outlet")]
    [InlineData("/api/barcodes/variants/search", "barcode.view", "?locationType=%20")]
    [InlineData("/api/barcodes/variants/search", "barcode.view", "?locationId=0&locationType=outlet")]
    public async Task VariantSearch_RejectsMalformedLocationFilters(
        string route,
        string permission,
        string locationQuery)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync($"{route}{locationQuery}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view")]
    [InlineData("/api/barcodes/variants/search", "barcode.view")]
    public async Task VariantSearch_AllowsMultipleExplicitOutletAssignments(
        string route,
        string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync(
            $"{route}?query=Classic&locationId=5002&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variant = document.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == 9901);
        Assert.Equal(4, variant.GetProperty("stockQuantity").GetInt32());
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view")]
    [InlineData("/api/barcodes/variants/search", "barcode.view")]
    public async Task VariantSearch_AllowsExplicitSuperAdminGlobalLocationAccessInTheSelectedBusiness(
        string route,
        string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateSuperAdminClient(selectedBusinessId: 1, permission);

        using var response = await client.GetAsync(
            $"{route}?query=Classic&locationId=5003&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variant = document.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt64() == 9901);
        Assert.Equal(8, variant.GetProperty("stockQuantity").GetInt32());
    }

    [Theory]
    [InlineData("/api/products/variants/search", "products.view")]
    [InlineData("/api/barcodes/variants/search", "barcode.view")]
    public async Task VariantSearch_SuperAdminIsDeniedAnotherBusinessesLocationAndNeedsABusiness(
        string route,
        string permission)
    {
        await ResetAndSeedAsync();
        using var business1 = CreateSuperAdminClient(selectedBusinessId: 1, permission);
        using var noContext = CreateSuperAdminClient(selectedBusinessId: null, permission);

        using var foreignLocation = await business1.GetAsync(
            $"{route}?query=Classic&locationId=6001&locationType=outlet");
        using var withoutBusiness = await noContext.GetAsync($"{route}?query=Classic");

        Assert.Equal(HttpStatusCode.Forbidden, foreignLocation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutBusiness.StatusCode);
    }

    [Fact]
    public async Task UnauthorizedLocationRequests_DoNotDispatchStockQueries()
    {
        using var factory = new NoQueryDispatchRetailPosApiFactory();
        await ResetAndSeedAsync(factory);
        using var posClient = CreateClient(factory, "sales.create");
        using var productClient = CreateClient(factory, "products.view");
        using var barcodeClient = CreateClient(factory, "barcode.view");

        using var posResponse = await posClient.GetAsync(
            "/api/pos/stock-hint?variantId=9901&outletId=6001");
        using var productResponse = await productClient.GetAsync(
            "/api/products/variants/search?query=Classic&locationId=6001&locationType=outlet");
        using var barcodeResponse = await barcodeClient.GetAsync(
            "/api/barcodes/variants/search?query=Classic&locationId=6001&locationType=outlet");

        Assert.Equal(HttpStatusCode.Forbidden, posResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, productResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, barcodeResponse.StatusCode);
        factory.SenderMock.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> VariantLookupPermissions() =>
        RetailPOS.API.Authorization.LookupAuthorizationPolicies
            .PermissionsFor(RetailPOS.API.Authorization.LookupAuthorizationPolicies.ProductVariantSearch)
            .Select(permission => new object[] { permission });

    [Theory]
    [MemberData(nameof(VariantLookupPermissions))]
    public async Task VariantLookup_IsOpenToEveryScreenThatEmbedsItWithoutProductsView(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync("/api/products/variants/search?query=Classic&locationId=5001&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("customers.view")]
    [InlineData("variations.view")]
    [InlineData("inventory.edit")]
    [InlineData("sales.view")]
    public async Task VariantLookup_RejectsPermissionsOfUnrelatedScreens(string permission)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(permission);

        using var response = await client.GetAsync("/api/products/variants/search?query=Classic");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VariantLookup_HonoursTheBusinessOwnerWildcard()
    {
        await ResetAndSeedAsync();
        using var owner = CreateClient("*", role: "BusinessOwner");

        using var ownerResponse = await owner.GetAsync("/api/products/variants/search?query=Classic&locationId=5001&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
    }

    [Fact]
    public async Task VariantLookup_StillRedactsCostWithoutViewCost()
    {
        await ResetAndSeedAsync();
        using var withoutCost = CreateClient("inventory.view");
        using var withCost = CreateClient("inventory.view,products.view_cost");

        using var redacted = await withoutCost.GetAsync("/api/products/variants/search?query=Classic");
        using var visible = await withCost.GetAsync("/api/products/variants/search?query=Classic");

        Assert.Equal(HttpStatusCode.OK, redacted.StatusCode);
        using var redactedDocument = JsonDocument.Parse(await redacted.Content.ReadAsStringAsync());
        Assert.All(redactedDocument.RootElement.GetProperty("data").EnumerateArray(),
            item => Assert.Equal(JsonValueKind.Null, item.GetProperty("costPrice").ValueKind));
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
    }

    private HttpClient CreateSuperAdminClient(long? selectedBusinessId, string permission) =>
        TestSuperAdmin.Configure(_factory.CreateClient(), SuperAdminUserId, selectedBusinessId, permission);

    private const long SuperAdminUserId = 9999;

    private HttpClient CreateClient(string permission, long userId = 777, string role = "InventoryAdmin", long businessId = 1) =>
        CreateClient(_factory, permission, userId, role, businessId);

    private static HttpClient CreateClient(
        RetailPosApiFactory factory,
        string permission,
        long userId = 777,
        string role = "InventoryAdmin",
        long businessId = 1)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permission);
        return client;
    }

    private Task ResetAndSeedAsync() => ResetAndSeedAsync(_factory);

    private static async Task ResetAndSeedAsync(RetailPosApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business A", IsActive = true },
            new Business { Id = 2, Name = "Business B", IsActive = true });
        db.Categories.Add(new Category { Id = 7001, BusinessId = 1, Name = "Apparel" });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "A1" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "A2" },
            new Outlet { Id = 5003, BusinessId = 1, Name = "A3" },
            new Outlet { Id = 6001, BusinessId = 2, Name = "B1" });
        db.Users.Add(new User
        {
            Id = 777,
            BusinessId = 1,
            Name = "Assigned inventory user",
            Email = "stock-location-test@example.com",
            PasswordHash = "x",
            OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessSpecific,
            IsActive = true
        });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { Id = 1, UserId = 777, OutletId = 5001, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { Id = 2, UserId = 777, OutletId = 5002, BusinessId = 1 });
        db.Products.Add(new Product
        {
            Id = 9001,
            BusinessId = 1,
            Name = "Classic T-Shirt",
            ProductCode = "TSH-001",
            CategoryId = 7001,
            BasePrice = 100m,
            CostPrice = 70m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 9901,
            BusinessId = 1,
            ProductId = 9001,
            Name = "Red / Small",
            Sku = "TSH-RED-S",
            Barcode = "BRC-RED-S",
            Attributes = "{\"color\":\"Crimson\",\"size\":\"S\"}",
            PriceAdjustment = 5m
        });
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory { Id = 9901, VariantId = 9901, LocationId = 5001, LocationType = "outlet", Quantity = 9 },
            new RetailPOS.Core.Entities.Inventory { Id = 9902, VariantId = 9901, LocationId = 5002, LocationType = "outlet", Quantity = 4 },
            new RetailPOS.Core.Entities.Inventory { Id = 9903, VariantId = 9901, LocationId = 5003, LocationType = "outlet", Quantity = 8 },
            new RetailPOS.Core.Entities.Inventory { Id = 9904, VariantId = 9901, LocationId = 6001, LocationType = "outlet", Quantity = 3 });
        await db.SaveChangesAsync();
        await TestSuperAdmin.SeedAsync(db, SuperAdminUserId);
    }

    private sealed class NoQueryDispatchRetailPosApiFactory : RetailPosApiFactory
    {
        public Mock<ISender> SenderMock { get; } = new(MockBehavior.Strict);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton(SenderMock.Object);
            });
        }
    }
}
