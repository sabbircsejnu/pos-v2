using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Outlets.SalesTargets;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.OwnerDashboards;

/// <summary>Real PostgreSQL, real HTTP pipeline: save with the outlet, history, tenancy, and the figures reports read.</summary>
public sealed class OutletSalesTargetPostgresHttpTests : IClassFixture<OwnerDashboardPostgresFixture>
{
    private const string Outlets = "/api/outlets";
    private const string Dashboard = "/api/v1/owner-dashboard";
    private const string SalesPermission = "dashboard.sales_finance.view";
    private readonly OwnerDashboardPostgresFixture _fixture;

    public OutletSalesTargetPostgresHttpTests(OwnerDashboardPostgresFixture fixture) => _fixture = fixture;

    private HttpClient Owner() => _fixture.Client(OwnerDashboardSeed.OwnerId, "BusinessOwner", "*");

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..30];

    private static string Today => DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd");

    private static string InDays(int days) => DateTime.UtcNow.AddHours(6).AddDays(days).ToString("yyyy-MM-dd");

    private static object[] Days(decimal amount, params int[] off) =>
        Enumerable.Range(0, 7)
            .Select(day => (object)new
            {
                dayOfWeek = day,
                isOffDay = off.Contains(day),
                targetAmount = off.Contains(day) ? (decimal?)null : amount
            })
            .ToArray();

    private static object Targets(object[] days, string? effectiveFrom = null, bool enabled = true, int? expected = null, object[]? overrides = null) =>
        new { isEnabled = enabled, effectiveFrom = effectiveFrom ?? Today, days, overrides, expectedVersionNo = expected };

    private static object OutletBody(string name, object? salesTargets = null) =>
        new { name, address = "12 Road, Dhaka", contactNumber = "01700000000", salesTargets };

    private async Task<long> CreateAsync(HttpClient client, object? salesTargets = null)
    {
        using var response = await client.PostAsJsonAsync(Outlets, OutletBody(Unique("Target outlet"), salesTargets));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> DataAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private static decimal? Metric(JsonElement data, string name)
    {
        var value = data.GetProperty(name).GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    }

    private static string Status(JsonElement data, string name) =>
        data.GetProperty(name).GetProperty("status").GetString()!;

    // ---- Saved with the outlet ----------------------------------------------------------------------------------

    [Fact]
    public async Task Create_SavesOutletAndTargetsTogether_AndTheyLoadBack()
    {
        using var client = Owner();

        var id = await CreateAsync(client, Targets(Days(100.50m, off: 2)));

        // Next week is entirely after the effective date, so every weekday counts.
        var data = await DataAsync(client, $"{Outlets}/{id}/sales-targets?referenceDate={InDays(7)}");
        var latest = data.GetProperty("latest");
        Assert.Equal(1, latest.GetProperty("versionNo").GetInt32());
        Assert.True(latest.GetProperty("isEnabled").GetBoolean());
        var days = latest.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(7, days.Count);
        var tuesday = days.Single(day => day.GetProperty("dayOfWeek").GetInt32() == 2);
        Assert.True(tuesday.GetProperty("isOffDay").GetBoolean());
        Assert.Equal(JsonValueKind.Null, tuesday.GetProperty("targetAmount").ValueKind);
        Assert.Equal(100.50m, days.Single(day => day.GetProperty("dayOfWeek").GetInt32() == 1).GetProperty("targetAmount").GetDecimal());

        var weekly = data.GetProperty("periods").EnumerateArray().Single(period => period.GetProperty("periodType").GetString() == "weekly");
        Assert.Equal(1, weekly.GetProperty("offDays").GetInt32());
    }

    [Fact]
    public async Task Create_WithBadTargets_LeavesNoOutletBehind()
    {
        using var client = Owner();
        var name = Unique("Rolled back");

        using var response = await client.PostAsJsonAsync(Outlets, OutletBody(name, Targets(Days(-5m))));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SalesTargetErrorCodes.InvalidConfiguration, await ErrorCodeAsync(response));
        await _fixture.WithDbAsync(async db => Assert.False(await db.Outlets.AnyAsync(outlet => outlet.Name == name)));
    }

    [Fact]
    public async Task Create_WithoutTheBlock_HasNoConfiguration()
    {
        using var client = Owner();

        var id = await CreateAsync(client);

        var data = await DataAsync(client, $"{Outlets}/{id}/sales-targets");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("latest").ValueKind);
        Assert.Empty(data.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Disabled_StillKeepsWeeklyOffDays()
    {
        using var client = Owner();

        var id = await CreateAsync(client, Targets(Days(0m, off: 5), enabled: false));

        var latest = (await DataAsync(client, $"{Outlets}/{id}/sales-targets")).GetProperty("latest");
        Assert.False(latest.GetProperty("isEnabled").GetBoolean());
        Assert.Contains(latest.GetProperty("days").EnumerateArray(),
            day => day.GetProperty("dayOfWeek").GetInt32() == 5 && day.GetProperty("isOffDay").GetBoolean());
    }

    [Fact]
    public async Task AllSevenDaysOff_IsRefused()
    {
        using var client = Owner();

        using var response = await client.PostAsJsonAsync(
            Outlets, OutletBody(Unique("Always closed"), Targets(Days(0m, 0, 1, 2, 3, 4, 5, 6))));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SalesTargetErrorCodes.AllDaysOff, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task PastEffectiveDate_IsRefused()
    {
        using var client = Owner();

        using var response = await client.PostAsJsonAsync(
            Outlets, OutletBody(Unique("Backdated"), Targets(Days(10m), effectiveFrom: InDays(-3))));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SalesTargetErrorCodes.InvalidEffectiveDate, await ErrorCodeAsync(response));
    }

    // ---- History and concurrency --------------------------------------------------------------------------------

    [Fact]
    public async Task Update_AppendsAVersion_AndKeepsTheOldOne()
    {
        using var client = Owner();
        var name = Unique("Versioned");
        using var created = await client.PostAsJsonAsync(Outlets, OutletBody(name, Targets(Days(100m))));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        using var response = await client.PutAsJsonAsync(
            $"{Outlets}/{id}", OutletBody(name, Targets(Days(200m, off: 2), effectiveFrom: InDays(7), expected: 1)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await DataAsync(client, $"{Outlets}/{id}/sales-targets");
        Assert.Equal(2, data.GetProperty("history").GetArrayLength());
        Assert.Equal(2, data.GetProperty("latest").GetProperty("versionNo").GetInt32());
        // The new version has not started, so today still reads the first one.
        Assert.Equal(1, data.GetProperty("current").GetProperty("versionNo").GetInt32());
    }

    [Fact]
    public async Task Change_IsAudited_WithWhatChanged()
    {
        using var client = Owner();

        var id = await CreateAsync(client, Targets(Days(100m, off: 2)));

        await _fixture.WithDbAsync(async db =>
        {
            // The outlet's own automatic audit event is here too, alongside the target one.
            var audits = await db.AuditEvents
                .AsNoTracking()
                .Include(item => item.Entities)
                .Where(item => item.PrimaryEntityType == nameof(Outlet) && item.PrimaryEntityId == id.ToString())
                .ToListAsync();
            Assert.Contains(audits.SelectMany(item => item.Entities), entity => entity.EntityType == nameof(OutletSalesTargetVersion));
        });
    }

    [Fact]
    public async Task Update_WithSameValues_AddsNothing()
    {
        using var client = Owner();
        var name = Unique("Unchanged");
        using var created = await client.PostAsJsonAsync(Outlets, OutletBody(name, Targets(Days(100m))));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        using var response = await client.PutAsJsonAsync($"{Outlets}/{id}", OutletBody(name, Targets(Days(100m), expected: 1)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, (await DataAsync(client, $"{Outlets}/{id}/sales-targets")).GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Update_WithoutTheBlock_LeavesTargetsAlone()
    {
        using var client = Owner();
        var name = Unique("Other tab");
        using var created = await client.PostAsJsonAsync(Outlets, OutletBody(name, Targets(Days(100m))));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        using var response = await client.PutAsJsonAsync($"{Outlets}/{id}", OutletBody(name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, (await DataAsync(client, $"{Outlets}/{id}/sales-targets")).GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Update_FromAStaleForm_IsRefused()
    {
        using var client = Owner();
        var name = Unique("Stale form");
        using var created = await client.PostAsJsonAsync(Outlets, OutletBody(name, Targets(Days(100m))));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        using var response = await client.PutAsJsonAsync($"{Outlets}/{id}", OutletBody(name, Targets(Days(200m), expected: null)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SalesTargetErrorCodes.Conflict, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Override_ThenResetToAuto_KeepsTheRowAsHistory()
    {
        using var client = Owner();
        var name = Unique("Overridden");
        var firstOfNextMonth = DateOnly.Parse(InDays(0)).AddMonths(1);
        firstOfNextMonth = new DateOnly(firstOfNextMonth.Year, firstOfNextMonth.Month, 1);
        var month = firstOfNextMonth.ToString("yyyy-MM-dd");
        object[] Override(decimal? amount) =>
            [new { periodType = SalesTargetPeriodTypes.Monthly, periodFrom = month, overrideAmount = amount, reason = "Eid" }];

        using var created = await client.PostAsJsonAsync(
            Outlets, OutletBody(name, Targets(Days(100m), overrides: Override(5_000m))));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        var monthly = (await DataAsync(client, $"{Outlets}/{id}/sales-targets?referenceDate={month}"))
            .GetProperty("periods").EnumerateArray().Single(period => period.GetProperty("periodType").GetString() == "monthly");
        Assert.True(monthly.GetProperty("isOverridden").GetBoolean());
        Assert.Equal(5_000m, monthly.GetProperty("target").GetDecimal());
        Assert.Equal("Eid", monthly.GetProperty("reason").GetString());

        using var reset = await client.PutAsJsonAsync(
            $"{Outlets}/{id}", OutletBody(name, Targets(Days(100m), expected: 1, overrides: Override(null))));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var afterReset = (await DataAsync(client, $"{Outlets}/{id}/sales-targets?referenceDate={month}"))
            .GetProperty("periods").EnumerateArray().Single(period => period.GetProperty("periodType").GetString() == "monthly");
        Assert.False(afterReset.GetProperty("isOverridden").GetBoolean());
        await _fixture.WithDbAsync(async db =>
        {
            var row = await db.Set<OutletSalesTargetOverride>().SingleAsync(item => item.OutletId == id);
            Assert.NotNull(row.ClearedAt);
            Assert.Equal(OwnerDashboardSeed.OwnerId, row.ClearedByUserId);
        });
    }

    [Fact]
    public async Task DeletingAnOutlet_RemovesItsConfiguration()
    {
        using var client = Owner();
        var month = DateOnly.Parse(InDays(0)).AddMonths(1);
        var id = await CreateAsync(client, Targets(
            Days(100m),
            overrides: [new { periodType = SalesTargetPeriodTypes.Monthly, periodFrom = new DateOnly(month.Year, month.Month, 1).ToString("yyyy-MM-dd"), overrideAmount = 1m, reason = (string?)null }]));

        using var response = await client.DeleteAsync($"{Outlets}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fixture.WithDbAsync(async db =>
        {
            Assert.False(await db.Set<OutletSalesTargetVersion>().AnyAsync(version => version.OutletId == id));
            Assert.False(await db.Set<OutletSalesTargetOverride>().AnyAsync(item => item.OutletId == id));
        });
    }

    // ---- Authorization and tenancy ------------------------------------------------------------------------------

    [Fact]
    public async Task ForeignOutlet_IsNotFound_ForReadAndWrite()
    {
        using var client = Owner();

        using var read = await client.GetAsync($"{Outlets}/{OwnerDashboardSeed.ForeignOutlet}/sales-targets");
        using var write = await client.PutAsJsonAsync(
            $"{Outlets}/{OwnerDashboardSeed.ForeignOutlet}", OutletBody(Unique("Hijack"), Targets(Days(1m))));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        await _fixture.WithDbAsync(async db =>
            Assert.False(await db.Set<OutletSalesTargetVersion>().AnyAsync(version =>
                version.OutletId == OwnerDashboardSeed.ForeignOutlet && version.Days.Any(day => day.TargetAmount == 1m))));
    }

    [Fact]
    public async Task ViewOnlyUser_CanReadButNotChange()
    {
        using var owner = Owner();
        var id = await CreateAsync(owner);
        using var viewer = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", "outlets.view");

        using var read = await viewer.GetAsync($"{Outlets}/{id}/sales-targets");
        using var write = await viewer.PutAsJsonAsync($"{Outlets}/{id}", OutletBody(Unique("Nope"), Targets(Days(1m))));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task NoOutletPermission_CannotRead()
    {
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        using var response = await client.GetAsync($"{Outlets}/{OwnerDashboardSeed.OutletA}/sales-targets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Dashboard ----------------------------------------------------------------------------------------------

    /// <summary>Outlet A: 1,000 a day, closed Saturdays, since September. Outlet B stays unconfigured. Only added once.</summary>
    private async Task SeedDashboardTargetsAsync()
    {
        await _fixture.WithDbAsync(async db =>
        {
            if (await db.Set<OutletSalesTargetVersion>().AnyAsync(version => version.OutletId == OwnerDashboardSeed.OutletA))
                return;

            db.Set<OutletSalesTargetVersion>().AddRange(
                SeedVersion(OwnerDashboardSeed.Business, OwnerDashboardSeed.OutletA, 1_000m, DayOfWeek.Saturday),
                SeedVersion(OwnerDashboardSeed.ForeignBusiness, OwnerDashboardSeed.ForeignOutlet, 777_777m));
            await db.SaveChangesAsync();
        });
    }

    private static OutletSalesTargetVersion SeedVersion(long business, long outlet, decimal amount, params DayOfWeek[] off) => new()
    {
        BusinessId = business,
        OutletId = outlet,
        VersionNo = 1,
        EffectiveFrom = new DateOnly(2026, 9, 1),
        IsEnabled = true,
        Days = Enum.GetValues<DayOfWeek>()
            .Select(day => new OutletSalesTargetVersionDay
            {
                DayOfWeek = day,
                IsOffDay = off.Contains(day),
                TargetAmount = off.Contains(day) ? null : amount
            })
            .ToList()
    };

    [Fact]
    public async Task Dashboard_ClosedDay_ZeroTarget_NoShortfall_SalesStillCount()
    {
        await SeedDashboardTargetsAsync();
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        // 10 October 2026 is a Saturday.
        var data = await DataAsync(client, $"{Dashboard}/sales/summary?from=2026-10-10&to=2026-10-10&outletId={OwnerDashboardSeed.OutletA}");

        Assert.Equal(0m, Metric(data, "target"));
        Assert.Equal("off_day", Status(data, "target"));
        Assert.Null(Metric(data, "shortfall"));
        Assert.Equal(1500m, Metric(data, "netSales"));
    }

    [Fact]
    public async Task Dashboard_SingleShowroom_UsesActualCalendarDates()
    {
        await SeedDashboardTargetsAsync();
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        // October 2026 has 31 days and five Saturdays: 26 working days.
        var data = await DataAsync(client, $"{Dashboard}/sales/summary?from=2026-10-01&to=2026-10-31&outletId={OwnerDashboardSeed.OutletA}");

        Assert.Equal(26_000m, Metric(data, "target"));
        Assert.Equal("ok", Status(data, "target"));
        Assert.True(data.GetProperty("targetCoverage").GetProperty("isComplete").GetBoolean());
    }

    [Fact]
    public async Task Dashboard_AllShowrooms_FlagsIncompleteCoverage_AndIgnoresOtherBusinesses()
    {
        await SeedDashboardTargetsAsync();
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        var data = await DataAsync(client, $"{Dashboard}/sales/summary?from=2026-10-01&to=2026-10-31");

        Assert.Equal(26_000m, Metric(data, "target"));
        Assert.Equal("partial", Status(data, "target"));
        var coverage = data.GetProperty("targetCoverage");
        Assert.False(coverage.GetProperty("isComplete").GetBoolean());
        // Other tests in this class add outlets, none with a target in October.
        Assert.True(coverage.GetProperty("notConfiguredShowrooms").GetInt32() >= 1);
    }

    [Fact]
    public async Task Dashboard_ShowroomRows_MarkEachOutletsOwnState()
    {
        await SeedDashboardTargetsAsync();
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        var data = await DataAsync(client, $"{Dashboard}/sales/showrooms?from=2026-10-01&to=2026-10-31");
        var rows = data.GetProperty("showrooms").EnumerateArray().ToList();

        var a = rows.Single(row => row.GetProperty("outletId").GetInt64() == OwnerDashboardSeed.OutletA);
        Assert.Equal(26_000m, a.GetProperty("target").GetDecimal());
        Assert.Equal("ok", a.GetProperty("targetStatus").GetString());
        var b = rows.Single(row => row.GetProperty("outletId").GetInt64() == OwnerDashboardSeed.OutletB);
        Assert.Equal(JsonValueKind.Null, b.GetProperty("target").ValueKind);
        Assert.Equal("not_configured", b.GetProperty("targetStatus").GetString());
    }

    [Fact]
    public async Task Dashboard_Trend_HasATargetLineWithZeroOnClosedDays()
    {
        await SeedDashboardTargetsAsync();
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        var data = await DataAsync(client, $"{Dashboard}/sales/trend?from=2026-10-10&to=2026-10-11&outletId={OwnerDashboardSeed.OutletA}");
        var points = data.GetProperty("points").EnumerateArray().ToList();

        Assert.Equal(0m, points[0].GetProperty("target").GetDecimal());
        Assert.Equal(1_000m, points[1].GetProperty("target").GetDecimal());
        Assert.Equal("ok", data.GetProperty("targetStatus").GetString());
    }

    [Fact]
    public async Task Dashboard_LaterVersion_DoesNotRewriteHistory()
    {
        await SeedDashboardTargetsAsync();
        var before = await SummaryTargetAsync();

        // A change saved today applies from today, so a period that already ended reads exactly as it did.
        using var owner = Owner();
        var name = await OutletNameAsync(OwnerDashboardSeed.OutletA);
        using var response = await owner.PutAsJsonAsync(
            $"{Outlets}/{OwnerDashboardSeed.OutletA}",
            OutletBody(name, Targets(Days(9_000m), expected: 1)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(before, await SummaryTargetAsync());
    }

    private async Task<decimal?> SummaryTargetAsync()
    {
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);
        return Metric(
            await DataAsync(client, $"{Dashboard}/sales/summary?from=2026-10-01&to=2026-10-31&outletId={OwnerDashboardSeed.OutletA}"),
            "target");
    }

    private async Task<string> OutletNameAsync(long id)
    {
        var name = string.Empty;
        await _fixture.WithDbAsync(async db => name = await db.Outlets.Where(outlet => outlet.Id == id).Select(outlet => outlet.Name).SingleAsync());
        return name;
    }

    // ---- Cash Sessions ------------------------------------------------------------------------------------------

    [Fact]
    public async Task CashSession_DayTarget_MatchesTheDashboardCalculation()
    {
        await SeedDashboardTargetsAsync();
        using var client = Owner();

        // 10 October 2026 is a Saturday, and outlet A is closed on Saturdays.
        var data = await DataAsync(client, $"/api/cash-sessions/business-day-target?outletId={OwnerDashboardSeed.OutletA}&businessDate=2026-10-10");

        Assert.Equal("off_day", data.GetProperty("status").GetString());
        Assert.Equal(0m, data.GetProperty("target").GetDecimal());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("variance").ValueKind);
        Assert.Equal(1500m, data.GetProperty("netSales").GetDecimal());
    }

    [Fact]
    public async Task CashSession_DayTarget_NotConfigured_IsNotZero()
    {
        using var client = Owner();

        var data = await DataAsync(client, $"/api/cash-sessions/business-day-target?outletId={OwnerDashboardSeed.OutletB}&businessDate=2026-10-10");

        Assert.Equal("not_configured", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("target").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("variance").ValueKind);
    }

    [Fact]
    public async Task CashSession_DayTarget_ForeignOutlet_IsRefused()
    {
        using var client = Owner();

        using var response = await client.GetAsync($"/api/cash-sessions/business-day-target?outletId={OwnerDashboardSeed.ForeignOutlet}");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task CashSession_DayTarget_NeedsTheCashSessionPermission()
    {
        using var client = _fixture.Client(OwnerDashboardSeed.UnassignedUserId, "Viewer", SalesPermission);

        using var response = await client.GetAsync($"/api/cash-sessions/business-day-target?outletId={OwnerDashboardSeed.OutletA}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
