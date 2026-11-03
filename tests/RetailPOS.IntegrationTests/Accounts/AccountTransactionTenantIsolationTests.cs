using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Accounts.Abstractions;
using RetailPOS.Application.Accounts.Queries;
using RetailPOS.Application.Transactions.Abstractions;
using RetailPOS.Application.Transactions.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Accounts;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Transactions;

namespace RetailPOS.Tests.Accounts;

public class AccountTransactionTenantIsolationTests : IDisposable
{
    private readonly RetailPOSDbContext _db;

    public AccountTransactionTenantIsolationTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RetailPOSDbContext(options);
        SeedData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task GetAccountByIdQuery_CrossTenant_ThrowsKeyNotFound()
    {
        var account = new Account
        {
            Id = 100,
            BusinessId = 1,
            Name = "Tenant A Cash",
            Type = "asset",
            Balance = 0m
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();

        var handler = new GetAccountByIdQueryHandler(
            new AccountReadStore(_db),
            new TestAccountReadScope(2));
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new GetAccountByIdQuery(account.Id), CancellationToken.None));
        Assert.Contains($"Account with ID {account.Id} not found", ex.Message);
    }

    [Fact]
    public async Task AccountQueries_FilterListByNormalizedType_AndComputeDetailBalanceAsCreditsMinusDebits()
    {
        var account = new Account
        {
            Id = 101,
            BusinessId = 1,
            Name = "Cash account",
            Type = "asset",
            Balance = 12m
        };
        _db.Accounts.Add(account);
        _db.Transactions.AddRange(
            new Transaction { AccountId = account.Id, Account = account, Amount = 50m, Type = "credit" },
            new Transaction { AccountId = account.Id, Account = account, Amount = 20m, Type = "debit" });
        await _db.SaveChangesAsync();

        var scope = new TestAccountReadScope(1);
        var list = await new GetAccountsQueryHandler(
                new AccountReadStore(_db),
                scope)
            .Handle(new GetAccountsQuery("ASSET"), CancellationToken.None);
        var listedAccount = Assert.Single(list.Accounts);
        Assert.Equal(account.Id, listedAccount.Id);
        Assert.Equal(12m, listedAccount.Balance);
        Assert.Equal(1, list.TotalCount);

        var detail = await new GetAccountByIdQueryHandler(
                new AccountReadStore(_db),
                scope)
            .Handle(new GetAccountByIdQuery(account.Id), CancellationToken.None);
        Assert.Equal(30m, detail.Balance);
    }

    [Fact]
    public async Task TransactionQueries_EnforceBusinessBoundaryForLedgerAndDetail()
    {
        var account = new Account
        {
            Id = 102,
            BusinessId = 1,
            Name = "Tenant A Bank",
            Type = "asset",
            Balance = 0m
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        _db.Transactions.Add(new Transaction
        {
            AccountId = account.Id,
            Account = account,
            Amount = 100m,
            Type = "credit",
        });
        await _db.SaveChangesAsync();

        var ownLedger = await new GetAccountTransactionLedgerQueryHandler(
                new TransactionReadStore(_db),
                new TestAccountReadScope(1))
            .Handle(
                new GetAccountTransactionLedgerQuery(account.Id),
                CancellationToken.None);
        Assert.Single(ownLedger);

        var crossTenantScope = new TestAccountReadScope(2);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetAccountTransactionLedgerQueryHandler(
                    new TransactionReadStore(_db),
                    crossTenantScope)
                .Handle(
                    new GetAccountTransactionLedgerQuery(account.Id),
                    CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new GetTransactionByIdQueryHandler(
                    new TransactionReadStore(_db),
                    crossTenantScope)
                .Handle(
                    new GetTransactionByIdQuery(1),
                    CancellationToken.None));
    }

    [Fact]
    public async Task TransactionQueries_PreserveFilteringPagingAndOrdering()
    {
        var account = new Account
        {
            Id = 103,
            BusinessId = 1,
            Name = "Tenant A Cash",
            Type = "asset"
        };
        _db.Accounts.Add(account);
        _db.Transactions.AddRange(
            new Transaction
            {
                Id = 1031,
                Account = account,
                AccountId = account.Id,
                Amount = 10m,
                Type = "credit",
                TransactionDate = new DateTime(2026, 4, 1)
            },
            new Transaction
            {
                Id = 1032,
                Account = account,
                AccountId = account.Id,
                Amount = 20m,
                Type = "credit",
                TransactionDate = new DateTime(2026, 4, 2)
            },
            new Transaction
            {
                Id = 1033,
                Account = account,
                AccountId = account.Id,
                Amount = 5m,
                Type = "debit",
                TransactionDate = new DateTime(2026, 4, 3)
            });
        await _db.SaveChangesAsync();

        var scope = new TestAccountReadScope(1);
        var store = new TransactionReadStore(_db);
        var search = await new SearchTransactionsQueryHandler(store, scope)
            .Handle(
                new SearchTransactionsQuery(
                    account.Id,
                    new DateTime(2026, 4, 1),
                    new DateTime(2026, 4, 3),
                    "CREDIT",
                    1,
                    1),
                CancellationToken.None);

        Assert.Equal(2, search.TotalCount);
        Assert.Equal(1, search.PageNumber);
        Assert.Equal(1, search.PageSize);
        Assert.Equal(1032, Assert.Single(search.Transactions).Id);
        Assert.Equal("Tenant A Cash", search.Transactions[0].AccountName);
        Assert.Equal(
            1032,
            (await new GetTransactionByIdQueryHandler(store, scope)
                .Handle(new GetTransactionByIdQuery(1032), CancellationToken.None)).Id);
        Assert.Equal(
            new long[] { 1033, 1032, 1031 },
            (await new GetAccountTransactionLedgerQueryHandler(store, scope)
                .Handle(
                    new GetAccountTransactionLedgerQuery(account.Id),
                    CancellationToken.None))
            .Select(transaction => transaction.Id));
    }

    private void SeedData()
    {
        _db.Businesses.AddRange(
            new Business { Id = 1, Name = "Biz A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Business { Id = 2, Name = "Biz B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        _db.SaveChanges();
    }

    private sealed class TestAccountReadScope(long businessId) :
        IAccountReadScope,
        ITransactionReadScope
    {
        public long? BusinessId { get; } = businessId;
    }

}
