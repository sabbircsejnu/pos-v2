using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Account;
using RetailPOS.API.DTOs.Transaction;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

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
    public async Task AccountService_GetByIdAsync_CrossTenant_ThrowsKeyNotFound()
    {
        var accountRepository = new AccountRepository(_db);

        var tenantA = CreateTenantAccessMock(1);
        var tenantB = CreateTenantAccessMock(2);

        var serviceA = new AccountService(accountRepository, tenantA.Object, NullLogger<AccountService>.Instance);
        var serviceB = new AccountService(accountRepository, tenantB.Object, NullLogger<AccountService>.Instance);

        var accountA = await serviceA.CreateAsync(new CreateAccountDto
        {
            Name = "Tenant A Cash",
            Type = "asset"
        });

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => serviceB.GetByIdAsync(accountA.Id));
        Assert.Contains($"Account with ID {accountA.Id} not found", ex.Message);
    }

    [Fact]
    public async Task TransactionService_CreateAndLedger_EnforcesTenantBoundary()
    {
        var accountRepository = new AccountRepository(_db);
        var transactionRepository = new TransactionRepository(_db);

        var tenantA = CreateTenantAccessMock(1);
        var tenantB = CreateTenantAccessMock(2);

        var accountServiceA = new AccountService(accountRepository, tenantA.Object, NullLogger<AccountService>.Instance);
        var transactionServiceA = new TransactionService(
            transactionRepository,
            accountRepository,
            tenantA.Object,
            NullLogger<TransactionService>.Instance);
        var transactionServiceB = new TransactionService(
            transactionRepository,
            accountRepository,
            tenantB.Object,
            NullLogger<TransactionService>.Instance);

        var accountA = await accountServiceA.CreateAsync(new CreateAccountDto
        {
            Name = "Tenant A Bank",
            Type = "asset"
        });

        var createdTx = await transactionServiceA.CreateAsync(new CreateTransactionDto
        {
            AccountId = accountA.Id,
            Amount = 100,
            Type = "credit",
            Description = "Tenant A deposit"
        });

        Assert.Equal(accountA.Id, createdTx.AccountId);

        var ownLedger = await transactionServiceA.GetLedgerAsync(accountA.Id);
        Assert.Single(ownLedger);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            transactionServiceB.CreateAsync(new CreateTransactionDto
            {
                AccountId = accountA.Id,
                Amount = 25,
                Type = "credit",
                Description = "Cross-tenant write"
            }));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => transactionServiceB.GetLedgerAsync(accountA.Id));
    }

    private static Mock<ITenantAccessService> CreateTenantAccessMock(long businessId)
    {
        var mock = new Mock<ITenantAccessService>();
        mock.Setup(x => x.IsSuperAdmin).Returns(false);
        mock.Setup(x => x.EffectiveBusinessId).Returns(businessId);
        mock.Setup(x => x.RequireBusinessId()).Returns(businessId);
        return mock;
    }

    private void SeedData()
    {
        _db.Businesses.AddRange(
            new Business { Id = 1, Name = "Biz A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Business { Id = 2, Name = "Biz B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        _db.SaveChanges();
    }
}
