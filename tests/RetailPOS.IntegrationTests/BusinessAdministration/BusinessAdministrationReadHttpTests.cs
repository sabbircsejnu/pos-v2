using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessAdministrationReadHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public BusinessAdministrationReadHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task BusinessSummaryAndFeatureReads_KeepSuperAdminHttpEnvelopeAndPayloads()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business { Id = 9101, Name = "Read Contract Business" });
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 9101,
                FeatureKey = "sales.refund",
                IsEnabled = false,
                LimitValue = 4
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");

        using var summaryResponse = await client.GetAsync("/api/businesses?search=contract&isActive=true");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        using var summaryDocument = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        Assert.True(summaryDocument.RootElement.GetProperty("success").GetBoolean());
        var summary = Assert.Single(summaryDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(9101, summary.GetProperty("businessId").GetInt64());
        Assert.Equal("Read Contract Business", summary.GetProperty("businessName").GetString());
        Assert.Equal(0, summary.GetProperty("userCount").GetInt64());

        using var featuresResponse = await client.GetAsync("/api/businesses/9101/features");
        Assert.Equal(HttpStatusCode.OK, featuresResponse.StatusCode);
        using var featuresDocument = JsonDocument.Parse(await featuresResponse.Content.ReadAsStringAsync());
        Assert.True(featuresDocument.RootElement.GetProperty("success").GetBoolean());
        var feature = Assert.Single(featuresDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("sales.refund", feature.GetProperty("featureKey").GetString());
        Assert.False(feature.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(4, feature.GetProperty("limitValue").GetInt32());
    }
}
