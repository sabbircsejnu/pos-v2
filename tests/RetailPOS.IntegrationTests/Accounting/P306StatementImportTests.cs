using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Application.BankStatements.Abstractions;
using RetailPOS.Application.BankStatements.ReadModels;
using RetailPOS.Application.BankStatements.Parsing;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BankStatements;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306StatementImportTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IBankStatementImportLocationAuthorizer> _locations = new();

    public P306StatementImportTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        _db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        _db.ChartAccounts.AddRange(Account(101, 1), Account(201, 2));
        _db.BankAccounts.AddRange(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "One Bank", IsActive = true }, new BankAccount { Id = 502, BusinessId = 2, ChartAccountId = 201, DisplayName = "Two Bank", IsActive = true }, new BankAccount { Id = 503, BusinessId = 1, ChartAccountId = 101, DisplayName = "Inactive", IsActive = false });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ValidCsv_PreservesEvidenceAndNormalizesDebitCredit()
    {
        var service = CreateService();
        var csv = "transaction_date,value_date,external_reference,description,debit,credit,running_balance\n2026-09-01,20260902,EXT-1,Card settlement,12.50,,100.00\n2026-09-02,,EXT-2,Customer receipt,,20.00,107.50\n";
        var result = await Import(service, "statement.csv", csv);
        var lines = await _db.BankStatementLines.OrderBy(x => x.SourceRowNumber).ToListAsync();
        Assert.Equal(BankStatementImportStatuses.Imported, result.Status);
        Assert.Equal(2, result.AcceptedRowCount);
        Assert.Equal(12.50m, lines[0].DebitAmount);
        Assert.Equal(-12.50m, lines[0].SignedAmount);
        Assert.Equal(20m, lines[1].CreditAmount);
        Assert.Equal(20m, lines[1].SignedAmount);
        Assert.Equal(new DateTime(2026, 9, 2), lines[0].ValueDate);
        Assert.Equal("EXT-1", lines[0].ExternalTransactionReference);
        Assert.Equal("Card settlement", lines[0].Description);
        Assert.Equal(100m, lines[0].RunningBalance);
    }

    [Fact]
    public async Task MissingBalances_AreNotFabricated_AndDuplicateContentIsIdempotentAcrossFilenames()
    {
        var service = CreateService();
        var csv = "transaction_date,description,debit,credit\n2026-09-01,No balance,,10.00\n";
        var first = await Import(service, "first.csv", csv);
        var retry = await Import(service, "renamed.csv", csv);
        Assert.Equal(first.Id, retry.Id);
        Assert.Null(first.OpeningBalance);
        Assert.Null(first.ClosingBalance);
        Assert.Single(await _db.BankStatementImports.ToListAsync());
        Assert.Single(await _db.BankStatementLines.ToListAsync());
    }

    [Fact]
    public async Task SameFilenameDifferentContentCreatesDistinctImport()
    {
        var service = CreateService();
        await Import(service, "same.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,10\n");
        await Import(service, "same.csv", "transaction_date,description,debit,credit\n2026-09-02,B,,12\n");
        Assert.Equal(2, await _db.BankStatementImports.CountAsync());
    }

    [Theory]
    [InlineData("transaction_date,description,debit,credit\nnot-a-date,A,,1\n")]
    [InlineData("transaction_date,description,debit,credit\n2026-09-01,A,bad,\n")]
    [InlineData("wrong,headers\n2026-09-01,A\n")]
    public async Task MalformedCsv_IsRejectedWithoutEvidence(string csv)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Import(service, "bad.csv", csv));
        Assert.Empty(await _db.BankStatementImports.ToListAsync());
        Assert.Empty(await _db.BankStatementLines.ToListAsync());
    }

    [Fact]
    public async Task CrossBusinessAndInactiveBankAccountsAreRejected()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Import(service, "cross.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,1\n", 502));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Import(service, "inactive.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,1\n", 503));
    }

    [Fact]
    public async Task StatementLinesAreImmutable()
    {
        var service = CreateService();
        await Import(service, "immutable.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,1\n");
        var line = await _db.BankStatementLines.SingleAsync();
        line.Description = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    private BankStatementImportCommandStore CreateService() => new(_db, _locations.Object, new BankStatementCsvParser());
    private async Task<BankStatementImportReadModel> Import(BankStatementImportCommandStore service, string name, string csv, long account = 501) =>
        await service.ImportCsvAsync(1, 9, account, name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), CancellationToken.None);
    private static ChartAccount Account(long id, long businessId) => new() { Id = id, BusinessId = businessId, AccountCode = id.ToString(), AccountName = $"Bank {id}", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}

public sealed class P306StatementImportRelationalAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FailOnStatementLineInterceptor _interceptor = new();

    public P306StatementImportRelationalAtomicityTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;"; command.ExecuteNonQuery();
        using var db = CreateContext(); db.Database.EnsureCreated(); db.Businesses.Add(new Business { Id = 1, Name = "Atomic" }); db.ChartAccounts.Add(new ChartAccount { Id = 101, BusinessId = 1, AccountCode = "101", AccountName = "Bank", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true }); db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank" }); db.SaveChanges();
    }

    [Fact]
    public async Task PartialLineFailureRollsBackHeaderAndLines_ThenRetryIsExact()
    {
        _interceptor.Enabled = true;
        await using (var failed = CreateContext()) await Assert.ThrowsAsync<InvalidOperationException>(() => Import(CreateService(failed), "retry.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,1\n2026-09-02,B,2,\n"));
        await using (var verify = CreateContext()) { Assert.Empty(await verify.BankStatementImports.ToListAsync()); Assert.Empty(await verify.BankStatementLines.ToListAsync()); }
        _interceptor.Enabled = false;
        await using (var retry = CreateContext()) { var result = await Import(CreateService(retry), "renamed.csv", "transaction_date,description,debit,credit\n2026-09-01,A,,1\n2026-09-02,B,2,\n"); Assert.Equal(BankStatementImportStatuses.Imported, result.Status); }
        await using var final = CreateContext(); Assert.Single(await final.BankStatementImports.ToListAsync()); Assert.Equal(2, await final.BankStatementLines.CountAsync());
    }

    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
    private BankStatementImportCommandStore CreateService(RetailPOSDbContext db) =>
        new(db, Mock.Of<IBankStatementImportLocationAuthorizer>(), new BankStatementCsvParser());
    private static Task<BankStatementImportReadModel> Import(
        BankStatementImportCommandStore store,
        string fileName,
        string csv) =>
        store.ImportCsvAsync(
            1,
            9,
            501,
            fileName,
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)),
            CancellationToken.None);

    public void Dispose() => _connection.Dispose();
    private sealed class FailOnStatementLineInterceptor : SaveChangesInterceptor { public bool Enabled { get; set; } public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { if (Enabled && eventData.Context?.ChangeTracker.Entries<BankStatementLine>().Any(x => x.State == EntityState.Added) == true) throw new InvalidOperationException("Injected statement line failure."); return result; } public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new(SavingChanges(eventData, result)); }
}
