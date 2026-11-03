using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOrganisationApiTests : IClassFixture<SqliteRoleGovernanceApiFactory>
{
    private readonly SqliteRoleGovernanceApiFactory _factory;

    public HrOrganisationApiTests(SqliteRoleGovernanceApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreatesDepartmentAndTeamThenEditsStatusWithoutRewritingHistoricalStart()
    {
        await ResetDatabaseAsync();
        using var client = Client("hr.organisation.view,hr.organisation.manage");
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        using var response = await client.PostAsJsonAsync("/api/hr/v1/org-units",
            new { code = "SALES", name = "Sales", unitType = "Department" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var department = (await response.Content.ReadFromJsonAsync<HrOrgUnitReadModel>())!;
        Assert.InRange(department.EffectiveFrom, before, DateOnly.FromDateTime(DateTime.UtcNow));

        using var teamResponse = await client.PostAsJsonAsync("/api/hr/v1/org-units",
            new { code = "RETAIL", name = "Retail Sales", unitType = "Team", parentOrgUnitId = department.Id });
        Assert.Equal(HttpStatusCode.Created, teamResponse.StatusCode);
        var team = (await teamResponse.Content.ReadFromJsonAsync<HrOrgUnitReadModel>())!;
        Assert.Equal(department.Id, team.ParentOrgUnitId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var row = await db.HrOrgUnits.FindAsync(team.Id);
            row!.EffectiveFrom = new(2020, 1, 1);
            await db.SaveChangesAsync();
        }
        using var edited = await client.PutAsJsonAsync($"/api/hr/v1/org-units/{team.Id}",
            new { expectedVersion = team.Version,
                data = new { code = "RETAIL", name = "Retail Team", unitType = "Team",
                    parentOrgUnitId = department.Id, isActive = false } });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var updated = (await edited.Content.ReadFromJsonAsync<HrOrgUnitReadModel>())!;
        Assert.Equal(new DateOnly(2020, 1, 1), updated.EffectiveFrom);
        Assert.Equal("Retail Team", updated.Name);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task ViewDoesNotGrantWriteAndManageDoesNotGrantView()
    {
        await ResetDatabaseAsync();
        using var viewer = Client("hr.organisation.view");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/hr/v1/org-units")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/hr/v1/org-units",
            new { code = "DENIED", name = "Denied", unitType = "Department" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/hr/v1/org-units/1",
            new { expectedVersion = 1, data = new { code = "DENIED", name = "Denied", unitType = "Team" } })).StatusCode);
        using var manager = Client("hr.organisation.manage");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/hr/v1/org-units")).StatusCode);
    }

    [Theory]
    [InlineData("", "Sales", "Department")]
    [InlineData("SALES", "", "Department")]
    [InlineData("SALES", "Sales", "")]
    public async Task CreateRequiresCodeNameAndType(string code, string name, string unitType)
    {
        await ResetDatabaseAsync();
        using var client = Client("hr.organisation.manage");
        using var response = await client.PostAsJsonAsync("/api/hr/v1/org-units", new { code, name, unitType });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidParentsCyclesAndCrossBusinessWritesAreRejected()
    {
        await ResetDatabaseAsync();
        using var client = Client("hr.organisation.view,hr.organisation.manage");
        using var other = Client("hr.organisation.view,hr.organisation.manage", 2);
        var department = await Create(client, "SALES", "Department");
        var team = await Create(client, "TEAM", "Team", department.Id);
        var foreign = await Create(other, "OTHER", "Department");
        using var badParent = await client.PostAsJsonAsync("/api/hr/v1/org-units",
            new { code = "BAD", name = "Bad", unitType = "Team", parentOrgUnitId = foreign.Id });
        Assert.Equal(HttpStatusCode.Conflict, badParent.StatusCode);
        foreach (var parent in new[] { department.Id, team.Id })
        {
            using var invalid = await client.PutAsJsonAsync($"/api/hr/v1/org-units/{department.Id}",
                new { expectedVersion = 1,
                    data = new { code = "SALES", name = "Sales", unitType = "Department", parentOrgUnitId = parent } });
            Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        }
        using var foreignEdit = await client.PutAsJsonAsync($"/api/hr/v1/org-units/{foreign.Id}",
            new { expectedVersion = 1, data = new { code = "X", name = "X", unitType = "Department" } });
        Assert.Equal(HttpStatusCode.NotFound, foreignEdit.StatusCode);
        var visible = (await client.GetFromJsonAsync<HrOrgUnitReadModel[]>("/api/hr/v1/org-units"))!;
        Assert.Equal(2, visible.Length);
        Assert.All(visible, unit => Assert.Equal(1, unit.BusinessId));
    }

    private static async Task<HrOrgUnitReadModel> Create(HttpClient client, string code, string type, long? parent = null)
    {
        using var response = await client.PostAsJsonAsync("/api/hr/v1/org-units",
            new { code, name = code, unitType = type, parentOrgUnitId = parent });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HrOrgUnitReadModel>())!;
    }

    private HttpClient Client(string permissions, long businessId = 1)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", businessId == 1 ? "101" : "201");
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
        foreach (var id in new[] { 1, 2 })
        {
            db.Businesses.Add(new Business { Id = id, Name = $"Organisation API Business {id}" });
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                { BusinessId = id, FeatureKey = "hr.enabled", IsEnabled = true });
            db.Users.Add(new User
            {
                Id = id == 1 ? 101 : 201, BusinessId = id, Name = "Organisation API User",
                Email = $"org-{id}@example.invalid", PasswordHash = "test", IsActive = true,
                InventoryLocationAccessScope = User.InventoryAccessAll
            });
        }
        await db.SaveChangesAsync();
    }
}
