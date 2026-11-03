using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.ChartAccounts;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class ChartAccountCommandStoreRelationalTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly ChartAccountCommandStore _store;

    public ChartAccountCommandStoreRelationalTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        _db.Database.EnsureCreated();
        _db.Businesses.Add(new Business { Id = 17, Name = "Chart Account Test" });
        _db.SaveChanges();
        _store = new ChartAccountCommandStore(
            _db,
            null,
            NullLogger<ChartAccountCommandStore>.Instance);
    }

    [Fact]
    public async Task CreateUpdateAndDeactivate_PersistNormalizedValuesAndRetainRetryBehavior()
    {
        var created = await CreateAccountAsync(" 1200 ", "  Cash Main  ");
        Assert.Equal("1200", created.AccountCode);
        Assert.Equal("Cash Main", created.AccountName);
        Assert.Equal(ChartAccountTypes.Asset, created.AccountType);

        var updated = await _store.UpdateAsync(
            17,
            101,
            new UpdateChartAccountCommand(
                created.Id,
                " Renamed Cash ",
                null,
                false,
                true,
                true,
                12,
                101),
            CancellationToken.None);
        Assert.Equal("Renamed Cash", updated.AccountName);

        Assert.True(await _store.DeactivateAsync(17, 101, created.Id, CancellationToken.None));
        Assert.False(await _store.DeactivateAsync(17, 101, created.Id, CancellationToken.None));

        _db.ChangeTracker.Clear();
        var persisted = await _db.ChartAccounts.SingleAsync(account => account.Id == created.Id);
        Assert.Equal("Renamed Cash", persisted.AccountName);
        Assert.False(persisted.IsActive);
        Assert.Equal(12, persisted.DisplayOrder);
    }

    [Fact]
    public async Task Create_WhenRelationalInsertFails_PersistsNoAccount()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_chart_account_insert BEFORE INSERT ON chart_accounts " +
            "BEGIN SELECT RAISE(ABORT, 'forced chart account failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(
            () => CreateAccountAsync("1300", "Rejected"));

        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.ChartAccounts.Where(account => account.BusinessId == 17).ToListAsync());
    }

    [Fact]
    public async Task Deactivate_RejectsActiveParentAndAllowsChildAfterItIsInactive()
    {
        var parent = await CreateAccountAsync("1400", "Parent");
        var child = await _store.CreateAsync(
            17,
            101,
            new CreateChartAccountCommand(
                "1401",
                "Child",
                ChartAccountTypes.Asset,
                NormalBalanceTypes.Debit,
                parent.Id,
                false,
                true,
                true,
                2,
                101),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.DeactivateAsync(17, 101, parent.Id, CancellationToken.None));
        Assert.Equal("Account cannot be deactivated while it has active child accounts.", error.Message);
        Assert.True(await _store.DeactivateAsync(17, 101, child.Id, CancellationToken.None));
        Assert.True(await _store.DeactivateAsync(17, 101, parent.Id, CancellationToken.None));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<RetailPOS.Application.ChartAccounts.ChartAccountReadModel> CreateAccountAsync(
        string code,
        string name) =>
        _store.CreateAsync(
            17,
            101,
            new CreateChartAccountCommand(
                code,
                name,
                ChartAccountTypes.Asset,
                NormalBalanceTypes.Debit,
                null,
                false,
                true,
                true,
                1,
                101),
            CancellationToken.None);
}
