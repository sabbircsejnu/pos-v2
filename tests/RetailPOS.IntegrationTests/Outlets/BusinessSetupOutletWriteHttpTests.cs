using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Outlets;

public sealed class BusinessSetupOutletWriteHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task LegacyBusinessSetupWrites_PreserveExplicitBusinessScopeAndAuditOutcomes()
    {
        await ResetAndSeedAsync();
        using var client = CreateSuperAdminClient();

        using var create = await client.PostAsJsonAsync("/api/businesses/17/outlets", new
        {
            name = "Created",
            address = "New Street",
            contactNumber = "555-0300",
            managerId = 101
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadDocumentAsync(create);
        Assert.True(created.RootElement.GetProperty("success").GetBoolean());
        var createdOutlet = created.RootElement.GetProperty("data");
        var createdId = createdOutlet.GetProperty("id").GetInt64();
        Assert.Equal("Manager A", createdOutlet.GetProperty("managerName").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(17, (await db.Outlets.SingleAsync(outlet => outlet.Id == createdId)).BusinessId);
            var audit = await db.AuditEvents.Include(value => value.Entities)
                .SingleAsync(value => value.ActionSummary == $"Outlet 'Created' created for business 17");
            Assert.Equal(AuditActionType.Create, audit.ActionType);
            Assert.Equal("Outlet", audit.PrimaryEntityType);
            Assert.Equal(createdId.ToString(), audit.PrimaryEntityId);
            Assert.Equal("{\"businessId\":17}", Assert.Single(audit.Entities).Metadata);
        }

        using var update = await client.PutAsJsonAsync("/api/businesses/17/outlets/10", new
        {
            name = "Alpha Updated",
            address = "Updated Street",
            contactNumber = "555-0199",
            managerId = 103
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = await ReadDocumentAsync(update);
        Assert.True(updated.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Alpha Updated", updated.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var delete = await client.DeleteAsync($"/api/businesses/17/outlets/{createdId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        using var deleted = await ReadDocumentAsync(delete);
        Assert.True(deleted.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Outlet deleted successfully", deleted.RootElement.GetProperty("message").GetString());

        using var verification = factory.Services.CreateScope();
        var verifiedDb = verification.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.DoesNotContain(await verifiedDb.Outlets.ToListAsync(), outlet => outlet.Id == createdId);
        Assert.Equal(1, await verifiedDb.AuditEvents.CountAsync(value =>
            value.ActionType == AuditActionType.Update
            && value.ActionSummary == "Outlet 'Alpha Updated' updated for business 17"));
        Assert.Equal(1, await verifiedDb.AuditEvents.CountAsync(value =>
            value.ActionType == AuditActionType.Delete
            && value.ActionSummary == $"Outlet {createdId} deleted from business 17"));
    }

    [Fact]
    public async Task BusinessSetupMutations_KeepBusinessLimitsAndNotFoundContracts()
    {
        await ResetAndSeedAsync(maxOutlets: 1);
        using var client = CreateSuperAdminClient();

        using var limited = await client.PostAsJsonAsync("/api/businesses/17/outlets", new
        {
            name = "Over Limit",
            address = "Limit Street"
        });
        Assert.Equal(HttpStatusCode.BadRequest, limited.StatusCode);
        using var limitedBody = await ReadDocumentAsync(limited);
        Assert.Equal(
            "Outlet limit reached (1). Upgrade subscription or increase outlet limit.",
            limitedBody.RootElement.GetProperty("message").GetString());

        using var missingBusiness = await client.PostAsJsonAsync("/api/businesses/999/outlets", new
        {
            name = "No Business",
            address = "No Street"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingBusiness.StatusCode);
        using var missingBusinessBody = await ReadDocumentAsync(missingBusiness);
        Assert.Equal("Business 999 not found", missingBusinessBody.RootElement.GetProperty("message").GetString());

        using var foreignUpdate = await client.PutAsJsonAsync("/api/businesses/17/outlets/20", new
        {
            name = "Foreign",
            address = "No Access"
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);
        using var foreignUpdateBody = await ReadDocumentAsync(foreignUpdate);
        Assert.Equal("Outlet 20 not found in business 17",
            foreignUpdateBody.RootElement.GetProperty("message").GetString());

        using var foreignDelete = await client.DeleteAsync("/api/businesses/17/outlets/20");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        using var foreignDeleteBody = await ReadDocumentAsync(foreignDelete);
        Assert.Equal("Outlet 20 not found in business 17",
            foreignDeleteBody.RootElement.GetProperty("message").GetString());
    }

    private HttpClient CreateSuperAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        return client;
    }

    private async Task ResetAndSeedAsync(int maxOutlets = 10)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 17, Name = "Business A", MaxOutlets = maxOutlets },
            new Business { Id = 18, Name = "Business B", MaxOutlets = 10 });
        db.Users.AddRange(
            CreateUser(101, 17, 10, "Manager A"),
            CreateUser(102, 18, 20, "Manager B"),
            CreateUser(103, 17, 10, "Active A"),
            CreateUser(104, 18, 20, "Active B"));
        db.Outlets.AddRange(
            CreateOutlet(10, 17, "Alpha", "Main Street", "555-0100", 101),
            CreateOutlet(20, 18, "Beta", "North Road", "555-0200", 102));
        await db.SaveChangesAsync();
    }

    private static Outlet CreateOutlet(
        long id,
        long businessId,
        string name,
        string address,
        string contact,
        long managerId) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Name = name,
            Address = address,
            ContactNumber = contact,
            ManagerId = managerId
        };

    private static User CreateUser(long id, long businessId, long outletId, string name) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            OutletId = outletId,
            Name = name,
            Email = $"{id}@example.test",
            PasswordHash = "not-used"
        };

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
