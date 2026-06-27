using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.API.Services;
using RetailPOS.API.Settings;
using RetailPOS.Core.Entities;
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

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = db.Inventories.Single(i => i.VariantId == 301 && i.LocationId == 11 && i.LocationType == "outlet");
        Assert.Equal(10, inventory.Quantity);
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

        db.Categories.Add(new Category { Id = 201, Name = "Test Category", Description = "Workflow tests" });

        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "A" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Second Outlet", Address = "B" });

        db.Warehouses.Add(
            new Warehouse { Id = 21, BusinessId = 1, Name = "Central Warehouse", Address = "W" });

        db.Products.Add(new Product
        {
            Id = 300,
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

        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settingsService.UpdateCompanySettingsAsync((await settingsService.GetCompanySettingsAsync()));
        await settingsService.UpdateCurrencySettingsAsync((await settingsService.GetCurrencySettingsAsync()));
        await settingsService.UpdateTaxSettingsAsync((await settingsService.GetTaxSettingsAsync()));
        await settingsService.UpdateReceiptSettingsAsync((await settingsService.GetReceiptSettingsAsync()));
        await settingsService.UpdateInventorySettingsAsync(new InventorySettings
        {
            LowStockThreshold = 10,
            EnableLowStockAlerts = true,
            AllowNegativeStock = allowNegativeStock,
            AutoReorder = false
        });
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
    }
}