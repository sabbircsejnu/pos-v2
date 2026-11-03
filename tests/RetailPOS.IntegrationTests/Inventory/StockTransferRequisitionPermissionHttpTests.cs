using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.StockRequisition;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;
using RetailPOS.Application.Inventory.StockTransfers;
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
    public async Task TransferCreate_PersistsDraftMetadataAndQuantityFallbackWithoutChangingInventory()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_transfers.view,stock_transfers.create", role: "BusinessOwner");

        var response = await client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = " DIRECT ",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = DateTime.UtcNow.Date,
            notes = "creation-characterization",
            items = new[]
            {
                new { variantId = 301L, quantity = 3, unitCost = 12.5m, remarks = "line" }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var transfer = await db.StockTransfers
            .Include(candidate => candidate.Items)
            .SingleAsync();

        Assert.StartsWith($"TRF-{DateTime.UtcNow.Year}-", transfer.TransferNo);
        Assert.Equal("direct", transfer.TransferType);
        Assert.Equal(StockTransfer.StatusDraft, transfer.Status);
        Assert.Equal(11, transfer.FromLocationId);
        Assert.Equal("outlet", transfer.FromLocationType);
        Assert.Equal(21, transfer.ToLocationId);
        Assert.Equal("warehouse", transfer.ToLocationType);
        Assert.Equal("creation-characterization", transfer.Notes);
        Assert.Equal(101, transfer.CreatedBy);

        var item = Assert.Single(transfer.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(3, item.RequestedQuantity);
        Assert.Equal(3, item.TransferQuantity);
        Assert.Equal(12.5m, item.UnitCost);
        Assert.Equal("line", item.Remarks);
        Assert.Equal(50, (await db.Inventories.FindAsync(401L))?.Quantity);
        Assert.Empty(db.StockLedgers);
    }

    [Fact]
    public async Task TransferCreate_WithInsufficientSourceStock_DoesNotPersistTransferOrChangeInventory()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var inventory = await db.Inventories.FindAsync(401L);
            Assert.NotNull(inventory);
            inventory!.Quantity = 0;
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(101, permissions: "stock_transfers.view,stock_transfers.create", role: "BusinessOwner");
        var response = await client.PostAsJsonAsync("/api/stock-transfers", BuildTransferCreatePayload());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Insufficient stock for variant POL-001-DEF. Available: 0, Requested: 1", body?.Message ?? string.Empty);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(verifyDb.StockTransfers);
        Assert.Equal(0, (await verifyDb.Inventories.FindAsync(401L))?.Quantity);
        Assert.Empty(verifyDb.StockLedgers);
    }

    [Fact]
    public async Task TransferCreationStore_RejectsLocationsOutsideTrustedBusinessScope()
    {
        await ResetAndSeedAsync();
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStockTransferCreationStore>();

        var input = new StockTransferCreateInput(
            "direct",
            null,
            11,
            "outlet",
            21,
            "warehouse",
            DateTime.UtcNow.Date,
            null,
            [new StockTransferCreateItem(301, 1, 1, 1, 10m, null)]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CreateAsync(input, businessId: 2, createdBy: 101, cancellationToken: CancellationToken.None));

        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(db.StockTransfers);
        Assert.Equal(50, (await db.Inventories.FindAsync(401L))?.Quantity);
    }

    [Fact]
    public async Task TransferUpdate_Draft_ReplacesHeaderAndLines()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            101,
            permissions: "stock_transfers.view,stock_transfers.create,stock_transfers.edit",
            role: "BusinessOwner");
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/stock-transfers", BuildTransferCreatePayload())).StatusCode);

        long transferId;
        long originalItemId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var created = await db.StockTransfers.Include(transfer => transfer.Items).SingleAsync();
            transferId = created.Id;
            originalItemId = Assert.Single(created.Items).Id;
        }

        var updatedDate = DateTime.UtcNow.Date.AddDays(-1);
        var response = await client.PutAsJsonAsync($"/api/stock-transfers/{transferId}", new
        {
            transferType = "return",
            fromLocationId = 11L,
            fromLocationType = "outlet",
            toLocationId = 21L,
            toLocationType = "warehouse",
            transferDate = updatedDate,
            notes = "updated-draft",
            items = new[]
            {
                new
                {
                    variantId = 301L,
                    quantity = 2,
                    requestedQuantity = 4,
                    transferQuantity = 2,
                    unitCost = 12.5m,
                    remarks = "replacement"
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var updated = await verifyDb.StockTransfers
            .Include(transfer => transfer.Items)
            .SingleAsync(transfer => transfer.Id == transferId);
        Assert.Equal("return", updated.TransferType);
        Assert.Equal(updatedDate, updated.TransferDate);
        Assert.Equal("updated-draft", updated.Notes);
        Assert.Equal(StockTransfer.StatusDraft, updated.Status);
        var item = Assert.Single(updated.Items);
        Assert.NotEqual(originalItemId, item.Id);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(4, item.RequestedQuantity);
        Assert.Equal(2, item.TransferQuantity);
        Assert.Equal("replacement", item.Remarks);
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
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionDto>>();
        Assert.NotNull(body?.Data);
        Assert.Equal("draft", body.Data.Status);
        Assert.Equal(101, body.Data.RequestedBy);
        Assert.Equal("Main Outlet", body.Data.RequestingLocationName);
        Assert.Equal("Main Warehouse", body.Data.SourceLocationName);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await db.StockRequisitions
            .Include(candidate => candidate.Lines)
            .SingleAsync();
        Assert.StartsWith($"REQ-{DateTime.UtcNow.Year}-", requisition.RequisitionNo);
        Assert.Equal("policy-test", requisition.Notes);
        Assert.Equal(StockRequisition.StatusDraft, requisition.Status);
        Assert.Equal(101, requisition.RequestedBy);
        var line = Assert.Single(requisition.Lines);
        Assert.Equal(301, line.VariantId);
        Assert.Equal(2, line.RequestedQuantity);
        Assert.Equal(0, line.FulfilledQuantity);
        Assert.Equal("policy-test", line.Remarks);
    }

    [Fact]
    public async Task RequisitionUpdate_Draft_ReplacesHeaderAndLines()
    {
        await ResetAndSeedAsync();
        long requisitionId;
        long oldLineId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var requisition = new StockRequisition
            {
                Id = 501,
                RequisitionNo = "REQ-TEST-501",
                RequestingLocationId = 11,
                RequestingLocationType = "outlet",
                SourceLocationId = 21,
                SourceLocationType = "warehouse",
                RequestedBy = 101,
                RequestDate = DateTime.UtcNow,
                Status = StockRequisition.StatusDraft,
                Notes = "before",
                CreatedAt = DateTime.UtcNow,
                Lines =
                [
                    new StockRequisitionLine
                    {
                        Id = 601,
                        VariantId = 301,
                        RequestedQuantity = 2,
                        FulfilledQuantity = 0,
                        Remarks = "old-line"
                    }
                ]
            };
            db.StockRequisitions.Add(requisition);
            await db.SaveChangesAsync();
            requisitionId = requisition.Id;
            oldLineId = Assert.Single(requisition.Lines).Id;
        }

        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.edit",
            role: "BusinessOwner");
        var response = await client.PutAsJsonAsync($"/api/stock-requisitions/{requisitionId}", new
        {
            requestingLocationId = 11L,
            requestingLocationType = "outlet",
            sourceLocationId = 21L,
            sourceLocationType = "warehouse",
            notes = "after",
            lines = new[]
            {
                new { variantId = 301L, requestedQuantity = 4, remarks = "new-line" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionDto>>();
        Assert.NotNull(body?.Data);
        Assert.Equal(requisitionId, body.Data.Id);
        Assert.Equal("after", body.Data.Notes);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var updated = await verifyDb.StockRequisitions
            .Include(candidate => candidate.Lines)
            .SingleAsync();
        Assert.Equal("draft", updated.Status);
        Assert.Equal(101, updated.UpdatedBy);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal("after", updated.Notes);
        var updatedLine = Assert.Single(updated.Lines);
        Assert.NotEqual(oldLineId, updatedLine.Id);
        Assert.Equal(4, updatedLine.RequestedQuantity);
        Assert.Equal(0, updatedLine.FulfilledQuantity);
        Assert.Equal("new-line", updatedLine.Remarks);
    }

    [Fact]
    public async Task RequisitionWriteStore_Create_RejectsLocationsOutsideTrustedBusiness()
    {
        await ResetAndSeedAsync();
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStockRequisitionWriteStore>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateAsync(
            new StockRequisitionWriteInput(
                11,
                "outlet",
                21,
                "warehouse",
                null,
                [new StockRequisitionWriteLineInput(301, 1, null)]),
            businessId: 2,
            requestedBy: 101,
            cancellationToken: CancellationToken.None));

        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(db.StockRequisitions);
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
    public async Task RequisitionSubmit_Draft_RecordsActorAndTimestamp()
    {
        await ResetAndSeedAsync();
        await SeedRequisitionAsync(504, StockRequisition.StatusDraft);
        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.create",
            role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-requisitions/504/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionDto>>();
        Assert.Equal("Stock requisition submitted", body?.Message);
        Assert.Equal(StockRequisition.StatusSubmitted, body?.Data?.Status);
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await db.StockRequisitions.SingleAsync();
        Assert.Equal(StockRequisition.StatusSubmitted, requisition.Status);
        Assert.Equal(101, requisition.SubmittedBy);
        Assert.NotNull(requisition.SubmittedAt);
        Assert.Equal(101, requisition.UpdatedBy);
        Assert.NotNull(requisition.UpdatedAt);
    }

    [Fact]
    public async Task RequisitionSubmit_SequentialDuplicate_ReturnsEstablishedEligibilityError()
    {
        await ResetAndSeedAsync();
        await SeedRequisitionAsync(504, StockRequisition.StatusDraft);
        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.create",
            role: "BusinessOwner");

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/api/stock-requisitions/504/submit", null)).StatusCode);
        var duplicateResponse = await client.PostAsync("/api/stock-requisitions/504/submit", null);
        var body = await duplicateResponse.Content.ReadFromJsonAsync<ApiResponse<object>>();

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Contains("Can only submit draft requisitions. Current status: submitted", body?.Message);
    }

    [Fact]
    public async Task RequisitionApprove_Submitted_RecordsActorAndTimestamp()
    {
        await ResetAndSeedAsync();
        await SeedRequisitionAsync(505, StockRequisition.StatusSubmitted);
        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.approve",
            role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-requisitions/505/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionDto>>();
        Assert.Equal("Stock requisition approved", body?.Message);
        Assert.Equal(StockRequisition.StatusApproved, body?.Data?.Status);
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await db.StockRequisitions.SingleAsync();
        Assert.Equal(StockRequisition.StatusApproved, requisition.Status);
        Assert.Equal(101, requisition.ApprovedBy);
        Assert.NotNull(requisition.ApprovedAt);
        Assert.Equal(101, requisition.UpdatedBy);
        Assert.NotNull(requisition.UpdatedAt);
    }

    [Fact]
    public async Task RequisitionReject_Submitted_PersistsReasonAndActor()
    {
        await ResetAndSeedAsync();
        await SeedRequisitionAsync(506, StockRequisition.StatusSubmitted);
        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.reject",
            role: "BusinessOwner");

        var response = await client.PostAsJsonAsync(
            "/api/stock-requisitions/506/reject",
            new { reason = "Quantity needs correction" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionDto>>();
        Assert.Equal("Stock requisition rejected", body?.Message);
        Assert.Equal(StockRequisition.StatusRejected, body?.Data?.Status);
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await db.StockRequisitions.SingleAsync();
        Assert.Equal(StockRequisition.StatusRejected, requisition.Status);
        Assert.Equal(101, requisition.RejectedBy);
        Assert.NotNull(requisition.RejectedAt);
        Assert.Equal("Quantity needs correction", requisition.RejectionReason);
        Assert.Equal(101, requisition.UpdatedBy);
        Assert.NotNull(requisition.UpdatedAt);
    }

    [Fact]
    public async Task RequisitionConvert_WithoutConvertPermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");

        var response = await client.PostAsync("/api/stock-requisitions/999/convert-to-transfer", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionRead_ListAndSearch_PreserveFilteringPagingAndDtoMapping()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockRequisitions.AddRange(
                new StockRequisition
                {
                    Id = 501,
                    RequisitionNo = "REQ-TEST-501",
                    RequestingLocationId = 11,
                    RequestingLocationType = "outlet",
                    SourceLocationId = 21,
                    SourceLocationType = "warehouse",
                    RequestedBy = 101,
                    RequestDate = new DateTime(2026, 1, 1),
                    Status = StockRequisition.StatusDraft,
                    CreatedAt = new DateTime(2026, 1, 1),
                    Lines =
                    [
                        new StockRequisitionLine
                        {
                            Id = 601,
                            VariantId = 301,
                            RequestedQuantity = 5,
                            FulfilledQuantity = 2,
                            Remarks = "read-test"
                        }
                    ]
                },
                new StockRequisition
                {
                    Id = 502,
                    RequisitionNo = "REQ-TEST-502",
                    RequestingLocationId = 11,
                    RequestingLocationType = "outlet",
                    SourceLocationId = 21,
                    SourceLocationType = "warehouse",
                    RequestedBy = 101,
                    RequestDate = new DateTime(2026, 1, 2),
                    Status = StockRequisition.StatusApproved,
                    CreatedAt = new DateTime(2026, 1, 2)
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");

        var listResponse = await client.GetAsync("/api/stock-requisitions?status=draft");
        Assert.True(listResponse.StatusCode == HttpStatusCode.OK,
            await listResponse.Content.ReadAsStringAsync());
        var list = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<StockRequisitionDto>>>();
        var listed = Assert.Single(Assert.IsType<List<StockRequisitionDto>>(list?.Data));
        Assert.Equal("Main Outlet", listed.RequestingLocationName);
        Assert.Equal("Main Warehouse", listed.SourceLocationName);
        Assert.Equal("Policy User", listed.RequesterName);
        var line = Assert.Single(listed.Lines);
        Assert.Equal(5, line.RequestedQuantity);
        Assert.Equal(2, line.FulfilledQuantity);
        Assert.Equal(3, line.PendingQuantity);
        Assert.Equal("POL-001-DEF", line.VariantSku);

        var searchResponse = await client.PostAsJsonAsync("/api/stock-requisitions/search", new
        {
            status = "draft",
            requestingLocationId = 11L,
            requestingLocationType = "outlet",
            pageNumber = 1,
            pageSize = 1
        });
        Assert.True(searchResponse.StatusCode == HttpStatusCode.OK,
            await searchResponse.Content.ReadAsStringAsync());
        var search = await searchResponse.Content.ReadFromJsonAsync<ApiResponse<StockRequisitionListDto>>();
        Assert.NotNull(search?.Data);
        Assert.Equal(1, search.Data.TotalCount);
        Assert.Equal(1, search.Data.PageNumber);
        Assert.Equal(1, search.Data.PageSize);
        Assert.Equal(501, Assert.Single(search.Data.StockRequisitions).Id);

        var byIdResponse = await client.GetAsync("/api/stock-requisitions/501");
        Assert.Equal(HttpStatusCode.OK, byIdResponse.StatusCode);
    }

    [Fact]
    public async Task RequisitionRead_ById_DoesNotExposeAnotherBusinessesRequisition()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = 2, Name = "Other Business" });
            db.Outlets.Add(new Outlet
            {
                Id = 12,
                BusinessId = 2,
                Name = "Other Outlet",
                Address = "B"
            });
            db.Warehouses.Add(new Warehouse
            {
                Id = 22,
                BusinessId = 2,
                Name = "Other Warehouse",
                Address = "B"
            });
            db.StockRequisitions.Add(new StockRequisition
            {
                Id = 503,
                RequisitionNo = "REQ-OTHER-503",
                RequestingLocationId = 12,
                RequestingLocationType = "outlet",
                SourceLocationId = 22,
                SourceLocationType = "warehouse",
                RequestedBy = 101,
                RequestDate = DateTime.UtcNow,
                Status = StockRequisition.StatusDraft,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(101, permissions: "stock_requisitions.view", role: "BusinessOwner");
        var response = await client.GetAsync("/api/stock-requisitions/503");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequisitionConvert_ApprovedRequisition_CreatesTransferAndFulfillsPendingQuantity()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockRequisitions.Add(new StockRequisition
            {
                Id = 501,
                RequisitionNo = "REQ-TEST-501",
                RequestingLocationId = 11,
                RequestingLocationType = "outlet",
                SourceLocationId = 21,
                SourceLocationType = "warehouse",
                RequestedBy = 101,
                RequestDate = DateTime.UtcNow,
                Status = StockRequisition.StatusApproved,
                CreatedAt = DateTime.UtcNow,
                Lines =
                [
                    new StockRequisitionLine
                    {
                        Id = 601,
                        VariantId = 301,
                        RequestedQuantity = 5,
                        FulfilledQuantity = 2,
                        Remarks = "pending quantity"
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(
            101,
            permissions: "stock_requisitions.view,stock_requisitions.convert_to_transfer",
            role: "BusinessOwner");
        var response = await client.PostAsync("/api/stock-requisitions/501/convert-to-transfer", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<StockTransferDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Transfer created from requisition", body?.Message);
        var data = Assert.IsType<StockTransferDto>(body?.Data);
        Assert.Equal("requisition", data.TransferType);
        Assert.Equal(501, data.RelatedRequisitionId);
        Assert.Equal(21, data.FromLocationId);
        Assert.Equal(11, data.ToLocationId);
        var item = Assert.Single(data.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(5, item.RequestedQuantity);
        Assert.Equal(3, item.TransferQuantity);
        Assert.Equal(0, item.AcceptedQuantity);
        Assert.Equal(0, item.RejectedQuantity);
        Assert.Equal(0m, item.UnitCost);
        Assert.Equal("pending quantity", item.Remarks);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var requisition = await verifyDb.StockRequisitions
            .Include(row => row.Lines)
            .SingleAsync(row => row.Id == 501);
        Assert.Equal(StockRequisition.StatusFullyFulfilled, requisition.Status);
        Assert.Equal(5, Assert.Single(requisition.Lines).FulfilledQuantity);
        var transfer = await verifyDb.StockTransfers
            .Include(row => row.Items)
            .SingleAsync(row => row.RelatedRequisitionId == 501);
        Assert.Equal(data.Id, transfer.Id);
        Assert.Equal(StockTransfer.StatusDraft, transfer.Status);
        Assert.Equal(101, transfer.CreatedBy);
        Assert.Empty(verifyDb.StockLedgers);
        Assert.Equal(50, (await verifyDb.Inventories.FindAsync(401L))?.Quantity);
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
        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Policy Category", Description = "Policy tests" });

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
            BusinessId = 1,
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
            BusinessId = 1,
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

    private async Task SeedRequisitionAsync(long id, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.StockRequisitions.Add(new StockRequisition
        {
            Id = id,
            RequisitionNo = $"REQ-TEST-{id}",
            RequestingLocationId = 11,
            RequestingLocationType = "outlet",
            SourceLocationId = 21,
            SourceLocationType = "warehouse",
            RequestedBy = 101,
            RequestDate = DateTime.UtcNow,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
