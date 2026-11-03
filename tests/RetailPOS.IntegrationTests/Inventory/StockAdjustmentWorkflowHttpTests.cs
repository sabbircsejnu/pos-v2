using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.API.Services;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Commands;
using RetailPOS.Application.Settings.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public class StockAdjustmentWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockAdjustmentWorkflowHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDraft_DoesNotUpdateInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments", new
        {
            locationId = 11L,
            locationType = "outlet",
            variantId = 301L,
            quantityChange = -3,
            reason = "Found",
            notes = "Draft should not touch stock"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(created);
        Assert.Equal("Draft", created!.Status);
        Assert.Equal(10, created.PreviousQuantity);
        Assert.Equal(7, created.NewQuantity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);
        Assert.Empty(db.StockLedgers.Where(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment));
    }

    [Fact]
    public async Task CreateBatch_RejectsUnsupportedActionWithoutPersistingAdjustment()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            action = "Unknown",
            locationId = 11L,
            locationType = "outlet",
            items = new[]
            {
                new
                {
                    variantId = 301L,
                    quantityChange = 2,
                    reason = "Found"
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(error.GetProperty("success").GetBoolean());
        Assert.Contains(
            "Invalid create action 'Unknown'. Valid actions are:",
            error.GetProperty("message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(db.StockAdjustments);
        Assert.Equal(
            10,
            db.Inventories.Single(inventory =>
                inventory.VariantId == 301
                && inventory.LocationId == 11
                && inventory.LocationType == "outlet").Quantity);
    }

    [Fact]
    public async Task CreateBatch_SubmitPersistsPendingAdjustmentAndCreationAudits()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            action = "Submit",
            locationId = 11L,
            locationType = "outlet",
            reason = "Found",
            notes = "Batch submission",
            items = new[]
            {
                new { variantId = 301L, quantityChange = 2 }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(created);
        Assert.Equal("PendingApproval", created!.Status);
        Assert.NotNull(created.SubmittedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(
            10,
            db.Inventories.Single(i =>
                i.VariantId == 301
                && i.LocationId == 11
                && i.LocationType == "outlet").Quantity);
        Assert.Empty(db.StockLedgers.Where(row =>
            row.ReferenceType == StockLedgerReferenceType.StockAdjustment
            && row.ReferenceId == created.Id));
        var actions = db.AuditEvents
            .Where(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == created.Id.ToString())
            .Select(row => row.ActionType)
            .ToArray();
        Assert.Contains(AuditActionType.Create, actions);
        Assert.Contains(AuditActionType.Submit, actions);
    }

    [Fact]
    public async Task CreateBatch_SubmitAndApprove_RetainsLegacyApprovalEffectsDuringMigration()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(
            _factory,
            105,
            permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.approve");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            action = "SubmitAndApprove",
            locationId = 11L,
            locationType = "outlet",
            reason = "Found",
            items = new[]
            {
                new { variantId = 301L, quantityChange = 2 }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(created);
        Assert.Equal("Approved", created!.Status);
        Assert.NotNull(created.ApprovedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(
            12,
            db.Inventories.Single(i =>
                i.VariantId == 301
                && i.LocationId == 11
                && i.LocationType == "outlet").Quantity);
        Assert.Single(db.StockLedgers.Where(row =>
            row.ReferenceType == StockLedgerReferenceType.StockAdjustment
            && row.ReferenceId == created.Id));
        Assert.Single(db.JournalEntries.Where(row =>
            row.ReferenceType == "StockAdjustment"
            && row.ReferenceId == created.Id.ToString()
            && row.Status == JournalEntryStatuses.Posted));
        var actions = db.AuditEvents
            .Where(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == created.Id.ToString())
            .Select(row => row.ActionType)
            .ToArray();
        Assert.Contains(AuditActionType.Create, actions);
        Assert.Contains(AuditActionType.Approve, actions);
    }

    [Fact]
    public async Task Submit_TransitionsDraftToPendingApproval()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(client, 11, "outlet", 301, -2, "Found");

        var response = await client.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(data);
        Assert.Equal("PendingApproval", data!.Status);
        Assert.NotNull(data.SubmittedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvent = await db.AuditEvents
            .Include(row => row.Entities)
            .ThenInclude(entity => entity.FieldChanges)
            .SingleAsync(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == draftId.ToString()
                && row.ActionType == AuditActionType.Submit);
        Assert.Contains(
            auditEvent.Entities.SelectMany(entity => entity.FieldChanges),
            change => change.FieldName == "Status"
                && change.NewValue?.Contains(StockAdjustment.StatusPendingApproval) == true);
    }

    [Fact]
    public async Task Update_ReplacesDraftLinesAndAuditsSummaryWithoutChangingInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(
            _factory,
            101,
            permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.edit");
        var draftId = await CreateDraftAsync(client, 11, "outlet", 301, -2, "Found");

        var response = await client.PutAsJsonAsync($"/api/stock-adjustments/{draftId}", new
        {
            locationId = 11L,
            locationType = "outlet",
            items = new[]
            {
                new { variantId = 301L, quantityChange = 3, reason = "Damaged", notes = "Recounted" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(updated);
        Assert.Equal("Draft", updated!.Status);
        Assert.Equal(3, updated.QuantityChange);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var adjustment = await db.StockAdjustments
            .Include(row => row.Lines)
            .SingleAsync(row => row.Id == draftId);
        Assert.Single(adjustment.Lines);
        var updatedLine = Assert.Single(adjustment.Lines);
        Assert.Equal("Damaged", updatedLine.Reason);
        Assert.Equal("Recounted", updatedLine.Notes);
        Assert.Equal(
            10,
            await db.Inventories.Where(row =>
                    row.VariantId == 301
                    && row.LocationId == 11
                    && row.LocationType == "outlet")
                .Select(row => row.Quantity)
                .SingleAsync());
        var updateAudit = await db.AuditEvents
            .Include(row => row.Entities)
            .ThenInclude(entity => entity.FieldChanges)
            .SingleAsync(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == draftId.ToString()
                && row.ActionType == AuditActionType.Update);
        var changedFields = updateAudit.Entities
            .SelectMany(entity => entity.FieldChanges)
            .Select(change => change.FieldName)
            .ToArray();
        Assert.Contains("LineCount", changedFields);
        Assert.Contains("NetQuantityChange", changedFields);
        Assert.Contains("UpdatedAt", changedFields);
    }

    [Fact]
    public async Task Delete_RemovesDraftAndItsLinesWithoutLifecycleAudit()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(
            _factory,
            101,
            permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.delete");
        var draftId = await CreateDraftAsync(client, 11, "outlet", 301, -2, "Found");

        var response = await client.DeleteAsync($"/api/stock-adjustments/{draftId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadApiEnvelope(response);
        Assert.True(envelope.Success);
        Assert.Equal("Stock adjustment draft deleted successfully", envelope.Message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.StockAdjustments.AnyAsync(row => row.Id == draftId));
        Assert.False(await db.StockAdjustmentLines.AnyAsync(row => row.StockAdjustmentId == draftId));
        Assert.False(await db.AuditEvents.AnyAsync(row =>
            row.PrimaryEntityType == "StockAdjustment"
            && row.PrimaryEntityId == draftId.ToString()
            && row.ActionType == AuditActionType.Delete));
    }

    [Fact]
    public async Task Approve_UpdatesInventoryAndWritesLedger()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, -2, "Found");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var approver = CreateClient(_factory, 105, permissions: "stock_adjustments.view,stock_adjustments.approve");
        var approveResponse = await approver.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var data = await ReadApiData<StockAdjustmentResponse>(approveResponse);
        Assert.NotNull(data);
        Assert.Equal("Approved", data!.Status);
        Assert.Equal(10, data.PreviousQuantity);
        Assert.Equal(8, data.NewQuantity);
        Assert.NotNull(data.ApprovedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(8, inventory.Quantity);
        Assert.Single(db.StockLedgers.Where(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment && s.ReferenceId == draftId));
    }

    [Fact]
    public async Task Approve_ForPositiveQuantityChange_PostsInventoryGainJournal()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, 2, "Found");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var approver = CreateClient(_factory, 105, permissions: "stock_adjustments.view,stock_adjustments.approve");
        var approveResponse = await approver.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var journal = db.JournalEntries
            .Include(x => x.Lines)
            .Single(x =>
                x.IsSystemGenerated
                && x.JournalType == "StockGainPosting"
                && x.ReferenceType == "StockAdjustment"
                && x.ReferenceId == draftId.ToString());

        Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(100m, journal.Lines.Sum(x => x.DebitAmount));
        Assert.Equal(100m, journal.Lines.Sum(x => x.CreditAmount));

        var auditEvent = await db.AuditEvents
            .Include(x => x.Entities)
            .ThenInclude(x => x.FieldChanges)
            .SingleAsync(x => x.PrimaryEntityType == "StockAdjustment" && x.PrimaryEntityId == draftId.ToString() && x.ActionType == AuditActionType.Approve);
        Assert.Contains($"\"journalEntryId\":{journal.Id}", auditEvent.Metadata);
        Assert.Contains(auditEvent.Entities.SelectMany(x => x.FieldChanges), x => x.FieldName == "JournalEntryId" && (x.NewValue?.Contains(journal.Id.ToString()) ?? false));
    }

    [Fact]
    public async Task Approve_ForNegativeQuantityChange_PostsInventoryShortageJournal()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, -3, "Damaged");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var approver = CreateClient(_factory, 105, permissions: "stock_adjustments.view,stock_adjustments.approve");
        var approveResponse = await approver.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var journal = db.JournalEntries
            .Include(x => x.Lines)
            .Single(x =>
                x.IsSystemGenerated
                && x.JournalType == "StockShortagePosting"
                && x.ReferenceType == "StockAdjustment"
                && x.ReferenceId == draftId.ToString());

        Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
        Assert.Equal(2, journal.Lines.Count);
        Assert.Equal(150m, journal.Lines.Sum(x => x.DebitAmount));
        Assert.Equal(150m, journal.Lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task Reverse_ApprovedGainAdjustment_CreatesReversalJournalAndRestoresInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, 2, "Found");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var approver = CreateClient(_factory, 105, permissions: "stock_adjustments.view,stock_adjustments.approve");
        var approveResponse = await approver.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var reverser = CreateClient(_factory, 106, permissions: "stock_adjustments.view,stock_adjustments.reject");
        var reverseResponse = await reverser.PostAsJsonAsync($"/api/stock-adjustments/{draftId}/reverse", new { reason = "Correction" });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);

        var originalJournal = db.JournalEntries
            .Single(x =>
                x.IsSystemGenerated
                && x.JournalType == "StockGainPosting"
                && x.ReferenceType == "StockAdjustment"
                && x.ReferenceId == draftId.ToString());
        Assert.Equal(JournalEntryStatuses.Reversed, originalJournal.Status);

        var reversalJournal = db.JournalEntries
            .Single(x => x.IsSystemGenerated && x.ReversalOfJournalEntryId == originalJournal.Id);
        var reversalLines = db.JournalEntryLines.Where(x => x.JournalEntryId == reversalJournal.Id).ToList();
        Assert.Equal(JournalEntryStatuses.Posted, reversalJournal.Status);
        Assert.Equal(2, reversalLines.Count);
        Assert.Equal(100m, reversalLines.Sum(x => x.DebitAmount));
        Assert.Equal(100m, reversalLines.Sum(x => x.CreditAmount));

        var adjustmentLedgerRows = db.StockLedgers
            .Where(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment && s.ReferenceId == draftId)
            .OrderBy(s => s.MovementSequence)
            .ToList();

        Assert.Equal(2, adjustmentLedgerRows.Count);
        Assert.True(adjustmentLedgerRows[0].IsCostValuationReliable);
        Assert.True(adjustmentLedgerRows[1].IsCostValuationReliable);
        Assert.Equal(adjustmentLedgerRows[0].UnitCost, adjustmentLedgerRows[1].UnitCost);
        Assert.Equal(adjustmentLedgerRows[0].MovementValue, adjustmentLedgerRows[1].MovementValue);
        Assert.True(db.StockLedgers.Any(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment && s.ReferenceId == draftId));
    }

    [Fact]
    public async Task Reverse_ApprovedShortageAdjustment_CreatesReversalJournalAndRestoresInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, -3, "Damaged");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var approver = CreateClient(_factory, 105, permissions: "stock_adjustments.view,stock_adjustments.approve");
        var approveResponse = await approver.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var reverser = CreateClient(_factory, 106, permissions: "stock_adjustments.view,stock_adjustments.reject");
        var reverseResponse = await reverser.PostAsJsonAsync($"/api/stock-adjustments/{draftId}/reverse", new { reason = "Correction" });
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);

        var originalJournal = db.JournalEntries
            .Single(x =>
                x.IsSystemGenerated
                && x.JournalType == "StockShortagePosting"
                && x.ReferenceType == "StockAdjustment"
                && x.ReferenceId == draftId.ToString());
        Assert.Equal(JournalEntryStatuses.Reversed, originalJournal.Status);

        var reversalJournal = db.JournalEntries
            .Single(x => x.IsSystemGenerated && x.ReversalOfJournalEntryId == originalJournal.Id);
        var reversalLines = db.JournalEntryLines.Where(x => x.JournalEntryId == reversalJournal.Id).ToList();
        Assert.Equal(JournalEntryStatuses.Posted, reversalJournal.Status);
        Assert.Equal(2, reversalLines.Count);
        Assert.Equal(150m, reversalLines.Sum(x => x.DebitAmount));
        Assert.Equal(150m, reversalLines.Sum(x => x.CreditAmount));
        Assert.True(db.StockLedgers.Any(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment && s.ReferenceId == draftId));
    }

    [Fact]
    public async Task Reject_DoesNotUpdateInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, -2, "Found");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var rejector = CreateClient(_factory, 106, permissions: "stock_adjustments.view,stock_adjustments.reject");
        var rejectResponse = await rejector.PostAsJsonAsync($"/api/stock-adjustments/{draftId}/reject", new { reason = "Count sheet mismatch" });

        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var data = await ReadApiData<StockAdjustmentResponse>(rejectResponse);
        Assert.NotNull(data);
        Assert.Equal("Rejected", data!.Status);
        Assert.Equal("Count sheet mismatch", data.RejectionReason);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);
        Assert.Empty(db.StockLedgers.Where(s => s.ReferenceType == StockLedgerReferenceType.StockAdjustment && s.ReferenceId == draftId));
        Assert.Empty(db.JournalEntries.Where(x => x.ReferenceType == "StockAdjustment" && x.ReferenceId == draftId.ToString()));
        Assert.Single(db.AuditEvents.Where(row =>
            row.PrimaryEntityType == "StockAdjustment"
            && row.PrimaryEntityId == draftId.ToString()
            && row.ActionType == AuditActionType.Reject));
    }

    [Fact]
    public async Task Cancel_DoesNotUpdateInventory()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.edit");
        var draftId = await CreateDraftAsync(client, 11, "outlet", 301, -1, "Found");

        var cancelResponse = await client.PostAsync($"/api/stock-adjustments/{draftId}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var data = await ReadApiData<StockAdjustmentResponse>(cancelResponse);
        Assert.NotNull(data);
        Assert.Equal("Cancelled", data!.Status);
        Assert.NotNull(data.CancelledAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);
        var adjustment = await db.StockAdjustments.SingleAsync(row => row.Id == draftId);
        Assert.Equal(StockAdjustment.StatusCancelled, adjustment.Status);
        Assert.Equal(101, adjustment.CancelledBy);
        Assert.NotNull(adjustment.CancelledAt);
        var cancelAudit = await db.AuditEvents
            .Include(row => row.Entities)
            .ThenInclude(entity => entity.FieldChanges)
            .SingleAsync(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == draftId.ToString()
                && row.ActionType == AuditActionType.Cancel);
        Assert.Contains(
            cancelAudit.Entities.SelectMany(entity => entity.FieldChanges),
            change => change.FieldName == "Status"
                && change.OldValue?.Contains(StockAdjustment.StatusDraft) == true
                && change.NewValue?.Contains(StockAdjustment.StatusCancelled) == true);
        Assert.Contains(
            cancelAudit.Entities.SelectMany(entity => entity.FieldChanges),
            change => change.FieldName == "CancelledAt");
    }

    [Fact]
    public async Task Cancel_TransitionsPendingApprovalAdjustment()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(
            _factory,
            101,
            permissions: "stock_adjustments.view,stock_adjustments.create,stock_adjustments.edit");
        var draftId = await CreateDraftAsync(client, 11, "outlet", 301, -1, "Found");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/stock-adjustments/{draftId}/submit", null)).StatusCode);

        var cancelResponse = await client.PostAsync(
            $"/api/stock-adjustments/{draftId}/cancel",
            null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var data = await ReadApiData<StockAdjustmentResponse>(cancelResponse);
        Assert.NotNull(data);
        Assert.Equal("Cancelled", data!.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var cancelAudit = await db.AuditEvents
            .Include(row => row.Entities)
            .ThenInclude(entity => entity.FieldChanges)
            .SingleAsync(row =>
                row.PrimaryEntityType == "StockAdjustment"
                && row.PrimaryEntityId == draftId.ToString()
                && row.ActionType == AuditActionType.Cancel);
        Assert.Contains(
            cancelAudit.Entities.SelectMany(entity => entity.FieldChanges),
            change => change.FieldName == "Status"
                && change.OldValue?.Contains(StockAdjustment.StatusPendingApproval) == true
                && change.NewValue?.Contains(StockAdjustment.StatusCancelled) == true);
    }

    [Fact]
    public async Task BatchCreate_WithDuplicateVariant_ReturnsBadRequest()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments/batch", new
        {
            locationId = 11L,
            locationType = "outlet",
            reason = "Found",
            items = new object[]
            {
                new { variantId = 301L, quantityChange = -1, reason = "Found" },
                new { variantId = 301L, quantityChange = -2, reason = "Found" }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope(response);
        Assert.Contains("Duplicate variants are not allowed", error.Message);
    }

    [Fact]
    public async Task CreateDraft_NegativeStockViolation_ReturnsBadRequest()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments", new
        {
            locationId = 11L,
            locationType = "outlet",
            variantId = 301L,
            quantityChange = -50,
            reason = "Found"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await ReadApiEnvelope(response);
        Assert.Contains("negative stock", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateDraft_SpecificLocationUser_CannotWriteUnauthorizedOutlet()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var client = CreateClient(_factory, 104, permissions: "stock_adjustments.view,stock_adjustments.create");

        var response = await client.PostAsJsonAsync("/api/stock-adjustments", new
        {
            locationId = 12L,
            locationType = "outlet",
            variantId = 301L,
            quantityChange = -1,
            reason = "Found"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_WithoutApprovePermission_ReturnsForbidden()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using var creator = CreateClient(_factory, 101, permissions: "stock_adjustments.view,stock_adjustments.create");
        var draftId = await CreateDraftAsync(creator, 11, "outlet", 301, -1, "Found");
        var submitResponse = await creator.PostAsync($"/api/stock-adjustments/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        using var noApprove = CreateClient(_factory, 107, permissions: "stock_adjustments.view");
        var response = await noApprove.PostAsync($"/api/stock-adjustments/{draftId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoints_ReturnOnlyAuthorizedAdjustmentsAndPreserveResponseEnvelope()
    {
        await ResetAndSeedAsync(_factory, allowNegativeStock: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.StockAdjustments.AddRange(
                ReadAdjustment(901, 11, 101),
                ReadAdjustment(902, 12, 101));
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(_factory, 101, permissions: "stock_adjustments.view");
        using var listResponse = await client.GetAsync("/api/stock-adjustments");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listed = listJson.RootElement.GetProperty("data");
        var listedAdjustment = Assert.Single(listed.EnumerateArray().ToArray());
        Assert.Equal(901, listedAdjustment.GetProperty("id").GetInt64());
        Assert.Equal("Main Outlet", listedAdjustment.GetProperty("locationName").GetString());
        Assert.Equal("WF-001-DEF", listedAdjustment.GetProperty("variantSku").GetString());
        Assert.Equal(1, listedAdjustment.GetProperty("lineCount").GetInt32());

        using var byIdResponse = await client.GetAsync("/api/stock-adjustments/901");
        Assert.Equal(HttpStatusCode.OK, byIdResponse.StatusCode);
        using var byIdJson = JsonDocument.Parse(await byIdResponse.Content.ReadAsStringAsync());
        Assert.Equal(901, byIdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64());

        using var hiddenByIdResponse = await client.GetAsync("/api/stock-adjustments/902");
        Assert.Equal(HttpStatusCode.NotFound, hiddenByIdResponse.StatusCode);

        using var unauthorizedLocationResponse = await client.GetAsync(
            "/api/stock-adjustments?locationId=12&locationType=outlet");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedLocationResponse.StatusCode);

        using var searchResponse = await client.PostAsJsonAsync("/api/stock-adjustments/search", new
        {
            variantId = 301,
            pageNumber = 1,
            pageSize = 10
        });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        var searchData = searchJson.RootElement.GetProperty("data");
        Assert.Equal(1, searchData.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            901,
            Assert.Single(searchData.GetProperty("stockAdjustments").EnumerateArray().ToArray())
                .GetProperty("id")
                .GetInt64());

        using var historyResponse = await client.GetAsync(
            "/api/stock-adjustments/history?variantId=301&locationId=11");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            901,
            Assert.Single(historyJson.RootElement.GetProperty("data").EnumerateArray().ToArray())
                .GetProperty("id")
                .GetInt64());
    }

    private static HttpClient CreateClient(RetailPosApiFactory factory, long userId, string permissions, string role = "AccountsAdmin", long businessId = 1)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync(RetailPosApiFactory factory, bool allowNegativeStock)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Test Business" });

        db.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Test Category", Description = "Workflow tests" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Second Outlet", Address = "B" });

        db.Warehouses.Add(
            new Warehouse { Id = 21, BusinessId = 1, Name = "Central Warehouse", Address = "W" });

        db.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Workflow Product",
            ProductCode = "WF-001",
            CategoryId = 201,
            BasePrice = 100m,
            CostPrice = 50m,
            HasVariants = true,
            Status = ProductStatus.Active
        });

        db.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Default Variant",
            Sku = "WF-001-DEF",
            Barcode = "WF-BC-001",
            Attributes = "{}"
        });

        db.Inventories.AddRange(
            new InventoryEntity { Id = 401, VariantId = 301, LocationId = 11, LocationType = "outlet", Quantity = 10 },
            new InventoryEntity { Id = 402, VariantId = 301, LocationId = 12, LocationType = "outlet", Quantity = 5 },
            new InventoryEntity { Id = 403, VariantId = 301, LocationId = 21, LocationType = "warehouse", Quantity = 20 });

        db.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Creator",
                Email = "creator@test.local",
                PasswordHash = "x",
                OutletId = 11,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 104,
                BusinessId = 1,
                Name = "Specific User",
                Email = "specific@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessSpecific,
                IsActive = true
            },
            new User
            {
                Id = 105,
                BusinessId = 1,
                Name = "Approver",
                Email = "approver@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAll,
                IsActive = true
            },
            new User
            {
                Id = 106,
                BusinessId = 1,
                Name = "Rejector",
                Email = "rejector@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAll,
                IsActive = true
            },
            new User
            {
                Id = 107,
                BusinessId = 1,
                Name = "No Approve",
                Email = "viewer@test.local",
                PasswordHash = "x",
                InventoryLocationAccessScope = User.InventoryAccessAll,
                IsActive = true
            });

        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 501,
            UserId = 104,
            OutletId = 11,
            BusinessId = 1,
            IsPrimary = true,
            IsActive = true
        });

        await db.SaveChangesAsync();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var currentSettings = await sender.Send(new GetSystemSettingsQuery());
        await sender.Send(new UpdateCompanySettingsCommand(currentSettings.Company));
        await sender.Send(new UpdateCurrencySettingsCommand(currentSettings.Currency));
        await sender.Send(new UpdateTaxSettingsCommand(currentSettings.Tax));
        await sender.Send(new UpdateReceiptSettingsCommand(currentSettings.Receipt));
        await sender.Send(new UpdateInventorySettingsCommand(new InventorySettings
        {
            LowStockThreshold = 10,
            EnableLowStockAlerts = true,
            AllowNegativeStock = allowNegativeStock,
            AutoReorder = false
        }));
    }

    private static StockAdjustment ReadAdjustment(long id, long locationId, long adjustedBy)
    {
        var adjustment = new StockAdjustment
        {
            Id = id,
            AdjustmentNumber = $"SA-{id}",
            Status = StockAdjustment.StatusDraft,
            LocationId = locationId,
            LocationType = "outlet",
            AdjustedBy = adjustedBy,
            AdjustmentDate = new DateTime(2026, 10, 5),
            CreatedAt = new DateTime(2026, 10, 5),
            UpdatedAt = new DateTime(2026, 10, 5)
        };
        adjustment.Lines.Add(new StockAdjustmentLine
        {
            VariantId = 301,
            PreviousQuantity = 10,
            QuantityChange = -1,
            NewQuantity = 9,
            Reason = "HTTP read test"
        });
        return adjustment;
    }

    private async Task<long> CreateDraftAsync(HttpClient client, long locationId, string locationType, long variantId, int quantityChange, string reason)
    {
        var response = await client.PostAsJsonAsync("/api/stock-adjustments", new
        {
            locationId,
            locationType,
            variantId,
            quantityChange,
            reason
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadApiData<StockAdjustmentResponse>(response);
        Assert.NotNull(created);
        return created!.Id;
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private static async Task<ApiEnvelope<object>> ReadApiEnvelope(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonSerializer.DeserializeAsync<ApiEnvelope<object>>(stream, JsonOptions))!;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    private sealed class StockAdjustmentResponse
    {
        public long Id { get; set; }
        public string AdjustmentNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int PreviousQuantity { get; set; }
        public int QuantityChange { get; set; }
        public int NewQuantity { get; set; }
        public string? SubmittedAt { get; set; }
        public string? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public string? CancelledAt { get; set; }
    }
}