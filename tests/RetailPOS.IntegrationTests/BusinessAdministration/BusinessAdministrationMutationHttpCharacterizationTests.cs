using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessAdministrationMutationHttpCharacterizationTests
    : IClassFixture<AuditEnabledRetailPosApiFactory>
{
    private readonly AuditEnabledRetailPosApiFactory _factory;

    public BusinessAdministrationMutationHttpCharacterizationTests(AuditEnabledRetailPosApiFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task StatusAndSubscriptionUpdates_PreserveLegacyHttpBehavior()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business
            {
                Id = 9201,
                Name = "Lifecycle Business",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");

        using var statusResponse = await client.PatchAsync(
            "/api/businesses/9201/status",
            JsonContent("""{"isActive":false}"""));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var statusDocument = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.True(statusDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Business deactivated", statusDocument.RootElement.GetProperty("message").GetString());
        Assert.False(statusDocument.RootElement.GetProperty("data").GetProperty("isActive").GetBoolean());

        using var subscriptionResponse = await client.PatchAsync(
            "/api/businesses/9201/subscription",
            JsonContent(
                """{"subscriptionPlan":"  Gold  ","trialEndsAt":"2026-12-01T00:00:00Z","subscriptionEndsAt":"2027-01-01T00:00:00Z","maxOutlets":5,"maxUsers":20}"""));
        Assert.Equal(HttpStatusCode.OK, subscriptionResponse.StatusCode);
        using var subscriptionDocument = JsonDocument.Parse(
            await subscriptionResponse.Content.ReadAsStringAsync());
        Assert.True(subscriptionDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Business subscription updated",
            subscriptionDocument.RootElement.GetProperty("message").GetString());
        var summary = subscriptionDocument.RootElement.GetProperty("data");
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal("Gold", summary.GetProperty("subscriptionPlan").GetString());
        Assert.Equal(5, summary.GetProperty("maxOutlets").GetInt32());
        Assert.Equal(20, summary.GetProperty("maxUsers").GetInt32());
        Assert.Equal("2026-12-01T00:00:00", summary.GetProperty("trialEndsAt").GetDateTime().ToString("s"));
        Assert.Equal("2027-01-01T00:00:00", summary.GetProperty("subscriptionEndsAt").GetDateTime().ToString("s"));

        using var verifyScope = _factory.Services.CreateScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .Businesses.AsNoTracking().SingleAsync(business => business.Id == 9201);
        Assert.False(persisted.IsActive);
        Assert.Equal("Gold", persisted.SubscriptionPlan);
        Assert.Equal(5, persisted.MaxOutlets);
        Assert.Equal(20, persisted.MaxUsers);
    }

    [Fact]
    public async Task FeatureSettingsUpsert_PreservesLegacyNormalizationDuplicatesAndEmptyInput()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business { Id = 9202, Name = "Feature Business" });
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 9202,
                FeatureKey = "sales.refund",
                IsEnabled = true,
                LimitValue = 1
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");

        using var updateResponse = await client.PatchAsync(
            "/api/businesses/9202/features",
            JsonContent(
                """{"features":[{"featureKey":" SALES.REFUND ","isEnabled":false,"limitValue":7},{"featureKey":"sales.refund","isEnabled":true,"limitValue":9},{"featureKey":"new.feature","isEnabled":true,"limitValue":3},{"featureKey":" ","isEnabled":false}]}"""));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateDocument = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.True(updateDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Business feature settings updated",
            updateDocument.RootElement.GetProperty("message").GetString());
        var updatedFeatures = updateDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(new[] { "new.feature", "sales.refund" },
            updatedFeatures.Select(feature => feature.GetProperty("featureKey").GetString()));
        Assert.True(updatedFeatures[1].GetProperty("isEnabled").GetBoolean());
        Assert.Equal(9, updatedFeatures[1].GetProperty("limitValue").GetInt32());

        using var emptyResponse = await client.PatchAsync(
            "/api/businesses/9202/features",
            JsonContent("""{"features":[]}"""));
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        using var emptyDocument = JsonDocument.Parse(await emptyResponse.Content.ReadAsStringAsync());
        Assert.Equal(updatedFeatures.Length, emptyDocument.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal(
            "Business feature settings updated",
            emptyDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PayrollFeatureRequiresHrAndPayrollApiIsPermissionAndBusinessCapabilityProtected()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.AddRange(
                new Business { Id = 1, Name = "Payroll Disabled" },
                new Business { Id = 2, Name = "Payroll Enabled" },
                new Business { Id = 3, Name = "HR Only" });
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting { BusinessId = 2, FeatureKey = "hr.enabled", IsEnabled = true },
                new BusinessFeatureSetting { BusinessId = 2, FeatureKey = "payroll.enabled", IsEnabled = true },
                new BusinessFeatureSetting { BusinessId = 3, FeatureKey = "hr.enabled", IsEnabled = true },
                new BusinessFeatureSetting { BusinessId = 3, FeatureKey = "payroll.enabled", IsEnabled = false });
            await db.SaveChangesAsync();
        }

        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        using var invalidEnable = await admin.PatchAsync(
            "/api/businesses/1/features",
            JsonContent("""{"features":[{"featureKey":"payroll.enabled","isEnabled":true}]}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEnable.StatusCode);

        using var disabledBusinessClient = _factory.CreateClient();
        disabledBusinessClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "3");
        disabledBusinessClient.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.dashboard.view");
        using var disabledResponse = await disabledBusinessClient.GetAsync("/api/payroll/v1/capabilities");
        Assert.Equal(HttpStatusCode.Forbidden, disabledResponse.StatusCode);

        using var missingPermissionClient = _factory.CreateClient();
        missingPermissionClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        missingPermissionClient.DefaultRequestHeaders.Add("X-Test-Permissions", "hr.employee.view");
        using var missingPermissionResponse = await missingPermissionClient.GetAsync("/api/payroll/v1/capabilities");
        Assert.Equal(HttpStatusCode.Forbidden, missingPermissionResponse.StatusCode);

        using var enabledBusinessClient = _factory.CreateClient();
        enabledBusinessClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        enabledBusinessClient.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.dashboard.view");
        using var enabledResponse = await enabledBusinessClient.GetAsync("/api/payroll/v1/capabilities");
        Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);
        using var enabledPayload = JsonDocument.Parse(await enabledResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, enabledPayload.RootElement.GetProperty("businessId").GetInt64());
        Assert.True(enabledPayload.RootElement.GetProperty("payrollEnabled").GetBoolean());

        using var validEnable = await admin.PatchAsync(
            "/api/businesses/1/features",
            JsonContent("""{"features":[{"featureKey":"hr.enabled","isEnabled":true},{"featureKey":"payroll.enabled","isEnabled":true}]}"""));
        Assert.Equal(HttpStatusCode.OK, validEnable.StatusCode);
        using (var auditScope = _factory.Services.CreateScope())
        {
            var db = auditScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var featureSettingAudit = await db.AuditEvents
                .AsNoTracking()
                .Include(auditEvent => auditEvent.Entities)
                .Where(auditEvent => auditEvent.Status == "Success")
                .OrderByDescending(auditEvent => auditEvent.CreatedAt)
                .FirstOrDefaultAsync(auditEvent =>
                    auditEvent.Entities.Any(entity =>
                        entity.EntityType == nameof(BusinessFeatureSetting)));

            Assert.NotNull(featureSettingAudit);
            Assert.Contains(featureSettingAudit.Entities, entity =>
                entity.EntityType == nameof(BusinessFeatureSetting)
                && entity.FieldsChangedCount > 0);
        }

        using var mismatchedAdmin = _factory.CreateClient();
        mismatchedAdmin.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        mismatchedAdmin.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        using var mismatchedScope = await mismatchedAdmin.PatchAsync(
            "/api/businesses/2/features",
            JsonContent("""{"features":[{"featureKey":"payroll.enabled","isEnabled":false}]}"""));
        // Feature changes act on the explicit target Business; the selected context is irrelevant.
        Assert.Equal(HttpStatusCode.OK, mismatchedScope.StatusCode);
        using var disabledAgainClient = _factory.CreateClient();
        disabledAgainClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        disabledAgainClient.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.dashboard.view");
        using var disabledAgainResponse = await disabledAgainClient.GetAsync("/api/payroll/v1/capabilities");
        Assert.Equal(HttpStatusCode.Forbidden, disabledAgainResponse.StatusCode);
        using var newlyEnabledClient = _factory.CreateClient();
        newlyEnabledClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        newlyEnabledClient.DefaultRequestHeaders.Add("X-Test-Permissions", "payroll.dashboard.view");
        using var newlyEnabledResponse = await newlyEnabledClient.GetAsync("/api/payroll/v1/capabilities");
        Assert.Equal(HttpStatusCode.OK, newlyEnabledResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerAccessReset_PreservesInvitationInvalidationAndHashedTokenBehavior()
    {
        var before = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business { Id = 9203, Name = "Owner Reset Business" });
            db.Roles.Add(new Role { Id = 9303, Name = "BusinessOwner" });
            db.Users.Add(new User
            {
                Id = 9204,
                BusinessId = 9203,
                RoleId = 9303,
                Name = "Business Owner",
                Email = "owner-reset@example.test"
            });
            db.UserInvitations.AddRange(
                new UserInvitation
                {
                    Id = 9401,
                    UserId = 9204,
                    Purpose = "onboarding",
                    TokenHash = "old-onboarding-hash",
                    ExpiresAt = before.AddDays(1)
                },
                new UserInvitation
                {
                    Id = 9402,
                    UserId = 9204,
                    Purpose = "other-active-purpose",
                    TokenHash = "old-other-hash",
                    ExpiresAt = before.AddDays(1)
                },
                new UserInvitation
                {
                    Id = 9403,
                    UserId = 9204,
                    Purpose = "expired",
                    TokenHash = "expired-hash",
                    ExpiresAt = before.AddDays(-1)
                });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        using var response = await client.PostAsync("/api/businesses/9203/owner/reset-access", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Business owner access reset invitation issued",
            document.RootElement.GetProperty("message").GetString());
        var data = document.RootElement.GetProperty("data");
        var token = data.GetProperty("invitationToken").GetString()!;
        var expiresAt = data.GetProperty("invitationExpiresAt").GetDateTime();
        Assert.Equal(9204, data.GetProperty("userId").GetInt64());
        Assert.Equal("owner-reset@example.test", data.GetProperty("ownerEmail").GetString());
        Assert.Equal(64, token.Length);
        Assert.InRange(expiresAt, before.AddDays(7), DateTime.UtcNow.AddDays(7).AddSeconds(2));

        using var verifyScope = _factory.Services.CreateScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var owner = await dbContext.Users.AsNoTracking().SingleAsync(user => user.Id == 9204);
        Assert.True(owner.MustResetPassword);
        var invitations = await dbContext.UserInvitations.AsNoTracking()
            .Where(invitation => invitation.UserId == 9204)
            .ToListAsync();
        Assert.NotNull(invitations.Single(invitation => invitation.Id == 9401).ConsumedAt);
        Assert.NotNull(invitations.Single(invitation => invitation.Id == 9402).ConsumedAt);
        Assert.Null(invitations.Single(invitation => invitation.Id == 9403).ConsumedAt);
        var replacement = invitations.Single(invitation => invitation.Purpose == "owner-reset");
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            replacement.TokenHash);
        Assert.NotEqual(token, replacement.TokenHash);
        Assert.Equal(expiresAt, replacement.ExpiresAt);
        Assert.Equal(replacement.CreatedAt, owner.UpdatedAt);
    }

    [Fact]
    public async Task BusinessCreation_PreservesSuperAdminGateAndSuccessEnvelope()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        using var client = _factory.CreateClient();
        using var unauthorizedResponse = await client.PostAsync(
            "/api/businesses",
            JsonContent("""{"businessName":"Onboarding HTTP Business"}"""));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        using var response = await client.PostAsync(
            "/api/businesses",
            JsonContent(
                """
                {
                  "businessName":"  HTTP Northwind  ",
                  "businessEmail":"  http-northwind@example.test  ",
                  "businessPhone":"  555-0100  ",
                  "businessAddress":"  7 Market Road  ",
                  "ownerName":"HTTP Owner",
                  "ownerEmail":"http-owner@example.test",
                  "outletManagerName":"HTTP Manager",
                  "outletManagerEmail":"http-manager@example.test",
                  "salesPersonName":"HTTP Sales",
                  "salesPersonEmail":"http-sales@example.test",
                  "accountsAdminName":"HTTP Accounts",
                  "accountsAdminEmail":"http-accounts@example.test",
                  "createDefaultWarehouse":false
                }
                """));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Business onboarded successfully",
            document.RootElement.GetProperty("message").GetString());
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("businessId").GetInt64() > 0);
        Assert.Equal("HTTP Northwind", data.GetProperty("businessName").GetString());
        Assert.True(data.GetProperty("defaultOutletId").GetInt64() > 0);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("defaultWarehouseId").ValueKind);
        var users = data.GetProperty("users").EnumerateArray().ToArray();
        Assert.Equal(4, users.Length);
        Assert.Contains(users, user => user.GetProperty("role").GetString() == "BusinessOwner");
        Assert.All(users, user =>
        {
            Assert.True(user.GetProperty("userId").GetInt64() > 0);
            Assert.Equal(64, user.GetProperty("invitationToken").GetString()!.Length);
            Assert.True(user.GetProperty("invitationExpiresAt").GetDateTime() > DateTime.UtcNow.AddDays(6));
        });

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var business = await verifyDb.Businesses.AsNoTracking().SingleAsync();
        Assert.Equal("555-0100", business.Phone);
        Assert.Equal(4, await verifyDb.Users.CountAsync());
        Assert.Equal(AccountingDefaults.ChartOfAccounts.Count, await verifyDb.ChartAccounts.CountAsync());
        Assert.Single(await verifyDb.AccountingConfigurations.ToListAsync());
        Assert.Single(await verifyDb.AccountingOutletConfigurations.ToListAsync());
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");
}
