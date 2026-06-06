using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task AccountsAndTransactions_CrossTenantDenied_OwnTenantAllowed()
    {
        using var tenantAClient = CreateClientForTenant(1, 101);
        using var tenantBClient = CreateClientForTenant(2, 202);

        var createAccountResponse = await tenantAClient.PostAsJsonAsync("/api/accounts", new CreateAccountDto
        {
            Name = "HTTP Tenant A Account",
            Type = "asset"
        });

        Assert.Equal(HttpStatusCode.Created, createAccountResponse.StatusCode);

        var createdAccount = await ReadApiData<AccountDto>(createAccountResponse);
        Assert.NotNull(createdAccount);

        var crossRead = await tenantBClient.GetAsync($"/api/accounts/{createdAccount!.Id}");
        var crossLedger = await tenantBClient.GetAsync($"/api/transactions/ledger?accountId={createdAccount.Id}");
        var crossCreateTx = await tenantBClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = createdAccount.Id,
            Amount = 10,
            Type = "credit",
            Description = "cross tenant should fail"
        });

        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossLedger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossCreateTx.StatusCode);

        var ownRead = await tenantAClient.GetAsync($"/api/accounts/{createdAccount.Id}");
        var ownCreateTx = await tenantAClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = createdAccount.Id,
            Amount = 50,
            Type = "credit",
            Description = "own tenant should pass"
        });

        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ownCreateTx.StatusCode);
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
    public async Task AccountsAndTransactions_WildcardPermissionWithSuperAdmin_AllowsCrossTenantAccess()
    {
        using var tenantAClient = CreateClientForTenant(1, 401);
        using var superAdminClient = _factory.CreateClient();

        superAdminClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-UserId", "999");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        superAdminClient.DefaultRequestHeaders.Add("X-Test-Permissions", "*");

        var createAccountResponse = await tenantAClient.PostAsJsonAsync("/api/accounts", new CreateAccountDto
        {
            Name = "Tenant A Wildcard Target",
            Type = "asset"
        });

        Assert.Equal(HttpStatusCode.Created, createAccountResponse.StatusCode);

        var createdAccount = await ReadApiData<AccountDto>(createAccountResponse);
        Assert.NotNull(createdAccount);

        var readBySuperAdmin = await superAdminClient.GetAsync($"/api/accounts/{createdAccount!.Id}");
        var createTxBySuperAdmin = await superAdminClient.PostAsJsonAsync("/api/transactions", new CreateTransactionDto
        {
            AccountId = createdAccount.Id,
            Amount = 15,
            Type = "credit",
            Description = "Wildcard super-admin cross-tenant"
        });

        Assert.Equal(HttpStatusCode.OK, readBySuperAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createTxBySuperAdmin.StatusCode);
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
