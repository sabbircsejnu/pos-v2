using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessReceiptFooterHttpTests(RetailPosApiFactory factory) : IClassFixture<RetailPosApiFactory>
{
    private const string FooterBody = """{"showPoweredByOnReceipt":true,"poweredByText":"Hijacked"}""";
    private const string CreateBody = """
        {"businessName":"Hijack Business","ownerName":"O","ownerEmail":"o@hijack.test","outletManagerName":"M","outletManagerEmail":"m@hijack.test",
         "salesPersonName":"S","salesPersonEmail":"s@hijack.test","accountsAdminName":"A","accountsAdminEmail":"a@hijack.test",
         "defaultOutletName":"Main","showPoweredByOnReceipt":true,"poweredByText":"Hijacked"}
        """;

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private HttpClient Client(string role, string? effectiveRole = null, bool switched = false, string permissions = "*", long businessId = 8101)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (effectiveRole is not null)
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", effectiveRole);
        if (switched)
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        return client;
    }

    private async Task<(bool Show, string? Text)> SeedAsync(long id, bool show = false, string? text = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Businesses.Add(new Business { Id = id, Name = $"Footer {id}", IsActive = true, ShowPoweredByOnReceipt = show, PoweredByText = text });
        await db.SaveChangesAsync();
        return (show, text);
    }

    private async Task<(bool Show, string? Text)> ReadAsync(long id)
    {
        using var scope = factory.Services.CreateScope();
        var business = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .Businesses.AsNoTracking().SingleAsync(item => item.Id == id);
        return (business.ShowPoweredByOnReceipt, business.PoweredByText);
    }

    [Fact]
    public async Task SuperAdmin_CanEnableEditAndDisableTheFooter()
    {
        await SeedAsync(8101);
        using var admin = Client("Super Admin", businessId: 8101);

        using (var response = await admin.PatchAsync(
                   "/api/businesses/8101/receipt-footer",
                   Json("""{"showPoweredByOnReceipt":true,"poweredByText":"  Powered by: Irani Mart Technology  "}""")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("showPoweredByOnReceipt").GetBoolean());
            Assert.Equal("Powered by: Irani Mart Technology", data.GetProperty("poweredByText").GetString());
        }

        Assert.Equal((true, "Powered by: Irani Mart Technology"), await ReadAsync(8101));

        using var read = await admin.GetAsync("/api/businesses/8101");
        Assert.Contains("Powered by: Irani Mart Technology", await read.Content.ReadAsStringAsync());

        using var off = await admin.PatchAsync(
            "/api/businesses/8101/receipt-footer",
            Json("""{"showPoweredByOnReceipt":false,"poweredByText":"Kept for later"}"""));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal((false, "Kept for later"), await ReadAsync(8101));
    }

    [Fact]
    public async Task SuperAdmin_ValidationAndMissingBusinessAreReported()
    {
        await SeedAsync(8102);
        using var admin = Client("Super Admin", businessId: 8102);

        using var tooLong = await admin.PatchAsync(
            "/api/businesses/8102/receipt-footer",
            Json($$"""{"showPoweredByOnReceipt":true,"poweredByText":"{{new string('x', 151)}}"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal((false, null), await ReadAsync(8102));

        using var missing = await admin.PatchAsync("/api/businesses/999999/receipt-footer", Json(FooterBody));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(8201, "BusinessOwner")]
    [InlineData(8202, "Business Admin")]
    [InlineData(8203, "Admin")]
    [InlineData(8204, "Manager")]
    [InlineData(8205, "Cashier")]
    public async Task OtherRoles_AreForbiddenEvenWithEveryPermissionOnTheirOwnBusiness(long id, string role)
    {
        var original = await SeedAsync(id, true, "Platform text");
        using var client = Client(role, businessId: id);

        using var update = await client.PatchAsync($"/api/businesses/{id}/receipt-footer", Json(FooterBody));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using var create = await client.PostAsync("/api/businesses", Json(CreateBody));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using var read = await client.GetAsync($"/api/businesses/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        Assert.Equal(original, await ReadAsync(id));
    }

    [Theory]
    [InlineData(8301, "Super Admin", "Manager")]
    [InlineData(8302, "Super Admin", "Super Admin")]
    [InlineData(8303, "BusinessOwner", "Super Admin")]
    public async Task RoleSwitchedCallers_AreForbidden(long id, string realRole, string actingRole)
    {
        var original = await SeedAsync(id, true, "Platform text");
        using var client = Client(realRole, actingRole, switched: true, businessId: id);

        using var update = await client.PatchAsync($"/api/businesses/{id}/receipt-footer", Json(FooterBody));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using var create = await client.PostAsync("/api/businesses", Json(CreateBody));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        Assert.Equal(original, await ReadAsync(id));
    }

    [Fact]
    public async Task OtherBusinessAndSettingsApis_IgnoreFooterFieldsInTheirPayloads()
    {
        var original = await SeedAsync(8103, true, "Platform text");
        const string extras = "\"showPoweredByOnReceipt\":false,\"poweredByText\":\"Hijacked\",\"ShowPoweredByOnReceipt\":false,\"PoweredByText\":\"Hijacked\"";

        using var admin = Client("Super Admin", businessId: 8103);
        using var subscription = await admin.PatchAsync(
            "/api/businesses/8103/subscription",
            Json($$"""{"subscriptionPlan":"Gold",{{extras}}}"""));
        Assert.Equal(HttpStatusCode.OK, subscription.StatusCode);
        Assert.Equal(original, await ReadAsync(8103));

        using var status = await admin.PatchAsync("/api/businesses/8103/status", Json($$"""{"isActive":true,{{extras}}}"""));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(original, await ReadAsync(8103));

        using var owner = Client("BusinessOwner", businessId: 8103);
        using var receipt = await owner.PutAsync("/api/settings/receipt", Json($$"""{"footer":"Please come again",{{extras}}}"""));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.DoesNotContain("Hijacked", await receipt.Content.ReadAsStringAsync());
        Assert.Equal(original, await ReadAsync(8103));

        using var pos = await owner.PutAsync("/api/settings/pos", Json($$"""{"taxName":"VAT",{{extras}}}"""));
        Assert.DoesNotContain("Hijacked", await pos.Content.ReadAsStringAsync());
        Assert.Equal(original, await ReadAsync(8103));
    }

    [Fact]
    public async Task ReceiptSettings_RefuseAPoweredByFooterForEveryCaller()
    {
        using var owner = Client("BusinessOwner");
        using var response = await owner.PutAsync("/api/settings/receipt", Json("""{"footer":"Powered by: Someone Else"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Platform Super Admin", await response.Content.ReadAsStringAsync());

        using var read = await owner.GetAsync("/api/settings/receipt");
        Assert.DoesNotContain("Someone Else", await read.Content.ReadAsStringAsync());
    }
}
