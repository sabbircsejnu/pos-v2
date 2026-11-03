using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Identity;

public sealed class UserOutletAccessHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public UserOutletAccessHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAuthorizedOutlets_MapsApplicationResultToExistingApiContract()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                Id = 7701,
                BusinessId = 1,
                OutletId = 7001,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
            });
            db.Outlets.Add(new Outlet { Id = 7001, BusinessId = 1, Name = "Assigned outlet" });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7701");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");

        using var response = await client.GetAsync("/api/me/outlets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(7001, data.GetProperty("defaultOutletId").GetInt64());
        Assert.Equal(
            "assigned_only",
            data.GetProperty("accessScope").GetString());
        Assert.Equal(
            "Assigned outlet",
            Assert.Single(data.GetProperty("outlets").EnumerateArray())
                .GetProperty("name").GetString());

        using var missingUserClient = _factory.CreateClient();
        missingUserClient.DefaultRequestHeaders.Add("X-Test-UserId", "7702");
        missingUserClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        using var missingUserResponse = await missingUserClient.GetAsync("/api/me/outlets");
        Assert.Equal(HttpStatusCode.Unauthorized, missingUserResponse.StatusCode);
    }

    [Theory]
    [InlineData("BusinessOwner", "*", false, true)]
    [InlineData("BusinessOwner", "purchases.create", false, false)]
    [InlineData("BusinessOwner", "purchases.create", true, false)]
    [InlineData("BusinessOwner", "*", true, false)]
    [InlineData("InventoryAdmin", "*", false, false)]
    [InlineData(SystemRoleNames.SuperAdmin, "*", false, true)]
    public async Task GetAuthorizedOutlets_RecognizesOwnerWildcardWithoutWideningOtherRolesOrBusiness(
        string role, string permissions, bool switched, bool global)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User { Id = 7710, BusinessId = 1 });
            db.Outlets.AddRange(
                new Outlet { Id = 7001, BusinessId = 1, Name = "Business outlet" },
                new Outlet { Id = 7002, BusinessId = 2, Name = "Foreign outlet" });
            db.Warehouses.AddRange(
                new Warehouse { Id = 8001, BusinessId = 1, Name = "Business warehouse" },
                new Warehouse { Id = 8002, BusinessId = 2, Name = "Foreign warehouse" });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7710");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (switched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", "PurchaseManager");
        }

        using var response = await client.GetAsync("/api/me/outlets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(global, data.GetProperty("isGlobalAccess").GetBoolean());
        var outlets = data.GetProperty("outlets").EnumerateArray().ToList();
        var warehouses = data.GetProperty("warehouses").EnumerateArray().ToList();
        if (global)
        {
            Assert.Equal(7001, Assert.Single(outlets).GetProperty("id").GetInt64());
            Assert.Equal("outlet", outlets[0].GetProperty("type").GetString());
            Assert.Equal(8001, Assert.Single(warehouses).GetProperty("id").GetInt64());
            Assert.Equal("warehouse", warehouses[0].GetProperty("type").GetString());

            if (role == SystemRoleNames.BusinessOwner)
            {
                foreach (var endpoint in new[] { "/api/outlets", "/api/warehouses" })
                {
                    using var locationsResponse = await client.GetAsync(endpoint);
                    Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);
                    using var locations = JsonDocument.Parse(await locationsResponse.Content.ReadAsStringAsync());
                    Assert.Single(locations.RootElement.GetProperty("data").EnumerateArray());
                }
            }
        }
        else
        {
            Assert.Empty(outlets);
            Assert.Empty(warehouses);
        }
    }
}
