using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Outlets;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Outlets;

namespace RetailPOS.Tests.Outlets;

public sealed class OutletPosSetupStoreTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly OutletWriteStore _store;
    private readonly OutletReadStore _reader;

    public OutletPosSetupStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        _db.SaveChanges();
        _store = new OutletWriteStore(_db, Mock.Of<IOperationalLocationSourceEventWriter>(), NullLogger<OutletWriteStore>.Instance);
        _reader = new OutletReadStore(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OutletPosSetupInput Setup(
        string? prefix = null, string? zone = null, IReadOnlyList<OutletTerminalInput>? terminals = null, bool canManage = true) =>
        new(prefix, zone, terminals, canManage);

    private Task<OutletReadModel> CreateAsync(string name, long businessId, OutletPosSetupInput setup) =>
        _store.CreateAsync(name, "Address", null, null, businessId, setup, CancellationToken.None);

    private static async Task<OutletSetupException> RefusedAsync(Task task) => await Assert.ThrowsAsync<OutletSetupException>(() => task);

    [Fact]
    public async Task Create_PersistsPrefixTimeZoneAndTerminalsTogether()
    {
        var outlet = await CreateAsync("Main", 1, Setup(" dhk01 ", "Asia/Dhaka", [new OutletTerminalInput(null, "Counter 1"), new OutletTerminalInput(null, "Counter 2")]));

        Assert.Equal("DHK01", outlet.InvoicePrefix);
        Assert.Equal("Asia/Dhaka", outlet.TimeZoneId);
        Assert.False(outlet.InvoicePrefixLocked);
        Assert.Equal(["Counter 1", "Counter 2"], outlet.Terminals!.Select(t => t.Name));
        Assert.Equal(["T01", "T02"], outlet.Terminals!.Select(t => t.Code));
        Assert.All(outlet.Terminals!, t => Assert.True(t.IsActive));
    }

    [Fact]
    public async Task Create_AllowsAnOutletWithoutTerminalsOrPrefix()
    {
        var outlet = await CreateAsync("Bare", 1, OutletPosSetupInput.None);

        Assert.Null(outlet.InvoicePrefix);
        Assert.Empty(outlet.Terminals!);
        Assert.Equal(0, await _db.PosTerminals.CountAsync());
    }

    [Fact]
    public async Task InvoicePrefix_IsUniquePerBusinessAndRejectsAmbiguousExtensions()
    {
        await CreateAsync("Main", 1, Setup("DHK"));

        Assert.Equal(OutletSetupErrorCodes.DuplicateInvoicePrefix, (await RefusedAsync(CreateAsync("Dup", 1, Setup("dhk")))).Code);
        Assert.Equal(OutletSetupErrorCodes.InvoicePrefixAmbiguous, (await RefusedAsync(CreateAsync("Ext", 1, Setup("DHK1")))).Code);
        Assert.Equal(OutletSetupErrorCodes.InvalidInvoicePrefix, (await RefusedAsync(CreateAsync("Bad", 1, Setup("9X")))).Code);

        // Another Business may reuse the prefix, and a letter extension cannot collide.
        Assert.Equal("DHK", (await CreateAsync("Other business", 2, Setup("DHK"))).InvoicePrefix);
        Assert.Equal("DHKA", (await CreateAsync("Letter ext", 1, Setup("DHKA"))).InvoicePrefix);
        Assert.Equal(3, await _db.Outlets.CountAsync());
    }

    [Fact]
    public async Task InvoicePrefix_IsEditableUntilTheFirstInvoiceThenLocked()
    {
        var outlet = await CreateAsync("Main", 1, Setup("AAA"));
        var renamed = await _store.UpdateAsync(outlet.Id, "Main", "Address", null, null, 1, Setup("BBB"), CancellationToken.None);
        Assert.Equal("BBB", renamed.InvoicePrefix);

        _db.SaleInvoiceCounters.Add(new SaleInvoiceCounter { BusinessId = 1, OutletId = outlet.Id, BusinessDate = new DateTime(2026, 11, 2), LastSequence = 1 });
        await _db.SaveChangesAsync();

        var locked = await _reader.GetByIdAsync(outlet.Id, 1, CancellationToken.None);
        Assert.True(locked!.InvoicePrefixLocked);
        var refused = await RefusedAsync(_store.UpdateAsync(outlet.Id, "Main", "Address", null, null, 1, Setup("CCC"), CancellationToken.None));
        Assert.Equal(OutletSetupErrorCodes.InvoicePrefixLocked, refused.Code);

        var same = await _store.UpdateAsync(outlet.Id, "Main 2", "Address", null, null, 1, Setup("BBB"), CancellationToken.None);
        Assert.Equal("BBB", same.InvoicePrefix);
    }

    [Fact]
    public async Task Terminals_RequireThePermissionAndHaveUniqueNames()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateAsync("Main", 1, Setup(terminals: [new OutletTerminalInput(null, "Counter 1")], canManage: false)));
        Assert.Equal(0, await _db.Outlets.CountAsync());

        var duplicate = await RefusedAsync(CreateAsync("Main", 1, Setup(terminals: [new OutletTerminalInput(null, "Counter 1"), new OutletTerminalInput(null, " counter 1 ")])));
        Assert.Equal(OutletSetupErrorCodes.DuplicateTerminalName, duplicate.Code);
        Assert.Equal(0, await _db.Outlets.CountAsync());
    }

    [Fact]
    public async Task Update_EditsRenamesAndAddsTerminalsWithoutDeletingTheOnesOmitted()
    {
        var outlet = await CreateAsync("Main", 1, Setup(terminals: [new OutletTerminalInput(null, "Counter 1"), new OutletTerminalInput(null, "Counter 2")]));
        var first = outlet.Terminals![0];

        var updated = await _store.UpdateAsync(
            outlet.Id, "Main", "Address", null, null, 1,
            Setup(terminals: [new OutletTerminalInput(first.Id, "Front desk"), new OutletTerminalInput(null, "Counter 3")]),
            CancellationToken.None);

        Assert.Equal(["Front desk", "Counter 2", "Counter 3"], updated.Terminals!.Select(t => t.Name));

        var untouched = await _store.UpdateAsync(outlet.Id, "Main", "Address", null, null, 1, OutletPosSetupInput.None, CancellationToken.None);
        Assert.Equal(3, untouched.Terminals!.Count);

        var foreign = await RefusedAsync(_store.UpdateAsync(
            outlet.Id, "Main", "Address", null, null, 1, Setup(terminals: [new OutletTerminalInput(99999, "Ghost")]), CancellationToken.None));
        Assert.Equal(OutletSetupErrorCodes.InvalidTerminal, foreign.Code);

        var clash = await RefusedAsync(_store.UpdateAsync(
            outlet.Id, "Main", "Address", null, null, 1, Setup(terminals: [new OutletTerminalInput(first.Id, "counter 2")]), CancellationToken.None));
        Assert.Equal(OutletSetupErrorCodes.DuplicateTerminalName, clash.Code);
    }

    [Fact]
    public async Task Deactivation_IsBlockedWhileACashSessionIsOpenAndAllowedOtherwise()
    {
        var outlet = await CreateAsync("Main", 1, Setup(terminals: [new OutletTerminalInput(null, "Counter 1"), new OutletTerminalInput(null, "Counter 2")]));
        var busy = outlet.Terminals![0];
        var idle = outlet.Terminals[1];
        _db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
        _db.CashSessions.Add(new CashSession
        {
            OutletId = outlet.Id, BusinessId = 1, TerminalId = busy.Id, BusinessDate = DateTime.UtcNow.Date,
            OpenedAt = DateTime.UtcNow, Status = "open", OpenedByUserId = 5
        });
        await _db.SaveChangesAsync();

        var blocked = await RefusedAsync(_store.UpdateAsync(
            outlet.Id, "Main", "Address", null, null, 1, Setup(terminals: [new OutletTerminalInput(busy.Id, busy.Name, false)]), CancellationToken.None));
        Assert.Equal(OutletSetupErrorCodes.TerminalHasOpenSession, blocked.Code);

        var read = await _reader.GetByIdAsync(outlet.Id, 1, CancellationToken.None);
        Assert.True(read!.Terminals!.Single(t => t.Id == busy.Id).HasOpenSession);
        Assert.True(read.Terminals!.Single(t => t.Id == busy.Id).HasHistory);
        Assert.False(read.Terminals!.Single(t => t.Id == idle.Id).HasHistory);

        var allowed = await _store.UpdateAsync(
            outlet.Id, "Main", "Address", null, null, 1, Setup(terminals: [new OutletTerminalInput(idle.Id, idle.Name, false)]), CancellationToken.None);
        Assert.False(allowed.Terminals!.Single(t => t.Id == idle.Id).IsActive);
    }

    [Fact]
    public async Task DeleteTerminal_ProtectsHistoryAndRemovesUnusedTerminals()
    {
        var outlet = await CreateAsync("Main", 1, Setup(terminals: [new OutletTerminalInput(null, "Used"), new OutletTerminalInput(null, "Unused")]));
        var used = outlet.Terminals![0];
        var unused = outlet.Terminals[1];
        _db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
        _db.CashSessions.Add(new CashSession
        {
            OutletId = outlet.Id, BusinessId = 1, TerminalId = used.Id, BusinessDate = DateTime.UtcNow.Date,
            OpenedAt = DateTime.UtcNow, Status = "closed", OpenedByUserId = 5
        });
        await _db.SaveChangesAsync();

        var refused = await RefusedAsync(_store.DeleteTerminalAsync(outlet.Id, used.Id, 1, CancellationToken.None));
        Assert.Equal(OutletSetupErrorCodes.TerminalHasHistory, refused.Code);

        await _store.DeleteTerminalAsync(outlet.Id, unused.Id, 1, CancellationToken.None);
        Assert.Equal([used.Id], await _db.PosTerminals.Select(t => t.Id).ToListAsync());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _store.DeleteTerminalAsync(outlet.Id, unused.Id, 1, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _store.DeleteTerminalAsync(outlet.Id, used.Id, 2, CancellationToken.None));
    }

    [Fact]
    public async Task TimeZone_OverrideCanBeSetValidatedAndCleared()
    {
        var outlet = await CreateAsync("Main", 1, Setup(zone: "Asia/Dhaka"));
        Assert.Equal("Asia/Dhaka", outlet.TimeZoneId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(outlet.Id, "Main", "Address", null, null, 1, Setup(zone: "Nowhere/Land"), CancellationToken.None));

        var cleared = await _store.UpdateAsync(outlet.Id, "Main", "Address", null, null, 1, Setup(zone: ""), CancellationToken.None);
        Assert.Null(cleared.TimeZoneId);
    }
}
