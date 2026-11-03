using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Account;
using RetailPOS.API.DTOs.Transaction;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounts;

public class AccountTransactionTenantIsolationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AccountTransactionTenantIsolationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AccountsAndTransactions_LegacyWritesAreBlocked_AndReadsRemainTenantScoped()
    {
        await SeedLegacyAccountAsync(1, 9001, "HTTP Tenant A Account");
        using var tenantBClient = CreateClientForTenant(2, 202);
        using var tenantAClient = CreateClientForTenant(1, 101);

        var createAccountResponse = await tenantAClient.PostAsJsonAsync("/api/accounts", new CreateAccountDto
        {
            Name = "Blocked Account",
            Type = "asset"
        });

        Assert.Equal(HttpStatusCode.Conflict, createAccountResponse.StatusCode);

        var crossRead = await tenantBClient.GetAsync("/api/accounts/9001");
        var crossLedger = await tenantBClient.GetAsync("/api/transactions/ledger?accountId=9001");
        var crossCreateTx = await tenantBClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = 9001,
            Amount = 10,
            Type = "credit",
            Description = "cross tenant should fail"
        });

        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossLedger.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, crossCreateTx.StatusCode);

        var ownRead = await tenantAClient.GetAsync("/api/accounts/9001");
        var ownCreateTx = await tenantAClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = 9001,
            Amount = 50,
            Type = "credit",
            Description = "own tenant should pass"
        });

        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ownCreateTx.StatusCode);
    }

    [Fact]
    public async Task AccountsAndTransactions_MissingCreatePermissions_ReturnsForbidden()
    {
        using var restrictedClient = _factory.CreateClient();
        restrictedClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        restrictedClient.DefaultRequestHeaders.Add("X-Test-UserId", "303");
        restrictedClient.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        restrictedClient.DefaultRequestHeaders.Add("X-Test-Permissions", "accounts.view,transactions.view");

        var createAccountResponse = await restrictedClient.PostAsJsonAsync("/api/accounts", new CreateAccountDto
        {
            Name = "Forbidden Account",
            Type = "asset"
        });

        Assert.Equal(HttpStatusCode.Forbidden, createAccountResponse.StatusCode);

        var createTransactionResponse = await restrictedClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = 9999,
            Amount = 5,
            Type = "credit",
            Description = "Should be blocked by policy"
        });

        Assert.Equal(HttpStatusCode.Forbidden, createTransactionResponse.StatusCode);
    }

    [Fact]
    public async Task Accounts_ReadQueries_PreserveLegacyListAndComputedDetailBalance()
    {
        await SeedLegacyAccountWithTransactionsAsync();
        using var client = CreateClientForTenant(1, 101);

        var listResponse = await client.GetAsync("/api/accounts?type=ASSET");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadApiData<AccountListDto>(listResponse);
        Assert.NotNull(list);
        var listedAccount = Assert.Single(list!.Accounts, account => account.Id == 9003);
        Assert.Equal(12m, listedAccount.Balance);

        var detailResponse = await client.GetAsync("/api/accounts/9003");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadApiData<AccountDto>(detailResponse);
        Assert.NotNull(detail);
        Assert.Equal("HTTP Tenant A Account", detail!.Name);
        Assert.Equal(60m, detail.Balance);

        var searchResponse = await client.GetAsync("/api/transactions?accountId=9003&type=CREDIT");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var search = await ReadApiData<TransactionListDto>(searchResponse);
        Assert.NotNull(search);
        Assert.Equal(2, search!.TotalCount);
        Assert.Equal(1, search.PageNumber);
        Assert.Equal(20, search.PageSize);
        Assert.Equal(2, search.Transactions.Count);
        Assert.All(search.Transactions, transaction =>
            Assert.Equal("HTTP Tenant A Account", transaction.AccountName));

        var pagedSearchResponse = await client.PostAsJsonAsync(
            "/api/transactions/search",
            new TransactionSearchDto
            {
                AccountId = 9003,
                StartDate = new DateTime(2026, 4, 1),
                EndDate = new DateTime(2026, 4, 3),
                Type = "CREDIT",
                PageNumber = 1,
                PageSize = 1
            });
        Assert.Equal(HttpStatusCode.OK, pagedSearchResponse.StatusCode);
        var pagedSearch = await ReadApiData<TransactionListDto>(pagedSearchResponse);
        Assert.NotNull(pagedSearch);
        Assert.Equal(2, pagedSearch!.TotalCount);
        Assert.Equal(90033, Assert.Single(pagedSearch.Transactions).Id);

        var ledgerResponse = await client.GetAsync("/api/transactions/ledger?accountId=9003");
        Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);
        var ledger = await ReadApiData<IEnumerable<TransactionDto>>(ledgerResponse);
        Assert.NotNull(ledger);
        Assert.Equal(3, ledger!.Count());
        Assert.Equal(90033, ledger.First().Id);

        var transactionResponse = await client.GetAsync("/api/transactions/90033");
        Assert.Equal(HttpStatusCode.OK, transactionResponse.StatusCode);
        var transaction = await ReadApiData<TransactionDto>(transactionResponse);
        Assert.NotNull(transaction);
        Assert.Equal("HTTP Tenant A Account", transaction!.AccountName);
        Assert.Equal(30m, transaction.Amount);
    }

    [Fact]
    public async Task AccountsAndTransactions_WildcardPermissionWithSuperAdmin_CannotBypassLegacyWriteGuard()
    {
        await SeedLegacyAccountAsync(1, 9002, "Tenant A Wildcard Target");
        using var superAdminClient = _factory.CreateClient();

        superAdminClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-UserId", "999");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-Permissions", "*");

        var readBySuperAdmin = await superAdminClient.GetAsync("/api/accounts/9002");
        var createTxBySuperAdmin = await superAdminClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = 9002,
            Amount = 15,
            Type = "credit",
            Description = "Wildcard super-admin cross-tenant"
        });

        // The Super Admin is scoped to the selected Business (2) and cannot read Business 1's account.
        Assert.Equal(HttpStatusCode.NotFound, readBySuperAdmin.StatusCode);
        Assert.False(createTxBySuperAdmin.IsSuccessStatusCode);
    }

    private async Task SeedLegacyAccountAsync(long businessId, long accountId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOS.Infrastructure.Data.RetailPOSDbContext>();
        if (await db.Accounts.FindAsync(accountId) != null)
            return;
        db.Accounts.Add(new RetailPOS.Core.Entities.Account { Id = accountId, BusinessId = businessId, Name = name, Type = "asset", Balance = 0m });
        await db.SaveChangesAsync();
    }

    private async Task SeedLegacyAccountWithTransactionsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOS.Infrastructure.Data.RetailPOSDbContext>();
        db.Accounts.Add(new RetailPOS.Core.Entities.Account
        {
            Id = 9003,
            BusinessId = 1,
            Name = "HTTP Tenant A Account",
            Type = "asset",
            Balance = 12m
        });
        db.Transactions.AddRange(
            new RetailPOS.Core.Entities.Transaction
            {
                Id = 90031,
                AccountId = 9003,
                Amount = 50m,
                Type = "credit",
                TransactionDate = new DateTime(2026, 4, 2)
            },
            new RetailPOS.Core.Entities.Transaction
            {
                Id = 90032,
                AccountId = 9003,
                Amount = 20m,
                Type = "debit",
                TransactionDate = new DateTime(2026, 4, 1)
            },
            new RetailPOS.Core.Entities.Transaction
            {
                Id = 90033,
                AccountId = 9003,
                Amount = 30m,
                Type = "credit",
                TransactionDate = new DateTime(2026, 4, 3)
            });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClientForTenant(long businessId, long userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "accounts.view,accounts.create,transactions.view,transactions.create");
        return client;
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public T? Data { get; set; }
    }
}
