using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// Today's Summary and the sale list through the real HTTP pipeline and PostgreSQL. Each outlet's sale has a distinct
/// amount, so a total shows exactly which outlets were counted:
/// Business 1: outlet 11 = 1, outlet 12 = 10, outlet 13 = 100. Business 2: outlet 21 = 1000.
/// </summary>
public sealed class SaleBusinessIsolationPostgresHttpTests
{
    private const long Owner = 1;
    private const long TwoOutletUser = 2;
    private const long DefaultOutletUser = 3;
    private const long UnassignedUser = 4;
    private const long AllLocationsUser = 5;
    private const long OtherOwner = 7;
    private const long PlatformAdmin = 8;
    private const long ForeignDefaultUser = 9;

    private const string Cashier = "Cashier";
    private const string BusinessOwner = "BusinessOwner";

    private const decimal Business1Total = 111m;
    private const decimal Business2Total = 1000m;

    [Fact]
    public Task BusinessOwner_SeesEveryOutletOfTheirBusiness_AndNoOther() => RunAsync(async factory =>
    {
        Assert.Equal(Business1Total, await SummaryTotalAsync(factory, BusinessOwner, Owner));
        Assert.Equal(1m, await SummaryTotalAsync(factory, BusinessOwner, Owner, outletId: 11));
        Assert.Equal(5, await ListCountAsync(factory, BusinessOwner, Owner));
        await AssertForbiddenAsync(factory, BusinessOwner, Owner, outletId: 21);

        Assert.Equal(Business2Total, await SummaryTotalAsync(factory, BusinessOwner, OtherOwner, businessId: "2"));
        Assert.Equal(1, await ListCountAsync(factory, BusinessOwner, OtherOwner, businessId: "2"));
        await AssertForbiddenAsync(factory, BusinessOwner, OtherOwner, outletId: 11, businessId: "2");
    });

    [Fact]
    public Task UserWithTwoAssignedOutlets_AggregatesOnlyThose() => RunAsync(async factory =>
    {
        Assert.Equal(11m, await SummaryTotalAsync(factory, Cashier, TwoOutletUser));
        Assert.Equal(10m, await SummaryTotalAsync(factory, Cashier, TwoOutletUser, outletId: 12));
        Assert.Equal(4, await ListCountAsync(factory, Cashier, TwoOutletUser));
        await AssertForbiddenAsync(factory, Cashier, TwoOutletUser, outletId: 13);
        await AssertForbiddenAsync(factory, Cashier, TwoOutletUser, outletId: 21);
    });

    [Fact]
    public Task UserWithOnlyADefaultOutlet_SeesThatOutlet() => RunAsync(async factory =>
    {
        Assert.Equal(1m, await SummaryTotalAsync(factory, Cashier, DefaultOutletUser));
        await AssertForbiddenAsync(factory, Cashier, DefaultOutletUser, outletId: 12);
    });

    [Fact]
    public Task AllLocationsUser_IsLimitedToTheirOwnBusiness() => RunAsync(async factory =>
    {
        Assert.Equal(Business1Total, await SummaryTotalAsync(factory, Cashier, AllLocationsUser));
        Assert.Equal(5, await ListCountAsync(factory, Cashier, AllLocationsUser));
        await AssertForbiddenAsync(factory, Cashier, AllLocationsUser, outletId: 21);
    });

    [Fact]
    public Task UnassignedUser_GetsNoSalesData() => RunAsync(async factory =>
    {
        using var client = Client(factory, Cashier, UnassignedUser);

        using var summary = await client.GetAsync("/api/sales/today/summary");
        using var list = await client.GetAsync("/api/sales?pageNumber=1&pageSize=50");

        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    });

    [Fact]
    public Task UserWhoseDefaultOutletBelongsToAnotherBusiness_GetsNothingFromIt() => RunAsync(async factory =>
    {
        using var client = Client(factory, Cashier, ForeignDefaultUser);

        using var summary = await client.GetAsync("/api/sales/today/summary");
        using var list = await client.GetAsync("/api/sales?pageNumber=1&pageSize=50");

        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    });

    [Fact]
    public Task SuperAdmin_NeedsAnExplicitlySelectedBusiness() => RunAsync(async factory =>
    {
        using var noBusiness = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        using var summary = await noBusiness.GetAsync("/api/sales/today/summary");
        using var list = await noBusiness.GetAsync("/api/sales?pageNumber=1&pageSize=50");
        Assert.Equal(HttpStatusCode.BadRequest, summary.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);

        using var first = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        first.DefaultRequestHeaders.Add("X-Business-Id", "1");
        Assert.Equal(Business1Total, await TotalAsync(first, "/api/sales/today/summary"));
        using var foreignOutlet = await first.GetAsync("/api/sales/today/summary?outletId=21");
        Assert.Equal(HttpStatusCode.Forbidden, foreignOutlet.StatusCode);

        using var second = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        second.DefaultRequestHeaders.Add("X-Business-Id", "2");
        Assert.Equal(Business2Total, await TotalAsync(second, "/api/sales/today/summary"));
    });

    [Fact]
    public Task ANonSuperAdmin_CannotChooseABusinessWithTheHeader() => RunAsync(async factory =>
    {
        using var client = Client(factory, BusinessOwner, Owner);
        client.DefaultRequestHeaders.Add("X-Business-Id", "2");

        using var response = await client.GetAsync("/api/sales/today/summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    });

    [Fact]
    public Task RoleSwitching_DoesNotWidenAccess() => RunAsync(async factory =>
    {
        // Acting as a Business Owner at an unassigned outlet, with context switching not enabled for the user.
        using var regular = Client(factory, Cashier, DefaultOutletUser);
        regular.DefaultRequestHeaders.Add("X-Test-EffectiveRole", BusinessOwner);
        regular.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        regular.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "12");
        Assert.Equal(1m, await TotalAsync(regular, "/api/sales/today/summary"));
        Assert.Equal(2, await CountAsync(regular, "/api/sales?pageNumber=1&pageSize=50"));
        using var acting = await regular.GetAsync("/api/sales/today/summary?outletId=12");
        Assert.Equal(HttpStatusCode.Forbidden, acting.StatusCode);

        // An acting outlet in another Business.
        using var foreign = Client(factory, Cashier, DefaultOutletUser);
        foreign.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        foreign.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "21");
        Assert.Equal(1m, await TotalAsync(foreign, "/api/sales/today/summary"));

        // A switched Business Owner stays inside their Business.
        using var owner = Client(factory, BusinessOwner, Owner);
        owner.DefaultRequestHeaders.Add("X-Test-EffectiveRole", Cashier);
        owner.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        owner.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "11");
        Assert.Equal(1m, await TotalAsync(owner, "/api/sales/today/summary"));
        using var ownerForeign = await owner.GetAsync("/api/sales/today/summary?outletId=21");
        Assert.Equal(HttpStatusCode.Forbidden, ownerForeign.StatusCode);
    });

    [Fact]
    public Task CallerSuppliedIds_CannotReachAnotherBusiness() => RunAsync(async factory =>
    {
        using var client = Client(factory, BusinessOwner, Owner);

        Assert.Equal(0, await CountAsync(client, "/api/sales?customerId=901&pageNumber=1&pageSize=50"));
        Assert.Equal(0, await CountAsync(client, $"/api/sales?cashierId={OtherOwner}&pageNumber=1&pageSize=50"));
        using var search = await client.PostAsJsonAsync("/api/sales/search", new { outletId = 21, pageNumber = 1, pageSize = 50 });
        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        using var unfiltered = await client.PostAsJsonAsync("/api/sales/search", new { pageNumber = 1, pageSize = 50 });
        Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
        using var body = await JsonDocument.ParseAsync(await unfiltered.Content.ReadAsStreamAsync());
        Assert.Equal(5, body.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
    });

    [Fact]
    public Task SummaryTotals_KeepExcludingVoidedAndEarlierSales() => RunAsync(async factory =>
    {
        using var client = Client(factory, BusinessOwner, Owner);

        using var response = await client.GetAsync("/api/sales/today/summary");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalSales").GetInt32());
        Assert.Equal(Business1Total, data.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(Business1Total, data.GetProperty("paymentBreakdown").GetProperty("cash").GetDecimal());
    });

    private static HttpClient Client(
        MobileSalesDashboardPostgresApiFactory factory,
        string role,
        long userId,
        string businessId = "1")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "sales.view");
        return client;
    }

    private static async Task<decimal> SummaryTotalAsync(
        MobileSalesDashboardPostgresApiFactory factory,
        string role,
        long userId,
        long? outletId = null,
        string businessId = "1")
    {
        using var client = Client(factory, role, userId, businessId);
        return await TotalAsync(client, $"/api/sales/today/summary{(outletId is null ? string.Empty : $"?outletId={outletId}")}");
    }

    private static async Task<int> ListCountAsync(
        MobileSalesDashboardPostgresApiFactory factory,
        string role,
        long userId,
        string businessId = "1")
    {
        using var client = Client(factory, role, userId, businessId);
        return await CountAsync(client, "/api/sales?pageNumber=1&pageSize=50");
    }

    private static async Task AssertForbiddenAsync(
        MobileSalesDashboardPostgresApiFactory factory,
        string role,
        long userId,
        long outletId,
        string businessId = "1")
    {
        using var client = Client(factory, role, userId, businessId);
        using var summary = await client.GetAsync($"/api/sales/today/summary?outletId={outletId}");
        using var list = await client.GetAsync($"/api/sales?outletId={outletId}&pageNumber=1&pageSize=50");
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    private static async Task<decimal> TotalAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {text}");
        using var body = JsonDocument.Parse(text);
        return body.RootElement.GetProperty("data").GetProperty("totalAmount").GetDecimal();
    }

    private static async Task<int> CountAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {text}");
        using var body = JsonDocument.Parse(text);
        return body.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32();
    }

    private static Task RunAsync(Func<MobileSalesDashboardPostgresApiFactory, Task> test) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            PostgresTestDatabaseStrategy.TemplateClone,
            async database =>
            {
                await using var factory = new MobileSalesDashboardPostgresApiFactory(database.ConnectionString);
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                    await db.Database.MigrateAsync();
                    await SeedAsync(db);
                }

                await test(factory);
            });

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business 1", IsActive = true },
            new Business { Id = 2, Name = "Business 2", IsActive = true });
        await db.SaveChangesAsync();
        db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet 11", Address = "A", InvoicePrefix = "O11" },
            new Outlet { Id = 12, BusinessId = 1, Name = "Outlet 12", Address = "A", InvoicePrefix = "O12" },
            new Outlet { Id = 13, BusinessId = 1, Name = "Outlet 13", Address = "A", InvoicePrefix = "O13" },
            new Outlet { Id = 21, BusinessId = 2, Name = "Outlet 21", Address = "B", InvoicePrefix = "O21" });
        await db.SaveChangesAsync();
        db.Users.AddRange(
            // The Owner's narrow inventory scope must not limit a Business Owner's view of their own sales.
            CreateUser(Owner, 1, null, User.InventoryAccessAssignedOnly),
            CreateUser(TwoOutletUser, 1, null, User.InventoryAccessSpecific),
            CreateUser(DefaultOutletUser, 1, 11, User.InventoryAccessAssignedOnly),
            CreateUser(UnassignedUser, 1, null, User.InventoryAccessAssignedOnly),
            CreateUser(AllLocationsUser, 1, null, User.InventoryAccessAll),
            CreateUser(OtherOwner, 2, null, User.InventoryAccessAssignedOnly),
            CreateUser(ForeignDefaultUser, 1, 21, User.InventoryAccessAssignedOnly));
        await db.SaveChangesAsync();
        await TestSuperAdmin.SeedAsync(db, PlatformAdmin);
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = TwoOutletUser, OutletId = 11, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { UserId = TwoOutletUser, OutletId = 12, BusinessId = 1 });
        db.Customers.Add(new Customer { Id = 901, BusinessId = 2, Name = "Foreign customer", Phone = "01700000901" });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.Sales.AddRange(
            CreateSale(1, 1, 11, now, 1m, DefaultOutletUser),
            CreateSale(2, 1, 12, now, 10m, DefaultOutletUser),
            // A sale from before Business tenancy: its outlet decides the Business.
            CreateSale(3, null, 13, now, 100m, DefaultOutletUser),
            CreateSale(4, 2, 21, now, 1000m, OtherOwner, customerId: 901),
            // Never in today's totals, but still listed.
            CreateSale(5, 1, 11, now.AddDays(-3), 5000m, DefaultOutletUser));
        var voided = CreateSale(6, 1, 12, now, 7000m, DefaultOutletUser);
        voided.Status = "voided";
        db.Sales.Add(voided);
        await db.SaveChangesAsync();
    }

    private static User CreateUser(long id, long? businessId, long? outletId, string scope) => new()
    {
        Id = id,
        BusinessId = businessId,
        OutletId = outletId,
        Name = $"User {id}",
        Email = $"user{id}@example.test",
        PasswordHash = "x",
        IsActive = true,
        InventoryLocationAccessScope = scope
    };

    private static Sale CreateSale(
        long id,
        long? businessId,
        long outletId,
        DateTime at,
        decimal total,
        long cashierId,
        long? customerId = null) => new()
    {
        Id = id,
        SaleNumber = $"ISO-{id}",
        BusinessId = businessId,
        OutletId = outletId,
        CashierId = cashierId,
        CustomerId = customerId,
        SaleDate = at,
        TotalAmount = total,
        PaymentMethod = "cash",
        Status = "completed"
    };
}
