using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// The real HTTP pipeline and PostgreSQL. Business 1 has outlets 11, 12 and 13; Business 2 has outlet 21.
/// Every refusal has a positive control, so a 403 proves the check rather than a broken fixture.
/// </summary>
public sealed class SalesPosCashSessionAccessPostgresHttpTests
{
    private const long NarrowOwner = 1;
    private const long Regular = 3;
    private const long UnassignedUser = 4;
    private const long OutletTwelveUser = 5;
    private const long OtherOwner = 7;
    private const long PlatformAdmin = 8;
    private const long ForeignDefaultUser = 9;
    private const long ForeignPrimaryUser = 10;

    private const long SaleAtOutlet11 = 1;
    private const long SaleAtOutlet13 = 2;
    private const long SaleInBusiness2 = 3;

    private const string Cashier = "Cashier";
    private const string BusinessOwner = "BusinessOwner";

    private const string AllPermissions =
        "sales.view,sales.create,sales.void,sales.refund,cash_sessions.view,cash_sessions.open,cash_sessions.close," +
        "cash_sessions.handover.confirm,cash_sessions.attachments.upload,cash_sessions.attachments.download," +
        "cash_sessions.variance.view,cash_sessions.variance.approve,cash_sessions.variance.retry_posting";

    // ── Default Outlet ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task ForeignDefaultOutlet_NeverResolves() => RunAsync(async (factory, _) =>
    {
        using var client = Client(factory, Cashier, ForeignDefaultUser);

        using var locations = await client.GetAsync("/api/me/outlets");
        Assert.Equal(HttpStatusCode.OK, locations.StatusCode);
        using var body = await JsonDocument.ParseAsync(await locations.Content.ReadAsStreamAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("defaultOutletId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("defaultLocationId").ValueKind);
        Assert.Empty(data.GetProperty("outlets").EnumerateArray());

        foreach (var url in new[]
                 {
                     "/api/pos/terminals",
                     "/api/pos/terminals?outletId=21",
                     "/api/pos/salespersons",
                     "/api/pos/salespersons?outletId=21",
                     "/api/pos/salespersons?outletId=21&terminalId=211"
                 })
        {
            using var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url}: {(int)response.StatusCode}");
            Assert.DoesNotContain("Terminal 211", await response.Content.ReadAsStringAsync());
        }

        using var open = await client.PostAsJsonAsync(
            "/api/cash-sessions/open",
            new { outletId = 21, terminalId = 211, openingCashAmount = 0m });
        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
    });

    [Fact]
    public Task ForeignPrimaryAssignment_IsNotADefault_AndOnlyTheBusinessAssignmentCounts() => RunAsync(async (factory, _) =>
    {
        using var client = Client(factory, Cashier, ForeignPrimaryUser);

        using var locations = await client.GetAsync("/api/me/outlets");
        using var body = await JsonDocument.ParseAsync(await locations.Content.ReadAsStreamAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(12, data.GetProperty("defaultLocationId").GetInt64());
        Assert.Equal("outlet", data.GetProperty("defaultLocationType").GetString());
        Assert.Equal(12, Assert.Single(data.GetProperty("outlets").EnumerateArray()).GetProperty("id").GetInt64());

        Assert.Contains("Terminal 121", await BodyAsync(client, "/api/pos/terminals"));
        using var foreign = await client.GetAsync("/api/pos/terminals?outletId=21");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    });

    [Fact]
    public Task PosLookups_StillWorkForTheUsersOwnOutlet() => RunAsync(async (factory, _) =>
    {
        using var client = Client(factory, Cashier, Regular);

        Assert.Contains("Terminal 111", await BodyAsync(client, "/api/pos/terminals"));
        var salespersons = await BodyAsync(client, "/api/pos/salespersons");
        Assert.Contains("User 3", salespersons);
        Assert.DoesNotContain("User 7", salespersons);
        using var other = await client.GetAsync("/api/pos/terminals?outletId=12");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    });

    // ── Sale detail and receipts ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("/receipt")]
    [InlineData("/receipt-document")]
    [InlineData("/receipt-pdf")]
    public Task BusinessOwner_ReadsEverySaleOfTheirBusiness_ButNoOther(string path) => RunAsync(async (factory, _) =>
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var owner = Client(factory, BusinessOwner, NarrowOwner);

        using var outsideInventoryScope = await owner.GetAsync($"/api/sales/{SaleAtOutlet13}{path}");
        var text = await outsideInventoryScope.Content.ReadAsStringAsync();
        Assert.True(outsideInventoryScope.StatusCode == HttpStatusCode.OK, $"{(int)outsideInventoryScope.StatusCode} {text}");

        using var foreign = await owner.GetAsync($"/api/sales/{SaleInBusiness2}{path}");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.DoesNotContain("ACC-3", await foreign.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task BusinessOwner_ReceiptActions_FollowTheSameReadAccess() => RunAsync(async (factory, _) =>
    {
        using var owner = Client(factory, BusinessOwner, NarrowOwner);

        using var reprint = await owner.PostAsync($"/api/sales/{SaleAtOutlet13}/reprint", null);
        Assert.Equal(HttpStatusCode.OK, reprint.StatusCode);
        using var foreign = await owner.PostAsync($"/api/sales/{SaleInBusiness2}/reprint", null);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    });

    [Fact]
    public Task VoidAndRefund_KeepTheirStricterOutletRule_ForABusinessOwner() => RunAsync(async (factory, _) =>
    {
        using var owner = Client(factory, BusinessOwner, NarrowOwner);

        // The Owner can read this sale, but outlet 13 is outside the outlet policy that governs mutations.
        using var voided = await owner.PostAsJsonAsync($"/api/sales/{SaleAtOutlet13}/void", new { reason = "Test" });
        Assert.Equal(HttpStatusCode.Forbidden, voided.StatusCode);
        using var refunded = await owner.PostAsJsonAsync($"/api/sales/{SaleAtOutlet13}/refund", new { reason = "Test" });
        Assert.Equal(HttpStatusCode.Forbidden, refunded.StatusCode);

        // Read access never replaces the mutation permissions.
        using var readOnly = Client(factory, BusinessOwner, NarrowOwner, permissions: "sales.view");
        using var denied = await readOnly.PostAsJsonAsync($"/api/sales/{SaleAtOutlet11}/void", new { reason = "Test" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.All(await db.Sales.AsNoTracking().ToListAsync(), sale => Assert.Equal("completed", sale.Status));
    });

    [Fact]
    public Task RegularUser_AndSwitchedOwner_DoNotGainBusinessWideDetail() => RunAsync(async (factory, _) =>
    {
        using var regular = Client(factory, Cashier, Regular);
        using var own = await regular.GetAsync($"/api/sales/{SaleAtOutlet11}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var other = await regular.GetAsync($"/api/sales/{SaleAtOutlet13}");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        using var receipt = await regular.GetAsync($"/api/sales/{SaleAtOutlet13}/receipt-document");
        Assert.Equal(HttpStatusCode.Forbidden, receipt.StatusCode);

        using var switched = Client(factory, BusinessOwner, NarrowOwner);
        switched.DefaultRequestHeaders.Add("X-Test-EffectiveRole", Cashier);
        switched.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        switched.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "11");
        using var acting = await switched.GetAsync($"/api/sales/{SaleAtOutlet11}");
        Assert.Equal(HttpStatusCode.OK, acting.StatusCode);
        using var outside = await switched.GetAsync($"/api/sales/{SaleAtOutlet13}");
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
    });

    [Fact]
    public Task SuperAdmin_ReadsSaleDetailOnlyInTheSelectedBusiness() => RunAsync(async (factory, _) =>
    {
        using var client = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        using var noBusiness = await client.GetAsync($"/api/sales/{SaleAtOutlet11}");
        Assert.Equal(HttpStatusCode.BadRequest, noBusiness.StatusCode);

        client.DefaultRequestHeaders.Add("X-Business-Id", "1");
        using var selected = await client.GetAsync($"/api/sales/{SaleAtOutlet13}");
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        using var other = await client.GetAsync($"/api/sales/{SaleInBusiness2}");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    });

    // ── Cash sessions by ID ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BusinessOwner, OtherOwner, "2")]
    [InlineData(Cashier, OutletTwelveUser, "1")]
    [InlineData(Cashier, UnassignedUser, "1")]
    [InlineData(Cashier, ForeignDefaultUser, "1")]
    public Task CashSessionRoutes_RefuseCallersOutsideTheOutlet(string role, long userId, string businessId) =>
        RunAsync(async (factory, session) =>
        {
            using var client = Client(factory, role, userId, businessId);
            var id = session.SessionId;

            foreach (var url in new[]
                     {
                         $"/api/cash-sessions/{id}",
                         $"/api/cash-sessions/{id}/summary",
                         $"/api/cash-sessions/{id}/closing-summary.pdf",
                         $"/api/cash-sessions/{id}/attachments/1",
                         "/api/cash-sessions?outletId=11",
                         "/api/cash-sessions/active?outletId=11",
                         "/api/cash-sessions/open-context?outletId=11",
                         "/api/cash-sessions/business-day-sales?outletId=11",
                         "/api/cash-sessions/variances?outletId=11"
                     })
            {
                using var response = await client.GetAsync(url);
                var text = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url}: {(int)response.StatusCode} {text}");
                Assert.DoesNotContain("ACC-1", text);
            }

            using var close = await client.PostAsJsonAsync(
                $"/api/cash-sessions/{id}/close",
                new { actualClosingCashAmount = 0m });
            using var confirm = await client.PostAsync($"/api/cash-sessions/{id}/handovers/1/confirm", null);
            using var upload = await client.PostAsync($"/api/cash-sessions/{id}/attachments", Attachment());
            using var review = await client.PostAsJsonAsync(
                $"/api/cash-sessions/variances/{session.ApprovalId}/review",
                new { approve = true, notes = "Test" });
            using var post = await client.PostAsync($"/api/cash-sessions/variances/{session.ApprovalId}/post", null);
            using var pending = await client.PostAsync("/api/cash-sessions/variances/process-pending?outletId=11", null);

            Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, pending.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(CashSessionStatuses.Open, (await db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Status);
            var approval = await db.CashDifferenceApprovals.AsNoTracking().SingleAsync(a => a.Id == session.ApprovalId);
            Assert.Equal(CashDifferenceStatuses.Pending, approval.Status);
        });

    [Fact]
    public Task CashSessionDetail_ShowsItsSalesToTheOutletsOwnUser() => RunAsync(async (factory, session) =>
    {
        using var client = Client(factory, Cashier, Regular);

        Assert.Contains("ACC-1", await BodyAsync(client, $"/api/cash-sessions/{session.SessionId}"));
        using var summary = await client.GetAsync($"/api/cash-sessions/{session.SessionId}/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        // Past the outlet check, a missing attachment is simply not found.
        using var attachment = await client.GetAsync($"/api/cash-sessions/{session.SessionId}/attachments/999");
        Assert.Equal(HttpStatusCode.NotFound, attachment.StatusCode);
    });

    [Fact]
    public Task CashSession_ActingOutletOfAnUnauthorizedSwitch_GrantsNothing() => RunAsync(async (factory, session) =>
    {
        using var client = Client(factory, Cashier, OutletTwelveUser);
        client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", BusinessOwner);
        client.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "11");

        using var response = await client.GetAsync($"/api/cash-sessions/{session.SessionId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    });

    [Fact]
    public Task CashSession_SuperAdminFollowsTheSelectedBusiness() => RunAsync(async (factory, session) =>
    {
        using var other = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        other.DefaultRequestHeaders.Add("X-Business-Id", "2");
        using var refused = await other.GetAsync($"/api/cash-sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var selected = Client(factory, TestSuperAdmin.RoleName, PlatformAdmin, businessId: "none");
        selected.DefaultRequestHeaders.Add("X-Business-Id", "1");
        Assert.Contains("ACC-1", await BodyAsync(selected, $"/api/cash-sessions/{session.SessionId}"));
    });

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed record SeededSession(long SessionId, long ApprovalId);

    private static MultipartFormDataContent Attachment()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "evidence.pdf");
        return content;
    }

    private static HttpClient Client(
        MobileSalesDashboardPostgresApiFactory factory,
        string role,
        long userId,
        string businessId = "1",
        string permissions = AllPermissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static async Task<string> BodyAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {text}");
        return text;
    }

    private static Task RunAsync(Func<MobileSalesDashboardPostgresApiFactory, SeededSession, Task> test) =>
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

                using var opener = Client(factory, Cashier, Regular);
                using var opened = await opener.PostAsJsonAsync(
                    "/api/cash-sessions/open",
                    new { outletId = 11, terminalId = 111, openingCashAmount = 100m });
                var text = await opened.Content.ReadAsStringAsync();
                Assert.True(opened.StatusCode == HttpStatusCode.OK, text);
                using var body = JsonDocument.Parse(text);
                var sessionId = body.RootElement.GetProperty("data").GetProperty("id").GetInt64();

                long approvalId;
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                    await db.Sales.Where(sale => sale.Id == SaleAtOutlet11)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(sale => sale.CashSessionId, sessionId));
                    var approval = new CashDifferenceApproval
                    {
                        BusinessId = 1,
                        OutletId = 11,
                        CashSessionId = sessionId,
                        VarianceAmount = 5m,
                        Reason = "Test",
                        CreatedByUserId = Regular
                    };
                    db.CashDifferenceApprovals.Add(approval);
                    await db.SaveChangesAsync();
                    approvalId = approval.Id;
                }

                await test(factory, new SeededSession(sessionId, approvalId));
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
        db.PosTerminals.AddRange(
            new PosTerminal { Id = 111, OutletId = 11, Name = "Terminal 111", Code = "T111", IsActive = true },
            new PosTerminal { Id = 121, OutletId = 12, Name = "Terminal 121", Code = "T121", IsActive = true },
            new PosTerminal { Id = 211, OutletId = 21, Name = "Terminal 211", Code = "T211", IsActive = true });
        await db.SaveChangesAsync();
        db.Users.AddRange(
            CreateUser(NarrowOwner, 1, 11, User.InventoryAccessAssignedOnly),
            CreateUser(Regular, 1, 11, User.InventoryAccessAssignedOnly),
            CreateUser(UnassignedUser, 1, null, User.InventoryAccessAssignedOnly),
            CreateUser(OutletTwelveUser, 1, 12, User.InventoryAccessAssignedOnly),
            CreateUser(OtherOwner, 2, 21, User.InventoryAccessAssignedOnly),
            CreateUser(ForeignDefaultUser, 1, 21, User.InventoryAccessAssignedOnly),
            CreateUser(ForeignPrimaryUser, 1, null, User.InventoryAccessSpecific));
        await db.SaveChangesAsync();
        await TestSuperAdmin.SeedAsync(db, PlatformAdmin);
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { UserId = ForeignPrimaryUser, OutletId = 21, BusinessId = 2, IsPrimary = true },
            new UserOutletAssignment { UserId = ForeignPrimaryUser, OutletId = 12, BusinessId = 1 });
        await db.SaveChangesAsync();
        db.Sales.AddRange(
            CreateSale(SaleAtOutlet11, 1, 11, Regular),
            CreateSale(SaleAtOutlet13, 1, 13, Regular),
            CreateSale(SaleInBusiness2, 2, 21, OtherOwner));
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

    private static Sale CreateSale(long id, long businessId, long outletId, long cashierId) => new()
    {
        Id = id,
        SaleNumber = $"ACC-{id}",
        BusinessId = businessId,
        OutletId = outletId,
        CashierId = cashierId,
        SaleDate = DateTime.UtcNow,
        TotalAmount = 10m,
        PaymentMethod = "cash",
        Status = "completed"
    };
}
