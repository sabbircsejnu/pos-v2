using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class StockTransferActionOwnershipHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public StockTransferActionOwnershipHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SourceUser_CanApproveAndDispatchTransfer()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);

        var approveResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var dispatchResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null);
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);
    }

    [Fact]
    public async Task SourceUser_ApprovalRecordsActorWithoutMovingStock_AndRejectsDuplicateApproval()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve",
            "BusinessOwner");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        var approvalResponse = await sourceClient.PostAsync(
            $"/api/stock-transfers/{transferId}/approve",
            null);
        var approvalBody = await approvalResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferEnvelope>>();

        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        Assert.Equal(transferId, approvalBody?.Data?.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await db.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusApproved, transfer!.Status);
        Assert.Equal(101, transfer.ApprovedBy);
        Assert.Equal(101, transfer.UpdatedBy);
        Assert.NotNull(transfer.ApprovedAt);
        Assert.NotNull(transfer.UpdatedAt);
        Assert.Equal(50, (await db.Inventories.FindAsync(401L))!.Quantity);
        Assert.Empty(db.StockLedgers.Where(row =>
            row.ReferenceId == transferId
            && row.ReferenceType == StockLedgerReferenceType.StockTransfer));

        var duplicateResponse = await sourceClient.PostAsync(
            $"/api/stock-transfers/{transferId}/approve",
            null);
        var duplicateBody = await duplicateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(
            "Can only approve submitted transfers. Current status: approved",
            duplicateBody?.Message);
    }

    [Fact]
    public async Task SourceUser_CanCancelBeforeDispatch_AndRejectsDuplicateCancellation()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.cancel",
            "BusinessOwner");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        var cancelResponse = await sourceClient.PostAsJsonAsync(
            $"/api/stock-transfers/{transferId}/cancel",
            new { reason = "no longer required" });
        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferEnvelope>>();

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(transferId, cancelBody?.Data?.Id);
        Assert.Equal("Stock transfer cancelled", cancelBody?.Message);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var transfer = await db.StockTransfers.FindAsync(transferId);
            Assert.NotNull(transfer);
            Assert.Equal(StockTransfer.StatusCancelled, transfer!.Status);
            Assert.Equal(101, transfer.CancelledBy);
            Assert.Equal(101, transfer.UpdatedBy);
            Assert.NotNull(transfer.CancelledAt);
            Assert.NotNull(transfer.UpdatedAt);
            Assert.Null(transfer.Notes);
            Assert.Equal(50, (await db.Inventories.FindAsync(401L))!.Quantity);
            Assert.Empty(db.StockLedgers.Where(row =>
                row.ReferenceId == transferId
                && row.ReferenceType == StockLedgerReferenceType.StockTransfer));
        }

        var duplicateResponse = await sourceClient.PostAsJsonAsync(
            $"/api/stock-transfers/{transferId}/cancel",
            new { reason = "duplicate attempt" });
        var duplicateBody = await duplicateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal("Transfer is already cancelled", duplicateBody?.Message);
    }

    [Fact]
    public async Task SourceUser_CannotCancelAfterDispatch()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch,stock_transfers.cancel",
            "BusinessOwner");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        Assert.Equal(
            HttpStatusCode.OK,
            (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/send", null)).StatusCode);

        var cancelResponse = await sourceClient.PostAsJsonAsync(
            $"/api/stock-transfers/{transferId}/cancel",
            new { reason = "too late" });
        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, cancelResponse.StatusCode);
        Assert.Equal(
            $"Cannot cancel transfer in status '{StockTransfer.StatusInTransit}'. Cancellation is allowed only before dispatch.",
            cancelBody?.Message);
    }

    [Fact]
    public async Task SourceUser_SendDeductsStockAndWritesValuedLedgerOnce_AndRejectsDuplicateSend()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch",
            "BusinessOwner");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        Assert.Equal(
            HttpStatusCode.OK,
            (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);

        var sendResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/send", null);
        var sendBody = await sendResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferEnvelope>>();
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);
        Assert.Equal(transferId, sendBody?.Data?.Id);
        Assert.Equal("Stock transfer marked as in-transit", sendBody?.Message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await db.StockTransfers.FindAsync(transferId);
        Assert.NotNull(transfer);
        Assert.Equal(StockTransfer.StatusInTransit, transfer!.Status);
        Assert.Equal(101, transfer.DispatchedBy);
        Assert.Equal(101, transfer.UpdatedBy);
        Assert.NotNull(transfer.DispatchedAt);
        Assert.Equal(48, (await db.Inventories.FindAsync(401L))!.Quantity);

        var ledger = db.StockLedgers
            .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer
                && row.ReferenceId == transferId
                && row.TransactionType == StockLedgerTransactionType.TransferOut)
            .ToArray();
        var movement = Assert.Single(ledger);
        Assert.Equal(2, movement.QtyOut);
        Assert.Equal(48, movement.BalanceAfter);
        Assert.Equal(10m, movement.UnitCost);
        Assert.True(movement.IsCostValuationReliable);

        var duplicateResponse = await sourceClient.PostAsync(
            $"/api/stock-transfers/{transferId}/send",
            null);
        var duplicateBody = await duplicateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(
            "Can only send approved transfers. Current status: in_transit",
            duplicateBody?.Message);
    }

    [Fact]
    public async Task DestinationUser_RejectsInTransitTransferAndCreatesOneReturnDraft()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(
            101,
            "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch",
            "BusinessOwner");
        using var destinationClient = CreateClient(
            102,
            "stock_transfers.view,stock_transfers.reject_receive",
            "Manager");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        Assert.Equal(
            HttpStatusCode.OK,
            (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/send", null)).StatusCode);

        var rejectResponse = await destinationClient.PostAsJsonAsync(
            $"/api/stock-transfers/{transferId}/reject",
            new { reason = "damaged on arrival" });
        var rejectBody = await rejectResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferEnvelope>>();
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        Assert.Equal(transferId, rejectBody?.Data?.Id);
        Assert.Equal("Stock transfer rejected", rejectBody?.Message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var rejectedTransfer = await db.StockTransfers
            .Include(row => row.Items)
            .SingleAsync(row => row.Id == transferId);
        Assert.Equal(StockTransfer.StatusRejected, rejectedTransfer.Status);
        Assert.Equal(102, rejectedTransfer.RejectedBy);
        Assert.Equal(102, rejectedTransfer.UpdatedBy);
        Assert.NotNull(rejectedTransfer.RejectedAt);
        Assert.Equal(0, rejectedTransfer.Items.Single().AcceptedQuantity);
        Assert.Equal(2, rejectedTransfer.Items.Single().RejectedQuantity);

        var rejectionLedger = Assert.Single(db.StockLedgers.Where(row =>
            row.ReferenceType == StockLedgerReferenceType.StockTransfer
            && row.ReferenceId == transferId
            && row.TransactionType == StockLedgerTransactionType.TransferRejection));
        Assert.Equal(0, rejectionLedger.QtyIn);
        Assert.Equal(0, rejectionLedger.QtyOut);
        Assert.Equal(102, rejectionLedger.CreatedBy);
        Assert.Contains("damaged on arrival", rejectionLedger.Remarks);

        var returnTransfer = await db.StockTransfers
            .Include(row => row.Items)
            .SingleAsync(row =>
                row.TransferType == StockTransfer.TransferTypeReturn
                && row.FromLocationId == 21
                && row.ToLocationId == 11);
        Assert.Equal(StockTransfer.StatusDraft, returnTransfer.Status);
        Assert.Equal(102, returnTransfer.CreatedBy);
        Assert.Contains("Auto-created return transfer", returnTransfer.Notes);
        var returnItem = Assert.Single(returnTransfer.Items);
        Assert.Equal(2, returnItem.Quantity);
        Assert.Equal(2, returnItem.RequestedQuantity);
        Assert.Equal(2, returnItem.TransferQuantity);
        Assert.Equal(0m, returnItem.UnitCost);

        var duplicateResponse = await destinationClient.PostAsJsonAsync(
            $"/api/stock-transfers/{transferId}/reject",
            new { reason = "duplicate" });
        var duplicateBody = await duplicateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal(HttpStatusCode.Forbidden, duplicateResponse.StatusCode);
        Assert.Equal(
            "This transfer must be In Transit before it can be rejected.",
            duplicateBody?.Message);
    }

    [Fact]
    public async Task DestinationUser_CannotApproveTransfer_EvenWithApprovePermission()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view,stock_transfers.approve", "Manager");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);

        var response = await destinationClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only an authorized user of the source location can approve this transfer.", body?.Message);
    }

    [Fact]
    public async Task DestinationUser_CannotDispatchTransfer_EvenWithDispatchPermission()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view,stock_transfers.dispatch", "Manager");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        var approveResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var response = await destinationClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only an authorized user of the source location can dispatch this transfer.", body?.Message);
    }

    [Fact]
    public async Task DestinationUser_CanReceiveOnlyAfterInTransit()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");
        using var destinationClient = CreateClient(102, "stock_transfers.view,stock_transfers.receive", "Manager");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        var approveResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var receiveBeforeDispatch = await destinationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", BuildReceivePayload());
        var errorBody = await receiveBeforeDispatch.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, receiveBeforeDispatch.StatusCode);
        Assert.Equal("This transfer must be In Transit before it can be received.", errorBody?.Message);

        var dispatchResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null);
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);

        var receiveAfterDispatch = await destinationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", BuildReceivePayload());
        Assert.Equal(HttpStatusCode.OK, receiveAfterDispatch.StatusCode);
    }

    [Fact]
    public async Task UserFromAnotherLocation_CannotReceiveTransfer()
    {
        await ResetAndSeedAsync();
        using var sourceClient = CreateClient(101, "stock_transfers.view,stock_transfers.create,stock_transfers.approve,stock_transfers.dispatch", "BusinessOwner");
        using var otherLocationClient = CreateClient(103, "stock_transfers.view,stock_transfers.receive", "Manager");

        var transferId = await CreateAndSubmitTransferAsync(sourceClient);
        var approveResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var dispatchResponse = await sourceClient.PostAsync($"/api/stock-transfers/{transferId}/dispatch", null);
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);

        var response = await otherLocationClient.PostAsJsonAsync($"/api/stock-transfers/{transferId}/receive", BuildReceivePayload());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only an authorized user of the destination location can receive this transfer.", body?.Message);
    }

    private async Task<long> CreateAndSubmitTransferAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/stock-transfers", BuildTransferCreatePayload());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<StockTransferEnvelope>>();
        var transferId = created?.Data?.Id ?? 0;
        Assert.True(transferId > 0);

        var submitResponse = await client.PostAsync($"/api/stock-transfers/{transferId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        return transferId;
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
                    quantity = 2,
                    requestedQuantity = 2,
                    transferQuantity = 2,
                    unitCost = 10.0m,
                    remarks = "ownership-test"
                }
            }
        };
    }

    private static object BuildReceivePayload()
    {
        return new
        {
            notes = "received",
            items = new[]
            {
                new { variantId = 301L, acceptedQuantity = 2, rejectedQuantity = 0, remarks = "ok" }
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

        db.Businesses.Add(new Business { Id = 1, Name = "Ownership Test Business" });
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Ownership Category", Description = "Ownership tests" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Source Outlet", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Other Outlet", Address = "B" });

        db.Warehouses.AddRange(
            new Warehouse { Id = 21, BusinessId = 1, Name = "Destination Warehouse", Address = "W1" },
            new Warehouse { Id = 22, BusinessId = 1, Name = "Other Warehouse", Address = "W2" });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Ownership Product",
            ProductCode = "OWN-001",
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
            Name = "Ownership Variant",
            Sku = "OWN-001-DEF",
            Barcode = "OWN-BC-001",
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

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Source User",
                Email = "source.user@test.local",
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
                Email = "destination.user@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 103,
                BusinessId = 1,
                Name = "Other User",
                Email = "other.user@test.local",
                PasswordHash = "x",
                OutletId = 12,
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

        await db.SaveChangesAsync();
    }

    private sealed class StockTransferEnvelope
    {
        public long Id { get; set; }
    }
}
