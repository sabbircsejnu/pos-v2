using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class StockTransferSubmitAndDispatchHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public StockTransferSubmitAndDispatchHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SubmitOnly_MovesDraftToSubmitted()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, "stock_transfers.view,stock_transfers.create", "BusinessOwner");

        var transferId = await CreateDraftTransferAsync(client, quantity: 2);

        var response = await client.PostAsync($"/api/stock-transfers/{transferId}/submit", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TransferEnvelope>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("submitted", body?.Data?.Status, ignoreCase: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await db.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusSubmitted, transfer!.Status);
        Assert.Equal(101, transfer.SubmittedBy);
        Assert.Equal(101, transfer.UpdatedBy);
        Assert.NotNull(transfer.SubmittedAt);
        Assert.NotNull(transfer.UpdatedAt);
    }

    [Fact]
    public async Task DestinationUser_CannotSubmitTransfer()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view", "Manager");
        var transferId = await CreateDraftTransferAsync(sourceClient, quantity: 1);

        var response = await destinationClient.PostAsync(
            $"/api/stock-transfers/{transferId}/submit",
            null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "Only an authorized user of the source location can submit this transfer.",
            body?.Message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await db.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusDraft, transfer!.Status);
        Assert.Null(transfer.SubmittedBy);
        Assert.Null(transfer.SubmittedAt);
    }

    [Fact]
    public async Task SubmitAndDispatch_MovesDraftToInTransit_AndDeductsInventoryOnce()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");

        var transferId = await CreateDraftTransferAsync(client, quantity: 3);

        var response = await client.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TransferEnvelope>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("in_transit", body?.Data?.Status, ignoreCase: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var transfer = await db.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusInTransit, transfer!.Status);
        Assert.Equal(101, transfer.SubmittedBy);
        Assert.Equal(101, transfer.ApprovedBy);
        Assert.Equal(101, transfer.DispatchedBy);
        Assert.NotNull(transfer.SubmittedAt);
        Assert.NotNull(transfer.ApprovedAt);
        Assert.NotNull(transfer.DispatchedAt);

        var inventory = await db.Inventories.FindAsync(401L);
        Assert.NotNull(inventory);
        Assert.Equal(7, inventory!.Quantity);

        var ledgerRows = db.StockLedgers
            .Where(x => x.ReferenceId == transferId && x.ReferenceType == StockLedgerReferenceType.StockTransfer && x.TransactionType == StockLedgerTransactionType.TransferOut)
            .ToList();

        Assert.Single(ledgerRows);
        Assert.Equal(3, ledgerRows[0].QtyOut);
        Assert.Equal(7, ledgerRows[0].BalanceAfter);
    }

    [Fact]
    public async Task SubmitAndDispatch_AndReceive_PreserveTransferValuationMetadata()
    {
        await ResetAndSeedAsync();
        await SeedSourceCostAsync(301, 11, "outlet", 10m);
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view,stock_transfers.receive", "Manager");

        var transferId = await CreateDraftTransferAsync(sourceClient, quantity: 3);

        Assert.Equal(HttpStatusCode.OK, (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await destinationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", new
        {
            items = new[]
            {
                new { variantId = 301L, acceptedQuantity = 3, rejectedQuantity = 0, remarks = "received" }
            }
        })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var ledgerRows = db.StockLedgers
            .Where(x => x.ReferenceId == transferId && x.ReferenceType == StockLedgerReferenceType.StockTransfer)
            .OrderBy(x => x.Id)
            .ToList();

        Assert.Equal(2, ledgerRows.Count);
        Assert.Equal(3, ledgerRows[0].QtyOut);
        Assert.Equal(3, ledgerRows[1].QtyIn);
        Assert.True(ledgerRows[0].IsCostValuationReliable);
        Assert.True(ledgerRows[1].IsCostValuationReliable);
        Assert.Equal(10m, ledgerRows[0].UnitCost);
        Assert.Equal(30m, ledgerRows[0].MovementValue);
        Assert.Equal(10m, ledgerRows[1].UnitCost);
        Assert.Equal(30m, ledgerRows[1].MovementValue);
    }

    [Fact]
    public async Task UnauthorizedUser_CannotSubmitAndDispatch()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view,stock_transfers.approve,stock_transfers.dispatch", "Manager");

        var transferId = await CreateDraftTransferAsync(sourceClient, quantity: 2);

        var response = await destinationClient.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only an authorized user of the source location can submit and dispatch this transfer.", body?.Message);
    }

    [Fact]
    public async Task SubmitAndDispatch_WithInsufficientStock_RollsBackStatusAndLedger()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");

        var transferId = await CreateDraftTransferAsync(client, quantity: 4);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var inventory = await db.Inventories.FindAsync(401L);
            Assert.NotNull(inventory);
            inventory!.Quantity = 2;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Insufficient stock", body?.Message ?? string.Empty);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await verifyDb.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusDraft, transfer!.Status);
        Assert.Null(transfer.SubmittedAt);
        Assert.Null(transfer.ApprovedAt);
        Assert.Null(transfer.DispatchedAt);

        var inventoryAfter = await verifyDb.Inventories.FindAsync(401L);
        Assert.NotNull(inventoryAfter);
        Assert.Equal(2, inventoryAfter!.Quantity);

        Assert.Empty(verifyDb.StockLedgers.Where(x => x.ReferenceId == transferId && x.ReferenceType == StockLedgerReferenceType.StockTransfer));
    }

    [Fact]
    public async Task RepeatedSubmitAndDispatchRequest_DoesNotDeductInventoryTwice()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");

        var transferId = await CreateDraftTransferAsync(client, quantity: 2);

        var firstResponse = await client.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null);
        var secondResponse = await client.PostAsync($"/api/stock-transfers/{transferId}/submit-and-dispatch", null);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = await db.Inventories.FindAsync(401L);
        Assert.NotNull(inventory);
        Assert.Equal(8, inventory!.Quantity);

        var ledgerRows = db.StockLedgers
            .Where(x => x.ReferenceId == transferId && x.ReferenceType == StockLedgerReferenceType.StockTransfer && x.TransactionType == StockLedgerTransactionType.TransferOut)
            .ToList();

        Assert.Single(ledgerRows);
    }

    [Fact]
    public async Task NormalApproveThenDispatch_StillWorks()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");

        var transferId = await CreateDraftTransferAsync(client, quantity: 1);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/stock-transfers/{transferId}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = await db.Inventories.FindAsync(401L);
        Assert.NotNull(inventory);
        Assert.Equal(9, inventory!.Quantity);
    }

    private async Task<long> CreateDraftTransferAsync(HttpClient client, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            notes = "submit-dispatch-test",
            items = new[]
            {
                new
                {
                    variantId = 301L,
                    quantity,
                    requestedQuantity = quantity,
                    transferQuantity = quantity,
                    unitCost = 10m,
                    remarks = "submit-dispatch-test"
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ApiResponse<TransferEnvelope>>();
        Assert.NotNull(created?.Data);
        Assert.Equal("draft", created!.Data!.Status, ignoreCase: true);
        return created.Data.Id;
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

    /// <summary>Gives the source a real cost history: a transfer takes its cost from there, never from the client.</summary>
    private async Task SeedSourceCostAsync(long variantId, long locationId, string locationType, decimal unitCost)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var onHand = db.Inventories
            .Where(row => row.VariantId == variantId && row.LocationId == locationId)
            .Sum(row => row.Quantity);
        db.StockLedgers.Add(StockCostTestData.Receipt(variantId, locationId, locationType, onHand, unitCost));
        await db.SaveChangesAsync();
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Submit Dispatch Test Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Submit Dispatch Category", Description = "Workflow tests" });

        db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Source Outlet",
            Address = "A"
        });

        db.Warehouses.Add(new Warehouse
        {
            Id = 21,
            BusinessId = 1,
            Name = "Destination Warehouse",
            Address = "W"
        });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Submit Dispatch Product",
            ProductCode = "SD-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 70m,
            HasVariants = true,
            Status = ProductStatus.Active
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Submit Dispatch Variant",
            Sku = "SD-001-DEF",
            Barcode = "SD-BC-001",
            Attributes = "{}"
        });

        db.Inventories.Add(new InventoryEntity
        {
            Id = 401,
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 10
        });

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Source User",
                Email = "source.submitdispatch@test.local",
                PasswordHash = "x",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 102,
                BusinessId = 1,
                Name = "Destination User",
                Email = "destination.submitdispatch@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            });

        db.UserWarehouseAssignments.AddRange(
            new UserWarehouseAssignment
            {
                UserId = 101,
                WarehouseId = 21,
                IsActive = true,
                IsPrimary = true
            },
            new UserWarehouseAssignment
            {
                UserId = 102,
                WarehouseId = 21,
                IsActive = true,
                IsPrimary = true
            });

        await db.SaveChangesAsync();
    }

    private sealed class TransferEnvelope
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
