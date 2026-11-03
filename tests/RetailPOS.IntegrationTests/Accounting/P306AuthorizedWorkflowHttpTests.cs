using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P306AuthorizedWorkflowHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    public P306AuthorizedWorkflowHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AuthorizedCloseReopenAdjustmentAndExceptionWorkflowsSucceedAndTenantTwoCannotAct()
    {
        await SeedAsync();
        await SeedMatchAsync();
        using var client = Client(1, "accounting.reconciliation.view,accounting.reconciliation.close,accounting.reconciliation.reopen,accounting.adjustments.propose,accounting.adjustments.approve,accounting.adjustments.reverse,accounting.exceptions.manage");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounting/reconciliation/sessions/701/close", new { reason = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounting/reconciliation/sessions/701/reopen", new { reason = "correction" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounting/reconciliation/adjustments", new { bankStatementLineId = 1001, adjustmentType = "bank_fee", amount = 1, reason = "fee", idempotencyKey = "http-fee" })).StatusCode);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(); var adjustmentId = db.BankStatementAdjustments.Single(x => x.IdempotencyKey == "http-fee").Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/accounting/reconciliation/adjustments/{adjustmentId}/approve?postingDate=2026-09-10", new { reason = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/accounting/reconciliation/adjustments/{adjustmentId}/reverse", new { reason = "reverse", postingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounting/reconciliation/exceptions?statementLineId=1001", new { classification = "chargeback", reason = "review" })).StatusCode);
        var exceptionId = db.BankStatementExceptions.Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/accounting/reconciliation/exceptions/{exceptionId}/resolve", new { resolutionNotes = "resolved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/accounting/reconciliation/exceptions/{exceptionId}/resolve", new { resolutionNotes = "ignored retry" })).StatusCode);
        var bankAccounts = await client.GetStringAsync("/api/accounting/reconciliation/bank-accounts");
        var matches = await client.GetStringAsync("/api/accounting/reconciliation/matches?sessionId=701");
        var adjustments = await client.GetStringAsync("/api/accounting/reconciliation/adjustments?bankAccountId=501");
        var exceptions = await client.GetStringAsync("/api/accounting/reconciliation/exceptions?bankAccountId=501");
        Assert.Contains("Bank", bankAccounts, StringComparison.Ordinal);
        Assert.Contains("9001", matches, StringComparison.Ordinal);
        Assert.Contains(adjustmentId.ToString(), adjustments, StringComparison.Ordinal);
        Assert.Contains("chargeback", exceptions, StringComparison.Ordinal);

        using var other = Client(2, "accounting.reconciliation.view,accounting.adjustments.approve,accounting.adjustments.reverse,accounting.exceptions.manage");
        Assert.DoesNotContain("Bank", await other.GetStringAsync("/api/accounting/reconciliation/bank-accounts"), StringComparison.Ordinal);
        Assert.DoesNotContain("9001", await other.GetStringAsync("/api/accounting/reconciliation/matches?sessionId=701"), StringComparison.Ordinal);
        Assert.DoesNotContain(adjustmentId.ToString(), await other.GetStringAsync("/api/accounting/reconciliation/adjustments?bankAccountId=501"), StringComparison.Ordinal);
        Assert.DoesNotContain("chargeback", await other.GetStringAsync("/api/accounting/reconciliation/exceptions?bankAccountId=501"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/accounting/reconciliation/exceptions?statementLineId=1001", new { classification = "chargeback", reason = "cross business" })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await other.PostAsJsonAsync($"/api/accounting/reconciliation/adjustments/{adjustmentId}/reverse", new { reason = "leak", postingDate = "2026-10-01" })).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await other.PostAsJsonAsync($"/api/accounting/reconciliation/exceptions/{exceptionId}/resolve", new { resolutionNotes = "leak" })).StatusCode);
    }

    private async Task SeedMatchAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.BankReconciliationMatches.Add(new BankReconciliationMatch
        {
            Id = 9001,
            BusinessId = 1,
            BankAccountId = 501,
            BankReconciliationSessionId = 701,
            CreatedBy = 201
        });
        await db.SaveChangesAsync();
    }

    private HttpClient Client(long businessId, string permissions) { var c = _factory.CreateClient(); c.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString()); c.DefaultRequestHeaders.Add("X-Test-UserId", "201"); c.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin"); c.DefaultRequestHeaders.Add("X-Test-Permissions", permissions); return c; }
    private async Task SeedAsync() { using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(); await db.Database.EnsureDeletedAsync(); await db.Database.EnsureCreatedAsync(); db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" }); db.ChartAccounts.AddRange(Account(101, ChartAccountTypes.Asset), Account(601, ChartAccountTypes.Expense)); db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank" }); db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, BankFeeExpenseAccountId = 601, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear }); db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true }); db.AccountingPeriods.AddRange(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 9, Name = "Sep", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30), Status = AccountingPeriodStatuses.Open }, new AccountingPeriod { Id = 2, FinancialYearId = 1, PeriodNumber = 10, Name = "Oct", StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31), Status = AccountingPeriodStatuses.Open }); db.BankReconciliationSessions.Add(new BankReconciliationSession { Id = 701, BusinessId = 1, BankAccountId = 501, PeriodStart = new DateTime(2026, 8, 1), PeriodEnd = new DateTime(2026, 8, 31), StatementOpeningBalance = 100, StatementClosingBalance = 100, GlOpeningBalance = 100, Status = BankReconciliationSessionStatuses.ReadyForClose }); db.BankStatementImports.Add(new BankStatementImport { Id = 100, BusinessId = 1, BankAccountId = 501, SourceFileName = "s", SourceFileChecksum = "s", Status = BankStatementImportStatuses.Imported }); db.BankStatementLines.Add(new BankStatementLine { Id = 1001, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 1, TransactionDate = new DateTime(2026, 9, 10), Description = "fee", SignedAmount = -1, DebitAmount = 1 }); await db.SaveChangesAsync(); }
    private static ChartAccount Account(long id, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = "Account " + id, AccountType = type, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
