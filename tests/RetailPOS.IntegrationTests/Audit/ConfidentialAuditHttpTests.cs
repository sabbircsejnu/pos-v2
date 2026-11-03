using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class ConfidentialAuditHttpTests : IClassFixture<SqliteRoleGovernanceApiFactory>
{
    private readonly SqliteRoleGovernanceApiFactory factory;

    public ConfidentialAuditHttpTests(SqliteRoleGovernanceApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task ApiDetailListAndExportRequireBusinessCapabilityAndSensitiveGrants()
    {
        await SeedAsync();
        using var permitted = CreateClient(1, "*");
        permitted.DefaultRequestHeaders.Add("X-Acting-User-Id", "999");
        permitted.DefaultRequestHeaders.Add("X-Acting-Role-Id", "999");
        using var detail = await permitted.GetAsync("/api/audit-events/10");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("***REDACTED***", body);
        Assert.DoesNotContain("private-", body);
        Assert.Equal(HttpStatusCode.NotFound, (await permitted.GetAsync("/api/audit-events/20")).StatusCode);

        using var list = await permitted.GetAsync("/api/audit-events?module=payroll");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("private-", await list.Content.ReadAsStringAsync());
        Assert.DoesNotContain("\"id\":20", await list.Content.ReadAsStringAsync());
        using var export = await permitted.PostAsJsonAsync("/api/audit-events/export", new { Module = new[] { "payroll" }, Format = "csv" });
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.DoesNotContain("private-", await export.Content.ReadAsStringAsync());

        using var ordinaryAuditor = CreateClient(1, "audit.view");
        Assert.Equal(HttpStatusCode.NotFound, (await ordinaryAuditor.GetAsync("/api/audit-events/10")).StatusCode);
        using var otherBusiness = CreateClient(2, "*");
        Assert.Equal(HttpStatusCode.NotFound, (await otherBusiness.GetAsync("/api/audit-events/10")).StatusCode);
        using var unscopedAdmin = CreateClient(0, "*", SystemRoleNames.SuperAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await unscopedAdmin.GetAsync("/api/audit-events/10")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var accessEvents = await db.AuditEvents.Where(row =>
            row.PrimaryEntityType == "ConfidentialAuditAccess").ToListAsync();
        Assert.All(accessEvents, row => Assert.True(row.BusinessId is > 0));
        Assert.All(accessEvents, row =>
        {
            Assert.Equal(101, row.RealUserId);
            Assert.Equal(101, row.ActingUserId);
            Assert.NotEqual(999, row.ActingRoleId);
        });
        Assert.Single(accessEvents, row => row.ActionType == "export");
        Assert.False(await db.AuditEvents.AnyAsync(row =>
            row.Module == "reports" && row.ActionType == "export"));
        var payrollFlag = await db.BusinessFeatureSettings.SingleAsync(row =>
            row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
        payrollFlag.IsEnabled = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await permitted.GetAsync("/api/audit-events/10")).StatusCode);
    }

    private HttpClient CreateClient(long businessId, string permissions, string? role = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", role ?? SystemRoleNames.BusinessOwner);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        foreach (var businessId in new[] { 1L, 2L })
        {
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting { BusinessId = businessId, FeatureKey = "hr.enabled", IsEnabled = true },
                new BusinessFeatureSetting { BusinessId = businessId, FeatureKey = "payroll.enabled", IsEnabled = true });
            db.AuditEvents.Add(new AuditEvent
            {
                Id = businessId * 10, BusinessId = businessId, Module = "payroll", Source = "api",
                ActionType = "update", ActionSummary = "private-salary-99999", CorrelationId = Guid.NewGuid(),
                PrimaryEntityType = "PayrollPayment", PrimaryEntityId = "42", RequestPath = "/private-account",
                ErrorMessage = "private-banking-error",
                Entities = [new AuditEventEntity
                {
                    EntityType = "PayrollPayment", EntityId = "42", OperationType = "update", FieldsChangedCount = 1,
                    FieldChanges = [new AuditEventFieldChange
                    {
                        FieldName = "MobilePaymentNumber", NewValue = "\"private-phone\"",
                        NewDisplayValue = "private-owner"
                    }]
                }]
            });
        }
        await db.SaveChangesAsync();
    }
}
