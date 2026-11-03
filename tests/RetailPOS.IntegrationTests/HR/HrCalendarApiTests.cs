using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrCalendarApiTests
{
    [Theory]
    [InlineData("shifts", "hr.shift.view")] [InlineData("holiday-calendars", "hr.holiday.view")]
    [InlineData("weekly-off-policies", "hr.weekly_off.view")]
    public async Task ReadEndpointsUseRealCapabilityAndPermissionChecks(string path, string permission)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services);
        Auth(client, "");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/hr/v1/{path}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Permissions"); client.DefaultRequestHeaders.Add("X-Test-Permissions", permission);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/hr/v1/{path}")).StatusCode);
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task HrAndAttendanceMustBothBeEnabled(bool hr, bool attendance)
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services, hr, attendance); Auth(c, "hr.shift.view,hr.holiday.view,hr.weekly_off.view");
        foreach (var path in new[] { "shifts", "holiday-calendars", "weekly-off-policies" })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"/api/hr/v1/{path}")).StatusCode);
    }

    [Fact]
    public async Task CreateUpdateStaleAndDeactivateShiftThroughApi()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.shift.view,hr.shift.manage");
        var response = await c.PostAsJsonAsync("/api/hr/v1/shifts", HrCalendarPersistenceTests.Shift());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.NotNull(response.Headers.Location);
        var row = (await response.Content.ReadFromJsonAsync<HrShiftResponse>())!;
        var update = new { expectedVersion = row.Version, data = row.Data with { Name = "Changed" } };
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/hr/v1/shifts/{row.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/hr/v1/shifts/{row.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/hr/v1/shifts/{row.Id}/deactivate", new { expectedVersion = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/hr/v1/shifts/{row.Id}", new { expectedVersion = 3, data = row.Data })).StatusCode);
    }

    [Fact]
    public async Task ScopedConfigurationDoesNotGainBusinessWideAccessFromEmployeeLocations()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services, global: false); Auth(c, "hr.shift.manage,hr.holiday.manage,hr.weekly_off.manage");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/hr/v1/shifts", HrCalendarPersistenceTests.Shift())).StatusCode);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/hr/v1/holiday-calendars",
            new HrHolidayCalendarData("Company", new("Company"), date, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/hr/v1/weekly-off-policies",
            new HrWeeklyOffData(new("Company"), date, null, [DayOfWeek.Friday]))).StatusCode);
        var scopedResponse = await c.PostAsJsonAsync("/api/hr/v1/holiday-calendars",
            new HrHolidayCalendarData("Other", new("Location", null, 999), date, null));
        Assert.True(scopedResponse.StatusCode == HttpStatusCode.Forbidden, await scopedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingBusinessAndForeignIdsRemainDeniedWithoutConfigurationDisclosure()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services); Auth(c, "hr.shift.view,hr.holiday.view");
        c.DefaultRequestHeaders.Remove("X-Test-BusinessId"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "0");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/hr/v1/shifts")).StatusCode);
        c.DefaultRequestHeaders.Remove("X-Test-BusinessId"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/hr/v1/holiday-calendars/999/holidays")).StatusCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OnlyIdentitySourceGrantsNotAdditionalEmploymentAssignmentAllowScopedConfiguration(bool identityGrant)
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        await Seed(f.Services, global: false); Auth(c, "hr.holiday.manage,hr.holiday.view,hr.location.view");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Outlets.Add(new Outlet { Id = 10, BusinessId = 1, Name = "Outlet" });
            db.OperationalLocationProjections.Add(new OperationalLocationProjection { Id = 10, BusinessId = 1,
                SourceSystem = "RetailPOS", SourceEntityType = "Outlet", SourceEntityId = "10", Name = "Outlet", SourceVersion = 1 });
            db.HrEmploymentTypes.Add(new HrEmploymentType { Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent" });
            db.HrEmployees.Add(new HrEmployee { Id = 1, BusinessId = 1, EmployeeCode = "EMP", FirstName = "Actor",
                DisplayName = "Actor", CurrentEmploymentTypeId = 1 });
            db.HrEmployeeUserLinks.Add(new HrEmployeeUserLink { BusinessId = 1, EmployeeId = 1, IdentityUserId = 101,
                LinkedByUserId = 101, LinkedAtUtc = DateTime.UtcNow });
            db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment { BusinessId = 1, EmployeeId = 1,
                OperationalLocationProjectionId = 10, AssignmentType = "Additional", EffectiveFrom = new(2026, 1, 1),
                LocationNameSnapshot = "Outlet", SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet",
                SourceEntityIdSnapshot = "10", LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow });
            if (identityGrant) db.UserOutletAssignments.Add(new UserOutletAssignment { BusinessId = 1, UserId = 101, OutletId = 10, IsActive = true });
            await db.SaveChangesAsync();
        }
        var response = await c.PostAsJsonAsync("/api/hr/v1/holiday-calendars",
            new HrHolidayCalendarData("Scoped", new("Location", null, 10), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null));
        Assert.True(response.StatusCode == (identityGrant ? HttpStatusCode.Created : HttpStatusCode.Forbidden),
            await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/hr/v1/operational-locations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/hr/v1/holiday-calendars")).StatusCode);
    }

    [Fact]
    public void OpenApiContainsCalendarRosterAttendanceAndReportRoutes()
    {
        using var f = new SqliteRoleGovernanceApiFactory(); using var c = f.CreateClient();
        var paths = f.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1").Paths;
        foreach (var path in new[] { "/shifts", "/holiday-calendars", "/weekly-off-policies", "/day-classification" })
            Assert.Contains("/api/hr/v1" + path, paths.Keys);
        Assert.Contains("/api/hr/v1/rosters", paths.Keys);
        Assert.Contains("/api/hr/v1/attendance/process", paths.Keys);
        Assert.Contains("/api/hr/v1/attendance/reports", paths.Keys);
        Assert.Contains("/api/hr/v1/reports/core", paths.Keys);
        Assert.Contains("/api/hr/v1/attendance/daily/{dailyId}/corrections", paths.Keys);
        Assert.Contains("/api/hr/v1/attendance/corrections/approvals/{instanceId}/approve", paths.Keys);
    }

    [Fact]
    public async Task LeaveApiRequiresLeavePermissionAndExplicitAccrualMethod()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services, attendance: false);
        Auth(client, "");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/hr/v1/leave/types")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "hr.leave.admin,hr.leave.view");
        var typeResponse = await client.PostAsJsonAsync("/api/hr/v1/leave/types",
            new { code = "ANNUAL", name = "Annual", isPaid = true, requiresDocument = false });
        Assert.Equal(HttpStatusCode.Created, typeResponse.StatusCode);
        var type = await typeResponse.Content.ReadFromJsonAsync<HrLeaveTypeModel>();
        Assert.NotNull(type);

        var policyResponse = await client.PostAsJsonAsync("/api/hr/v1/leave/policies",
            new
            {
                leaveTypeId = type.Id,
                code = "STANDARD",
                name = "Standard",
                scopeType = HrLeavePolicyScopeType.Business,
                scopeReference = (string?)null,
                versionNo = 1,
                annualEntitlement = 12m,
                carryForwardAllowed = false,
                negativeBalanceAllowed = false,
                leaveYearStartMonth = 1,
                leaveYearStartDay = 1,
                effectiveFrom = new DateOnly(2026, 1, 1),
                effectiveTo = (DateOnly?)null,
                isActive = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, policyResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.HrLeavePolicyVersions.ToListAsync());
    }

    private static void Auth(HttpClient c, string permissions)
    {
        c.DefaultRequestHeaders.Add("X-Test-UserId", "101"); c.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        c.DefaultRequestHeaders.Add("X-Test-Role", "HR"); c.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
    private static async Task Seed(IServiceProvider services, bool hr = true, bool attendance = true, bool global = true)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Calendars" });
        db.BusinessFeatureSettings.AddRange(new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = hr },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = attendance });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Actor", Email = "actor@example.invalid", PasswordHash = "test",
            IsActive = true, InventoryLocationAccessScope = global ? User.InventoryAccessAll : User.InventoryAccessSpecific });
        await db.SaveChangesAsync();
    }
}
