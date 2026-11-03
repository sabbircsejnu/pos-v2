using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.Accounting;

public sealed class ChartAccountReadHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task ListAndDetail_PreserveFiltersOrderingBusinessScopeAndResponse()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var listResponse = await client.GetAsync("/api/accounting/chart-accounts");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        Assert.True(list.RootElement.GetProperty("success").GetBoolean());
        var accounts = list.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["2001", "3001", "1001"], accounts.Select(account => account.GetProperty("accountCode").GetString()));

        using var filteredResponse = await client.GetAsync(
            "/api/accounting/chart-accounts?query=cash&accountType=Asset&isActive=false");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        using var filtered = await ReadDocumentAsync(filteredResponse);
        var filteredAccount = Assert.Single(filtered.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("3001", filteredAccount.GetProperty("accountCode").GetString());

        using var detailResponse = await client.GetAsync("/api/accounting/chart-accounts/1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadDocumentAsync(detailResponse);
        Assert.Equal("Cash Box", detail.RootElement.GetProperty("data").GetProperty("accountName").GetString());

        using var foreignResponse = await client.GetAsync("/api/accounting/chart-accounts/4");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        using var foreign = await ReadDocumentAsync(foreignResponse);
        Assert.Equal("Chart account with ID 4 was not found.", foreign.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChartAccountReadRoutes_RequireViewPermission()
    {
        using var client = CreateClient("products.view");

        using var list = await client.GetAsync("/api/accounting/chart-accounts");
        using var detail = await client.GetAsync("/api/accounting/chart-accounts/1");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    [Fact]
    public async Task Deactivate_PreservesSuccessMessageAndSequentialIdempotency()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.chart_accounts.edit");

        using var firstResponse = await client.PostAsync(
            "/api/accounting/chart-accounts/1/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var first = await ReadDocumentAsync(firstResponse);
        Assert.Equal(
            "Chart account deactivated successfully",
            first.RootElement.GetProperty("message").GetString());

        using var retryResponse = await client.PostAsync(
            "/api/accounting/chart-accounts/1/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var retry = await ReadDocumentAsync(retryResponse);
        Assert.Equal(
            "Chart account is already inactive",
            retry.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void ChartAccountReadRoutesRemainInOpenApi()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var path = swagger.Paths["/api/accounting/chart-accounts"];
        Assert.Contains(OperationType.Get, path.Operations.Keys);
        Assert.Contains(OperationType.Get, swagger.Paths["/api/accounting/chart-accounts/{id}"].Operations.Keys);
    }

    private HttpClient CreateClient(string permissions = "accounting.chart_accounts.view")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        if (!string.IsNullOrEmpty(permissions))
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = 17, Name = "Business A" },
            new Business { Id = 18, Name = "Business B" });
        db.ChartAccounts.AddRange(
            CreateAccount(1, 17, "1001", "Cash Box", ChartAccountTypes.Asset, 10, true),
            CreateAccount(2, 17, "2001", "Payable", ChartAccountTypes.Liability, 5, true),
            CreateAccount(3, 17, "3001", "Cash Reserve", ChartAccountTypes.Asset, 5, false),
            CreateAccount(4, 18, "1001", "Foreign Cash", ChartAccountTypes.Asset, 1, true));
        await db.SaveChangesAsync();
    }

    private static ChartAccount CreateAccount(
        long id,
        long businessId,
        string code,
        string name,
        string type,
        int displayOrder,
        bool isActive) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = type is ChartAccountTypes.Asset or ChartAccountTypes.Expense
                or ChartAccountTypes.CostOfGoodsSold ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit,
            IsActive = isActive,
            DisplayOrder = displayOrder
        };

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
