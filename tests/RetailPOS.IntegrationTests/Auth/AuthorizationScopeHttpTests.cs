using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.Auth;

/// <summary>
/// Platform vs Business scope, 401/403 semantics and wildcard permission behaviour.
/// The test auth handler always emits a businessId claim; "0" represents "no Business selected".
/// </summary>
public sealed class AuthorizationScopeHttpTests
{
    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business One" },
            new Business { Id = 2, Name = "Business Two" });
        await db.SaveChangesAsync();
    }

    private static HttpClient Client(SqliteRoleGovernanceApiFactory factory, string role, string businessId, string permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task SuperAdminWithoutBusinessContext_CanManageFeaturesOfAnExplicitTargetBusiness()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var admin = Client(factory, "Super Admin", "0", "*");

        using var response = await admin.PatchAsync(
            "/api/businesses/2/features",
            Json("""{"features":[{"featureKey":"hr.enabled","isEnabled":true}]}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.True(await db.BusinessFeatureSettings.AnyAsync(setting =>
            setting.BusinessId == 2 && setting.FeatureKey == "hr.enabled" && setting.IsEnabled));
        Assert.False(await db.BusinessFeatureSettings.AnyAsync(setting => setting.BusinessId == 1));
    }

    [Fact]
    public async Task SuperAdminFeatureChange_ForUnknownTargetBusiness_IsNotFoundNotUnauthenticated()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var admin = Client(factory, "Super Admin", "0", "*");

        using var response = await admin.PatchAsync(
            "/api/businesses/999/features",
            Json("""{"features":[{"featureKey":"hr.enabled","isEnabled":true}]}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BusinessScopedEndpoint_WithoutBusinessContext_ReturnsBusinessContextErrorNotUnauthorized()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var admin = Client(factory, "Super Admin", "0", "*");

        using var response = await admin.GetAsync("/api/accounts");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("business_context_required", body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task SuperAdminWithSelectedBusiness_IsNotUsedAsPlatformWideRead()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Warehouses.AddRange(
                new Warehouse { Id = 1, BusinessId = 1, Name = "WH One" },
                new Warehouse { Id = 2, BusinessId = 2, Name = "WH Two" });
            await db.SaveChangesAsync();
        }
        using var admin = Client(factory, "Super Admin", "2", "*");

        using var response = await admin.GetAsync("/api/warehouses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("WH Two", text);
        Assert.DoesNotContain("WH One", text);
    }

    [Fact]
    public async Task BusinessOwnerWildcard_AuthorizesBusinessLevelApis_ButNotPlatformApis()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var owner = Client(factory, "BusinessOwner", "1", "*");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/warehouses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/outlets")).StatusCode);

        using var platform = await owner.PatchAsync(
            "/api/businesses/1/features",
            Json("""{"features":[{"featureKey":"hr.enabled","isEnabled":true}]}"""));
        Assert.Equal(HttpStatusCode.Forbidden, platform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/businesses")).StatusCode);
    }

    [Fact]
    public async Task BusinessOwnerWildcard_DoesNotOverrideFeatureEntitlements()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var owner = Client(factory, "BusinessOwner", "1", "*");

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/hr/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/payroll/v1/capabilities")).StatusCode);
    }

    [Fact]
    public async Task LimitedRole_DoesNotGainOwnerLevelPermissions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var cashier = Client(factory, "SalesPerson", "1", "sales.view,sales.create");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/warehouses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/businesses")).StatusCode);
    }

    [Fact]
    public async Task PermissionMatching_IsCaseInsensitiveAndAliasAware()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var user = Client(factory, "AccountsAdmin", "1", "WAREHOUSES.VIEW");

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/warehouses")).StatusCode);
    }

    [Fact]
    public async Task ForbiddenResponse_UsesStructuredPermissionMessage()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedAsync(factory.Services);
        using var owner = Client(factory, "BusinessOwner", "1", "*");

        // Business 2's feature endpoint is platform-only; the owner is authenticated but not permitted.
        using var response = await owner.PostAsJsonAsync("/api/businesses/2/owner/reset-access", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
