using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Identity;

/// <summary>
/// API-level coverage of the operation-based location policies:
/// InventoryStockView (Step 3), StockCountOperation / StockTransferSource (Step 5 only) and
/// StockTransferDestination (every location of the business).
/// </summary>
public class LocationAccessPolicyHttpTests : IClassFixture<RetailPosApiFactory>
{
    // Business 1: outlets 31 (assigned), 32, 33 (Step 3 default of the all-scope user, never assigned);
    // warehouses 41 (assigned), 42. Business 2: outlet 39, warehouse 49.
    private const long AllScopeUser = 501;
    private const long AssignedOnlyUser = 502;
    private const long NoAssignmentUser = 503;
    private const long OwnerUser = 504;
    private const long SuperAdminUser = 505;
    private const long OtherBusinessUser = 506;

    private readonly RetailPosApiFactory _factory;

    public LocationAccessPolicyHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MeOutlets_WithoutPolicy_AndInventoryStockView_FollowStep3()
    {
        await ResetAndSeedAsync();

        using var allScope = CreateClient(AllScopeUser, "stock_transfers.view");
        Assert.Equal(new long[] { 31, 32, 33 }, (await GetLocationsAsync(allScope, null)).OutletIds);

        using var assignedOnly = CreateClient(AssignedOnlyUser, "stock_transfers.view");
        var view = await GetLocationsAsync(assignedOnly, "InventoryStockView");
        Assert.Equal("InventoryStockView", view.Policy);
        Assert.Equal(new long[] { 32 }, view.OutletIds);
    }

    [Theory]
    [InlineData("StockCountOperation")]
    [InlineData("StockTransferSource")]
    public async Task MeOutlets_Step5Policies_ReturnOnlyAssignedLocations_NotStep3(string policy)
    {
        await ResetAndSeedAsync();

        using var allScope = CreateClient(AllScopeUser, "stock_transfers.view");
        var access = await GetLocationsAsync(allScope, policy);
        Assert.Equal(policy, access.Policy);
        Assert.Equal(new long[] { 31 }, access.OutletIds);
        Assert.Equal(new long[] { 41 }, access.WarehouseIds);

        using var assignedOnly = CreateClient(AssignedOnlyUser, "stock_transfers.view");
        var limited = await GetLocationsAsync(assignedOnly, policy.ToLowerInvariant());
        Assert.Equal(new long[] { 32 }, limited.OutletIds);
        Assert.Empty(limited.WarehouseIds);

        using var none = CreateClient(NoAssignmentUser, "stock_transfers.view");
        var empty = await GetLocationsAsync(none, policy);
        Assert.Empty(empty.OutletIds);
        Assert.Empty(empty.WarehouseIds);
    }

    [Fact]
    public async Task MeOutlets_StockTransferDestination_IsWholeBusiness_AndTenantScoped()
    {
        await ResetAndSeedAsync();

        foreach (var userId in new[] { AllScopeUser, AssignedOnlyUser, NoAssignmentUser })
        {
            using var client = CreateClient(userId, "stock_transfers.view");
            var access = await GetLocationsAsync(client, "StockTransferDestination");
            Assert.Equal(new long[] { 31, 32, 33 }, access.OutletIds);
            Assert.Equal(new long[] { 41, 42 }, access.WarehouseIds);
        }

        using var otherBusiness = CreateClient(OtherBusinessUser, "stock_transfers.view", businessId: 2);
        var foreign = await GetLocationsAsync(otherBusiness, "StockTransferDestination");
        Assert.Equal(new long[] { 39 }, foreign.OutletIds);
        Assert.Equal(new long[] { 49 }, foreign.WarehouseIds);
    }

    [Theory]
    [InlineData("BusinessOwner", OwnerUser)]
    [InlineData("Super Admin", SuperAdminUser)]
    public async Task MeOutlets_PrivilegedRoles_KeepBusinessWideAccessUnderStep5Policies(string role, long userId)
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(userId, "stock_transfers.view", role);

        foreach (var policy in new[] { "StockCountOperation", "StockTransferSource", "StockTransferDestination" })
        {
            var access = await GetLocationsAsync(client, policy);
            Assert.Equal(new long[] { 31, 32, 33 }, access.OutletIds);
            Assert.Equal(new long[] { 41, 42 }, access.WarehouseIds);
        }
    }

    [Fact]
    public async Task MeOutlets_UnknownPolicy_ReturnsBadRequest_AndUnknownUser_ReturnsUnauthorized()
    {
        await ResetAndSeedAsync();

        using var client = CreateClient(AllScopeUser, "stock_transfers.view");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/outlets?policy=AllEverything")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/me/outlets?policy=7")).StatusCode);

        using var unknown = CreateClient(999999, "stock_transfers.view");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.GetAsync("/api/me/outlets?policy=StockCountOperation")).StatusCode);
    }

    [Fact]
    public async Task StockCount_Create_UsesStep5Only_NotStep3OrOtherBusinesses()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(AllScopeUser, "StockCount.ViewAll,StockCount.Create");

        // 33 is the user's Step 3/4 default under all_locations but is not a Step 5 assignment.
        foreach (var (id, type) in new[] { (33L, "outlet"), (32L, "outlet"), (42L, "warehouse"), (39L, "outlet"), (49L, "warehouse") })
        {
            var rejected = await client.PostAsJsonAsync("/api/stock-counts",
                new { locationId = id, locationType = type, stockCountDate = DateTime.UtcNow.Date });
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        foreach (var (id, type) in new[] { (31L, "outlet"), (41L, "warehouse") })
        {
            var created = await client.PostAsJsonAsync("/api/stock-counts",
                new { locationId = id, locationType = type, stockCountDate = DateTime.UtcNow.Date });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var withoutPermission = CreateClient(AllScopeUser, "StockCount.ViewAll");
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutPermission.PostAsJsonAsync("/api/stock-counts",
            new { locationId = 31L, locationType = "outlet", stockCountDate = DateTime.UtcNow.Date })).StatusCode);

        using var unassigned = CreateClient(NoAssignmentUser, "StockCount.ViewAll,StockCount.Create");
        Assert.Equal(HttpStatusCode.Forbidden, (await unassigned.PostAsJsonAsync("/api/stock-counts",
            new { stockCountDate = DateTime.UtcNow.Date })).StatusCode);
    }

    [Fact]
    public async Task StockCount_SearchAndDetail_AreLimitedToStep5Locations()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(AllScopeUser, "StockCount.ViewAll");

        var search = await client.PostAsJsonAsync("/api/stock-counts/search", new { pageSize = 50 });
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var locationIds = json.RootElement.GetProperty("data").GetProperty("stockCounts")
            .EnumerateArray().Select(item => item.GetProperty("locationId").GetInt64()).OrderBy(id => id).ToArray();
        Assert.Equal(new long[] { 31 }, locationIds);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/stock-counts/search",
            new { locationId = 33L, locationType = "outlet" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stock-counts/9031")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/stock-counts/9033")).StatusCode);

        using var owner = CreateClient(OwnerUser, "StockCount.ViewAll", "BusinessOwner");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/stock-counts/9033")).StatusCode);
    }

    [Fact]
    public async Task TransferCreate_SourceMustBeStep5_DestinationMayBeAnyBusinessLocation()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            AllScopeUser,
            "stock_transfers.view,stock_transfers.create,stock_transfers.transfer_from_any_location");

        // Destination 33 / 42 are neither Step 5 assignments nor otherwise visible to the user: still allowed.
        Assert.Equal(HttpStatusCode.Created, (await PostTransferAsync(client, 31, "outlet", 33, "outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostTransferAsync(client, 31, "outlet", 42, "warehouse")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostTransferAsync(client, 41, "warehouse", 32, "outlet")).StatusCode);

        // Sources outside Step 5 (even the Step 3/4 default) are rejected.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(client, 33, "outlet", 31, "outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(client, 42, "warehouse", 31, "outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(client, 39, "outlet", 31, "outlet")).StatusCode);

        // Destinations in another business are never allowed.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(client, 31, "outlet", 39, "outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(client, 31, "outlet", 49, "warehouse")).StatusCode);
    }

    [Fact]
    public async Task TransferCreate_RbacAndDefaultSourcePinRemainSeparateFromThePolicies()
    {
        await ResetAndSeedAsync();

        using var noCreate = CreateClient(AllScopeUser, "stock_transfers.view,stock_transfers.transfer_from_any_location");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(noCreate, 31, "outlet", 33, "outlet")).StatusCode);

        // Without transfer_from_any_location the source stays pinned to the default location (32), and Step 5 still applies.
        using var pinned = CreateClient(AssignedOnlyUser, "stock_transfers.view,stock_transfers.create");
        Assert.Equal(HttpStatusCode.Created, (await PostTransferAsync(pinned, 32, "outlet", 33, "outlet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(pinned, 31, "outlet", 33, "outlet")).StatusCode);

        using var unassigned = CreateClient(
            NoAssignmentUser, "stock_transfers.view,stock_transfers.create,stock_transfers.transfer_from_any_location");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTransferAsync(unassigned, 31, "outlet", 33, "outlet")).StatusCode);
    }

    [Fact]
    public async Task TransferSourceActions_RequireStep5Source()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(
            AllScopeUser,
            "stock_transfers.view,stock_transfers.view_all_locations,stock_transfers.cancel,stock_transfers.transfer_from_any_location");

        Assert.Equal(HttpStatusCode.OK, (await PostCancelAsync(client, 7031)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancelAsync(client, 7033)).StatusCode);

        using var owner = CreateClient(
            OwnerUser, "stock_transfers.view,stock_transfers.view_all_locations,stock_transfers.cancel", "BusinessOwner");
        Assert.Equal(HttpStatusCode.OK, (await PostCancelAsync(owner, 7033)).StatusCode);
    }

    private static Task<HttpResponseMessage> PostTransferAsync(
        HttpClient client, long fromId, string fromType, long toId, string toType) =>
        client.PostAsJsonAsync("/api/stock-transfers", new
        {
            transferType = "direct",
            fromLocationId = fromId,
            fromLocationType = fromType,
            toLocationId = toId,
            toLocationType = toType,
            transferDate = DateTime.UtcNow.Date,
            items = new[]
            {
                new { variantId = 401L, quantity = 1, requestedQuantity = 1, transferQuantity = 1, unitCost = 5m, remarks = "policy" }
            }
        });

    private static Task<HttpResponseMessage> PostCancelAsync(HttpClient client, long transferId) =>
        client.PostAsJsonAsync($"/api/stock-transfers/{transferId}/cancel", new { reason = "policy test" });

    private static async Task<LocationSet> GetLocationsAsync(HttpClient client, string? policy)
    {
        var url = policy is null ? "/api/me/outlets" : $"/api/me/outlets?policy={policy}";
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        return new LocationSet(
            data.GetProperty("policy").GetString() ?? string.Empty,
            Ids(data, "outlets"),
            Ids(data, "warehouses"));

        static long[] Ids(JsonElement data, string name) =>
            data.GetProperty(name).EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).OrderBy(id => id).ToArray();
    }

    private sealed record LocationSet(string Policy, long[] OutletIds, long[] WarehouseIds);

    private HttpClient CreateClient(long userId, string permissions, string role = "OutletManager", long businessId = 1)
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

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Policy Business" },
            new Business { Id = 2, Name = "Other Policy Business" });
        db.Categories.Add(new Category { Id = 101, BusinessId = 1, Name = "Category", Description = "test" });
        db.Outlets.AddRange(
            new Outlet { Id = 31, BusinessId = 1, Name = "Assigned outlet", Address = "A" },
            new Outlet { Id = 32, BusinessId = 1, Name = "Default outlet", Address = "B" },
            new Outlet { Id = 33, BusinessId = 1, Name = "Unassigned outlet", Address = "C" },
            new Outlet { Id = 39, BusinessId = 2, Name = "Foreign outlet", Address = "F" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 41, BusinessId = 1, Name = "Assigned warehouse", Address = "W1" },
            new Warehouse { Id = 42, BusinessId = 1, Name = "Unassigned warehouse", Address = "W2" },
            new Warehouse { Id = 49, BusinessId = 2, Name = "Foreign warehouse", Address = "WF" });
        db.Products.Add(new Product
        {
            Id = 301,
            BusinessId = 1,
            Name = "Policy Product",
            ProductCode = "POL-001",
            CategoryId = 101,
            BasePrice = 10m,
            CostPrice = 5m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 401,
            BusinessId = 1,
            ProductId = 301,
            Name = "Default Variant",
            Sku = "POL-001-DEF",
            Barcode = "POL-BC-001",
            Attributes = "{}"
        });
        db.Inventories.AddRange(
            new InventoryEntity { Id = 501, VariantId = 401, LocationId = 31, LocationType = "outlet", Quantity = 10 },
            new InventoryEntity { Id = 505, VariantId = 401, LocationId = 32, LocationType = "outlet", Quantity = 10 },
            new InventoryEntity { Id = 502, VariantId = 401, LocationId = 33, LocationType = "outlet", Quantity = 10 },
            new InventoryEntity { Id = 503, VariantId = 401, LocationId = 41, LocationType = "warehouse", Quantity = 10 },
            new InventoryEntity { Id = 504, VariantId = 401, LocationId = 42, LocationType = "warehouse", Quantity = 10 });

        db.Users.AddRange(
            NewUser(AllScopeUser, 1, 33, User.InventoryAccessAll),
            NewUser(AssignedOnlyUser, 1, 32, User.InventoryAccessAssignedOnly),
            NewUser(NoAssignmentUser, 1, 31, User.InventoryAccessSpecific),
            NewUser(OwnerUser, 1, null, User.InventoryAccessAssignedOnly),
            NewUser(SuperAdminUser, 1, null, User.InventoryAccessAssignedOnly),
            NewUser(OtherBusinessUser, 2, 39, User.InventoryAccessAssignedOnly));
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = AllScopeUser, BusinessId = 1, OutletId = 31, IsPrimary = true },
            new UserOutletAssignment { UserId = AssignedOnlyUser, BusinessId = 1, OutletId = 32, IsPrimary = true },
            new UserOutletAssignment { UserId = OtherBusinessUser, BusinessId = 2, OutletId = 39, IsPrimary = true });
        db.UserWarehouseAssignments.Add(
            new UserWarehouseAssignment { UserId = AllScopeUser, BusinessId = 1, WarehouseId = 41, IsPrimary = true });

        foreach (var locationId in new long[] { 31, 33 })
        {
            db.StockCounts.Add(new StockCount
            {
                Id = 9000 + locationId,
                StockCountNo = $"SC-POLICY-{locationId}",
                BusinessId = 1,
                LocationId = locationId,
                LocationType = "outlet",
                StockCountDate = DateTime.UtcNow.Date,
                Status = StockCount.StatusCompleted,
                CreatedBy = AllScopeUser,
                CreatedAt = DateTime.UtcNow
            });
            db.StockTransfers.Add(new StockTransfer
            {
                Id = 7000 + locationId,
                TransferNo = $"TRF-POLICY-{locationId}",
                TransferType = StockTransfer.TransferTypeDirect,
                FromLocationId = locationId,
                FromLocationType = "outlet",
                ToLocationId = locationId == 31 ? 32 : 31,
                ToLocationType = "outlet",
                TransferDate = DateTime.UtcNow.Date,
                Status = StockTransfer.StatusDraft,
                CreatedAt = DateTime.UtcNow,
                Items = new List<StockTransferItem>
                {
                    new() { Id = 6000 + locationId, VariantId = 401, Quantity = 1, RequestedQuantity = 1, TransferQuantity = 1, UnitCost = 5m }
                }
            });
        }

        await db.SaveChangesAsync();
    }

    private static User NewUser(long id, long businessId, long? outletId, string scope) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = $"Policy user {id}",
        Email = $"policy.{id}@test.local",
        PasswordHash = "x",
        OutletId = outletId,
        InventoryLocationAccessScope = scope,
        IsActive = true
    };
}
