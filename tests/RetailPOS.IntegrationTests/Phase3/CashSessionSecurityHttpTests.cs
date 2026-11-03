using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>
/// Phase 3 authorization at the HTTP boundary: granular permissions, Business/Outlet scope, terminal ownership and the
/// Super Admin explicit-Business rule. Route guards in Angular are never the only control.
/// </summary>
public sealed class CashSessionSecurityHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public CashSessionSecurityHttpTests(RetailPosApiFactory factory) => _factory = factory;

    private HttpClient Client(string permissions, long userId = 7001, string businessId = "1", string role = "Cashier")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync(bool openSession)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "B1", IsActive = true });
        db.Businesses.Add(new Business { Id = 2, Name = "B2", IsActive = true });
        db.Outlets.Add(new Outlet { Id = 5001, BusinessId = 1, Name = "Main", Address = "Dhaka" });
        db.Outlets.Add(new Outlet { Id = 5002, BusinessId = 1, Name = "Second", Address = "Dhaka" });
        db.Outlets.Add(new Outlet { Id = 6001, BusinessId = 2, Name = "Foreign", Address = "X" });
        db.Users.Add(new User
        {
            Id = 7001, BusinessId = 1, Name = "Cashier", Email = "c@t.test", PasswordHash = "x", OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 7101, BusinessId = 2, Name = "Foreign cashier", Email = "f@t.test", PasswordHash = "x", OutletId = 6001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true
        });
        db.PosTerminals.Add(new PosTerminal { Id = 8001, OutletId = 5001, Name = "C1", Code = "T1", IsActive = true, IsDefault = true });
        db.PosTerminals.Add(new PosTerminal { Id = 8002, OutletId = 5002, Name = "C2", Code = "T2", IsActive = true, IsDefault = true });
        db.PosTerminals.Add(new PosTerminal { Id = 8003, OutletId = 5001, Name = "Inactive", Code = "T3", IsActive = false });
        if (openSession)
            db.CashSessions.Add(new CashSession
            {
                Id = 8101, BusinessId = 1, OutletId = 5001, TerminalId = 8001, BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow.AddHours(-1), OpeningCashAmount = 100m, Status = "open", OpenedByUserId = 7001,
                TimeZoneIdSnapshot = "UTC", DurationSnapshotHours = 20
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_cashier_without_the_close_permission_cannot_close()
    {
        await SeedAsync(openSession: true);
        using var client = Client("sales.create,cash_sessions.view,cash_sessions.open");

        using var response = await client.PostAsJsonAsync("/api/cash-sessions/8101/close", new CloseCashSessionDto { ActualClosingCashAmount = 100m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("open", (await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().CashSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Each_cash_session_action_needs_its_own_permission()
    {
        await SeedAsync(openSession: true);
        using var viewOnly = Client("cash_sessions.view");

        using var open = await viewOnly.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5001, TerminalId = 8001 });
        using var close = await viewOnly.PostAsJsonAsync("/api/cash-sessions/8101/close", new CloseCashSessionDto());
        using var confirm = await viewOnly.PostAsync("/api/cash-sessions/8101/handovers/1/confirm", null);
        using var review = await viewOnly.PostAsJsonAsync("/api/cash-sessions/variances/1/review", new { approve = true });
        using var variances = await viewOnly.GetAsync("/api/cash-sessions/variances?outletId=5001");
        using var download = await viewOnly.GetAsync("/api/cash-sessions/8101/attachments/1");
        using var noPermissions = Client("sales.create");
        using var list = await noPermissions.GetAsync("/api/cash-sessions?outletId=5001");
        using var active = await noPermissions.GetAsync("/api/pos/cash-sessions/active?outletId=5001&terminalId=8001");

        Assert.All(new[] { open, close, confirm, review, variances, download, list, active },
            response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    [Fact]
    public async Task A_user_of_another_business_cannot_read_or_change_the_session()
    {
        await SeedAsync(openSession: true);
        using var foreign = Client("cash_sessions.view,cash_sessions.open,cash_sessions.close", userId: 7101, businessId: "2");

        using var detail = await foreign.GetAsync("/api/cash-sessions/8101");
        using var summary = await foreign.GetAsync("/api/cash-sessions/8101/summary");
        using var list = await foreign.GetAsync("/api/cash-sessions?outletId=5001");
        using var open = await foreign.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5001, TerminalId = 8001, OpeningCashAmount = 1m });
        using var close = await foreign.PostAsJsonAsync("/api/cash-sessions/8101/close", new CloseCashSessionDto { ActualClosingCashAmount = 100m });

        Assert.All(new[] { detail, summary, list, open, close }, response =>
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{response.StatusCode}"));
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("open", (await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().CashSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_user_cannot_open_close_or_list_sessions_of_an_unassigned_outlet_of_the_same_business()
    {
        await SeedAsync(openSession: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.CashSessions.Add(new CashSession
            {
                Id = 8102, BusinessId = 1, OutletId = 5002, TerminalId = 8002, BusinessDate = DateTime.UtcNow.Date,
                OpenedAt = DateTime.UtcNow, OpeningCashAmount = 10m, Status = "open", OpenedByUserId = 7001,
                TimeZoneIdSnapshot = "UTC", DurationSnapshotHours = 20
            });
            await db.SaveChangesAsync();
        }

        using var client = Client("cash_sessions.view,cash_sessions.open,cash_sessions.close");
        using var open = await client.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5002, TerminalId = 8002, OpeningCashAmount = 1m });
        using var close = await client.PostAsJsonAsync("/api/cash-sessions/8102/close", new CloseCashSessionDto { ActualClosingCashAmount = 10m });
        using var list = await client.GetAsync("/api/cash-sessions?outletId=5002");

        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task A_terminal_of_another_outlet_or_an_inactive_terminal_is_refused()
    {
        await SeedAsync(openSession: false);
        using var client = Client("cash_sessions.view,cash_sessions.open,cash_sessions.close");

        using var foreignTerminal = await client.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5001, TerminalId = 8002, OpeningCashAmount = 1m });
        using var inactive = await client.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5001, TerminalId = 8003, OpeningCashAmount = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, foreignTerminal.StatusCode);
        Assert.Contains("CASH_SESSION_TERMINAL_INVALID", await foreignTerminal.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    [Fact]
    public async Task Opening_ignores_a_client_supplied_business_date_and_derives_the_actor_from_the_token()
    {
        await SeedAsync(openSession: false);
        using var client = Client("cash_sessions.view,cash_sessions.open");

        using var response = await client.PostAsJsonAsync("/api/cash-sessions/open", new
        {
            outletId = 5001, terminalId = 8001, openingCashAmount = 25m, businessDate = "2001-01-01", openedByUserId = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().CashSessions.SingleAsync();
        Assert.NotEqual(new DateTime(2001, 1, 1), session.BusinessDate);
        Assert.Equal(7001, session.OpenedByUserId);
        Assert.Equal(20, session.DurationSnapshotHours);
    }

    [Fact]
    public async Task Super_admin_must_select_a_business_and_then_acts_only_inside_it()
    {
        await SeedAsync(openSession: true);
        using (var scope = _factory.Services.CreateScope())
            await TestSuperAdmin.SeedAsync(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(), 9001);

        using var withoutBusiness = TestSuperAdmin.Configure(_factory.CreateClient(), 9001, null);
        using var noBusinessResponse = await withoutBusiness.GetAsync("/api/cash-sessions?outletId=5001");
        Assert.Equal(HttpStatusCode.BadRequest, noBusinessResponse.StatusCode);

        using var wrongBusiness = TestSuperAdmin.Configure(_factory.CreateClient(), 9001, 2);
        using var wrongBusinessResponse = await wrongBusiness.GetAsync("/api/cash-sessions/8101");
        Assert.True(wrongBusinessResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{wrongBusinessResponse.StatusCode}");

        using var selected = TestSuperAdmin.Configure(_factory.CreateClient(), 9001, 1);
        using var ok = await selected.GetAsync("/api/cash-sessions?outletId=5001");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task A_business_owner_can_work_in_any_outlet_of_their_own_business_only()
    {
        await SeedAsync(openSession: false);
        using var owner = Client("*", userId: 7001, role: "BusinessOwner");

        using var own = await owner.PostAsJsonAsync("/api/cash-sessions/open", new OpenCashSessionDto { OutletId = 5002, TerminalId = 8002, OpeningCashAmount = 5m });
        using var foreign = await owner.GetAsync("/api/cash-sessions?outletId=6001");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task The_wildcard_never_satisfies_the_explicit_owner_self_approval_setting()
    {
        await SeedAsync(openSession: false);
        using var owner = Client("*,settings.edit", userId: 7001, role: "BusinessOwner");

        using var response = await owner.PutAsJsonAsync("/api/settings/pos", new { allowOwnerExpenseSelfApproval = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
