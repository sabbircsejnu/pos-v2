using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Account;
using RetailPOS.API.DTOs.Bill;
using RetailPOS.API.DTOs.Transaction;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class P308LegacyFinanceGuardHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public P308LegacyFinanceGuardHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LegacyFinanceWrites_AreBlocked_EvenThroughCanonicalPermissionAliases()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounting.chart_accounts.view,accounting.chart_accounts.create,accounting.chart_accounts.edit,accounting.journals.view,accounting.journals.create");

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/accounts", new CreateAccountDto { Name = "Blocked", Type = "asset" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/accounts/1001", new UpdateAccountDto { Name = "Blocked", Type = "asset" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/accounts/1001")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transactions", new CreateTransactionDto { AccountId = 1001, Amount = 10m, Type = "credit" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/bills", new CreateBillDto { SupplierId = 2001, AmountDue = 10m, DueDate = new DateTime(2026, 9, 30) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/bills/3001/status", new UpdateBillStatusDto { Status = "paid" })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.Accounts.Where(x => x.Id == 1001).ToListAsync());
        Assert.Equal(50m, await db.Accounts.Where(x => x.Id == 1001).Select(x => x.Balance).SingleAsync());
        Assert.Empty(await db.Transactions.ToListAsync());
        Assert.Equal("unpaid", await db.Bills.Where(x => x.Id == 3001).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task LegacyReads_RemainAvailable_ForHistoricalEvidence()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("accounts.view,transactions.view");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts/1001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/transactions?accountId=1001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bills/3001")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/bills/search", new BillSearchDto { SupplierId = 2001 })).StatusCode);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "201");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Legacy Guard" });
        db.Suppliers.Add(new Supplier { Id = 2001, BusinessId = 1, Name = "Supplier" });
        db.Accounts.Add(new Account { Id = 1001, BusinessId = 1, Name = "Legacy Cash", Type = "asset", Balance = 50m });
        db.Bills.Add(new Bill { Id = 3001, SupplierId = 2001, AmountDue = 25m, DueDate = new DateTime(2026, 9, 30), Status = "unpaid" });
        await db.SaveChangesAsync();
    }
}
