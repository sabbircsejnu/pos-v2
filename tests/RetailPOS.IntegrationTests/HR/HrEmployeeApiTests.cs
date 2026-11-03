using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeApiTests : IClassFixture<SqliteRoleGovernanceApiFactory>
{
    private readonly SqliteRoleGovernanceApiFactory _factory;

    public HrEmployeeApiTests(SqliteRoleGovernanceApiFactory factory) => _factory = factory;

    [Fact]
    public async Task EmployeeEndpointsEnforcePermissionsAndReturnFieldScopedProfiles()
    {
        await ResetDatabaseAsync();
        using var denied = CreateClient("hr.employee.create");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await denied.GetAsync("/api/hr/v1/employees")).StatusCode);

        using var creator = CreateClient(
            "hr.employee.create,hr.employee.view_sensitive,hr.employee.view_identity");
        using var created = await creator.PostAsJsonAsync("/api/hr/v1/employees", new
        {
            employeeCode = "EMP-HTTP-1",
            firstName = "Alex",
            lastName = "Employee",
            displayName = "Alex Employee",
            dateOfBirth = "1990-01-01",
            genderCode = "X",
            nationalId = "HTTP-NID-SECRET",
            passportNo = "HTTP-PASSPORT-SECRET",
            personalEmail = "alex@example.invalid",
            workEmail = "alex.work@example.invalid",
            mobile = "555-0101",
            presentAddress = "Restricted home address",
            permanentAddress = "Restricted permanent address",
            emergencyContactName = "Emergency Contact",
            emergencyContactPhone = "555-0199",
            employmentTypeId = 11,
            employmentStatus = "Active",
            joiningDate = "2026-10-01",
            confirmationDate = "2026-11-01"
        });
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)created.StatusCode}: {createdBody}");
        Assert.DoesNotContain("HTTP-NID-SECRET", createdBody, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0101", createdBody, StringComparison.Ordinal);

        using var ordinaryViewer = CreateClient("hr.employee.view");
        using var search = await ordinaryViewer.GetAsync("/api/hr/v1/employees");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchBody = await search.Content.ReadAsStringAsync();
        Assert.Contains("Alex Employee", searchBody, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP-NID-SECRET", searchBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Restricted home address", searchBody, StringComparison.Ordinal);

        var employeeId = System.Text.Json.JsonDocument.Parse(createdBody)
            .RootElement.GetProperty("id").GetInt64();
        using var maskedProfile = await ordinaryViewer.GetAsync($"/api/hr/v1/employees/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, maskedProfile.StatusCode);
        var maskedBody = await maskedProfile.Content.ReadAsStringAsync();
        Assert.DoesNotContain("HTTP-NID-SECRET", maskedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0101", maskedBody, StringComparison.Ordinal);
        using var deniedLocations = await ordinaryViewer.GetAsync($"/api/hr/v1/employees/{employeeId}/locations");
        Assert.Equal(HttpStatusCode.Forbidden, deniedLocations.StatusCode);
        using var locationViewer = CreateClient("hr.employee_transfer.view");
        using var locationHistory = await locationViewer.GetAsync($"/api/hr/v1/employees/{employeeId}/locations");
        Assert.Equal(HttpStatusCode.OK, locationHistory.StatusCode);
        Assert.Equal(0, System.Text.Json.JsonDocument.Parse(
            await locationHistory.Content.ReadAsStringAsync()).RootElement.GetArrayLength());

        using var sensitiveViewer = CreateClient("hr.employee.view,hr.employee.view_sensitive");
        using var sensitiveProfile = await sensitiveViewer.GetAsync($"/api/hr/v1/employees/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, sensitiveProfile.StatusCode);
        var sensitiveBody = await sensitiveProfile.Content.ReadAsStringAsync();
        Assert.Contains("555-0101", sensitiveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP-NID-SECRET", sensitiveBody, StringComparison.Ordinal);

        using var identityViewer = CreateClient("hr.employee.view,hr.employee.view_identity");
        using var identityProfile = await identityViewer.GetAsync($"/api/hr/v1/employees/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, identityProfile.StatusCode);
        var identityBody = await identityProfile.Content.ReadAsStringAsync();
        Assert.Contains("HTTP-NID-SECRET", identityBody, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0101", identityBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeUserLinkEndpointsEnforcePermissionsBusinessAndCurrentUserResolution()
    {
        await ResetDatabaseAsync();
        using var creator = CreateClient("hr.employee.create");
        using var created = await creator.PostAsJsonAsync("/api/hr/v1/employees", new
        {
            employeeCode = "EMP-LINK-HTTP",
            firstName = "Link",
            displayName = "Link Employee",
            employmentTypeId = 11,
            employmentStatus = "Active",
            joiningDate = "2026-10-01"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var employeeId = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt64();

        using var denied = CreateClient("hr.employee.view");
        using var deniedLink = await denied.PostAsJsonAsync(
            $"/api/hr/v1/employees/{employeeId}/user-link",
            new { identityUserId = 101, reason = "Explicit link." });
        Assert.Equal(HttpStatusCode.Forbidden, deniedLink.StatusCode);

        using var viewer = CreateClient("hr.employee.view");
        using var emptyLink = await viewer.GetAsync($"/api/hr/v1/employees/{employeeId}/user-link");
        Assert.Equal(HttpStatusCode.OK, emptyLink.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            System.Text.Json.JsonDocument.Parse(await emptyLink.Content.ReadAsStringAsync())
                .RootElement.GetProperty("activeLink").ValueKind);

        using var linker = CreateClient("hr.employee.link_user,hr.employee.unlink_user");
        using var link = await linker.PostAsJsonAsync(
            $"/api/hr/v1/employees/{employeeId}/user-link",
            new { identityUserId = 101, reason = "Employee requires HR context." });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);

        using var linkState = await viewer.GetAsync($"/api/hr/v1/employees/{employeeId}/user-link");
        Assert.Equal(HttpStatusCode.OK, linkState.StatusCode);
        var linkVersion = System.Text.Json.JsonDocument.Parse(await linkState.Content.ReadAsStringAsync())
            .RootElement.GetProperty("activeLink").GetProperty("version").GetInt64();

        using var self = CreateClient(string.Empty);
        using var selfResponse = await self.GetAsync("/api/hr/v1/me/employee");
        Assert.Equal(HttpStatusCode.OK, selfResponse.StatusCode);
        Assert.Equal(employeeId, System.Text.Json.JsonDocument.Parse(
            await selfResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt64());

        using var linkOnly = CreateClient("hr.employee.link_user");
        using var deniedUnlinkRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/hr/v1/employees/{employeeId}/user-link")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                expectedVersion = linkVersion,
                reason = "Permission check."
            })
        };
        using var deniedUnlink = await linkOnly.SendAsync(deniedUnlinkRequest);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUnlink.StatusCode);

        using var unlinkRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/hr/v1/employees/{employeeId}/user-link")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                expectedVersion = linkVersion,
                reason = "Access no longer required."
            })
        };
        using var unlink = await linker.SendAsync(unlinkRequest);
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        using var unlinkedSelf = await self.GetAsync("/api/hr/v1/me/employee");
        Assert.Equal(HttpStatusCode.NotFound, unlinkedSelf.StatusCode);

        using var unlinkedUser = CreateClient(string.Empty, businessId: 1, userId: 102);
        using var missingSelf = await unlinkedUser.GetAsync("/api/hr/v1/me/employee");
        Assert.Equal(HttpStatusCode.NotFound, missingSelf.StatusCode);

        using var otherBusinessUser = CreateClient(string.Empty, businessId: 1, userId: 201);
        using var mismatchedSelf = await otherBusinessUser.GetAsync("/api/hr/v1/me/employee");
        Assert.Equal(HttpStatusCode.Forbidden, mismatchedSelf.StatusCode);
        using var crossBusinessLink = await linker.PostAsJsonAsync(
            $"/api/hr/v1/employees/{employeeId}/user-link",
            new { identityUserId = 201, reason = "Must fail closed." });
        Assert.Equal(HttpStatusCode.Conflict, crossBusinessLink.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var hrFeature = await db.BusinessFeatureSettings.SingleAsync(setting =>
                setting.BusinessId == 1 && setting.FeatureKey == "hr.enabled");
            hrFeature.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        using var disabledHr = await self.GetAsync("/api/hr/v1/me/employee");
        Assert.Equal(HttpStatusCode.Forbidden, disabledHr.StatusCode);
    }

    private HttpClient CreateClient(string permissions, long businessId = 1, long userId = 101)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Employee API Business", IsActive = true },
            new Business { Id = 2, Name = "Other Employee API Business", IsActive = true });
        db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
        {
            BusinessId = 1,
            FeatureKey = "hr.enabled",
            IsEnabled = true
        });
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 11,
            BusinessId = 1,
            Code = "Permanent",
            Name = "Permanent",
            IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Employee API User",
            Email = "employee-api@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        db.Users.Add(new User
        {
            Id = 201,
            BusinessId = 2,
            Name = "Other Business User",
            Email = "other-business-user@example.invalid",
            PasswordHash = "test",
            IsActive = true
        });
        db.Users.Add(new User
        {
            Id = 102,
            BusinessId = 1,
            Name = "Unlinked Business User",
            Email = "unlinked-user@example.invalid",
            PasswordHash = "test",
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
}
