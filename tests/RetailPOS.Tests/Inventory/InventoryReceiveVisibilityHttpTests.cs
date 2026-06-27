using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class InventoryReceiveVisibilityHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public InventoryReceiveVisibilityHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PurchaseReceive_UpdatesWarehouseInventory_AndDoesNotSplitByLocationTypeCase()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/purchase-orders/purchase-receive", new
        {
            supplierId = 5001L,
            warehouseId = 2101L,
            orderDate = DateTime.UtcNow,
            expectedDelivery = DateTime.UtcNow.AddDays(1),
            notes = "purchase receive test",
            idempotencyKey = Guid.NewGuid().ToString(),
            items = new[]
            {
                new
                {
                    variantId = 3002L,
                    quantity = 7,
                    unitPrice = 10m,
                    discount = 0m,
                    tax = 0m,
                    unit = "pcs"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var matchingRows = db.Inventories
            .Where(i => i.VariantId == 3002 && i.LocationId == 2101 && i.LocationType.ToLower() == "warehouse")
            .ToList();

        Assert.Single(matchingRows);
        Assert.Equal(12, matchingRows[0].Quantity);
    }

    [Fact]
    public async Task TransferWarehouseToOutlet_ReceiveCreatesDestinationInventory()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        var transferId = await CreateSubmitApproveAndSendAsync(
            client,
            fromLocationId: 2101,
            fromLocationType: "warehouse",
            toLocationId: 12,
            toLocationType: "outlet",
            variantId: 3001,
            quantity: 8);

        var receiveResponse = await client.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new
                {
                    variantId = 3001L,
                    acceptedQuantity = 8,
                    rejectedQuantity = 0,
                    remarks = "all good"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var destinationRows = db.Inventories
            .Where(i => i.VariantId == 3001 && i.LocationId == 12 && i.LocationType.ToLower() == "outlet")
            .ToList();

        Assert.Single(destinationRows);
        Assert.Equal(8, destinationRows[0].Quantity);
    }

    [Fact]
    public async Task TransferOutletToOutlet_ReceiveCreatesDestinationInventory()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        var transferId = await CreateSubmitApproveAndSendAsync(
            client,
            fromLocationId: 11,
            fromLocationType: "outlet",
            toLocationId: 12,
            toLocationType: "outlet",
            variantId: 3003,
            quantity: 7);

        var receiveResponse = await client.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new
                {
                    variantId = 3003L,
                    acceptedQuantity = 7,
                    rejectedQuantity = 0,
                    remarks = "received"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var destinationRows = db.Inventories
            .Where(i => i.VariantId == 3003 && i.LocationId == 12 && i.LocationType.ToLower() == "outlet")
            .ToList();

        Assert.Single(destinationRows);
        Assert.Equal(7, destinationRows[0].Quantity);
    }

    [Fact]
    public async Task TransferReceive_AppliesAcceptedQuantity_Only()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        var transferId = await CreateSubmitApproveAndSendAsync(
            client,
            fromLocationId: 2101,
            fromLocationType: "warehouse",
            toLocationId: 12,
            toLocationType: "outlet",
            variantId: 3001,
            quantity: 10);

        var receiveResponse = await client.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new
                {
                    variantId = 3001L,
                    acceptedQuantity = 6,
                    rejectedQuantity = 4,
                    remarks = "partial accepted"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        var transfer = await ReadApiData<StockTransferResponse>(receiveResponse);
        Assert.NotNull(transfer);
        Assert.Equal("partially_received", transfer!.Status, ignoreCase: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var destinationInventory = db.Inventories.Single(i =>
            i.VariantId == 3001 && i.LocationId == 12 && i.LocationType.ToLower() == "outlet");

        Assert.Equal(6, destinationInventory.Quantity);
    }

    [Fact]
    public async Task TransferReceive_RejectedQuantity_DoesNotIncreaseDestinationStock()
    {
        await ResetAndSeedAsync(seedDestinationOutletInventory: true);
        using var client = CreateClient();

        var transferId = await CreateSubmitApproveAndSendAsync(
            client,
            fromLocationId: 2101,
            fromLocationType: "warehouse",
            toLocationId: 12,
            toLocationType: "outlet",
            variantId: 3001,
            quantity: 5);

        var receiveResponse = await client.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new
                {
                    variantId = 3001L,
                    acceptedQuantity = 0,
                    rejectedQuantity = 5,
                    remarks = "all rejected"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var destinationInventory = db.Inventories.Single(i =>
            i.VariantId == 3001 && i.LocationId == 12 && i.LocationType.ToLower() == "outlet");

        Assert.Equal(2, destinationInventory.Quantity);
    }

    [Fact]
    public async Task InventorySearch_AssignedOnlyOutletManager_DefaultsToAssignedOutlet()
    {
        await ResetAndSeedAsync();
        using var managerClient = CreateOutletManagerClient();

        var response = await managerClient.PostAsJsonAsync("/api/inventory/search", new
        {
            page = 1,
            pageSize = 25
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<InventorySearchRow>>(response);
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        Assert.All(data!, row => Assert.Equal(12, row.OutletId));
        Assert.All(data!, row => Assert.False(row.WarehouseId.HasValue));
    }

    [Fact]
    public async Task InventorySearch_AssignedOnlyOutletManager_SeesDestinationStockAfterTransferReceive()
    {
        await ResetAndSeedAsync();
        using var ownerClient = CreateClient();
        using var managerClient = CreateOutletManagerClient();

        var transferId = await CreateSubmitApproveAndSendAsync(
            ownerClient,
            fromLocationId: 2101,
            fromLocationType: "warehouse",
            toLocationId: 12,
            toLocationType: "outlet",
            variantId: 3001,
            quantity: 4);

        var receiveResponse = await ownerClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new
                {
                    variantId = 3001L,
                    acceptedQuantity = 4,
                    rejectedQuantity = 0,
                    remarks = "received for manager outlet"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        var inventorySearch = await managerClient.PostAsJsonAsync("/api/inventory/search", new
        {
            page = 1,
            pageSize = 25
        });

        Assert.Equal(HttpStatusCode.OK, inventorySearch.StatusCode);

        var rows = await ReadApiData<List<InventorySearchRow>>(inventorySearch);
        Assert.NotNull(rows);

        var receivedRow = rows!.SingleOrDefault(r => r.ProductVariantId == 3001 && r.OutletId == 12);
        Assert.NotNull(receivedRow);
        Assert.Equal(4, receivedRow!.Quantity);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "900");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "purchases.view,purchases.receive,stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch,stock_transfers.receive,stock_transfers.transfer_from_any_location");
        return client;
    }

    private HttpClient CreateOutletManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "901");
        client.DefaultRequestHeaders.Add("X-Test-Role", "OutletManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view,stock_transfers.view,stock_transfers.receive");
        return client;
    }

    private async Task<long> CreateSubmitApproveAndSendAsync(
        HttpClient client,
        long fromLocationId,
        string fromLocationType,
        long toLocationId,
        string toLocationType,
        long variantId,
        int quantity)
    {
        var createResponse = await client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId,
            fromLocationType,
            toLocationId,
            toLocationType,
            transferDate = DateTime.UtcNow,
            items = new[]
            {
                new
                {
                    variantId,
                    quantity,
                    requestedQuantity = quantity,
                    transferQuantity = quantity,
                    unitCost = 10m,
                    remarks = "flow-test"
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadApiData<StockTransferResponse>(createResponse);
        Assert.NotNull(created);

        var submitResponse = await client.PostAsync($"/api/stock-transfers/{created!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var approveResponse = await client.PostAsync($"/api/stock-transfers/{created.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var sendResponse = await client.PostAsync($"/api/stock-transfers/{created.Id}/send", null);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        return created.Id;
    }

    private async Task ResetAndSeedAsync(bool seedDestinationOutletInventory = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Inventory Flow Business", IsActive = true });

        db.Categories.Add(new Category
        {
            Id = 2001,
            Name = "Inventory Flow Category",
            Description = "Inventory receive visibility tests"
        });

        db.Products.Add(new Product
        {
            Id = 2501,
            Name = "Inventory Flow Product",
            ProductCode = "INV-FLOW-001",
            CategoryId = 2001,
            BasePrice = 100m,
            CostPrice = 80m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 3001,
                ProductId = 2501,
                Name = "Variant A",
                Sku = "INV-FLOW-A",
                Barcode = "INV-FLOW-A-BC",
                Attributes = "{}"
            },
            new ProductVariant
            {
                Id = 3002,
                ProductId = 2501,
                Name = "Variant B",
                Sku = "INV-FLOW-B",
                Barcode = "INV-FLOW-B-BC",
                Attributes = "{}"
            },
            new ProductVariant
            {
                Id = 3003,
                ProductId = 2501,
                Name = "Variant C",
                Sku = "INV-FLOW-C",
                Barcode = "INV-FLOW-C-BC",
                Attributes = "{}"
            });

        db.Suppliers.Add(new Supplier
        {
            Id = 5001,
            Name = "Flow Supplier",
            Contact = "01700000000",
            Address = "Dhaka"
        });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet A", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Outlet B", Address = "B" });

        db.Warehouses.Add(new Warehouse
        {
            Id = 2101,
            BusinessId = 1,
            Name = "Main Warehouse",
            Address = "W"
        });

        var inventories = new List<InventoryEntity>
        {
            new()
            {
                Id = 7001,
                VariantId = 3001,
                LocationId = 2101,
                LocationType = "Warehouse",
                Quantity = 50
            },
            new()
            {
                Id = 7002,
                VariantId = 3002,
                LocationId = 2101,
                LocationType = "Warehouse",
                Quantity = 5
            },
            new()
            {
                Id = 7003,
                VariantId = 3003,
                LocationId = 11,
                LocationType = "OUTLET",
                Quantity = 20
            }
        };

        if (seedDestinationOutletInventory)
        {
            inventories.Add(new InventoryEntity
            {
                Id = 7004,
                VariantId = 3001,
                LocationId = 12,
                LocationType = "Outlet",
                Quantity = 2
            });
        }

        db.Inventories.AddRange(inventories);

        db.Users.Add(new User
        {
            Id = 900,
            BusinessId = 1,
            Name = "Inventory Flow User",
            Email = "inventory.flow@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });

        db.Users.Add(new User
        {
            Id = 901,
            BusinessId = 1,
            OutletId = 12,
            Name = "Outlet Manager",
            Email = "outlet.manager@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            IsActive = true
        });

        db.Inventories.Add(new InventoryEntity
        {
            Id = 7005,
            VariantId = 3002,
            LocationId = 12,
            LocationType = "Outlet",
            Quantity = 3
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

    private sealed class StockTransferResponse
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    private sealed class InventorySearchRow
    {
        public long ProductVariantId { get; set; }
        public long? OutletId { get; set; }
        public long? WarehouseId { get; set; }
        public int Quantity { get; set; }
    }
}