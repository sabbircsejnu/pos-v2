using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class StockTransferRequisitionPermissionHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public StockTransferRequisitionPermissionHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TransferCreate_WithoutCreatePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-transfers", BuildTransferCreatePayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TransferCreate_WithCreatePermission_ReturnsCreated()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view,stock_transfers.create", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-transfers", BuildTransferCreatePayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TransferDispatch_WithoutDispatchPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view", role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-transfers/999/dispatch", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TransferReceive_WithoutReceivePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/999/receive", new
        {
            items = new[]
            {
                new { variantId = 301L, acceptedQuantity = 1, rejectedQuantity = 0 }
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TransferReject_WithoutRejectReceivePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-transfers/999/reject", new { reason = "No authority" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionCreate_WithoutCreatePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-requisitions", BuildRequisitionCreatePayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionCreate_WithCreatePermission_ReturnsCreated()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_requisitions.view,stock_requisitions.create", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-requisitions", BuildRequisitionCreatePayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionApprove_WithoutApprovePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-requisitions/999/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionConvert_WithoutConvertPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-requisitions/999/convert-to-transfer", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static object BuildTransferCreatePayload()
    {
        return new
        {
            transferType = "direct",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            items = new[]
            {
                new
                {
                    variantId = 301L,
                    quantity = 1,
                    requestedQuantity = 1,
                    transferQuantity = 1,
                    unitCost = 10.0m,
                    remarks = "policy-test"
                }
            }
        };
    }

    private static object BuildRequisitionCreatePayload()
    {
        return new
        {
            requestingLocationId = 11L,
            requestingLocationType = "outlet",
            sourceLocationId = 21L,
            sourceLocationType = "warehouse",
            notes = "policy-test",
            lines = new[]
            {
                new
                {
                    variantId = 301L,
                    requestedQuantity = 2,
                    remarks = "policy-test"
                }
            }
        };
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

        db.Businesses.Add(new Business { Id = 1, Name = "Policy Test Business" });
        db.Categories.Add(new Category { Id = 201, Name = "Policy Category", Description = "Policy tests" });

        db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Main Outlet",
            Address = "A"
        });

        db.Warehouses.Add(new Warehouse
        {
            Id = 21,
            BusinessId = 1,
            Name = "Main Warehouse",
            Address = "W"
        });

        db.Products.Add(new Product
        {
            Id = 300,
            Name = "Policy Product",
            ProductCode = "POL-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 70m,
            HasVariants = true,
            Status = ProductStatus.Active
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            ProductId = 300,
            Name = "Policy Variant",
            Sku = "POL-001-DEF",
            Barcode = "POL-BC-001",
            Attributes = "{}"
        });

        db.Inventories.Add(new InventoryEntity
        {
            Id = 401,
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 50
        });

        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Policy User",
            Email = "policy.user@test.local",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }
}
