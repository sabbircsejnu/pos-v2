using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrDeviceApiTests
{
    [Fact]
    public async Task DeviceAndMappingContractsRetainHistoryAndHideCredentialReference()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.device.view,hr.device.manage,hr.device_user_mapping.view,hr.device_user_mapping.manage");
        var creation = await c.PostAsJsonAsync("/api/hr/v1/attendance/devices", HrDevicePersistenceTests.Data());
        Assert.True(creation.StatusCode == HttpStatusCode.Created, await creation.Content.ReadAsStringAsync());
        var d = (await creation.Content.ReadFromJsonAsync<HrDeviceResponse>())!;
        Assert.NotNull(creation.Headers.Location); Assert.DoesNotContain("device-secret", await creation.Content.ReadAsStringAsync());
        var mappingResponse = await c.PostAsJsonAsync($"/api/hr/v1/attendance/devices/{d.Id}/mappings",
            new HrDeviceMappingData(1, "23", HrDevicePersistenceTests.Start));
        Assert.True(mappingResponse.StatusCode == HttpStatusCode.Created, await mappingResponse.Content.ReadAsStringAsync());
        var m = (await mappingResponse.Content.ReadFromJsonAsync<HrDeviceMappingResponse>())!;
        var closing = new HttpRequestMessage(HttpMethod.Delete, $"/api/hr/v1/attendance/device-user-mappings/{m.Id}") {
            Content = JsonContent.Create(new { expectedVersion = m.Version, effectiveToUtc = HrDevicePersistenceTests.Start.AddDays(1) }) };
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(closing)).StatusCode);
        var history = await c.GetFromJsonAsync<HrDeviceMappingResponse[]>($"/api/hr/v1/attendance/devices/{d.Id}/mappings");
        Assert.False(Assert.Single(history!).IsActive);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/hr/v1/attendance/devices/{d.Id}/deactivate", new { expectedVersion = d.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/hr/v1/attendance/devices/{d.Id}", new { expectedVersion = d.Version, data = HrDevicePersistenceTests.Data() })).StatusCode);
    }
    [Theory]
    [InlineData(false, true, true)] [InlineData(true, false, true)] [InlineData(true, true, false)]
    public async Task DeviceRequiresHrAttendanceAndDeviceCapabilities(bool hr, bool attendance, bool device)
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services, hr, attendance, device); Auth(c, "hr.device.view,hr.device.manage");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/hr/v1/attendance/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/hr/v1/attendance/devices", HrDevicePersistenceTests.Data())).StatusCode);
    }
    [Theory]
    [InlineData("", true)] [InlineData("hr.device.manage", false)]
    public async Task PermissionAndIdentityLocationScopeRequired(string permission, bool global)
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services, global: global); Auth(c, permission);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/hr/v1/attendance/devices", HrDevicePersistenceTests.Data())).StatusCode);
    }
    [Fact]
    public async Task MappingPermissionIsIndependentAndBusinessScoped()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.device.manage,hr.device.view");
        var creation = await c.PostAsJsonAsync("/api/hr/v1/attendance/devices", HrDevicePersistenceTests.Data());
        var d = (await creation.Content.ReadFromJsonAsync<HrDeviceResponse>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/hr/v1/attendance/devices/{d.Id}/mappings")).StatusCode);
        c.DefaultRequestHeaders.Remove("X-Test-BusinessId"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/hr/v1/attendance/devices/{d.Id}")).StatusCode);
    }
    [Fact]
    public async Task ManualPunchIsBusinessScopedNormalizedAndIdempotent()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.attendance.manual_entry,hr.attendance.raw_punch.view");
        var input = new ManualAttendancePunchInput(1, new(2026, 10, 10, 9, 0, 0), "India Standard Time", "Manual", "IN");
        var firstResponse = await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", input);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<RawAttendancePunchResponse>())!;
        var duplicateResponse = await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", input);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var duplicate = (await duplicateResponse.Content.ReadFromJsonAsync<RawAttendancePunchResponse>())!;
        Assert.True(duplicate.Duplicate); Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 3, 30, 0, TimeSpan.Zero), first.PunchTimeUtc);
        Assert.Null(first.DeviceId); Assert.Equal("Manual", first.SourceType);

        var second = input with { PunchTimeLocal = new(2026, 10, 11, 1, 0, 0), DirectionCode = "OUT" };
        var secondResponse = await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", second);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var afterMidnight = (await secondResponse.Content.ReadFromJsonAsync<RawAttendancePunchResponse>())!;
        Assert.Equal(new DateTime(2026, 10, 11, 1, 0, 0), afterMidnight.PunchTimeLocal);
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var list = await c.GetFromJsonAsync<RawAttendancePunchListItem[]>(
            $"/api/hr/v1/attendance/raw-punches?fromUtc={from}&toUtc={to}");
        Assert.Equal(2, list!.Length);
    }
    [Theory]
    [InlineData("", 403)]
    [InlineData("hr.attendance.manual_entry", 403)]
    public async Task ManualPunchRequiresRawReadPermissionAndBusinessCapability(string permissions, int status)
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, permissions);
        var input = new ManualAttendancePunchInput(1, new(2026, 10, 10, 9, 0, 0), "UTC");
        if (permissions.Length == 0)
            Assert.Equal((HttpStatusCode)status, (await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", input)).StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", input)).StatusCode);
            Assert.Equal((HttpStatusCode)status, (await c.GetAsync(
                "/api/hr/v1/attendance/raw-punches?fromUtc=2026-10-10T00:00:00Z&toUtc=2026-10-11T00:00:00Z")).StatusCode);
        }
    }
    [Fact]
    public async Task ManualPunchCannotCrossBusinessScope()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.attendance.manual_entry");
        c.DefaultRequestHeaders.Remove("X-Test-BusinessId"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        var input = new ManualAttendancePunchInput(1, new(2026, 10, 10, 9, 0, 0), "UTC");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/hr/v1/attendance/manual-punches", input)).StatusCode);
    }
    private static async Task Seed(IServiceProvider services, bool hr = true, bool attendance = true, bool device = true, bool global = true)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync(); await HrRosterPersistenceTests.Seed(db);
        db.BusinessFeatureSettings.AddRange(new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = hr },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = attendance },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "deviceintegration.enabled", IsEnabled = device });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Actor", Email = "device@example.invalid", PasswordHash = "test",
            IsActive = true, InventoryLocationAccessScope = global ? User.InventoryAccessAll : User.InventoryAccessSpecific });
        await db.SaveChangesAsync();
    }
    private static void Auth(HttpClient c, string permissions)
    {
        c.DefaultRequestHeaders.Add("X-Test-UserId", "101"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        c.DefaultRequestHeaders.Add("X-Test-Role", "HR"); c.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
