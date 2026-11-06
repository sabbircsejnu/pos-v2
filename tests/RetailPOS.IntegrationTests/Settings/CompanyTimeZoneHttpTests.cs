using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Settings;

public sealed class CompanyTimeZoneHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task FormattingEndpoint_GivesEachBusinessItsOwnZone()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            db.Businesses.AddRange(
                new Business { Id = 1, Name = "Tokyo", IsActive = true },
                new Business { Id = 2, Name = "New York", IsActive = true });
            db.BusinessPosSettings.AddRange(
                new BusinessPosSettings { BusinessId = 1, DefaultTimeZoneId = "Asia/Tokyo" },
                new BusinessPosSettings { BusinessId = 2, DefaultTimeZoneId = "America/New_York" });
            await db.SaveChangesAsync();
        }

        Assert.Equal("Asia/Tokyo", await ZoneForBusinessAsync("1"));
        Assert.Equal("America/New_York", await ZoneForBusinessAsync("2"));
    }

    private async Task<string?> ZoneForBusinessAsync(string businessId)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "sales.view");

        using var response = await client.GetAsync("/api/settings/formatting");

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("data").GetProperty("timeZone").GetString();
    }
}
