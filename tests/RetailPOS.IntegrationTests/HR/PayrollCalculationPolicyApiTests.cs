using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollCalculationPolicyApiTests
{
    [Fact]
    public async Task PolicyEndpointsEnforcePermissionAndBusinessIsolationOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "");
        const string route = "/api/payroll/v1/calculation-policies";
        var request = NewRequest();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, request)).StatusCode);
        SetPermissions(client, "payroll.calculation_policy.view");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, request)).StatusCode);

        SetPermissions(client, "payroll.calculation_policy.manage");
        var createdResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollCalculationPolicyResponse>())!;
        Assert.Equal("Fixed30", created.PartialMonthBasis);
        Assert.Equal(1, created.VersionNo);

        SetPermissions(client, "payroll.calculation_policy.view");
        var policies = await client.GetFromJsonAsync<PayrollCalculationPolicyResponse[]>(route);
        Assert.Single(policies!);
        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Empty(await client.GetFromJsonAsync<PayrollCalculationPolicyResponse[]>(route) ?? []);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.PayrollCalculationPolicyVersions.Where(row => row.BusinessId == 1).ToArrayAsync());
        Assert.Empty(await db.PayrollCalculationPolicyVersions.Where(row => row.BusinessId == 2).ToArrayAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PolicyEndpointsRequireBothHrAndPayrollCapabilities(
        bool hrEnabled, bool payrollEnabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, hrEnabled, payrollEnabled);
        Authorize(client, "payroll.calculation_policy.manage,payroll.calculation_policy.view");
        const string route = "/api/payroll/v1/calculation-policies";

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(route, NewRequest())).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollCalculationPolicyVersions.ToArrayAsync());
    }

    private static CreatePayrollCalculationPolicyRequest NewRequest() => new(
        new(2026, 10, 1), new(2026, 10, 31), null, [],
        false, false, null, null, null, 30, 0,
        null, null, null, null, null, null, null, null, null, null);

    private static async Task SeedAsync(
        IServiceProvider services, bool hrEnabled = true, bool payrollEnabled = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        await HrRosterPersistenceTests.Seed(db);
        foreach (var businessId in new long[] { 1, 2 })
        {
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = businessId, FeatureKey = "hr.enabled",
                    IsEnabled = businessId != 1 || hrEnabled
                },
                new BusinessFeatureSetting
                {
                    BusinessId = businessId, FeatureKey = "payroll.enabled",
                    IsEnabled = businessId != 1 || payrollEnabled
                });
        }
        db.Users.Add(new User
        {
            Id = 101, BusinessId = 1, Name = "Actor",
            Email = "payroll-policy@example.invalid", PasswordHash = "test",
            IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAll
        });
        await db.SaveChangesAsync();
    }

    private static void Authorize(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static void SetPermissions(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
