using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public sealed class MobileSalesDashboardPostgresHttpTests : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileSalesDashboardPostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dashboard_AppliesRoleScopesAndFailsClosedForNonSingleBusinessDeployment()
    {
        using var owner = _fixture.CreateClient(4101, 4110, "BusinessOwner", "*");
        using var ownerResponse = await owner.GetAsync(ReportPath);
        Assert.True(
            ownerResponse.IsSuccessStatusCode,
            await ownerResponse.Content.ReadAsStringAsync());
        using (var ownerJson = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync()))
        {
            var data = ownerJson.RootElement.GetProperty("data");
            Assert.Equal(685m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(545m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(10, data.GetProperty("invoiceCount").GetInt32());
            Assert.Equal(16, data.GetProperty("unitCount").GetInt64());
            Assert.Equal(54.5m, data.GetProperty("averageInvoiceValue").GetDecimal());
            Assert.Equal(
                new[]
                {
                    "averageInvoiceValue",
                    "endDateUtc",
                    "invoiceCount",
                    "netRevenue",
                    "startDateUtc",
                    "totalRevenue",
                    "unitCount"
                },
                data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        }

        const string activityPath =
            "/api/mobile/v1/insights/sales/activity?startDate=2026-09-15T00%3A00%3A00Z&endDate=2026-09-15T23%3A59%3A59Z";
        using var unauthenticated = _fixture.CreateUnauthenticatedClient();
        using var unauthenticatedActivityResponse = await unauthenticated.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, unauthenticatedActivityResponse.StatusCode);
        using var ownerActivityResponse = await owner.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.OK, ownerActivityResponse.StatusCode);
        using (var activityJson = JsonDocument.Parse(await ownerActivityResponse.Content.ReadAsStringAsync()))
        {
            var data = activityJson.RootElement.GetProperty("data");
            Assert.Equal("UTC", data.GetProperty("timeZoneId").GetString());
            Assert.Equal(
                new[] { "days", "endDateUtc", "startDateUtc", "timeZoneId" },
                data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            var days = data.GetProperty("days");
            Assert.Equal(1, days.GetArrayLength());
            Assert.Equal("2026-09-15", days[0].GetProperty("date").GetString());
            Assert.Equal(10, days[0].GetProperty("invoiceCount").GetInt32());
            Assert.Equal(
                new[] { "date", "invoiceCount" },
                days[0].EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        }

        using var manager = _fixture.CreateClient(4101, 4104, "OutletManager", "reports.sales");
        using var managerResponse = await manager.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode);
        using (var managerJson = JsonDocument.Parse(await managerResponse.Content.ReadAsStringAsync()))
        {
            var data = managerJson.RootElement.GetProperty("data");
            Assert.Equal(660m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(520m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(9, data.GetProperty("invoiceCount").GetInt32());
            Assert.Equal(15, data.GetProperty("unitCount").GetInt64());
            Assert.Equal(520m / 9m, data.GetProperty("averageInvoiceValue").GetDecimal());
        }
        using var managerActivityResponse = await manager.GetAsync(activityPath);
        using (var managerActivityJson = JsonDocument.Parse(await managerActivityResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                9,
                managerActivityJson.RootElement.GetProperty("data").GetProperty("days")[0]
                    .GetProperty("invoiceCount").GetInt32());
        }

        using var salesperson = _fixture.CreateClient(4101, 4102, "SalesPerson", "sales.view");
        using var salespersonResponse = await salesperson.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.OK, salespersonResponse.StatusCode);
        using (var salespersonJson = JsonDocument.Parse(await salespersonResponse.Content.ReadAsStringAsync()))
        {
            var data = salespersonJson.RootElement.GetProperty("data");
            Assert.Equal(380m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(320m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(7, data.GetProperty("invoiceCount").GetInt32());
            Assert.Equal(12, data.GetProperty("unitCount").GetInt64());
            Assert.Equal(320m / 7m, data.GetProperty("averageInvoiceValue").GetDecimal());
        }
        using var salespersonActivityResponse = await salesperson.GetAsync(activityPath);
        using (var salespersonActivityJson = JsonDocument.Parse(
            await salespersonActivityResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                7,
                salespersonActivityJson.RootElement.GetProperty("data").GetProperty("days")[0]
                    .GetProperty("invoiceCount").GetInt32());
        }

        using var cashier = _fixture.CreateClient(4101, 4105, "Cashier", "reports.sales");
        using var cashierResponse = await cashier.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, cashierResponse.StatusCode);
        using var cashierActivityResponse = await cashier.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, cashierActivityResponse.StatusCode);

        using var salespersonWithoutPermission =
            _fixture.CreateClient(4101, 4102, "SalesPerson", "");
        using var salespersonDeniedResponse = await salespersonWithoutPermission.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, salespersonDeniedResponse.StatusCode);
        using var salespersonActivityDeniedResponse =
            await salespersonWithoutPermission.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, salespersonActivityDeniedResponse.StatusCode);

        using var managerWithoutPermission =
            _fixture.CreateClient(4101, 4104, "OutletManager", "sales.view");
        using var managerDeniedResponse = await managerWithoutPermission.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, managerDeniedResponse.StatusCode);
        using var managerActivityDeniedResponse =
            await managerWithoutPermission.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, managerActivityDeniedResponse.StatusCode);

        using var roleSwitchedOwner =
            _fixture.CreateClient(4101, 4110, "BusinessOwner", "*", true, "OutletManager");
        using var roleSwitchedResponse = await roleSwitchedOwner.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, roleSwitchedResponse.StatusCode);
        using var roleSwitchedActivityResponse = await roleSwitchedOwner.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, roleSwitchedActivityResponse.StatusCode);

        using var mismatchedBusiness =
            _fixture.CreateClient(4201, 4110, "BusinessOwner", "*");
        using var mismatchedBusinessResponse = await mismatchedBusiness.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchedBusinessResponse.StatusCode);
        using var mismatchedBusinessActivityResponse = await mismatchedBusiness.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchedBusinessActivityResponse.StatusCode);

        using var invalidRangeResponse = await owner.GetAsync(
            "/api/mobile/v1/dashboard/sales?startDate=2026-10-01T00%3A00%3A00Z&endDate=2026-09-01T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRangeResponse.StatusCode);
        using var invalidActivityRangeResponse = await owner.GetAsync(
            "/api/mobile/v1/insights/sales/activity?startDate=2026-10-01T00%3A00%3A00Z&endDate=2026-09-01T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.BadRequest, invalidActivityRangeResponse.StatusCode);
        using (var invalidActivityRangeJson = JsonDocument.Parse(
            await invalidActivityRangeResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                "INVALID_DATE_RANGE",
                invalidActivityRangeJson.RootElement.GetProperty("message").GetString());
        }
        using var oversizedActivityRangeResponse = await owner.GetAsync(
            "/api/mobile/v1/insights/sales/activity?startDate=2025-09-14T00%3A00%3A00Z&endDate=2026-09-15T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.BadRequest, oversizedActivityRangeResponse.StatusCode);
        using (var oversizedActivityRangeJson = JsonDocument.Parse(
            await oversizedActivityRangeResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                "DATE_RANGE_TOO_LARGE",
                oversizedActivityRangeJson.RootElement.GetProperty("message").GetString());
        }
        using var maximumActivityRangeResponse = await owner.GetAsync(
            "/api/mobile/v1/insights/sales/activity?startDate=2025-09-15T00%3A00%3A00Z&endDate=2026-09-15T00%3A00%3A00Z");
        using (var maximumActivityRangeJson = JsonDocument.Parse(
            await maximumActivityRangeResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                366,
                maximumActivityRangeJson.RootElement.GetProperty("data").GetProperty("days").GetArrayLength());
        }

        using var discountResponse = await owner.GetAsync(
            "/api/mobile/v1/dashboard/sales?startDate=2026-09-15T11%3A00%3A00Z&endDate=2026-09-15T11%3A00%3A00Z");
        using (var discountJson = JsonDocument.Parse(await discountResponse.Content.ReadAsStringAsync()))
        {
            var data = discountJson.RootElement.GetProperty("data");
            Assert.Equal(90m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(90m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(1, data.GetProperty("invoiceCount").GetInt32());
        }

        using var boundaryResponse = await owner.GetAsync(
            "/api/mobile/v1/dashboard/sales?startDate=2026-09-15T12%3A00%3A00Z&endDate=2026-09-15T12%3A00%3A05Z");
        using (var boundaryJson = JsonDocument.Parse(await boundaryResponse.Content.ReadAsStringAsync()))
        {
            var data = boundaryJson.RootElement.GetProperty("data");
            Assert.Equal(230m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(90m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(4, data.GetProperty("invoiceCount").GetInt32());
            Assert.Equal(5, data.GetProperty("unitCount").GetInt64());
        }
        using var activityBoundaryResponse = await owner.GetAsync(
            "/api/mobile/v1/insights/sales/activity?startDate=2026-09-15T12%3A00%3A00Z&endDate=2026-09-15T12%3A00%3A05Z");
        using (var activityBoundaryJson = JsonDocument.Parse(
            await activityBoundaryResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                4,
                activityBoundaryJson.RootElement.GetProperty("data").GetProperty("days")[0]
                    .GetProperty("invoiceCount").GetInt32());
        }

        using var emptyPeriodResponse = await owner.GetAsync(
            "/api/mobile/v1/dashboard/sales?startDate=2027-01-01T00%3A00%3A00Z&endDate=2027-01-31T23%3A59%3A59Z");
        using (var emptyPeriodJson = JsonDocument.Parse(await emptyPeriodResponse.Content.ReadAsStringAsync()))
        {
            var data = emptyPeriodJson.RootElement.GetProperty("data");
            Assert.Equal(0m, data.GetProperty("totalRevenue").GetDecimal());
            Assert.Equal(0m, data.GetProperty("netRevenue").GetDecimal());
            Assert.Equal(0, data.GetProperty("invoiceCount").GetInt32());
            Assert.Equal(0L, data.GetProperty("unitCount").GetInt64());
            Assert.Equal(0m, data.GetProperty("averageInvoiceValue").GetDecimal());
        }
        using var emptyActivityResponse = await owner.GetAsync(
            "/api/mobile/v1/insights/sales/activity?startDate=2027-01-01T00%3A00%3A00Z&endDate=2027-01-03T23%3A59%3A59Z");
        using (var emptyActivityJson = JsonDocument.Parse(await emptyActivityResponse.Content.ReadAsStringAsync()))
        {
            var days = emptyActivityJson.RootElement.GetProperty("data").GetProperty("days");
            Assert.Equal(3, days.GetArrayLength());
            Assert.All(days.EnumerateArray(), day => Assert.Equal(0, day.GetProperty("invoiceCount").GetInt32()));
        }

        await _fixture.AddUnpostedRefundSaleAsync();
        using var unpostedRefundResponse = await owner.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.InternalServerError, unpostedRefundResponse.StatusCode);

        await _fixture.AddUnownedOutletAsync();
        using var blockedByUnownedOutlet = _fixture.CreateClient(4101, 4110, "BusinessOwner", "*");
        using var unownedOutletResponse = await blockedByUnownedOutlet.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, unownedOutletResponse.StatusCode);
        using var unownedOutletActivityResponse = await blockedByUnownedOutlet.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, unownedOutletActivityResponse.StatusCode);

        await _fixture.AddConflictingBusinessAsync();
        using var blockedOwner = _fixture.CreateClient(4101, 4110, "BusinessOwner", "*");
        using var blockedResponse = await blockedOwner.GetAsync(ReportPath);
        Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode);
        using var conflictingBusinessActivityResponse = await blockedOwner.GetAsync(activityPath);
        Assert.Equal(HttpStatusCode.Forbidden, conflictingBusinessActivityResponse.StatusCode);
    }

    private const string ReportPath =
        "/api/mobile/v1/dashboard/sales?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z";
}

public sealed class MobileSalesDashboardPostgresFixture : IAsyncLifetime
{
    private PostgresTestDatabase? _database;
    private MobileSalesDashboardPostgresApiFactory? _factory;

    public async Task InitializeAsync()
    {
        var connectionString = GetDedicatedConnectionString();
        await VerifyDedicatedPostgresAsync(connectionString);
        _database = await PostgresTestDatabase.CreateAsync(
            connectionString,
            PostgresTestDatabasePrefix.MobileSalesDashboard);
        try
        {
            _factory = new MobileSalesDashboardPostgresApiFactory(_database.ConnectionString);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.MigrateAsync();
            await SeedAsync(db);
        }
        catch
        {
            if (_factory is not null)
                await _factory.DisposeAsync();
            await _database.DisposeAsync();
            _database = null;
            throw;
        }
    }

    public HttpClient CreateClient(
        long businessId,
        long userId,
        string role,
        string permissions,
        bool roleSwitched = false,
        string? effectiveRole = null)
    {
        var client = (_factory ?? throw new InvalidOperationException("Fixture is not initialized."))
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", effectiveRole ?? role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (roleSwitched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
            client.DefaultRequestHeaders.Add("X-Test-ActingOutletId", "5101");
        }
        return client;
    }

    public HttpClient CreateUnauthenticatedClient() =>
        (_factory ?? throw new InvalidOperationException("Fixture is not initialized.")).CreateClient();

    public AsyncServiceScope CreateServiceScope() =>
        (_factory ?? throw new InvalidOperationException("Fixture is not initialized.")).Services.CreateAsyncScope();

    public async Task AddOutletAsync(long outletId)
    {
        await using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Outlets.Add(new Outlet { Id = outletId, BusinessId = 4101, Name = $"Submission outlet {outletId}" });
        await db.SaveChangesAsync();
    }

    public async Task AddUserWithRoleAsync(long userId, string roleName, params string[] permissions)
    {
        await using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null)
        {
            user = new User
            {
                Id = userId,
                BusinessId = 4101,
                InventoryLocationAccessScope = User.InventoryAccessAll,
                Name = $"Submission actor {userId}",
                Email = $"submission-actor-{userId}@example.test",
                PasswordHash = "test-only",
                IsActive = true
            };
            db.Users.Add(user);
        }

        await SetRolePermissionsAsync(db, user, roleName, permissions);
    }

    public async Task SetRolePermissionsAsync(long userId, string roleName, params string[] permissions)
    {
        await using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);
        await SetRolePermissionsAsync(db, user, roleName, permissions);
    }

    private static async Task SetRolePermissionsAsync(
        RetailPOSDbContext db,
        User user,
        string roleName,
        string[] permissions)
    {
        var role = await db.Roles.SingleOrDefaultAsync(candidate => candidate.Name == roleName);
        if (role is null)
        {
            role = new Role { Name = roleName };
            db.Roles.Add(role);
        }

        role.Permissions = JsonSerializer.Serialize(permissions);
        user.Role = role;
        await db.SaveChangesAsync();
    }

    public async Task AddConflictingBusinessAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Businesses.Add(new Business { Id = 4201, Name = "Conflicting business" });
        await db.SaveChangesAsync();
    }

    public async Task<int> CountOperationAuditEntitiesAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.AuditEventEntities
            .CountAsync(entity => entity.EntityType == "StockCountOperationReceipt");
    }

    public async Task<bool> MobileAdjustmentDraftIsBlockedAsync(long stockCountId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var stockCount = await db.StockCounts.SingleAsync(count => count.Id == stockCountId);
        stockCount.Status = RetailPOS.Core.Entities.StockCount.StatusApproved;
        await db.SaveChangesAsync();

        var adjustmentDrafts = scope.ServiceProvider.GetRequiredService<
            RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountAdjustmentDraftStore>();
        try
        {
            await adjustmentDrafts.GenerateAsync(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                stockCount.CreatedBy,
                CancellationToken.None);
            return false;
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "Stock movement reconciliation must be resolved before generating an adjustment draft.")
        {
            return true;
        }
    }

    public async Task<long> UploadCountAndGetRevisionAsync(
        long stockCountId,
        decimal physicalQuantity = 9.25m)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var stockCount = await db.StockCounts.SingleAsync(count => count.Id == stockCountId);
        var line = await db.StockCountLines
            .Where(candidate => candidate.StockCountId == stockCountId)
            .OrderBy(candidate => candidate.Id)
            .FirstAsync();
        var uploadStore = scope.ServiceProvider.GetRequiredService<
            RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountUploadStore>();
        await uploadStore.UploadAsync(
            new RetailPOS.Application.Inventory.StockCounts.StockCountUploadInput(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                stockCount.CreatedBy,
                [
                    new RetailPOS.Application.Inventory.StockCounts.StockCountUploadLineUpdate(
                        line.Id,
                        physicalQuantity,
                        physicalQuantity - line.CurrentStock,
                        "ERP upload revision test")
                ]),
            CancellationToken.None);

        return await db.StockCounts
            .AsNoTracking()
            .Where(count => count.Id == stockCountId)
            .Select(count => count.CountRevision)
            .SingleAsync();
    }

    public async Task AddUnownedOutletAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Outlets.Add(new Outlet { Id = 5201, Name = "Unowned outlet" });
        await db.SaveChangesAsync();
    }

    public async Task AddUnpostedRefundSaleAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var sale = CreateSale(
            10113,
            "DASHBOARD-UNPOSTED-REFUND",
            5101,
            4102,
            40m,
            0m,
            1,
            status: "refunded",
            saleDateTime: Utc(11, 7));
        db.Sales.Add(sale);
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 30104,
            BusinessId = 4101,
            JournalType = "SalesReturnPosting",
            TransactionDate = sale.SaleDate,
            PostingDate = Utc(12, 0),
            ReferenceType = "SaleRefund",
            ReferenceId = sale.Id.ToString(),
            OutletId = sale.OutletId,
            Status = JournalEntryStatuses.Draft,
            IsSystemGenerated = true,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = 9301,
                    Description = "Sales return",
                    DebitAmount = 40m,
                    OutletId = sale.OutletId
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private static string GetDedicatedConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "RETAILPOS_TEST_POSTGRES must explicitly target the dedicated A2 PostgreSQL test server.");

        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host != "127.0.0.1" || builder.Port != 55432
            || builder.Database != "a2_test_guard" || builder.Username != "a2_test_runner"
            || string.IsNullOrWhiteSpace(builder.Password))
            throw new InvalidOperationException(
                "RETAILPOS_TEST_POSTGRES does not match the approved isolated A2 PostgreSQL endpoint.");

        builder.Pooling = false;
        return builder.ConnectionString;
    }

    private static async Task VerifyDedicatedPostgresAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT current_database(), current_user, current_setting('server_version_num')",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()
            || reader.GetString(0) != "a2_test_guard"
            || reader.GetString(1) != "a2_test_runner"
            || !reader.GetString(2).StartsWith("16", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The PostgreSQL test target did not match the dedicated A2 PostgreSQL 16 instance.");
        }
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 4101, Name = "Mobile dashboard business" });
        db.Outlets.AddRange(
            new Outlet { Id = 5101, BusinessId = 4101, Name = "Dashboard outlet one" },
            new Outlet { Id = 5102, BusinessId = 4101, Name = "Dashboard outlet two" },
            new Outlet { Id = 5103, BusinessId = 4101, Name = "Dashboard outlet three" });
        db.Users.AddRange(
            new User
            {
                Id = 4110,
                BusinessId = 4101,
                InventoryLocationAccessScope = User.InventoryAccessAll,
                Name = "Dashboard business owner",
                Email = "dashboard-owner@example.test",
                PasswordHash = "test-only",
                IsActive = true
            },
            new User
            {
                Id = 4102,
                BusinessId = 4101,
                OutletId = 5101,
                InventoryLocationAccessScope = User.InventoryAccessSpecific,
                Name = "Dashboard salesperson",
                Email = "dashboard-salesperson@example.test",
                PasswordHash = "test-only",
                IsActive = true
            },
            new User
            {
                Id = 4103,
                BusinessId = 4101,
                OutletId = 5101,
                Name = "Dashboard colleague",
                Email = "dashboard-colleague@example.test",
                PasswordHash = "test-only",
                IsActive = true
            },
            new User
            {
                Id = 4104,
                BusinessId = 4101,
                InventoryLocationAccessScope = User.InventoryAccessSpecific,
                Name = "Dashboard manager",
                Email = "dashboard-manager@example.test",
                PasswordHash = "test-only",
                IsActive = true
            },
            new User
            {
                Id = 4105,
                BusinessId = 4101,
                OutletId = 5101,
                Name = "Dashboard cashier",
                Email = "dashboard-cashier@example.test",
                PasswordHash = "test-only",
                IsActive = true
            });
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 6101,
            UserId = 4104,
            OutletId = 5101,
            BusinessId = 4101,
            IsPrimary = true,
            IsActive = true
        });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment
            {
                Id = 6102,
                UserId = 4104,
                OutletId = 5102,
                BusinessId = 4101,
                IsActive = true
            },
            new UserOutletAssignment
            {
                Id = 6103,
                UserId = 4102,
                OutletId = 5101,
                BusinessId = 4101,
                IsPrimary = true,
                IsActive = true
            },
            new UserOutletAssignment
            {
                Id = 6104,
                UserId = 4102,
                OutletId = 5102,
                BusinessId = 4101,
                IsActive = true
            });
        db.Categories.Add(new Category { Id = 7101, BusinessId = 4101, Name = "Dashboard category", Description = "" });
        db.Products.AddRange(
            new Product { Id = 8101, BusinessId = 4101, Name = "Dashboard product", CategoryId = 7101 },
            new Product
            {
                Id = 8102,
                BusinessId = 4101,
                Name = "Mobile Jacket",
                Description = "Catalogue test product",
                ProductCode = "MOBILE-JACKET",
                Sku = "JACKET-PRODUCT-SKU",
                Barcode = "0000123456",
                CategoryId = 7101,
                BasePrice = 100m,
                CostPrice = 60m,
                TaxRate = 15m,
                HasVariants = true
            },
            new Product
            {
                Id = 8103,
                BusinessId = 4101,
                Name = "Inactive Mobile Jacket",
                ProductCode = "MOBILE-JACKET-INACTIVE",
                CategoryId = 7101,
                Status = ProductStatus.Inactive,
                HasVariants = true
            },
            new Product
            {
                Id = 8104,
                BusinessId = 4101,
                Name = "Mobile Coat",
                ProductCode = "MOBILE-COAT",
                Sku = "COAT-PRODUCT-SKU",
                Barcode = "0000001111",
                CategoryId = 7101
            });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 9101,
            BusinessId = 4101,
            ProductId = 8101,
            Name = "Dashboard variant",
            Sku = "DASHBOARD-1"
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 9102,
            BusinessId = 4101,
            ProductId = 8101,
            Name = "Dashboard variant two",
            Sku = "DASHBOARD-2"
        });
        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 9103,
                BusinessId = 4101,
                ProductId = 8102,
                Name = "Blue / Small",
                Sku = "JACKET-BLUE-S",
                Barcode = "0000123456",
                Attributes = """{"Colour":"Blue","Size":"S"}"""
            },
            new ProductVariant
            {
                Id = 9104,
                BusinessId = 4101,
                ProductId = 8102,
                Name = "Red / Medium",
                Sku = "JACKET-RED-M",
                Barcode = "0000009876",
                Attributes = """{"Colour":"Red","Size":"M"}"""
            },
            new ProductVariant
            {
                Id = 9105,
                BusinessId = 4101,
                ProductId = 8103,
                Name = "Black / Large",
                Sku = "JACKET-BLACK-L",
                Barcode = "0000009877",
                Attributes = """{"Colour":"Black","Size":"L"}"""
            });
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 50101,
                VariantId = 9103,
                LocationId = 5101,
                LocationType = "outlet",
                Quantity = 7,
                LowStockThreshold = 5
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 50102,
                VariantId = 9104,
                LocationId = 5101,
                LocationType = "outlet",
                Quantity = 2,
                LowStockThreshold = 5
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 50103,
                VariantId = 9103,
                LocationId = 5102,
                LocationType = "outlet",
                Quantity = 0,
                LowStockThreshold = 5
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 50104,
                VariantId = 9104,
                LocationId = 5103,
                LocationType = "outlet",
                Quantity = 99,
                LowStockThreshold = 5
            });

        var discountedSale = CreateSale(10101, "DASHBOARD-SALE-1", 5101, 4102, 90m, 10m, 2);
        var replacementSale = CreateSale(
            10109,
            "DASHBOARD-REPLACEMENT",
            5101,
            4102,
            60m,
            0m,
            1,
            originalSaleId: discountedSale.Id,
            saleDateTime: Utc(11, 6));
        discountedSale.ReplacementSaleId = replacementSale.Id;

        var partialRefundSale = CreateSale(
            10107,
            "DASHBOARD-PARTIAL-REFUND",
            5101,
            4102,
            120m,
            0m,
            4,
            status: "refunded",
            saleDateTime: Utc(12, 0, 2));
        partialRefundSale.Items =
        [
            new SaleItem { Id = 20107, VariantId = 9101, Quantity = 2, UnitPrice = 30m, Subtotal = 60m },
            new SaleItem { Id = 30107, VariantId = 9102, Quantity = 2, UnitPrice = 30m, Subtotal = 60m }
        ];
        var fullyRefundedSale = CreateSale(
            10108,
            "DASHBOARD-FULL-REFUND",
            5102,
            4103,
            80m,
            0m,
            2,
            status: "refunded",
            saleDateTime: Utc(12, 0, 3));

        var voidedSale = CreateSale(
            10105,
            "DASHBOARD-VOIDED",
            5101,
            4102,
            300m,
            0m,
            5,
            status: "voided",
            saleDateTime: Utc(11, 4));
        var cancelledSale = CreateSale(
            10106,
            "DASHBOARD-CANCELLED",
            5101,
            4102,
            400m,
            0m,
            4,
            status: "cancelled",
            saleDateTime: Utc(11, 5));

        db.Sales.AddRange(
            discountedSale,
            CreateSale(10102, "DASHBOARD-SALE-2", 5101, 4103, 200m, 0m, 3, saleDateTime: Utc(11, 1)),
            CreateSale(10103, "DASHBOARD-SALE-3", 5102, 4102, 50m, 0m, 1, saleDateTime: Utc(11, 2)),
            CreateSale(10104, "DASHBOARD-SALE-4", 5103, 4105, 25m, 0m, 1, saleDateTime: Utc(11, 3)),
            voidedSale,
            cancelledSale,
            partialRefundSale,
            fullyRefundedSale,
            replacementSale,
            CreateSale(10110, "DASHBOARD-START-BOUNDARY", 5101, 4102, 10m, 0m, 1, saleDateTime: Utc(12, 0, 0)),
            CreateSale(10111, "DASHBOARD-END-BOUNDARY", 5101, 4102, 20m, 0m, 2, saleDateTime: Utc(12, 0, 5)),
            CreateSale(10112, "DASHBOARD-AFTER-END", 5101, 4102, 30m, 0m, 3, saleDateTime: Utc(12, 0, 6)));

        db.ChartAccounts.Add(new ChartAccount
        {
            Id = 9301,
            BusinessId = 4101,
            AccountCode = "4020",
            AccountName = "Sales Returns",
            AccountType = ChartAccountTypes.Expense,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsActive = true
        });
        db.SaleVoids.Add(new SaleVoid
        {
            Id = 40101,
            SaleId = voidedSale.Id,
            PreviousStatus = "completed",
            Reason = "test void",
            VoidedAt = Utc(12, 0, 0)
        });
        db.JournalEntries.AddRange(
            CreateRefundJournal(30101, voidedSale, 300m, Utc(12, 0, 0)),
            CreateRefundJournal(30102, partialRefundSale, 60m, Utc(12, 0, 0)),
            CreateRefundJournal(30103, fullyRefundedSale, 80m, Utc(12, 0, 5)));
        db.StockLedgers.AddRange(
            CreateReturnMovement(9101, 5101, partialRefundSale.Id, 1, Utc(12, 0, 0), 1),
            CreateReturnMovement(9102, 5101, partialRefundSale.Id, 1, Utc(12, 0, 0), 2),
            CreateReturnMovement(9101, 5102, fullyRefundedSale.Id, 2, Utc(12, 0, 5), 3),
            CreateReturnMovement(9101, 5101, voidedSale.Id, 5, Utc(12, 0, 0), 4));
        await db.SaveChangesAsync();
    }

    private static Sale CreateSale(
        long saleId,
        string saleNumber,
        long outletId,
        long cashierId,
        decimal totalAmount,
        decimal discount,
        int quantity,
        string status = "completed",
        long? originalSaleId = null,
        DateTime? saleDateTime = null)
    {
        var saleTime = saleDateTime ?? Utc(11, 0);
        var itemSubtotal = totalAmount + discount;
        return new Sale
        {
            Id = saleId,
            SaleNumber = saleNumber,
            OutletId = outletId,
            CashierId = cashierId,
            SaleDate = saleTime,
            SaleDateTime = saleTime,
            Status = status,
            PaymentMethod = "cash",
            TotalAmount = totalAmount,
            Discount = discount,
            OriginalSaleId = originalSaleId,
            Items =
            [
                new SaleItem
                {
                    Id = saleId + 10000,
                    VariantId = 9101,
                    Quantity = quantity,
                    UnitPrice = itemSubtotal / quantity,
                    Subtotal = itemSubtotal
                }
            ]
        };
    }

    private static JournalEntry CreateRefundJournal(
        long journalId,
        Sale sale,
        decimal salesReturnAmount,
        DateTime postedAt) =>
        new()
        {
            Id = journalId,
            BusinessId = 4101,
            JournalType = "SalesReturnPosting",
            TransactionDate = sale.SaleDateTime ?? sale.SaleDate,
            PostingDate = postedAt,
            ReferenceType = "SaleRefund",
            ReferenceId = sale.Id.ToString(),
            OutletId = sale.OutletId,
            Status = JournalEntryStatuses.Posted,
            IsSystemGenerated = true,
            PostedAt = postedAt,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = 9301,
                    Description = "Sales return",
                    DebitAmount = salesReturnAmount,
                    OutletId = sale.OutletId
                },
                new JournalEntryLine
                {
                    AccountId = 9301,
                    Description = "VAT payable reversal",
                    DebitAmount = 5m,
                    OutletId = sale.OutletId
                }
            ]
        };

    private static StockLedger CreateReturnMovement(
        long variantId,
        long outletId,
        long saleId,
        int quantity,
        DateTime createdAt,
        long movementSequence) =>
        new()
        {
            VariantId = variantId,
            LocationId = outletId,
            LocationType = "outlet",
            TransactionType = StockLedgerTransactionType.Return,
            QtyIn = quantity,
            BalanceAfter = quantity,
            UnitCost = 1m,
            MovementValue = quantity,
            IsCostValuationReliable = true,
            BusinessDate = createdAt.Date,
            MovementSequence = movementSequence,
            ReferenceType = StockLedgerReferenceType.Sale,
            ReferenceId = saleId,
            CreatedAt = createdAt
        };

    private static DateTime Utc(int hour, int minute, int second = 0) =>
        new(2026, 9, 15, hour, minute, second, DateTimeKind.Utc);
}

internal sealed class MobileSalesDashboardPostgresApiFactory(string connectionString)
    : RetailPosApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FeatureFlags:StockCountAsyncAdjustmentEnabled"] = "true"
            }));
        builder.ConfigureTestServices(services =>
        {
            // Deterministic tests drive the processors directly instead of the background sweepers.
            foreach (var descriptor in services
                         .Where(candidate => candidate.ImplementationType == typeof(
                                 RetailPOS.API.Services.MobileStockTakeSubmissionSweeperHostedService)
                             || candidate.ImplementationType == typeof(
                                 RetailPOS.API.Services.StockCountAdjustmentSweeperHostedService))
                         .ToArray())
            {
                services.Remove(descriptor);
            }
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
            services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
                options
                    .UseNpgsql(connectionString)
                    .AddInterceptors(serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>()));
        });
    }
}
