using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

public class PurchaseOrderCreationFlowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PurchaseOrderCreationFlowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatePurchaseOrder_DraftFlow_PersistsHeaderAndItems()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();

        var request = BuildCreateRequest(status: "draft");

        var createResponse = await client.PostAsJsonAsync("/api/purchase-orders", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await ReadApiData<PurchaseOrderDto>(createResponse);
        Assert.NotNull(created);

        Assert.True(created!.Id > 0);
        Assert.StartsWith("PO-", created.PoNumber);
        Assert.Equal("draft", created.Status, ignoreCase: true);
        Assert.Equal(1001, created.SupplierId);
        Assert.Equal(1101, created.WarehouseId);
        Assert.Equal(2, created.Items.Count);

        // 3*12.50 with 10% discount and 5% tax = 35.44
        // 2*20.00 with 0% discount and 0% tax = 40.00
        Assert.Equal(75.44m, created.TotalAmount);
    }

    [Fact]
    public async Task CreateThenSubmitPurchaseOrder_StatusTransitionsToPending()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();

        var createResponse = await client.PostAsJsonAsync("/api/purchase-orders", BuildCreateRequest(status: "draft"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await ReadApiData<PurchaseOrderDto>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await client.PostAsync($"/api/purchase-orders/{created!.Id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var submitted = await ReadApiData<PurchaseOrderDto>(submitResponse);
        Assert.NotNull(submitted);
        Assert.Equal(created.Id, submitted!.Id);
        Assert.Equal("pending", submitted.Status, ignoreCase: true);

        var getResponse = await client.GetAsync($"/api/purchase-orders/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await ReadApiData<PurchaseOrderDto>(getResponse);
        Assert.NotNull(fetched);
        Assert.Equal("pending", fetched!.Status, ignoreCase: true);
        Assert.Equal(2, fetched.Items.Count);
    }

    private HttpClient CreatePurchaseClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "321");
        client.DefaultRequestHeaders.Add("X-Test-Role", "PurchaseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "purchases.view,purchases.create,purchases.approve");
        return client;
    }

    private static CreatePurchaseOrderDto BuildCreateRequest(string status)
    {
        return new CreatePurchaseOrderDto
        {
            SupplierId = 1001,
            WarehouseId = 1101,
            OrderDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            ExpectedDelivery = new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            Notes = "Automated PO creation flow test",
            Items =
            [
                new CreatePurchaseOrderItemDto
                {
                    VariantId = 1301,
                    Quantity = 3,
                    UnitPrice = 12.50m,
                    Discount = 10m,
                    Tax = 5m,
                    Unit = "pcs"
                },
                new CreatePurchaseOrderItemDto
                {
                    VariantId = 1302,
                    Quantity = 2,
                    UnitPrice = 20m,
                    Discount = 0m,
                    Tax = 0m,
                    Unit = "pcs"
                }
            ]
        };
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Test Business",
            IsActive = true
        });

        db.Categories.Add(new Category
        {
            Id = 1201,
            Name = "Test Category",
            Description = "PO flow test category"
        });

        db.Products.Add(new Product
        {
            Id = 1251,
            Name = "Paper Roll",
            ProductCode = "PAPER-001",
            CategoryId = 1201,
            BasePrice = 15m,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 1301,
                ProductId = 1251,
                Name = "White / Standard",
                Sku = "PAPER-WHT-STD",
                Barcode = "900001",
                Attributes = "{\"color\":\"White\",\"size\":\"Standard\"}",
                PriceAdjustment = 0m
            },
            new ProductVariant
            {
                Id = 1302,
                ProductId = 1251,
                Name = "Brown / Standard",
                Sku = "PAPER-BRN-STD",
                Barcode = "900002",
                Attributes = "{\"color\":\"Brown\",\"size\":\"Standard\"}",
                PriceAdjustment = 0m
            });

        db.Suppliers.Add(new Supplier
        {
            Id = 1001,
            Name = "Acme Supplier",
            Contact = "01700000000",
            Address = "Dhaka"
        });

        db.Warehouses.Add(new Warehouse
        {
            Id = 1101,
            Name = "Central Warehouse",
            Address = "Main Road",
            BusinessId = 1
        });

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
        public T? Data { get; set; }
    }
}
