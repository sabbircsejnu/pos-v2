using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollMonetaryConfigurationApiTests
{
    [Fact]
    public async Task MonetaryConfigurationEndpointsEnforcePermissionsAndBusinessIsolation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "");
        const string route = "/api/payroll/v1/monetary-configurations";
        var request = new CreatePayrollMonetaryConfigurationRequest(
            new(2026, 10, 1), "jpy", 0, "AwayFromZero");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, request)).StatusCode);
        SetPermissions(client, "payroll.calculation_policy.manage");
        var createdResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content
            .ReadFromJsonAsync<PayrollMonetaryConfigurationResponse>())!;
        Assert.Equal("JPY", created.CurrencyCode);
        Assert.Equal(0, created.DecimalPrecision);
        Assert.Equal("AwayFromZero", created.RoundingMode);
        Assert.Equal(1, created.VersionNo);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(route, request)).StatusCode);
        SetPermissions(client, "payroll.calculation_policy.view");
        var configurations = await client.GetFromJsonAsync<PayrollMonetaryConfigurationResponse[]>(route);
        Assert.Single(configurations!);
        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Empty(await client.GetFromJsonAsync<PayrollMonetaryConfigurationResponse[]>(route) ?? []);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var stored = await db.PayrollMonetaryConfigurationVersions.SingleAsync();
        Assert.Equal(1, stored.BusinessId);
        Assert.Single(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.monetary_configuration.created").ToArrayAsync());

        stored.CurrencyCode = "USD";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MonetaryConfigurationRequiresHrAndPayrollCapabilities(
        bool hrEnabled, bool payrollEnabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, hrEnabled, payrollEnabled);
        Authorize(client, "payroll.calculation_policy.manage,payroll.calculation_policy.view");

        var request = new CreatePayrollMonetaryConfigurationRequest(
            new(2026, 10, 1), "BDT", 2, "AwayFromZero");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/payroll/v1/monetary-configurations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/monetary-configurations", request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollMonetaryConfigurationVersions.ToArrayAsync());
    }

    [Theory]
    [InlineData("US", 2, "AwayFromZero")]
    [InlineData("USD", 7, "AwayFromZero")]
    [InlineData("USD", 2, "UnknownMode")]
    public async Task InvalidMonetaryConfigurationsAreRejected(
        string currency, int precision, string roundingMode)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.calculation_policy.manage");

        var request = new CreatePayrollMonetaryConfigurationRequest(
            new(2026, 10, 1), currency, precision, roundingMode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(
                "/api/payroll/v1/monetary-configurations", request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollMonetaryConfigurationVersions.ToArrayAsync());
    }

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
                    BusinessId = businessId,
                    FeatureKey = "hr.enabled",
                    IsEnabled = businessId != 1 || hrEnabled
                },
                new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = "payroll.enabled",
                    IsEnabled = businessId != 1 || payrollEnabled
                });
        }
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Actor",
            Email = "payroll-monetary-configuration@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
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
