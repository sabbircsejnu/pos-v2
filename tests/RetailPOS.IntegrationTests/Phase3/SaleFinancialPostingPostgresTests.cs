using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.IntegrationTests.Infrastructure;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>
/// Phase 3.1 (D10 asynchronous sale GL and the scheduled variance worker) on a real PostgreSQL schema built from the
/// migration chain on the isolated Test stack. Sales, voids, closes and reviews run through their production handlers;
/// postings run through the production DI container exactly as the background workers do: no HTTP user, the Business
/// taken from the posting itself. Nothing transactional is mocked; the only test double is a switch in front of the
/// real journal poster that simulates an accounting outage or holds a worker inside its posting transaction.
/// </summary>
public sealed partial class SaleFinancialPostingPostgresTests
{
    private const long OutletId = 5001;
    private const long Terminal1 = 8001;
    private const long Cashier = 7001;
    private const long Manager = 7002;
    private const long Reviewer = 7003;

    private const long OtherOutletId = 5002;
    private const long OtherTerminal = 8003;
    private const long OtherCashier = 7004;

    static SaleFinancialPostingPostgresTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    // ── Workstream D: sale → intent/outbox → GL ──────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Sale_commits_while_accounting_is_unavailable_and_is_posted_exactly_once_after_recovery() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            app.Accounting.Down = true;

            var sale = await SellAsync(database, "d10-outage");

            // The sale, its stock deduction, its intent and its outbox event committed; no journal exists.
            Assert.Equal("completed", await ScalarAsync<string>(database, $"SELECT status FROM sales WHERE id = {sale.Id}"));
            Assert.Equal(99, await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));
            Assert.Equal(1, await CountAsync(database, $"stock_ledgers WHERE reference_id = {sale.Id}"));
            Assert.Equal(SaleFinancialPostingStatuses.Pending, await PostingStatusAsync(database, sale.Id));
            Assert.Equal(1, await CountAsync(database,
                $"integration_outbox_messages WHERE message_type = '{SaleFinancialPostingEventTypes.Requested}'"));
            Assert.Equal(0, await CountAsync(database, "journal_entries"));

            var failed = await app.SaleRunner.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, failed.Failed);
            Assert.Equal(SaleFinancialPostingStatuses.Failed, await PostingStatusAsync(database, sale.Id));
            Assert.Equal(1, await ScalarAsync<int>(database, $"SELECT attempts FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            Assert.Equal(SaleFinancialPostingErrorCodes.Transient,
                await ScalarAsync<string>(database, $"SELECT last_error_code FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            Assert.True(await ScalarAsync<bool>(database,
                $"SELECT next_attempt_at_utc > now() FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            Assert.Equal(0, await CountAsync(database, "journal_entries"));
            // The committed sale is untouched by the failed posting.
            Assert.Equal("completed", await ScalarAsync<string>(database, $"SELECT status FROM sales WHERE id = {sale.Id}"));
            Assert.Equal(99, await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));

            // Backoff: nothing is due until the retry time.
            Assert.Equal(0, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Due);

            app.Accounting.Down = false;
            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            var postingId = await ScalarAsync<long>(database, $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id}");
            Assert.Equal(SaleFinancialPostingOutcome.AlreadyPosted,
                (await ProcessAsync(app, postingId, 1, Cashier, skipLocked: false)).Outcome);
            await MakeSalePostingsDueAsync(database);
            Assert.Equal(0, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Due);

            Assert.Equal(1, await CountAsync(database,
                $"journal_entries WHERE reference_type = 'Sale' AND reference_id = '{sale.Id}' AND status = 'Posted' AND journal_type = 'SalePosting'"));
            Assert.Equal(1, await CountAsync(database, "journal_entries"));
            Assert.Equal(await ScalarAsync<long>(database, $"SELECT journal_entry_id FROM sale_financial_postings WHERE sale_id = {sale.Id}"),
                await ScalarAsync<long>(database, $"SELECT id FROM journal_entries WHERE reference_id = '{sale.Id}'"));
            // Stock was deducted exactly once.
            Assert.Equal(1, await CountAsync(database, $"stock_ledgers WHERE reference_id = {sale.Id}"));
            Assert.Equal(99, await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));
            Assert.Equal(2, await CountAsync(database,
                $"integration_outbox_messages WHERE message_type = '{SaleFinancialPostingEventTypes.Changed}'"));

            await AssertSaleJournalReconcilesAsync(database, sale.Id);
        });

    [Fact]
    public Task Concurrent_workers_cannot_both_post_and_a_manual_retry_waits_for_the_holder() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellAsync(database, "d10-concurrent");
            var postingId = await ScalarAsync<long>(database, $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id}");

            await using var secondInstance = new PostingApp(database.ConnectionString);
            app.Accounting.HoldNextPosting();
            var holder = app.SaleRunner.RunOnceAsync(CancellationToken.None);
            await app.Accounting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // Another instance skips the claimed row instead of waiting for it or posting it again.
            var second = await secondInstance.SaleRunner.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, second.Due);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(SaleFinancialPostingOutcome.ClaimedElsewhere,
                (await ProcessAsync(secondInstance, postingId, 1, Cashier, skipLocked: true)).Outcome);

            // An operator retry waits on the row lock and then finds the posting already done.
            var retry = ProcessAsync(secondInstance, postingId, 1, Cashier, skipLocked: false);
            await Task.Delay(300);
            Assert.False(retry.IsCompleted);

            app.Accounting.Release();
            await AssertPostedAsync(database, await holder, 1);
            Assert.Equal(SaleFinancialPostingOutcome.AlreadyPosted, (await retry).Outcome);
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{sale.Id}'"));
            Assert.Equal(1, await ScalarAsync<int>(database, $"SELECT attempts FROM sale_financial_postings WHERE id = {postingId}"));
        });

    [Fact]
    public Task A_closed_period_fails_without_redating_and_posts_on_the_original_dates_once_reopened() =>
        RunAsync(async (database, app) =>
        {
            await ProvisionAccountingAsync(app, 1);
            await SetPeriodStatusAsync(database, 1, AccountingPeriodStatuses.Closed);
            await OpenAsync(database);
            var sale = await SellAsync(database, "d10-closed-period");

            var failed = await app.SaleRunner.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, failed.Failed);
            Assert.Equal(SaleFinancialPostingErrorCodes.AccountingPeriod,
                await ScalarAsync<string>(database, $"SELECT last_error_code FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            // No journal on any other date was created in its place.
            Assert.Equal(0, await CountAsync(database, "journal_entries"));

            await SetPeriodStatusAsync(database, 1, AccountingPeriodStatuses.Open);
            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT j.business_date = s.business_date
                   AND (j.posting_date AT TIME ZONE 'UTC')::date = (s.sale_date_time AT TIME ZONE 'UTC')::date
                FROM journal_entries j JOIN sales s ON s.id::text = j.reference_id
                WHERE s.id = {sale.Id}
                """));
        });

    [Fact]
    public Task Missing_accounting_configuration_is_recorded_as_failed_and_recovers_after_correction() =>
        RunAsync(async (database, app) =>
        {
            await ProvisionAccountingAsync(app, 1);
            await ExecuteAsync(database, """
                UPDATE chart_accounts SET is_active = false
                WHERE id = (SELECT sales_account_id FROM accounting_configurations WHERE business_id = 1)
                """);
            await OpenAsync(database);
            var sale = await SellAsync(database, "d10-config");

            Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);
            Assert.Equal(SaleFinancialPostingErrorCodes.AccountingConfiguration,
                await ScalarAsync<string>(database, $"SELECT last_error_code FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            Assert.Equal(0, await CountAsync(database, "journal_entries"));

            await ExecuteAsync(database, "UPDATE chart_accounts SET is_active = true WHERE business_id = 1");
            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            await AssertSaleJournalReconcilesAsync(database, sale.Id);
        });

    [Fact]
    public Task An_uncommitted_sale_leaves_no_posting_intent_outbox_event_or_stock_change() =>
        RunAsync(async (database, _) =>
        {
            await OpenAsync(database);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SellAsync(database, "d10-rollback", finalGateFailure: true));

            Assert.Equal(0, await CountAsync(database, "sales"));
            Assert.Equal(0, await CountAsync(database, "sale_financial_postings"));
            Assert.Equal(0, await CountAsync(database,
                $"integration_outbox_messages WHERE message_type = '{SaleFinancialPostingEventTypes.Requested}'"));
            Assert.Equal(0, await CountAsync(database, "stock_ledgers"));
            Assert.Equal(100, await ScalarAsync<int>(database, "SELECT quantity FROM inventories WHERE variant_id = 3201"));
        });

    [Fact]
    public Task A_void_reversal_waits_for_the_sale_journal_and_is_posted_exactly_once() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            app.Accounting.Down = true;
            var sale = await SellAsync(database, "d10-void");
            Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);

            await VoidAsync(database, sale.Id);
            Assert.Equal(1, await CountAsync(database,
                $"sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal' AND reversal_type = 'void' AND status = 'pending'"));

            // Accounting is back, but only the reversal is due: it must not run before the sale journal.
            app.Accounting.Down = false;
            var deferred = await app.SaleRunner.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, deferred.Due);
            Assert.Equal(1, deferred.Skipped);
            Assert.Equal(SaleFinancialPostingErrorCodes.AwaitingSalePosting, await ScalarAsync<string>(database,
                $"SELECT last_error_code FROM sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'"));
            Assert.Equal(0, await CountAsync(database, "journal_entries"));

            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 2);

            await MakeSalePostingsDueAsync(database);
            Assert.Equal(0, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Due);
            var reversalId = await ScalarAsync<long>(database,
                $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'");
            Assert.Equal(SaleFinancialPostingOutcome.AlreadyPosted,
                (await ProcessAsync(app, reversalId, 1, Manager, skipLocked: false)).Outcome);

            Assert.Equal(1, await CountAsync(database,
                $"journal_entries WHERE reference_type = 'SaleRefund' AND reference_id = '{sale.Id}' AND journal_type = 'SalesReturnPosting'"));
            Assert.Equal(1, await CountAsync(database,
                $"journal_entries WHERE reference_type = 'Sale' AND reference_id = '{sale.Id}'"));
            // The reversal credits exactly the payment account the sale journal debited.
            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT (SELECT payment_account_id FROM sale_payments WHERE sale_id = {sale.Id})
                     = (SELECT l.account_id FROM journal_entry_lines l JOIN journal_entries j ON j.id = l.journal_entry_id
                        WHERE j.reference_type = 'SaleRefund' AND j.reference_id = '{sale.Id}' AND l.credit_amount = 100)
                """));
        });

    [Fact]
    public Task The_intent_snapshot_is_immutable_and_posted_is_final_in_the_database() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellAsync(database, "d10-immutable");

            var tamper = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
                $"UPDATE sale_financial_postings SET snapshot_json = '{{}}' WHERE sale_id = {sale.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", tamper.MessageText);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
                $"DELETE FROM sale_financial_postings WHERE sale_id = {sale.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", delete.MessageText);

            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            var reopen = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
                $"UPDATE sale_financial_postings SET status = 'pending', journal_entry_id = NULL, posted_at_utc = NULL WHERE sale_id = {sale.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", reopen.MessageText);
            var detach = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
                $"UPDATE sale_financial_postings SET journal_entry_id = NULL WHERE sale_id = {sale.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", detach.MessageText);
        });

    [Fact]
    public Task Cancellation_mid_posting_and_an_instance_restart_recover_all_unprocessed_work() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var first = await SellAsync(database, "d10-restart-1");
            var second = await SellAsync(database, "d10-restart-2", variantId: 3202);

            // Graceful shutdown while a worker is inside its posting transaction: everything rolls back.
            using var shutdown = new CancellationTokenSource();
            app.Accounting.HoldNextPosting();
            var stopping = app.SaleRunner.RunOnceAsync(shutdown.Token);
            await app.Accounting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            shutdown.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
            Assert.Equal(0, await CountAsync(database, "journal_entries"));
            Assert.Equal(2, await CountAsync(database, "sale_financial_postings WHERE status = 'pending' AND attempts = 0"));

            // A new process starts and picks up both postings from PostgreSQL.
            await app.DisposeAsync();
            await using var restarted = new PostingApp(database.ConnectionString);
            await AssertPostedAsync(database, await restarted.SaleRunner.RunOnceAsync(CancellationToken.None), 2);
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{first.Id}'"));
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{second.Id}'"));
        });

    [Fact]
    public Task The_worker_confines_each_posting_to_its_own_Business() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            await OpenAsync(database, OtherOutletId, OtherTerminal, OtherCashier);
            var own = await SellAsync(database, "d10-tenant-1");
            var other = await SellAsync(database, "d10-tenant-2", OtherOutletId, OtherTerminal, 3401, OtherCashier, businessId: 2);
            var ownPosting = await ScalarAsync<long>(database, $"SELECT id FROM sale_financial_postings WHERE sale_id = {own.Id}");

            // A scope bound to Business 2 can never post Business 1's intent.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ProcessAsync(app, ownPosting, 2, OtherCashier, skipLocked: false));
            Assert.Equal(2, await CountAsync(database, "sale_financial_postings WHERE status = 'pending' AND attempts = 0"));

            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 2);
            Assert.Equal(1, await ScalarAsync<long>(database, $"SELECT business_id FROM journal_entries WHERE reference_id = '{own.Id}'"));
            Assert.Equal(2, await ScalarAsync<long>(database, $"SELECT business_id FROM journal_entries WHERE reference_id = '{other.Id}'"));
            // Every journal line uses an account of the journal's own Business.
            Assert.Equal(0, await CountAsync(database, """
                journal_entry_lines l JOIN journal_entries j ON j.id = l.journal_entry_id
                JOIN chart_accounts a ON a.id = l.account_id WHERE a.business_id <> j.business_id
                """));
        });

    // ── Workstream C/D: scheduled variance posting ───────────────────────────────────────────────────────────────────

    [Fact]
    public Task An_unapproved_variance_can_never_be_posted() =>
        RunAsync(async (database, app) =>
        {
            var session = await OpenAsync(database);
            await CloseAsync(database, session.Id, 80m, "short 20");
            var approvalId = await ScalarAsync<long>(database, "SELECT id FROM cash_difference_approvals");

            Assert.Equal(0, (await app.VarianceRunner.RunOnceAsync(CancellationToken.None)).Due);
            var refused = await Assert.ThrowsAsync<CashSessionException>(() =>
                PostVarianceAsync(app, approvalId, 1, Reviewer, skipLocked: false));
            Assert.Equal(CashSessionErrorCodes.VarianceNotPending, refused.Code);

            var constraint = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database,
                $"UPDATE cash_difference_approvals SET posting_status = 'posted', journal_entry_id = NULL WHERE id = {approvalId}"));
            Assert.Equal("23514", constraint.SqlState);
            Assert.Equal(0, await CountAsync(database, "journal_entries"));
            Assert.Equal(CashDifferencePostingStatuses.NotRequired,
                await ScalarAsync<string>(database, $"SELECT posting_status FROM cash_difference_approvals WHERE id = {approvalId}"));
        });

    [Fact]
    public Task Two_variance_workers_without_an_http_user_post_an_approved_variance_exactly_once() =>
        RunAsync(async (database, app) =>
        {
            var session = await OpenAsync(database);
            await CloseAsync(database, session.Id, 80m, "short 20");
            var approvalId = await ScalarAsync<long>(database, "SELECT id FROM cash_difference_approvals");
            await ReviewAsync(database, approvalId);

            await using var secondInstance = new PostingApp(database.ConnectionString);
            var results = await Task.WhenAll(
                app.VarianceRunner.RunOnceAsync(CancellationToken.None),
                secondInstance.VarianceRunner.RunOnceAsync(CancellationToken.None));
            Assert.Equal(1, results.Sum(result => result.Posted));
            Assert.Equal(0, results.Sum(result => result.Failed));

            Assert.Equal(CashDifferencePostingStatuses.Posted,
                await ScalarAsync<string>(database, $"SELECT posting_status FROM cash_difference_approvals WHERE id = {approvalId}"));
            Assert.Equal(1, await CountAsync(database,
                $"journal_entries WHERE reference_type = 'CashSessionClose' AND reference_id = '{session.Id}'"));
            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT j.business_id = 1 AND j.business_date = cs.business_date AND j.created_by = {Reviewer}
                   AND a.journal_entry_id = j.id
                FROM cash_difference_approvals a JOIN cash_sessions cs ON cs.id = a.cash_session_id
                JOIN journal_entries j ON j.reference_id = cs.id::text AND j.reference_type = 'CashSessionClose'
                WHERE a.id = {approvalId}
                """));

            // Later passes and an explicit retry are replays.
            Assert.Equal(0, (await app.VarianceRunner.RunOnceAsync(CancellationToken.None)).Due);
            Assert.True((await PostVarianceAsync(app, approvalId, 1, Reviewer, skipLocked: false)).Replayed);
            Assert.Equal(1, await CountAsync(database, "journal_entries"));
        });

    [Fact]
    public Task Closing_succeeds_while_accounting_is_closed_and_the_variance_retries_until_the_period_reopens() =>
        RunAsync(async (database, app) =>
        {
            await ProvisionAccountingAsync(app, 1);
            await SetPeriodStatusAsync(database, 1, AccountingPeriodStatuses.Locked);
            var session = await OpenAsync(database);
            await SellAsync(database, "d10-close");

            // Cash Session closing is independent of the accounting state.
            var closed = await CloseAsync(database, session.Id, 150m, "over 50");
            Assert.Equal(CashSessionStatuses.Closed, closed.Status);
            var approvalId = await ScalarAsync<long>(database, "SELECT id FROM cash_difference_approvals");
            await ReviewAsync(database, approvalId);

            Assert.Equal(1, (await app.VarianceRunner.RunOnceAsync(CancellationToken.None)).Failed);
            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT posting_status = 'failed' AND posting_attempts = 1 AND next_posting_attempt_at_utc > now()
                   AND last_posting_error ILIKE '%period%' AND journal_entry_id IS NULL
                FROM cash_difference_approvals WHERE id = {approvalId}
                """));
            Assert.Equal(CashSessionStatuses.Closed,
                await ScalarAsync<string>(database, $"SELECT status FROM cash_sessions WHERE id = {session.Id}"));
            Assert.Equal(1, await CountAsync(database, $"cash_session_close_snapshots WHERE cash_session_id = {session.Id}"));
            Assert.Equal(0, await CountAsync(database, "journal_entries WHERE reference_type = 'CashSessionClose'"));

            await SetPeriodStatusAsync(database, 1, AccountingPeriodStatuses.Open);
            await ExecuteAsync(database,
                "UPDATE cash_difference_approvals SET next_posting_attempt_at_utc = now() - interval '1 minute'");
            Assert.Equal(1, (await app.VarianceRunner.RunOnceAsync(CancellationToken.None)).Posted);
            Assert.Equal(1, await CountAsync(database, "journal_entries WHERE reference_type = 'CashSessionClose'"));
        });

    // ── migration rehearsal ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task The_P31_migration_rolls_back_to_P3_and_reapplies_cleanly() =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.MigrationBaseline,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                const string schemaState = """
                    SELECT (SELECT count(*) FROM information_schema.tables WHERE table_name = 'sale_financial_postings')
                         + (SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_sale_financial_postings_guard')
                         + (SELECT count(*) FROM pg_proc WHERE proname = 'fn_sale_financial_posting_guard')
                         + (SELECT count(*) FROM pg_constraint WHERE conname IN (
                               'ck_cash_difference_approvals_posting_requires_approval',
                               'ck_cash_difference_approvals_posted_journal'))
                    """;
                await using var db = Ctx(database);
                var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();

                await migrator.MigrateAsync();
                Assert.Equal(5, await ScalarAsync<int>(database, schemaState));
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());

                await migrator.MigrateAsync("20261103091638_PosV2P3CashSessionManagement");
                Assert.Equal(0, await ScalarAsync<int>(database, schemaState));
                Assert.Equal("20261103091638_PosV2P3CashSessionManagement",
                    (await db.Database.GetAppliedMigrationsAsync()).Last());

                await migrator.MigrateAsync();
                Assert.Equal(5, await ScalarAsync<int>(database, schemaState));
            });

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Task RunAsync(Func<PostgresTestDatabase, PostingApp, Task> operation) =>
        PostgresTestDatabase.RunAsync(
            InfrastructureTestSettings.RequireIsolatedPostgres(),
            PostgresTestDatabasePrefix.SaleCreation,
            async database =>
            {
                await SeedAsync(database);
                var app = new PostingApp(database.ConnectionString);
                try
                {
                    await operation(database, app);
                }
                finally
                {
                    await app.DisposeAsync();
                }
            });

    private static RetailPOSDbContext Ctx(PostgresTestDatabase database) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(database.ConnectionString).Options);

    private static async Task SeedAsync(PostgresTestDatabase database)
    {
        await using var db = Ctx(database);
        await db.Database.MigrateAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Phase 3.1 business", IsActive = true });
        db.Businesses.Add(new Business { Id = 2, Name = "Other business", IsActive = true });
        await db.SaveChangesAsync();
        db.Outlets.Add(new Outlet { Id = OutletId, BusinessId = 1, Name = "Main outlet", Address = "Dhaka", InvoicePrefix = "DHK" });
        db.Outlets.Add(new Outlet { Id = OtherOutletId, BusinessId = 2, Name = "Other outlet", Address = "Ctg", InvoicePrefix = "CTG" });
        foreach (var (id, business, outlet, name) in new[]
                 {
                     (Cashier, 1L, OutletId, "Cashier"), (Manager, 1L, OutletId, "Manager"),
                     (Reviewer, 1L, OutletId, "Reviewer"), (OtherCashier, 2L, OtherOutletId, "Other")
                 })
            db.Users.Add(new User
            {
                Id = id, BusinessId = business, Name = name, Email = $"{name.ToLowerInvariant()}@phase31.test",
                PasswordHash = "x", OutletId = outlet, IsActive = true
            });
        db.Categories.Add(new Category { Id = 3001, BusinessId = 1, Name = "General" });
        db.Categories.Add(new Category { Id = 3002, BusinessId = 2, Name = "General" });
        db.PosTerminals.Add(new PosTerminal { Id = Terminal1, OutletId = OutletId, Name = "Counter 1", Code = "T-1", IsActive = true, IsDefault = true });
        db.PosTerminals.Add(new PosTerminal { Id = OtherTerminal, OutletId = OtherOutletId, Name = "Counter 1", Code = "T-1", IsActive = true, IsDefault = true });
        foreach (var (productId, variantId, inventoryId, business, outlet, category) in new[]
                 {
                     (3101L, 3201L, 9001L, 1L, OutletId, 3001L), (3102L, 3202L, 9002L, 1L, OutletId, 3001L),
                     (3301L, 3401L, 9101L, 2L, OtherOutletId, 3002L)
                 })
        {
            db.Products.Add(new Product
            {
                Id = productId, BusinessId = business, Name = $"Product {productId}", ProductCode = $"P-{productId}",
                CategoryId = category, BasePrice = 100m, CostPrice = 70m, Status = ProductStatus.Active, HasVariants = true
            });
            db.ProductVariants.Add(new ProductVariant
            {
                Id = variantId, BusinessId = business, ProductId = productId, Name = "Default", Sku = $"SKU-{variantId}",
                Barcode = $"BC-{variantId}", Attributes = "{}"
            });
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = inventoryId, VariantId = variantId, LocationType = "outlet", LocationId = outlet, Quantity = 100,
                LowStockThreshold = 1
            });
        }

        await db.SaveChangesAsync();
    }

    private static ICashSessionEventWriter Events(RetailPOSDbContext db) =>
        new CashSessionEventWriter(new IntegrationOutbox(
            db, Mock.Of<IAuditContext>(), TimeProvider.System, Mock.Of<IIntegrationReplayAuthorization>()));

    private static async Task<CashSessionReadModel> OpenAsync(
        PostgresTestDatabase database,
        long outletId = OutletId,
        long terminalId = Terminal1,
        long cashier = Cashier)
    {
        await using var db = Ctx(database);
        var settings = new Mock<IBusinessPosSettingsStore>();
        settings.Setup(s => s.GetAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessPosSettingsSnapshot(1, 20, "Asia/Dhaka", true, 0m, "VAT", false, 100m, 1, null, null, true));
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long outlet, CancellationToken _) => outlet);
        var handler = new OpenCashSessionCommandHandler(
            new CashSessionStore(db), authorizer.Object, settings.Object, TestCompanySettings.Provider(), Mock.Of<ICashSessionOpenAuditWriter>(),
            Mock.Of<ICashSessionOpenedPublisher>(), Events(db), TimeProvider.System);
        return await handler.Handle(
            new OpenCashSessionCommand(outletId, terminalId, 100m, null, cashier, null), CancellationToken.None);
    }

    private static async Task<CashSessionReadModel> CloseAsync(
        PostgresTestDatabase database, long sessionId, decimal counted, string? notes)
    {
        await using var db = Ctx(database);
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long outlet, CancellationToken _) => outlet);
        var handler = new CloseCashSessionCommandHandler(
            new CashSessionStore(db), authorizer.Object, Mock.Of<ICashSessionCloseAuditWriter>(),
            Mock.Of<ICashSessionClosedPublisher>(), Events(db), TimeProvider.System);
        return await handler.Handle(new CloseCashSessionCommand(sessionId, counted, notes, Manager), CancellationToken.None);
    }

    private static async Task ReviewAsync(PostgresTestDatabase database, long approvalId)
    {
        await using var db = Ctx(database);
        var reader = new Mock<ICashSessionOutletAuthorizer>();
        reader.Setup(a => a.AuthorizeReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long outlet, CancellationToken _) => outlet);
        var handler = new ReviewCashDifferenceCommandHandler(
            new CashDifferenceStore(db), new CashSessionStore(db), reader.Object, Events(db),
            Mock.Of<ICashSessionAuditWriter>(), TimeProvider.System);
        var result = await handler.Handle(new ReviewCashDifferenceCommand(approvalId, true, "ok", Reviewer), CancellationToken.None);
        Assert.Equal(CashDifferencePostingStatuses.Pending, result.Approval.PostingStatus);
    }

    private sealed class FailingFinalGate(ISaleCashSessionValidator inner) : ISaleCashSessionValidator
    {
        public Task<CashSession> ValidateActiveForSaleAsync(long outletId, long terminalId, long? actor, CancellationToken ct) =>
            inner.ValidateActiveForSaleAsync(outletId, terminalId, actor, ct);

        public Task ValidateFinalGateAsync(CashSession session, CancellationToken ct) =>
            throw new InvalidOperationException("Injected failure at the final commit gate.");
    }

    private static async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> SellAsync(
        PostgresTestDatabase database,
        string key,
        long outletId = OutletId,
        long terminalId = Terminal1,
        long variantId = 3201,
        long cashier = Cashier,
        bool finalGateFailure = false,
        long businessId = 1)
    {
        await using var db = Ctx(database);
        ISaleCashSessionValidator validator = new SaleCashSessionValidator(
            new CashSessionStore(db), Mock.Of<IAuditService>(), Mock.Of<ICashSessionExpiryPublisher>(),
            TimeProvider.System, NullLogger<SaleCashSessionValidator>.Instance);
        if (finalGateFailure)
            validator = new FailingFinalGate(validator);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var handler = SaleCreationCommandTestFactory.Create(
            db, validator, new SaleInvoiceNumberAllocator(db), Mock.Of<IAuditService>(),
            new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
            cache.Object, Mock.Of<ISaleEventPublisher>(), businessId: businessId);
        return await handler.Handle(
            new CreateSaleCommand(
                outletId, terminalId, null, [new CreateSaleLine(variantId, 1, 100m, 0m, null, null)], 0m, 0m, "cash", [],
                cashier, null, null, key, 150m),
            CancellationToken.None);
    }

    private static async Task VoidAsync(PostgresTestDatabase database, long saleId)
    {
        await using var db = Ctx(database);
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var handler = new VoidSaleCommandHandler(
            new SaleVoidStore(db),
            new SaleReadStore(db, TestCompanySettings.Provider()),
            authorizer.Object,
            SaleCreationCommandTestFactory.FinancialPostings(db),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(Mock.Of<IAuditService>()),
            new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
            new NoCashRefunds());
        await handler.Handle(new VoidSaleCommand(saleId, "customer changed mind", Manager), CancellationToken.None);
    }

    /// <summary>Runs one posting exactly like the worker: a fresh scope whose only Business is the given one.</summary>
    private static async Task<SaleFinancialPostingResult> ProcessAsync(
        PostingApp app, long postingId, long businessId, long actor, bool skipLocked)
    {
        await using var scope = app.Services.CreateAsyncScope();
        InitializeWorkerAudit(scope.ServiceProvider, businessId, actor);
        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ProcessSaleFinancialPostingCommand(postingId, skipLocked), CancellationToken.None);
    }

    private static async Task<CashDifferenceApprovalReadModelResult> PostVarianceAsync(
        PostingApp app, long approvalId, long businessId, long actor, bool skipLocked)
    {
        await using var scope = app.Services.CreateAsyncScope();
        InitializeWorkerAudit(scope.ServiceProvider, businessId, actor);
        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new PostCashDifferenceCommand(approvalId, actor, skipLocked), CancellationToken.None);
    }

    private static void InitializeWorkerAudit(IServiceProvider services, long businessId, long actor) =>
        services.GetRequiredService<IAuditContext>().Initialize(
            AuditContext.ForBusinessSystemJob("phase31-test-worker", businessId) with
            {
                Source = AuditSource.Integration,
                RealUserId = actor,
                ActingUserId = actor
            });

    private static async Task ProvisionAccountingAsync(PostingApp app, long businessId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAccountingConfigurationInitializationStore>()
            .EnsureInitializedAsync(businessId, null, CancellationToken.None);
    }

    private static Task SetPeriodStatusAsync(PostgresTestDatabase database, long businessId, string status) =>
        ExecuteAsync(database, $"""
            UPDATE accounting_periods SET status = '{status}'
            WHERE financial_year_id IN (SELECT id FROM financial_years WHERE business_id = {businessId})
            """);

    private static Task MakeSalePostingsDueAsync(PostgresTestDatabase database) =>
        ExecuteAsync(database,
            "UPDATE sale_financial_postings SET next_attempt_at_utc = now() - interval '1 minute' WHERE status <> 'posted'");

    private static Task<string> PostingStatusAsync(PostgresTestDatabase database, long saleId) =>
        ScalarAsync<string>(database, $"SELECT status FROM sale_financial_postings WHERE sale_id = {saleId} AND kind = 'sale'");

    /// <summary>The posted journal is balanced and equals the sale's committed facts and its snapshot.</summary>
    private static async Task AssertSaleJournalReconcilesAsync(PostgresTestDatabase database, long saleId)
    {
        Assert.True(await ScalarAsync<bool>(database, $"""
            SELECT sum(l.debit_amount) = sum(l.credit_amount)
               AND sum(l.credit_amount) FILTER (WHERE l.description = 'Sales revenue') = (SELECT sum(quantity * unit_price) FROM sale_items WHERE sale_id = {saleId})
               AND sum(l.debit_amount) FILTER (WHERE l.description LIKE 'Sale payment%') = (SELECT total_amount FROM sales WHERE id = {saleId})
               AND sum(l.debit_amount) FILTER (WHERE l.description = 'Cost of goods sold') = (SELECT sum(quantity * historical_unit_cost) FROM sale_items WHERE sale_id = {saleId})
            FROM journal_entry_lines l JOIN journal_entries j ON j.id = l.journal_entry_id
            WHERE j.reference_type = 'Sale' AND j.reference_id = '{saleId}'
            """));
        await using var db = Ctx(database);
        var posting = await db.SaleFinancialPostings.AsNoTracking().SingleAsync(row => row.SaleId == saleId && row.Kind == "sale");
        var snapshot = SaleFinancialIntentFactory.ReadSaleSnapshot(posting);
        var sale = await db.Sales.AsNoTracking().Include(row => row.Payments).SingleAsync(row => row.Id == saleId);
        Assert.True(SaleFinancialIntentFactory.IsIntact(posting));
        Assert.Equal(sale.TotalAmount, snapshot.TotalAmount);
        Assert.Equal(sale.Tax, snapshot.Vat);
        Assert.Equal(DateOnly.FromDateTime(sale.BusinessDate!.Value), snapshot.BusinessDate);
        Assert.Equal(sale.Payments.Sum(payment => payment.Amount), snapshot.Payments.Sum(payment => payment.Amount));
        Assert.All(sale.Payments, payment => Assert.NotNull(payment.PaymentAccountId));
    }

    private static async Task AssertPostedAsync(PostgresTestDatabase database, FinancialPostingBatchResult result, int expected)
    {
        if (result.Posted == expected)
            return;
        var errors = await ScalarAsync<string>(database,
            "SELECT coalesce(string_agg(kind || ':' || status || ':' || coalesce(next_attempt_at_utc::text, '-') || ':' || coalesce(last_error, '-'), ' | '), 'none') || ' db-now=' || now()::text FROM sale_financial_postings");
        Assert.Fail($"Expected {expected} posted but the pass returned {result} at {DateTime.UtcNow:O}. Postings: {errors}");
    }

    private static async Task<T> ScalarAsync<T>(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static Task<int> CountAsync(PostgresTestDatabase database, string fromWhere) =>
        ScalarAsync<int>(database, $"SELECT count(*)::int FROM {fromWhere}");

    private static async Task ExecuteAsync(PostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Simulates an accounting outage, or holds the next posting inside its transaction (row lock held).</summary>
    private sealed class AccountingSwitch
    {
        private int _hold;
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Down;
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HoldNextPosting()
        {
            Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _hold, 1);
        }

        public void Release() => _release.TrySetResult();

        public async Task BeforePostingAsync(CancellationToken cancellationToken)
        {
            if (Down)
                throw new TimeoutException("Simulated accounting outage.");
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            {
                Entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class SwitchedJournalPoster(SaleFinancialJournalPoster inner, AccountingSwitch accounting)
        : ISaleFinancialJournalPoster
    {
        public async Task<SaleJournalPostingResult> PostSaleAsync(SaleJournalSnapshot snapshot, CancellationToken ct)
        {
            await accounting.BeforePostingAsync(ct);
            return await inner.PostSaleAsync(snapshot, ct);
        }

        public async Task<long> PostReversalAsync(
            SaleReversalJournalSnapshot snapshot,
            IReadOnlyDictionary<long, long?> paymentAccounts,
            CancellationToken ct)
        {
            await accounting.BeforePostingAsync(ct);
            return await inner.PostReversalAsync(snapshot, paymentAccounts, ct);
        }
    }

    /// <summary>One application instance over the test database; two of them model two API instances.</summary>
    private sealed class PostingApp(string connectionString, IReadOnlyDictionary<string, string?>? settings = null)
        : WebApplicationFactory<Program>
    {
        private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), $"phase31_{Guid.NewGuid():N}_system-settings.json");

        public AccountingSwitch Accounting { get; } = new();

        public SaleFinancialPostingBatchRunner SaleRunner => Services.GetRequiredService<SaleFinancialPostingBatchRunner>();

        public CashDifferencePostingBatchRunner VarianceRunner => Services.GetRequiredService<CashDifferencePostingBatchRunner>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // Host settings are visible to Program's top-level code (the worker plan is resolved before Build).
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
                builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:Secret"] = TestJwtConfiguration.SigningKey,
                    ["SystemSettings:FilePath"] = _settingsFile,
                    ["StockCount:Adjustment:WorkerEnabled"] = "false",
                    ["MobileStockTake:Submission:WorkerEnabled"] = "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
                services.RemoveAll<RetailPOSDbContext>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
                services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
                    options.UseNpgsql(connectionString).AddInterceptors(
                        serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>()));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                services.RemoveAll<ISaleFinancialJournalPoster>();
                services.AddScoped<SaleFinancialJournalPoster>();
                services.AddScoped<ISaleFinancialJournalPoster>(provider => new SwitchedJournalPoster(
                    provider.GetRequiredService<SaleFinancialJournalPoster>(), Accounting));
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            try
            {
                File.Delete(_settingsFile);
            }
            catch (IOException)
            {
            }
        }
    }
}
