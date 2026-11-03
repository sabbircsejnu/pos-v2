using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class StockTransferProductIdentityHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockTransferProductIdentityHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TransferWorkflow_PreservesProductIdentityFields_AcrossCreateViewAndReceive()
    {
        await ResetAndSeedAsync();

        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch",
            role: "BusinessOwner");
        using var destinationClient = CreateClient(
            102,
            "stock_transfers.view,stock_transfers.receive",
            role: "WarehouseManager");

        var createResponse = await sourceClient.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            notes = "identity-flow",
            items = new[]
            {
                new
                {
                    variantId = 301L,
                    quantity = 2,
                    requestedQuantity = 2,
                    transferQuantity = 2,
                    unitCost = 12.5m
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadApiData<StockTransferResponse>(createResponse);
        Assert.NotNull(created);
        AssertTransferIdentity(created!);

        var transferId = created!.Id;

        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null)).StatusCode);

        var inTransitResponse = await sourceClient.GetAsync($"/api/stock-transfers/{transferId}");
        Assert.Equal(HttpStatusCode.OK, inTransitResponse.StatusCode);
        var inTransit = await ReadApiData<StockTransferResponse>(inTransitResponse);
        Assert.NotNull(inTransit);
        Assert.Equal("in_transit", inTransit!.Status, ignoreCase: true);
        AssertTransferIdentity(inTransit);

        var receiveResponse = await destinationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new { variantId = 301L, acceptedQuantity = 2, rejectedQuantity = 0, remarks = "accepted" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);
        var received = await ReadApiData<StockTransferResponse>(receiveResponse);
        Assert.NotNull(received);
        AssertTransferIdentity(received!);
        Assert.Equal("received", received!.Status, ignoreCase: true);
    }

    [Fact]
    public async Task TransferWorkflow_PreservesDistinctAttributes_ForThreeVariants()
    {
        await ResetAndSeedAsync();

        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch",
            role: "BusinessOwner");
        using var destinationClient = CreateClient(
            102,
            "stock_transfers.view,stock_transfers.receive",
            role: "WarehouseManager");

        var createResponse = await sourceClient.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            notes = "three-variant-flow",
            items = new[]
            {
                new { variantId = 304L, quantity = 1, requestedQuantity = 1, transferQuantity = 1, unitCost = 10m },
                new { variantId = 301L, quantity = 1, requestedQuantity = 1, transferQuantity = 1, unitCost = 10m },
                new { variantId = 302L, quantity = 1, requestedQuantity = 1, transferQuantity = 1, unitCost = 10m }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadApiData<StockTransferResponse>(createResponse);
        Assert.NotNull(created);
        AssertDistinctVariantAttributes(created!);

        var transferId = created!.Id;
        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null)).StatusCode);

        var viewResponse = await sourceClient.GetAsync($"/api/stock-transfers/{transferId}");
        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
        var viewed = await ReadApiData<StockTransferResponse>(viewResponse);
        Assert.NotNull(viewed);
        AssertDistinctVariantAttributes(viewed!);

        var receiveResponse = await destinationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new { variantId = 304L, acceptedQuantity = 1, rejectedQuantity = 0 },
                new { variantId = 301L, acceptedQuantity = 1, rejectedQuantity = 0 },
                new { variantId = 302L, acceptedQuantity = 1, rejectedQuantity = 0 }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);
        var received = await ReadApiData<StockTransferResponse>(receiveResponse);
        Assert.NotNull(received);
        AssertDistinctVariantAttributes(received!);
    }

    [Fact]
    public async Task VariantDisplay_ExcludesInactiveVariationsAndOptions()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            (await db.Variations.SingleAsync(variation => variation.Id == 3)).IsActive = false;
            (await db.VariationOptions.SingleAsync(option => option.Id == 20)).IsActive = false;
            await db.SaveChangesAsync();
        }

        using var formatterScope = _factory.Services.CreateScope();
        var formatter = formatterScope.ServiceProvider.GetRequiredService<IVariantDisplayFormatterService>();

        var display = await formatter.FormatAsync(
            "{\"Color\":\"Blue\",\"Size\":\"50\",\"Fabric\":\"Korean Georgette\"}");

        Assert.Equal("Size 50 / Fabric Korean Georgette", display);
    }

    private static void AssertTransferIdentity(StockTransferResponse transfer)
    {
        var item = Assert.Single(transfer.Items);
        Assert.Equal("Identity Product", item.ProductName);
        Assert.Equal("IDN-001", item.ProductCode);
        Assert.Equal("IDN-001-BLK-52", item.VariantSku);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"52\",\"Fabric\":\"Korean Georgette\"}", item.VariantAttributes);
        Assert.Equal("Size 52 / Color Black / Fabric Korean Georgette", item.VariantDisplay);
        Assert.Equal("/images/products/identity-thumb.jpg", item.PrimaryImageThumb);
    }

    private static void AssertDistinctVariantAttributes(StockTransferResponse transfer)
    {
        Assert.Equal(3, transfer.Items.Count);

        var bySku = transfer.Items.ToDictionary(i => i.VariantSku);
        Assert.Contains("IDN-001-BLU-50", bySku.Keys);
        Assert.Contains("IDN-001-BLK-52", bySku.Keys);
        Assert.Contains("IDN-001-BLK-54", bySku.Keys);

        Assert.Equal("{\"Color\":\"Blue\",\"Size\":\"50\",\"Fabric\":\"Korean Georgette\"}", bySku["IDN-001-BLU-50"].VariantAttributes);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"52\",\"Fabric\":\"Korean Georgette\"}", bySku["IDN-001-BLK-52"].VariantAttributes);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"54\",\"Fabric\":\"Korean Georgette\"}", bySku["IDN-001-BLK-54"].VariantAttributes);
        Assert.Equal("Size 50 / Color Blue / Fabric Korean Georgette", bySku["IDN-001-BLU-50"].VariantDisplay);
        Assert.Equal("Size 52 / Color Black / Fabric Korean Georgette", bySku["IDN-001-BLK-52"].VariantDisplay);
        Assert.Equal("Size 54 / Color Black / Fabric Korean Georgette", bySku["IDN-001-BLK-54"].VariantDisplay);

        Assert.All(bySku.Values, item =>
        {
            Assert.Equal("Identity Product", item.ProductName);
            Assert.Equal("IDN-001", item.ProductCode);
            Assert.Equal("/images/products/identity-thumb.jpg", item.PrimaryImageThumb);
        });
    }

    private HttpClient CreateClient(long userId, string permissions, string role, long businessId = 1)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Identity Test Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Identity Category", Description = "Identity tests" });

        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = 21, BusinessId = 1, Name = "Main Warehouse", Address = "W" });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Identity Product",
            ProductCode = "IDN-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 50m,
            HasVariants = true,
            Status = ProductStatus.Active,
            Images = new List<ProductImage>
            {
                new()
                {
                    Id = 901,
                    ProductId = 300,
                    OriginalName = "identity.jpg",
                    MimeType = "image/jpeg",
                    Size = 128,
                    Width = 100,
                    Height = 100,
                    OriginalPath = "/images/products/identity.jpg",
                    MediumPath = "/images/products/identity-medium.jpg",
                    ThumbPath = "/images/products/identity-thumb.jpg",
                    IsPrimary = true,
                    SortOrder = 1
                }
            }
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Black 52",
            Sku = "IDN-001-BLK-52",
            Barcode = "IDN-BC-001",
            Attributes = "{\"Color\":\"Black\",\"Size\":\"52\",\"Fabric\":\"Korean Georgette\"}"
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 302,
            BusinessId = 1,
            ProductId = 300,
            Name = "Black 54",
            Sku = "IDN-001-BLK-54",
            Barcode = "IDN-BC-002",
            Attributes = "{\"Color\":\"Black\",\"Size\":\"54\",\"Fabric\":\"Korean Georgette\"}"
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 303,
            BusinessId = 1,
            ProductId = 300,
            Name = "Black 56",
            Sku = "IDN-001-BLK-56",
            Barcode = "IDN-BC-003",
            Attributes = "{\"Color\":\"Black\",\"Size\":\"56\",\"Fabric\":\"Korean Georgette\"}"
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 304,
            BusinessId = 1,
            ProductId = 300,
            Name = "Blue 50",
            Sku = "IDN-001-BLU-50",
            Barcode = "IDN-BC-004",
            Attributes = "{\"Color\":\"Blue\",\"Size\":\"50\",\"Fabric\":\"Korean Georgette\"}"
        });

        db.Inventories.AddRange(
            new InventoryEntity { Id = 401, VariantId = 301, LocationId = 11, LocationType = "outlet", Quantity = 20 },
            new InventoryEntity { Id = 402, VariantId = 301, LocationId = 21, LocationType = "warehouse", Quantity = 0 },
            new InventoryEntity { Id = 403, VariantId = 302, LocationId = 11, LocationType = "outlet", Quantity = 20 },
            new InventoryEntity { Id = 404, VariantId = 302, LocationId = 21, LocationType = "warehouse", Quantity = 0 },
            new InventoryEntity { Id = 405, VariantId = 303, LocationId = 11, LocationType = "outlet", Quantity = 20 },
            new InventoryEntity { Id = 406, VariantId = 303, LocationId = 21, LocationType = "warehouse", Quantity = 0 },
            new InventoryEntity { Id = 407, VariantId = 304, LocationId = 11, LocationType = "outlet", Quantity = 20 },
            new InventoryEntity { Id = 408, VariantId = 304, LocationId = 21, LocationType = "warehouse", Quantity = 0 });

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Identity Source User",
                Email = "identity.source.user@test.local",
                PasswordHash = "x",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 102,
                BusinessId = 1,
                Name = "Identity Destination User",
                Email = "identity.destination.user@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            });

        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            UserId = 102,
            WarehouseId = 21,
            IsActive = true,
            IsPrimary = true
        });

        db.Variations.AddRange(
            new Variation { Id = 1, Name = "Size", DisplayOrder = 1, IsActive = true },
            new Variation { Id = 2, Name = "Color", DisplayOrder = 2, IsActive = true },
            new Variation { Id = 3, Name = "Fabric", DisplayOrder = 3, IsActive = true }
        );

        db.VariationOptions.AddRange(
            new VariationOption { Id = 10, VariationId = 1, Name = "50", DisplayOrder = 0, IsActive = true },
            new VariationOption { Id = 11, VariationId = 1, Name = "52", DisplayOrder = 1, IsActive = true },
            new VariationOption { Id = 12, VariationId = 1, Name = "54", DisplayOrder = 2, IsActive = true },
            new VariationOption { Id = 13, VariationId = 1, Name = "56", DisplayOrder = 3, IsActive = true },
            new VariationOption { Id = 20, VariationId = 2, Name = "Blue", DisplayOrder = 0, IsActive = true },
            new VariationOption { Id = 21, VariationId = 2, Name = "Black", DisplayOrder = 1, IsActive = true },
            new VariationOption { Id = 31, VariationId = 3, Name = "Korean Georgette", DisplayOrder = 1, IsActive = true }
        );

        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class StockTransferResponse
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<StockTransferItemResponse> Items { get; set; } = new();
    }

    private sealed class StockTransferItemResponse
    {
        public long VariantId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string VariantSku { get; set; } = string.Empty;
        public string? VariantAttributes { get; set; }
        public string? VariantDisplay { get; set; }
        public string? PrimaryImageThumb { get; set; }
    }

    private sealed class ProductVariantSearchResponse
    {
        public long Id { get; set; }
        public string? ProductCode { get; set; }
        public string? Sku { get; set; }
        public string? Attributes { get; set; }
    }
}

public class ProductVariantSearchIdentityHttpTests : IClassFixture<SqliteRetailPosApiFactory>
{
    private readonly SqliteRetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProductVariantSearchIdentityHttpTests(SqliteRetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProductVariantSearch_ReturnsDistinctAttributes_ForThreeVariants()
    {
        await ResetAndSeedAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "products.view");

        var response = await client.GetAsync("/api/products/variants/search?query=IDN-001&pageNumber=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<List<ProductVariantSearchResponse>>>(stream, JsonOptions);
        var variants = envelope?.Data?
            .Where(v => v.ProductCode == "IDN-001")
            .OrderBy(v => v.Sku)
            .ToList();

        Assert.NotNull(variants);
        Assert.Equal(3, variants!.Count);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"52\",\"Fabric\":\"Korean Georgette\"}", variants[0].Attributes);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"54\",\"Fabric\":\"Korean Georgette\"}", variants[1].Attributes);
        Assert.Equal("{\"Color\":\"Black\",\"Size\":\"56\",\"Fabric\":\"Korean Georgette\"}", variants[2].Attributes);
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Identity Test Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Identity Category", Description = "Identity tests" });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Identity Product",
            ProductCode = "IDN-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 50m,
            HasVariants = true,
            Status = ProductStatus.Active,
            Images = new List<ProductImage>
            {
                new()
                {
                    Id = 901,
                    ProductId = 300,
                    OriginalName = "identity.jpg",
                    MimeType = "image/jpeg",
                    Size = 128,
                    Width = 100,
                    Height = 100,
                    OriginalPath = "/images/products/identity.jpg",
                    MediumPath = "/images/products/identity-medium.jpg",
                    ThumbPath = "/images/products/identity-thumb.jpg",
                    IsPrimary = true,
                    SortOrder = 1
                }
            }
        });

        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 301,
                BusinessId = 1,
                ProductId = 300,
                Name = "Black 52",
                Sku = "IDN-001-BLK-52",
                Barcode = "IDN-BC-001",
                Attributes = "{\"Color\":\"Black\",\"Size\":\"52\",\"Fabric\":\"Korean Georgette\"}"
            },
            new ProductVariant
            {
                Id = 302,
                BusinessId = 1,
                ProductId = 300,
                Name = "Black 54",
                Sku = "IDN-001-BLK-54",
                Barcode = "IDN-BC-002",
                Attributes = "{\"Color\":\"Black\",\"Size\":\"54\",\"Fabric\":\"Korean Georgette\"}"
            },
            new ProductVariant
            {
                Id = 303,
                BusinessId = 1,
                ProductId = 300,
                Name = "Black 56",
                Sku = "IDN-001-BLK-56",
                Barcode = "IDN-BC-003",
                Attributes = "{\"Color\":\"Black\",\"Size\":\"56\",\"Fabric\":\"Korean Georgette\"}"
            });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Identity User",
            Email = "identity.user@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class ProductVariantSearchResponse
    {
        public long Id { get; set; }
        public string? ProductCode { get; set; }
        public string? Sku { get; set; }
        public string? Attributes { get; set; }
    }
}

public sealed class SqliteRetailPosApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqliteRetailPosApiFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        TestJwtConfiguration.Configure(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));

            services.AddDbContext<RetailPOSDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}