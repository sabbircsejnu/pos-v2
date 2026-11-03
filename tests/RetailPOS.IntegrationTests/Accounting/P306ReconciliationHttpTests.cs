using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P306ReconciliationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    public P306ReconciliationHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ReconciliationEndpointsRequirePermissionsAndDoNotLeakTenants()
    {
        await SeedAsync();
        using var missing = Client(1, "accounting.statement_import.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/sessions", new { bankAccountId = 501 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/sessions/700/statement-candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/sessions/700/gl-candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/sessions/700/calculation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/sessions/700/close", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/sessions/700/reopen", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/bank-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/matches?sessionId=700")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/matches", new { sessionId = 700, statementLineIds = new[] { 1001 }, journalEntryLineIds = new[] { 2001 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/matches/1/unmatch", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/adjustments?bankAccountId=501")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.GetAsync("/api/accounting/reconciliation/exceptions?bankAccountId=501")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/adjustments", new { bankStatementLineId = 1001, adjustmentType = "bank_fee", amount = 1, reason = "x", idempotencyKey = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/adjustments/900/approve", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/adjustments/900/reverse", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.PostAsJsonAsync("/api/accounting/reconciliation/exceptions?statementLineId=1001", new { classification = "chargeback", reason = "x" })).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.ChartAccounts.Add(new ChartAccount { Id = 201, BusinessId = 2, AccountCode = "201", AccountName = "Tenant Two Bank", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true });
            db.BankAccounts.AddRange(
                new BankAccount { Id = 502, BusinessId = 2, ChartAccountId = 201, DisplayName = "Tenant Two Bank" },
                new BankAccount { Id = 503, BusinessId = 1, ChartAccountId = 101, DisplayName = "Second One Bank" });
            var shorterPeriod = await db.BankReconciliationSessions.SingleAsync(
                session => session.Id == 701);
            shorterPeriod.PeriodEnd = new DateTime(2026, 9, 15);
            db.BankReconciliationSessions.AddRange(
                new BankReconciliationSession
                {
                    Id = 702,
                    BusinessId = 1,
                    BankAccountId = 503,
                    PeriodStart = new DateTime(2026, 10, 1),
                    PeriodEnd = new DateTime(2026, 10, 31),
                    StatementOpeningBalance = 100,
                    StatementClosingBalance = 100,
                    GlOpeningBalance = 100,
                    Status = BankReconciliationSessionStatuses.Open
                },
                new BankReconciliationSession
                {
                    Id = 800,
                    BusinessId = 2,
                    BankAccountId = 502,
                    PeriodStart = new DateTime(2026, 10, 1),
                    PeriodEnd = new DateTime(2026, 10, 31),
                    Status = BankReconciliationSessionStatuses.Open
                });
            db.JournalEntries.Add(new JournalEntry
            {
                Id = 200,
                BusinessId = 1,
                JournalNumber = "JR-HTTP",
                JournalType = "Sale",
                ReferenceType = "Sale",
                ReferenceId = "1",
                Status = JournalEntryStatuses.Posted,
                PostingDate = new DateTime(2026, 9, 10),
                TransactionDate = new DateTime(2026, 9, 10),
                BusinessDate = new DateTime(2026, 9, 10),
                Lines =
                [
                    new JournalEntryLine
                    {
                        Id = 2001,
                        AccountId = 101,
                        DebitAmount = 1,
                        Description = "statement match"
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        using var tenantOne = Client(1, "accounting.reconciliation.view");
        Assert.Equal(HttpStatusCode.NotFound, (await tenantOne.GetAsync("/api/accounting/reconciliation/sessions/800/calculation")).StatusCode);
        var sessions = await tenantOne.GetStringAsync(
            "/api/accounting/reconciliation/sessions");
        Assert.DoesNotContain("\"id\":800", sessions, StringComparison.Ordinal);
        Assert.True(
            sessions.IndexOf("\"id\":702", StringComparison.Ordinal)
                < sessions.IndexOf("\"id\":700", StringComparison.Ordinal));
        Assert.True(
            sessions.IndexOf("\"id\":700", StringComparison.Ordinal)
                < sessions.IndexOf("\"id\":701", StringComparison.Ordinal));
        var accountSessions = await tenantOne.GetStringAsync(
            "/api/accounting/reconciliation/sessions?bankAccountId=501");
        Assert.Contains("\"id\":700", accountSessions, StringComparison.Ordinal);
        Assert.Contains("\"id\":701", accountSessions, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":702", accountSessions, StringComparison.Ordinal);
        var statementCandidates = await tenantOne.GetAsync(
            "/api/accounting/reconciliation/sessions/700/statement-candidates");
        var statementCandidateBody = await statementCandidates.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, statementCandidates.StatusCode);
        Assert.Contains("\"bankStatementLineId\":1001", statementCandidateBody, StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.OK,
            (await tenantOne.GetAsync(
                "/api/accounting/reconciliation/sessions/700/gl-candidates")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await tenantOne.GetAsync(
                "/api/accounting/reconciliation/sessions/800/statement-candidates")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await tenantOne.GetAsync(
                "/api/accounting/reconciliation/sessions/800/gl-candidates")).StatusCode);
        var calculationResponse = await tenantOne.GetAsync(
            "/api/accounting/reconciliation/sessions/700/calculation");
        var calculationBody = await calculationResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, calculationResponse.StatusCode);
        Assert.Contains("\"statementMovement\":1", calculationBody, StringComparison.Ordinal);
        Assert.Contains("\"glMovement\":1", calculationBody, StringComparison.Ordinal);
        Assert.Contains("\"reconciliationDifference\":0", calculationBody, StringComparison.Ordinal);
        using var proposalClient = Client(1, "accounting.adjustments.propose");
        var proposalResponse = await proposalClient.PostAsJsonAsync(
            "/api/accounting/reconciliation/adjustments",
            new
            {
                bankStatementLineId = 1001,
                adjustmentType = BankStatementAdjustmentTypes.BankFee,
                amount = 1,
                reason = " fee ",
                idempotencyKey = " http-adjustment "
            });
        var proposalBody = await proposalResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, proposalResponse.StatusCode);
        Assert.Contains("\"status\":\"proposed\"", proposalBody, StringComparison.Ordinal);
        var proposalRetryResponse = await proposalClient.PostAsJsonAsync(
            "/api/accounting/reconciliation/adjustments",
            new
            {
                bankStatementLineId = 9999,
                adjustmentType = "unsupported",
                amount = 999,
                reason = "different retry",
                idempotencyKey = "http-adjustment"
            });
        Assert.Equal(HttpStatusCode.OK, proposalRetryResponse.StatusCode);
        Assert.Contains("\"status\":\"proposed\"", await proposalRetryResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var adjustment = await db.BankStatementAdjustments.SingleAsync();
            Assert.Equal(201, adjustment.ProposedBy);
            Assert.Equal("fee", adjustment.Reason);
            Assert.Equal("http-adjustment", adjustment.IdempotencyKey);
        }
        using var closingClient = Client(1, "accounting.reconciliation.close");
        var prepareResponse = await closingClient.PostAsync(
            "/api/accounting/reconciliation/sessions/702/prepare-close",
            content: null);
        var prepareBody = await prepareResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        Assert.Contains("\"status\":\"ready_for_close\"", prepareBody, StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await closingClient.PostAsync(
                "/api/accounting/reconciliation/sessions/800/prepare-close",
                content: null)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var session = await db.BankReconciliationSessions.SingleAsync(row => row.Id == 702);
            Assert.Equal(201, session.ReconciledBy);
            Assert.Equal(201, session.UpdatedBy);
            Assert.NotNull(session.ReconciledAt);
        }
            var closeResponse = await closingClient.PostAsJsonAsync(
                "/api/accounting/reconciliation/sessions/702/close",
                new { reason = "Finance approved" });
            var closeBody = await closeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
            Assert.Contains("\"status\":\"closed\"", closeBody, StringComparison.Ordinal);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await closingClient.PostAsJsonAsync(
                    "/api/accounting/reconciliation/sessions/800/close",
                    new { reason = "cross-tenant" })).StatusCode);
            var closeRetryResponse = await closingClient.PostAsJsonAsync(
                "/api/accounting/reconciliation/sessions/702/close",
                new { reason = "retry" });
            Assert.Equal(HttpStatusCode.OK, closeRetryResponse.StatusCode);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var session = await db.BankReconciliationSessions.SingleAsync(row => row.Id == 702);
                Assert.Equal(201, session.ClosedBy);
                Assert.Equal("Finance approved", session.CloseResultReference);
                Assert.Equal(0m, session.ClosedDifference);
            }
            using var reopeningClient = Client(1, "accounting.reconciliation.reopen");
            var reopenResponse = await reopeningClient.PostAsJsonAsync(
                "/api/accounting/reconciliation/sessions/702/reopen",
                new { reason = "Correction required" });
            var reopenBody = await reopenResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
            Assert.Contains("\"status\":\"in_progress\"", reopenBody, StringComparison.Ordinal);
            var reopenRetryResponse = await reopeningClient.PostAsJsonAsync(
                "/api/accounting/reconciliation/sessions/702/reopen",
                new { reason = "different retry" });
            Assert.Equal(HttpStatusCode.OK, reopenRetryResponse.StatusCode);
            Assert.Contains("\"status\":\"in_progress\"", await reopenRetryResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var session = await db.BankReconciliationSessions.SingleAsync(row => row.Id == 702);
                Assert.Equal(201, session.ReopenedBy);
                Assert.Equal("Correction required", session.ReopenReason);
            }
            using var matchingClient = Client(1, "accounting.reconciliation.match");
        var matchResponse = await matchingClient.PostAsJsonAsync(
            "/api/accounting/reconciliation/matches",
            new
            {
                sessionId = 700,
                statementLineIds = new[] { 1001 },
                journalEntryLineIds = new[] { 2001 }
            });
        var matchBody = await matchResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, matchResponse.StatusCode);
        Assert.Contains("\"status\":\"effective\"", matchBody, StringComparison.Ordinal);
        Assert.Contains("\"statementTotal\":1", matchBody, StringComparison.Ordinal);
        long matchId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var match = await db.BankReconciliationMatches
                .Include(row => row.Lines)
                .SingleAsync(row => row.BankReconciliationSessionId == 700);
            matchId = match.Id;
            Assert.Equal(201, match.CreatedBy);
            Assert.Equal(2, match.Lines.Count);
        }
        using var unmatchingClient = Client(1, "accounting.reconciliation.unmatch");
        var unmatchResponse = await unmatchingClient.PostAsJsonAsync(
            $"/api/accounting/reconciliation/matches/{matchId}/unmatch",
            new { reason = "reviewed" });
        var unmatchBody = await unmatchResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, unmatchResponse.StatusCode);
        Assert.Contains("\"status\":\"unmatched\"", unmatchBody, StringComparison.Ordinal);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var match = await db.BankReconciliationMatches
                .Include(row => row.Lines)
                .SingleAsync(row => row.Id == matchId);
            Assert.Equal(201, match.UnmatchedBy);
            Assert.Equal("reviewed", match.UnmatchReason);
            Assert.All(match.Lines, line => Assert.False(line.IsActive));
        }
    }

    [Fact]
    public async Task SessionCreationUsesTheScopedCommandAndReturnsTheExistingContract()
    {
        await SeedAsync();
        using var client = Client(1, "accounting.reconciliation.create");
        var response = await client.PostAsJsonAsync(
            "/api/accounting/reconciliation/sessions",
            new
            {
                bankAccountId = 501,
                periodStart = new DateTime(2026, 8, 1),
                periodEnd = new DateTime(2026, 8, 31)
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"open\"", body, StringComparison.Ordinal);
        Assert.Contains("\"statementOpeningBalance\":null", body, StringComparison.Ordinal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var session = await db.BankReconciliationSessions.SingleAsync(
            row => row.BusinessId == 1
                && row.BankAccountId == 501
                && row.PeriodStart == new DateTime(2026, 8, 1));
        Assert.Equal(201, session.CreatedBy);
        Assert.Equal(201, session.PreparedBy);
    }

    private HttpClient Client(long businessId, string permissions) { var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString()); client.DefaultRequestHeaders.Add("X-Test-UserId", "201"); client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin"); client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions); return client; }
    private async Task SeedAsync() { using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>(); await db.Database.EnsureDeletedAsync(); await db.Database.EnsureCreatedAsync(); db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" }); db.ChartAccounts.AddRange(new ChartAccount { Id = 101, BusinessId = 1, AccountCode = "101", AccountName = "Bank", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true }, new ChartAccount { Id = 601, BusinessId = 1, AccountCode = "601", AccountName = "Fee", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true }, new ChartAccount { Id = 602, BusinessId = 1, AccountCode = "602", AccountName = "Interest", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsActive = true, AllowManualPosting = true }); db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank" }); db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, BankFeeExpenseAccountId = 601, BankInterestIncomeAccountId = 602, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear }); db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true }); db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 9, Name = "Sep", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30), Status = AccountingPeriodStatuses.Open }); db.BankReconciliationSessions.AddRange(new BankReconciliationSession { Id = 700, BusinessId = 1, BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), Status = BankReconciliationSessionStatuses.Open }, new BankReconciliationSession { Id = 701, BusinessId = 1, BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), StatementOpeningBalance = 100, StatementClosingBalance = 100, GlOpeningBalance = 100, Status = BankReconciliationSessionStatuses.ReadyForClose }); db.BankStatementImports.Add(new BankStatementImport { Id = 100, BusinessId = 1, BankAccountId = 501, SourceFileName = "s", SourceFileChecksum = "s", Status = BankStatementImportStatuses.Imported }); db.BankStatementLines.Add(new BankStatementLine { Id = 1001, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 1, TransactionDate = new DateTime(2026, 9, 10), Description = "x", SignedAmount = 1, CreditAmount = 1 }); await db.SaveChangesAsync(); }
}
