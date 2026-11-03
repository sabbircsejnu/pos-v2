using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.DTOs.Reports;
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
        Assert.Equal(321, created.CreatedBy);
        Assert.Equal(new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc), created.OrderDate);
        Assert.Equal(2, created.Items.Count);

        // 3*12.50 with 10% discount and 5% tax = 35.44
        // 2*20.00 with 0% discount and 0% tax = 40.00
        Assert.Equal(75.44m, created.TotalAmount);
    }

    [Fact]
    public async Task CreatePurchaseOrder_CannotUseWarehouseOutsideBusinessScope()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient(businessId: 2);

        var response = await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreateRequest(status: "draft"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PurchaseOrders.ToListAsync());
    }

    [Fact]
    public async Task PurchaseReports_AggregateWithinBusinessAndDateScope()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business
            {
                Id = 2,
                Name = "Other Business",
                IsActive = true
            });
            db.Suppliers.Add(new Supplier
            {
                Id = 1002,
                BusinessId = 2,
                Name = "Other Supplier",
                Contact = "01700000002",
                Address = "Other Address"
            });
            db.Warehouses.Add(new Warehouse
            {
                Id = 1102,
                Name = "Other Warehouse",
                Address = "Other Address",
                BusinessId = 2
            });
            db.PurchaseOrders.AddRange(
                ReportOrder(1401, "PO-RPT-1401", 1001, 1101, "pending", 25m, 2026, 6, 10),
                ReportOrder(1402, "PO-RPT-1402", 1001, 1101, "received", 50m, 2026, 6, 20),
                ReportOrder(1403, "PO-RPT-1403", 1001, 1101, "pending", 100m, 2026, 7, 1),
                ReportOrder(1404, "PO-RPT-1404", 1002, 1102, "pending", 400m, 2026, 6, 15));
            await db.SaveChangesAsync();
        }

        using var client = CreatePurchaseClient();
        var dateRange = "startDate=2026-06-01&endDate=2026-06-30";
        var summaryResponse = await client.GetAsync(
            $"/api/reports/purchases/summary?{dateRange}");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await ReadApiData<PurchaseSummaryDto>(summaryResponse);
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.TotalOrders);
        Assert.Equal(75m, summary.TotalAmount);
        Assert.Equal(1, summary.PendingApprovals);
        Assert.Equal(1, summary.ReceivedOrders);

        var supplierResponse = await client.GetAsync(
            $"/api/reports/purchases/by-supplier?{dateRange}");
        Assert.Equal(HttpStatusCode.OK, supplierResponse.StatusCode);
        var suppliers = await ReadApiData<List<PurchaseBySupplierDto>>(supplierResponse);
        var supplier = Assert.Single(suppliers!);
        Assert.Equal(1001, supplier.SupplierId);
        Assert.Equal("Acme Supplier", supplier.SupplierName);
        Assert.Equal(2, supplier.OrderCount);
        Assert.Equal(75m, supplier.TotalAmount);
    }

    [Fact]
    public async Task UpdatePurchaseOrder_ReplacesDraftItemsAndPreservesStatusRulesAndBusinessScope()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient(canEdit: true);
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var updateRequest = new UpdatePurchaseOrderDto
        {
            SupplierId = 1001,
            WarehouseId = 1101,
            OrderDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            ExpectedDelivery = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Notes = "Revised draft",
            Items =
            [
                new CreatePurchaseOrderItemDto
                {
                    VariantId = 1302,
                    Quantity = 4,
                    UnitPrice = 10m,
                    Discount = 0m,
                    Tax = 0m,
                    Unit = "box"
                }
            ]
        };

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/purchase-orders/{draft.Id}",
            updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadApiData<PurchaseOrderDto>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal(draft.Id, updated!.Id);
        Assert.Equal(draft.PoNumber, updated.PoNumber);
        Assert.Equal("draft", updated.Status, ignoreCase: true);
        Assert.Equal(updateRequest.OrderDate, updated.OrderDate);
        Assert.Equal(updateRequest.ExpectedDelivery, updated.ExpectedDelivery);
        Assert.Equal("Revised draft", updated.Notes);
        Assert.Equal(40m, updated.TotalAmount);
        var updatedItem = Assert.Single(updated.Items);
        Assert.Equal(1302, updatedItem.VariantId);
        Assert.Equal(4, updatedItem.Quantity);
        Assert.Equal("box", updatedItem.Unit);

        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var blockedResponse = await client.PutAsJsonAsync(
            $"/api/purchase-orders/{pending.Id}",
            updateRequest);
        Assert.Equal(HttpStatusCode.BadRequest, blockedResponse.StatusCode);
        using var blockedDocument = await JsonDocument.ParseAsync(
            await blockedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Cannot update purchase order in 'pending' status. Only draft or sent-back orders can be edited.",
            blockedDocument.RootElement.GetProperty("message").GetString());

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2, canEdit: true);
        var outOfScopeResponse = await otherBusinessClient.PutAsJsonAsync(
            $"/api/purchase-orders/{draft.Id}",
            updateRequest);
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);
    }

    [Fact]
    public async Task SubmitPurchaseOrder_TransitionsAllowedStatusesAndPreservesScopeAndErrors()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var sentBack = await CreatePurchaseOrderAsync(
            client,
            "sent_back",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var sentBackOrder = await db.PurchaseOrders.SingleAsync(order => order.Id == sentBack.Id);
            sentBackOrder.RejectionReason = "Correct the quantity";
            await db.SaveChangesAsync();
        }

        var draftSubmitResponse = await client.PostAsync(
            $"/api/purchase-orders/{draft.Id}/submit",
            content: null);
        Assert.Equal(HttpStatusCode.OK, draftSubmitResponse.StatusCode);
        var draftSubmitted = await ReadApiData<PurchaseOrderDto>(draftSubmitResponse);
        Assert.NotNull(draftSubmitted);
        Assert.Equal("pending", draftSubmitted!.Status, ignoreCase: true);

        var sentBackSubmitResponse = await client.PostAsync(
            $"/api/purchase-orders/{sentBack.Id}/submit",
            content: null);
        Assert.Equal(HttpStatusCode.OK, sentBackSubmitResponse.StatusCode);
        var sentBackSubmitted = await ReadApiData<PurchaseOrderDto>(sentBackSubmitResponse);
        Assert.NotNull(sentBackSubmitted);
        Assert.Equal("pending", sentBackSubmitted!.Status, ignoreCase: true);
        Assert.Null(sentBackSubmitted.RejectionReason);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var outOfScopeResponse = await otherBusinessClient.PostAsync(
            $"/api/purchase-orders/{draft.Id}/submit",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);

        var alreadyPending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var rejectedResponse = await client.PostAsync(
            $"/api/purchase-orders/{alreadyPending.Id}/submit",
            content: null);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        using var rejectedDocument = await JsonDocument.ParseAsync(
            await rejectedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Can only submit draft or sent-back purchase orders. Current status: pending",
            rejectedDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ApprovePurchaseOrder_TransitionsPendingOrderAndPreservesScopeAndErrors()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        var approvedResponse = await client.PostAsync(
            $"/api/purchase-orders/{pending.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        using var approvedDocument = await JsonDocument.ParseAsync(
            await approvedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Purchase order approved",
            approvedDocument.RootElement.GetProperty("message").GetString());
        var approved = JsonSerializer.Deserialize<PurchaseOrderDto>(
            approvedDocument.RootElement.GetProperty("data").GetRawText(),
            JsonOptions);
        Assert.NotNull(approved);
        Assert.Equal(pending.Id, approved!.Id);
        Assert.Equal("approved", approved.Status, ignoreCase: true);
        Assert.Equal(pending.TotalAmount, approved.TotalAmount);
        Assert.Equal(pending.Items.Count, approved.Items.Count);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persisted = await db.PurchaseOrders.SingleAsync(order => order.Id == pending.Id);
            Assert.Equal("approved", persisted.Status, ignoreCase: true);
            Assert.True(persisted.UpdatedAt > DateTime.MinValue);
        }

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var outOfScopeResponse = await otherBusinessClient.PostAsync(
            $"/api/purchase-orders/{pending.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);

        var rejectedResponse = await client.PostAsync(
            $"/api/purchase-orders/{draft.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        using var rejectedDocument = await JsonDocument.ParseAsync(
            await rejectedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Can only approve pending purchase orders. Current status: draft",
            rejectedDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RejectPurchaseOrder_StoresReasonAndPreservesScopeAndStatusRules()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pendingWithoutReason = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{pending.Id}/reject",
            new UpdatePurchaseOrderStatusDto { Reason = "Price is above approval limit" });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        using var rejectDocument = await JsonDocument.ParseAsync(
            await rejectResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Purchase order rejected",
            rejectDocument.RootElement.GetProperty("message").GetString());
        var rejected = JsonSerializer.Deserialize<PurchaseOrderDto>(
            rejectDocument.RootElement.GetProperty("data").GetRawText(),
            JsonOptions);
        Assert.NotNull(rejected);
        Assert.Equal("rejected", rejected!.Status, ignoreCase: true);
        Assert.Equal("Price is above approval limit", rejected.RejectionReason);

        var defaultReasonResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{pendingWithoutReason.Id}/reject",
            new UpdatePurchaseOrderStatusDto());
        Assert.Equal(HttpStatusCode.OK, defaultReasonResponse.StatusCode);
        var defaultReasonOrder = await ReadApiData<PurchaseOrderDto>(defaultReasonResponse);
        Assert.NotNull(defaultReasonOrder);
        Assert.Equal("No reason provided", defaultReasonOrder!.RejectionReason);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var outOfScopeResponse = await otherBusinessClient.PostAsJsonAsync(
            $"/api/purchase-orders/{pending.Id}/reject",
            new UpdatePurchaseOrderStatusDto { Reason = "Other tenant" });
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);

        var invalidStatusResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{draft.Id}/reject",
            new UpdatePurchaseOrderStatusDto { Reason = "Not pending" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
        using var invalidStatusDocument = await JsonDocument.ParseAsync(
            await invalidStatusResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Can only reject pending purchase orders. Current status: draft",
            invalidStatusDocument.RootElement.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.PurchaseOrders.SingleAsync(order => order.Id == pending.Id);
        Assert.Equal("rejected", persisted.Status, ignoreCase: true);
        Assert.Equal("Price is above approval limit", persisted.RejectionReason);
        Assert.True(persisted.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task CancelPurchaseOrder_AllowsExpectedStatusesAndPreservesScopeAndErrors()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var sentBack = await CreatePurchaseOrderAsync(
            client,
            "sent_back",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var approved = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var rejected = await CreatePurchaseOrderAsync(
            client,
            "rejected",
            new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var approvedOrder = await db.PurchaseOrders.SingleAsync(order => order.Id == approved.Id);
            approvedOrder.Status = "approved";
            await db.SaveChangesAsync();
        }

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var outOfScopeResponse = await otherBusinessClient.PostAsJsonAsync(
            $"/api/purchase-orders/{sentBack.Id}/cancel",
            new UpdatePurchaseOrderStatusDto { Reason = "Other tenant" });
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);

        var cancelableOrders = new[] { draft, pending, sentBack, approved };
        foreach (var (order, index) in cancelableOrders.Select((order, index) => (order, index)))
        {
            var reason = index == 0 ? null : $"Cancellation {index}";
            var response = await client.PostAsJsonAsync(
                $"/api/purchase-orders/{order.Id}/cancel",
                new UpdatePurchaseOrderStatusDto { Reason = reason });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync());
            Assert.Equal(
                "Purchase order cancelled",
                document.RootElement.GetProperty("message").GetString());
            var cancelled = JsonSerializer.Deserialize<PurchaseOrderDto>(
                document.RootElement.GetProperty("data").GetRawText(),
                JsonOptions);
            Assert.NotNull(cancelled);
            Assert.Equal("cancelled", cancelled!.Status, ignoreCase: true);
            Assert.Equal(reason ?? "No reason provided", cancelled.RejectionReason);
        }

        var invalidStatusResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{rejected.Id}/cancel",
            new UpdatePurchaseOrderStatusDto { Reason = "Not cancelable" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
        using var invalidStatusDocument = await JsonDocument.ParseAsync(
            await invalidStatusResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Cannot cancel a purchase order in 'rejected' status",
            invalidStatusDocument.RootElement.GetProperty("message").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            foreach (var order in cancelableOrders)
            {
                var persisted = await db.PurchaseOrders.SingleAsync(item => item.Id == order.Id);
                Assert.Equal("cancelled", persisted.Status, ignoreCase: true);
                Assert.True(persisted.UpdatedAt > DateTime.MinValue);
            }
            var notCancelled = await db.PurchaseOrders.SingleAsync(item => item.Id == rejected.Id);
            Assert.Equal("rejected", notCancelled.Status, ignoreCase: true);
        }
    }

    [Fact]
    public async Task SendBackPurchaseOrder_StoresReasonAndPreservesScopeAndStatusRules()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pendingWithoutReason = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        var sendBackResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{pending.Id}/send-back",
            new UpdatePurchaseOrderStatusDto { Reason = "Correct quantities" });
        Assert.Equal(HttpStatusCode.OK, sendBackResponse.StatusCode);
        using var sendBackDocument = await JsonDocument.ParseAsync(
            await sendBackResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Purchase order sent back for correction",
            sendBackDocument.RootElement.GetProperty("message").GetString());
        var sentBack = JsonSerializer.Deserialize<PurchaseOrderDto>(
            sendBackDocument.RootElement.GetProperty("data").GetRawText(),
            JsonOptions);
        Assert.NotNull(sentBack);
        Assert.Equal("sent_back", sentBack!.Status, ignoreCase: true);
        Assert.Equal("Correct quantities", sentBack.RejectionReason);

        var defaultReasonResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{pendingWithoutReason.Id}/send-back",
            new UpdatePurchaseOrderStatusDto());
        Assert.Equal(HttpStatusCode.OK, defaultReasonResponse.StatusCode);
        var defaultReasonOrder = await ReadApiData<PurchaseOrderDto>(defaultReasonResponse);
        Assert.NotNull(defaultReasonOrder);
        Assert.Equal("No reason provided", defaultReasonOrder!.RejectionReason);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var outOfScopeResponse = await otherBusinessClient.PostAsJsonAsync(
            $"/api/purchase-orders/{pending.Id}/send-back",
            new UpdatePurchaseOrderStatusDto { Reason = "Other tenant" });
        Assert.Equal(HttpStatusCode.NotFound, outOfScopeResponse.StatusCode);

        var invalidStatusResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/{draft.Id}/send-back",
            new UpdatePurchaseOrderStatusDto { Reason = "Not pending" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
        using var invalidStatusDocument = await JsonDocument.ParseAsync(
            await invalidStatusResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Can only send back pending purchase orders. Current status: draft",
            invalidStatusDocument.RootElement.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.PurchaseOrders.SingleAsync(order => order.Id == pending.Id);
        Assert.Equal("sent_back", persisted.Status, ignoreCase: true);
        Assert.Equal("Correct quantities", persisted.RejectionReason);
        Assert.True(persisted.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task DeletePurchaseOrder_RequiresDraftOrPendingWithoutGrnsAndBusinessOwnership()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient(canEdit: true);
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pendingWithGrn = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var rejected = await CreatePurchaseOrderAsync(
            client,
            "rejected",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Grns.Add(new Grn
            {
                PoId = pendingWithGrn.Id,
                ReceivedDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = "partial"
            });
            await db.SaveChangesAsync();
        }

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2, canEdit: true);
        var outOfScopeResponse = await otherBusinessClient.DeleteAsync(
            $"/api/purchase-orders/{draft.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, outOfScopeResponse.StatusCode);
        using (var errorDocument = await JsonDocument.ParseAsync(
                   await outOfScopeResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(
                "Cannot delete purchase order: either it's not in draft/pending status or has associated GRNs",
                errorDocument.RootElement.GetProperty("message").GetString());
        }

        var grnBlockedResponse = await client.DeleteAsync(
            $"/api/purchase-orders/{pendingWithGrn.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, grnBlockedResponse.StatusCode);
        var statusBlockedResponse = await client.DeleteAsync(
            $"/api/purchase-orders/{rejected.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, statusBlockedResponse.StatusCode);

        var deletedResponse = await client.DeleteAsync($"/api/purchase-orders/{draft.Id}");
        Assert.Equal(HttpStatusCode.OK, deletedResponse.StatusCode);
        using var deletedDocument = await JsonDocument.ParseAsync(
            await deletedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Purchase order deleted successfully",
            deletedDocument.RootElement.GetProperty("message").GetString());

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null(await verificationDb.PurchaseOrders.FindAsync(draft.Id));
        Assert.Empty(await verificationDb.PurchaseOrderItems.Where(item => item.PoId == draft.Id).ToListAsync());
        Assert.NotNull(await verificationDb.PurchaseOrders.FindAsync(pendingWithGrn.Id));
        Assert.NotNull(await verificationDb.Grns.SingleOrDefaultAsync(grn => grn.PoId == pendingWithGrn.Id));
        Assert.NotNull(await verificationDb.PurchaseOrders.FindAsync(rejected.Id));
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
        Assert.Equal(submitted!.PoNumber, fetched.PoNumber);
        Assert.Equal(submitted.SupplierName, fetched.SupplierName);
        Assert.Equal(submitted.WarehouseName, fetched.WarehouseName);
        Assert.Equal(submitted.CreatedByName, fetched.CreatedByName);
        Assert.Equal(submitted.LatestGrnId, fetched.LatestGrnId);
        Assert.Equal(submitted.TotalAmount, fetched.TotalAmount);
        Assert.Equal(submitted.TotalQuantity, fetched.TotalQuantity);
        Assert.Collection(
            fetched.Items,
            item =>
            {
                Assert.Equal(submitted.Items[0].VariantId, item.VariantId);
                Assert.Equal(submitted.Items[0].ProductName, item.ProductName);
                Assert.Equal(submitted.Items[0].VariantName, item.VariantName);
                Assert.Equal(submitted.Items[0].ProductCode, item.ProductCode);
                Assert.Equal(submitted.Items[0].Sku, item.Sku);
                Assert.Equal(submitted.Items[0].VariantAttributes, item.VariantAttributes);
                Assert.Equal(submitted.Items[0].TotalPrice, item.TotalPrice);
            },
            item =>
            {
                Assert.Equal(submitted.Items[1].VariantId, item.VariantId);
                Assert.Equal(submitted.Items[1].ProductName, item.ProductName);
                Assert.Equal(submitted.Items[1].VariantName, item.VariantName);
                Assert.Equal(submitted.Items[1].TotalPrice, item.TotalPrice);
            });
    }

    [Fact]
    public async Task GetPurchaseOrderById_DoesNotExposeAnotherBusinessOrder()
    {
        await ResetAndSeedAsync();
        using var ownerClient = CreatePurchaseClient();

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreateRequest(status: "draft"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadApiData<PurchaseOrderDto>(createResponse);
        Assert.NotNull(created);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var response = await otherBusinessClient.GetAsync($"/api/purchase-orders/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPurchaseOrders_AppliesFiltersBusinessScopeAndOrderDateOrdering()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();

        var olderResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreateRequest("draft", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)));
        var newerResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreateRequest("pending", new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(HttpStatusCode.Created, olderResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, newerResponse.StatusCode);
        var older = await ReadApiData<PurchaseOrderDto>(olderResponse);
        var newer = await ReadApiData<PurchaseOrderDto>(newerResponse);
        Assert.NotNull(older);
        Assert.NotNull(newer);

        var allResponse = await client.GetAsync("/api/purchase-orders");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var all = await ReadApiData<List<PurchaseOrderDto>>(allResponse);
        Assert.NotNull(all);
        Assert.Equal([newer!.Id, older!.Id], all!.Select(order => order.Id));
        Assert.Equal("Acme Supplier", all[0].SupplierName);
        Assert.Equal("Central Warehouse", all[0].WarehouseName);
        Assert.Null(all[0].LatestGrnId);
        Assert.Collection(
            all[0].Items,
            item =>
            {
                Assert.Equal(string.Empty, item.ProductName);
                Assert.Equal(string.Empty, item.VariantName);
                Assert.Null(item.ProductCode);
                Assert.Null(item.Sku);
                Assert.Null(item.VariantAttributes);
            },
            item =>
            {
                Assert.Equal(string.Empty, item.ProductName);
                Assert.Equal(string.Empty, item.VariantName);
            });

        var filteredResponse = await client.GetAsync(
            "/api/purchase-orders?status=draft&supplierId=1001&warehouseId=1101");
        var filtered = await ReadApiData<List<PurchaseOrderDto>>(filteredResponse);
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        Assert.NotNull(filtered);
        Assert.Equal([older.Id], filtered!.Select(order => order.Id));

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var otherBusinessResponse = await otherBusinessClient.GetAsync("/api/purchase-orders");
        var otherBusinessOrders = await ReadApiData<List<PurchaseOrderDto>>(otherBusinessResponse);
        Assert.Equal(HttpStatusCode.OK, otherBusinessResponse.StatusCode);
        Assert.Empty(otherBusinessOrders!);
    }

    [Fact]
    public async Task SearchPurchaseOrders_AppliesBusinessFiltersSortingAndPagination()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var olderDraft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 20m);
        var newerDraft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 30m);

        var firstPageResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto { PageNumber = 1, PageSize = 1 });
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        var firstPage = await ReadApiData<PurchaseOrderListDto>(firstPageResponse);
        Assert.NotNull(firstPage);
        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(1, firstPage.PageNumber);
        Assert.Equal(1, firstPage.PageSize);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(newerDraft.Id, Assert.Single(firstPage.PurchaseOrders).Id);

        var filteredResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto
            {
                Status = "DRAFT",
                SupplierId = 1001,
                WarehouseId = 1101,
                PageNumber = 1,
                PageSize = 10,
                SortBy = "total_amount",
                SortOrder = "desc"
            });
        var filtered = await ReadApiData<PurchaseOrderListDto>(filteredResponse);
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        Assert.NotNull(filtered);
        Assert.Equal(2, filtered!.TotalCount);
        Assert.Equal(newerDraft.Id, filtered.PurchaseOrders[0].Id);
        Assert.Equal(olderDraft.Id, filtered.PurchaseOrders[1].Id);

        var dateFilteredResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto
            {
                StartDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
                PageSize = 10
            });
        var dateFiltered = await ReadApiData<PurchaseOrderListDto>(dateFilteredResponse);
        Assert.Equal(HttpStatusCode.OK, dateFilteredResponse.StatusCode);
        Assert.NotNull(dateFiltered);
        Assert.Equal(1, dateFiltered!.TotalCount);
        Assert.Equal(pending.Id, Assert.Single(dateFiltered.PurchaseOrders).Id);

        var statusSortedResponse = await client.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto
            {
                SortBy = "status",
                SortOrder = "asc",
                PageSize = 10
            });
        var statusSorted = await ReadApiData<PurchaseOrderListDto>(statusSortedResponse);
        Assert.Equal(HttpStatusCode.OK, statusSortedResponse.StatusCode);
        Assert.NotNull(statusSorted);
        Assert.Equal("draft", statusSorted!.PurchaseOrders[0].Status, ignoreCase: true);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var otherBusinessResponse = await otherBusinessClient.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto());
        var otherBusinessResult = await ReadApiData<PurchaseOrderListDto>(otherBusinessResponse);
        Assert.Equal(HttpStatusCode.OK, otherBusinessResponse.StatusCode);
        Assert.Empty(otherBusinessResult!.PurchaseOrders);
        Assert.Equal(0, otherBusinessResult.TotalCount);
        Assert.DoesNotContain(pending.Id, filtered.PurchaseOrders.Select(order => order.Id));
    }

    [Fact]
    public async Task GetPendingApprovals_ReturnsBusinessScopedPendingOrdersInAscendingOrderDate()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var olderPending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var newerPending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);

        var response = await client.GetAsync("/api/purchase-orders/pending-approvals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approvals = await ReadApiData<List<PurchaseOrderDto>>(response);
        Assert.NotNull(approvals);
        Assert.Equal([olderPending.Id, newerPending.Id], approvals!.Select(order => order.Id));
        Assert.All(approvals, order => Assert.Equal("pending", order.Status, ignoreCase: true));
        Assert.Equal("Acme Supplier", approvals[0].SupplierName);
        Assert.Equal("Central Warehouse", approvals[0].WarehouseName);
        Assert.All(approvals[0].Items, item =>
        {
            Assert.Equal(string.Empty, item.ProductName);
            Assert.Equal(string.Empty, item.VariantName);
        });

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var otherBusinessResponse = await otherBusinessClient.GetAsync(
            "/api/purchase-orders/pending-approvals");
        var otherBusinessApprovals = await ReadApiData<List<PurchaseOrderDto>>(otherBusinessResponse);
        Assert.Equal(HttpStatusCode.OK, otherBusinessResponse.StatusCode);
        Assert.Empty(otherBusinessApprovals!);
    }

    [Fact]
    public async Task GetPurchaseOrderTotalAmount_AppliesStatusDateAndBusinessFilters()
    {
        await ResetAndSeedAsync();
        using var client = CreatePurchaseClient();
        var draft = await CreatePurchaseOrderAsync(
            client,
            "draft",
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 12.50m);
        var pending = await CreatePurchaseOrderAsync(
            client,
            "pending",
            new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
            firstItemUnitPrice: 30m);

        var allResponse = await client.GetAsync("/api/purchase-orders/total-amount");
        var allTotal = await ReadApiDecimal(allResponse);
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        Assert.Equal(draft.TotalAmount + pending.TotalAmount, allTotal);

        var statusResponse = await client.GetAsync(
            "/api/purchase-orders/total-amount?status=DRAFT");
        var statusTotal = await ReadApiDecimal(statusResponse);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(draft.TotalAmount, statusTotal);

        var dateResponse = await client.GetAsync(
            "/api/purchase-orders/total-amount?startDate=2026-06-13T00:00:00Z&endDate=2026-06-13T00:00:00Z");
        var dateTotal = await ReadApiDecimal(dateResponse);
        Assert.Equal(HttpStatusCode.OK, dateResponse.StatusCode);
        Assert.Equal(pending.TotalAmount, dateTotal);

        using var otherBusinessClient = CreatePurchaseClient(businessId: 2);
        var otherBusinessResponse = await otherBusinessClient.GetAsync(
            "/api/purchase-orders/total-amount");
        var otherBusinessTotal = await ReadApiDecimal(otherBusinessResponse);
        Assert.Equal(HttpStatusCode.OK, otherBusinessResponse.StatusCode);
        Assert.Equal(0m, otherBusinessTotal);
    }

    private HttpClient CreatePurchaseClient(long businessId = 1, bool canEdit = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "321");
        client.DefaultRequestHeaders.Add("X-Test-Role", "PurchaseManager");
        var permissions = "purchases.view,purchases.create,purchases.approve";
        if (canEdit)
            permissions += ",purchases.edit";
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static CreatePurchaseOrderDto BuildCreateRequest(string status, DateTime? orderDate = null)
    {
        return new CreatePurchaseOrderDto
        {
            SupplierId = 1001,
            WarehouseId = 1101,
            OrderDate = orderDate ?? new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc),
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

    private static PurchaseOrder ReportOrder(
        long id,
        string poNumber,
        long supplierId,
        long warehouseId,
        string status,
        decimal totalAmount,
        int year,
        int month,
        int day) =>
        new()
        {
            Id = id,
            PoNumber = poNumber,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            OrderDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = totalAmount,
            Status = status
        };

    private static async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(
        HttpClient client,
        string status,
        DateTime orderDate,
        decimal firstItemUnitPrice)
    {
        var request = BuildCreateRequest(status, orderDate);
        request.Items[0].UnitPrice = firstItemUnitPrice;
        var response = await client.PostAsJsonAsync("/api/purchase-orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadApiData<PurchaseOrderDto>(response))!;
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
            BusinessId = 1,
            Name = "Test Category",
            Description = "PO flow test category"
        });

        db.Products.Add(new Product
        {
            Id = 1251,
            BusinessId = 1,
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
                BusinessId = 1,
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
                BusinessId = 1,
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
            BusinessId = 1,
            Name = "Acme Supplier",
            Contact = "01700000000",
            Address = "Dhaka"
        });

        db.Users.Add(new User
        {
            Id = 321,
            BusinessId = 1,
            Name = "Purchase Manager",
            Email = "purchase.manager@example.test",
            PasswordHash = "test",
            InventoryLocationAccessScope = User.InventoryAccessAll
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

    private static async Task<decimal> ReadApiDecimal(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("data").GetDecimal();
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public T? Data { get; set; }
    }
}
