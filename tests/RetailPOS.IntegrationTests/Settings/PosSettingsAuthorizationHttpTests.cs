using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Settings;

/// <summary>
/// P2.2 / Phase 2.1 Workstream E: the manual-discount limit is changed only with sales.discount.configure, every other POS
/// setting only with settings.edit, and one permission never unlocks the other's fields.
/// </summary>
public sealed class PosSettingsAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public PosSettingsAuthorizationHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DiscountConfigurer_CanReadAndSetOnlyTheManualDiscountLimit()
    {
        await ResetAsync();
        using var client = CreateClient("sales.discount.configure");

        using var read = await client.GetAsync("/api/settings/pos");
        using var update = await client.PutAsJsonAsync("/api/settings/pos", new { maxManualDiscountPercent = 15m });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var document = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal(15m, document.RootElement.GetProperty("data").GetProperty("maxManualDiscountPercent").GetDecimal());
        Assert.Equal(15m, (await StoredAsync()).MaxManualDiscountPercent);
    }

    [Theory]
    [InlineData("{\"taxName\":\"Sneaky\"}")]
    [InlineData("{\"taxEnabled\":false}")]
    [InlineData("{\"defaultTaxRate\":0}")]
    [InlineData("{\"pricesIncludeTax\":true}")]
    [InlineData("{\"maxCashSessionDurationHours\":4}")]
    [InlineData("{\"defaultTimeZoneId\":\"UTC\"}")]
    [InlineData("{\"maxManualDiscountPercent\":10,\"taxName\":\"Sneaky\"}")]
    public async Task DiscountConfigurer_CannotChangeAnyOtherSettingEvenAlongsideTheDiscountLimit(string body)
    {
        await ResetAsync();
        var before = await StoredAsync();
        using var client = CreateClient("sales.discount.configure");

        using var response = await client.PutAsync(
            "/api/settings/pos", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await StoredAsync();
        Assert.Equal(before.MaxManualDiscountPercent, after.MaxManualDiscountPercent);
        Assert.Equal(before.TaxName, after.TaxName);
        Assert.Equal(before.TaxEnabled, after.TaxEnabled);
        Assert.Equal(before.PricesIncludeTax, after.PricesIncludeTax);
    }

    [Fact]
    public async Task SettingsEditor_CannotChangeTheManualDiscountLimitWithoutTheDiscountPermission()
    {
        await ResetAsync();
        using var client = CreateClient("settings.view,settings.edit");

        using var discount = await client.PutAsJsonAsync("/api/settings/pos", new { maxManualDiscountPercent = 90m });
        using var tax = await client.PutAsJsonAsync("/api/settings/pos", new { taxName = "VAT" });

        Assert.Equal(HttpStatusCode.Forbidden, discount.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tax.StatusCode);
        Assert.NotEqual(90m, (await StoredAsync()).MaxManualDiscountPercent);
        Assert.Equal("VAT", (await StoredAsync()).TaxName);
    }

    [Fact]
    public async Task HoldingBothPermissions_AllowsChangingBothInOneRequest()
    {
        await ResetAsync();
        using var client = CreateClient("settings.edit,sales.discount.configure");

        using var response = await client.PutAsJsonAsync(
            "/api/settings/pos", new { maxManualDiscountPercent = 20m, taxName = "GST" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await StoredAsync();
        Assert.Equal(20m, stored.MaxManualDiscountPercent);
        Assert.Equal("GST", stored.TaxName);
    }

    [Fact]
    public async Task UsersWithoutEitherPermission_CannotReadOrChangeThePosSettings()
    {
        await ResetAsync();
        using var client = CreateClient("customers.view");

        using var read = await client.GetAsync("/api/settings/pos");
        using var update = await client.PutAsJsonAsync("/api/settings/pos", new { maxManualDiscountPercent = 5m });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task AnEmptyUpdateIsRejectedRatherThanTreatedAsAnAuthorizedNoOp()
    {
        await ResetAsync();
        using var client = CreateClient("settings.edit,sales.discount.configure");

        using var response = await client.PutAsJsonAsync("/api/settings/pos", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1);
    }

    private async Task<StoredPosSettings> StoredAsync()
    {
        using var client = CreateClient("settings.view");
        using var response = await client.GetAsync("/api/settings/pos");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        return new StoredPosSettings(
            data.GetProperty("maxManualDiscountPercent").GetDecimal(),
            data.GetProperty("taxName").GetString(),
            data.GetProperty("taxEnabled").GetBoolean(),
            data.GetProperty("pricesIncludeTax").GetBoolean());
    }

    private sealed record StoredPosSettings(
        decimal MaxManualDiscountPercent, string? TaxName, bool TaxEnabled, bool PricesIncludeTax);
}
