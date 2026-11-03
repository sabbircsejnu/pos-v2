using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.API.Services;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.IntegrationTests.Infrastructure;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Sales;

namespace RetailPOS.IntegrationTests.Phase3;

/// <summary>
/// Phase 3.2 on real PostgreSQL: VAT-correct sale and reversal journals (P3.2-D01), the reversal's own accounting date
/// (P3.2-D02), the dedicated permissions over HTTP (P3.2-D03) and the same-process / dedicated worker modes (P3.2-D05).
/// Journals are created and posted by the production journal store against the real chart of accounts and periods.
/// </summary>
public sealed partial class SaleFinancialPostingPostgresTests
{
    private static readonly SaleTaxPolicy Inclusive10 = new(true, 10m, true);
    private static readonly SaleTaxPolicy Exclusive10 = new(true, 10m, false);

    // ── P3.2-D01: sale journals ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task An_inclusive_VAT_sale_credits_revenue_net_of_VAT_and_balances() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellLinesAsync(database, "p32-inclusive", Inclusive10, [new(3201, 10, 100m, 0m)],
                payments: [new CreateSalePayment("cash", 1000m, 1000m)]);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            var lines = await JournalLinesAsync(database, "Sale", sale.Id);
            Assert.Equal(1000m, Debit(lines, "Sale payment via cash"));
            Assert.Equal(909.09m, Credit(lines, "Sales revenue"));
            Assert.Equal(90.91m, Credit(lines, "VAT payable"));
            Assert.Equal(700m, Debit(lines, "Cost of goods sold"));
            Assert.Equal(700m, Credit(lines, "Inventory reduction"));
            AssertBalanced(lines);
            Assert.DoesNotContain(lines, line => line.Description == "Rounding difference");
        });

    [Fact]
    public Task Mixed_rates_an_invoice_discount_and_a_split_payment_with_change_balance_exactly() =>
        RunAsync(async (database, app) =>
        {
            await ExecuteAsync(database, "UPDATE products SET tax_rate = 15 WHERE id = 3101; UPDATE products SET tax_rate = 5 WHERE id = 3102;");
            await OpenAsync(database);
            var totals = SaleCheckoutCalculator.Calculate(
                [new SaleCheckoutLineInput(3201, 3, 100m, 0m, 15m), new SaleCheckoutLineInput(3202, 2, 100m, 0m, 5m)],
                20m, Exclusive10);
            var cash = totals.TotalAmount - 200m;
            var sale = await SellLinesAsync(database, "p32-mixed", Exclusive10,
                [new(3201, 3, 100m, 0m), new(3202, 2, 100m, 0m)], invoiceDiscount: 20m,
                payments: [new CreateSalePayment("card", 200m, null), new CreateSalePayment("cash", cash, decimal.Ceiling(cash) + 50m)],
                expectedTax: totals.Tax);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            var lines = await JournalLinesAsync(database, "Sale", sale.Id);
            AssertBalanced(lines);
            Assert.Equal(totals.Tax, Credit(lines, "VAT payable"));
            Assert.Equal(20m, Debit(lines, "Sales discount"));
            Assert.Equal(totals.Lines.Sum(line => line.TaxableAmount), Credit(lines, "Sales revenue") - Debit(lines, "Sales discount"));
            Assert.Equal(200m, Debit(lines, "Sale payment via card"));
            // The cash debit is the allocated amount; the change handed back is not revenue and not drawer cash.
            Assert.Equal(cash, Debit(lines, "Sale payment via cash"));
            Assert.Equal(totals.TotalAmount, lines.Where(line => line.Description.StartsWith("Sale payment")).Sum(line => line.Debit));
        });

    [Fact]
    public Task Zero_rated_and_exempt_lines_with_a_line_discount_post_no_VAT_and_revenue_equals_the_payable() =>
        RunAsync(async (database, app) =>
        {
            await ExecuteAsync(database,
                "UPDATE products SET tax_classification = 'zero_rated' WHERE id = 3101; UPDATE products SET tax_classification = 'exempt' WHERE id = 3102;");
            await OpenAsync(database);
            var sale = await SellLinesAsync(database, "p32-zero", Exclusive10, [new(3201, 1, 100m, 10m), new(3202, 1, 100m, 0m)],
                payments: [new CreateSalePayment("cash", 190m, 200m)], expectedTax: 0m);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            var lines = await JournalLinesAsync(database, "Sale", sale.Id);
            AssertBalanced(lines);
            Assert.DoesNotContain(lines, line => line.Description == "VAT payable");
            Assert.Equal(10m, Debit(lines, "Sales discount"));
            Assert.Equal(190m, Credit(lines, "Sales revenue") - Debit(lines, "Sales discount"));
            Assert.Equal(190m, Debit(lines, "Sale payment via cash"));
        });

    [Fact]
    public Task The_journal_comes_from_the_immutable_snapshot_not_from_current_product_tax_settings() =>
        RunAsync(async (database, app) =>
        {
            await ExecuteAsync(database, "UPDATE products SET tax_rate = 15 WHERE id = 3101;");
            await OpenAsync(database);
            app.Accounting.Down = true;
            var sale = await SellLinesAsync(database, "p32-replay", Exclusive10, [new(3201, 2, 100m, 0m)],
                payments: [new CreateSalePayment("cash", 230m, 230m)], expectedTax: 30m);
            Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);

            // Tax settings change after the sale: the retried journal must still carry the checkout's 30.00 VAT.
            await ExecuteAsync(database, "UPDATE products SET tax_rate = 50 WHERE id = 3101;");
            app.Accounting.Down = false;
            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            var lines = await JournalLinesAsync(database, "Sale", sale.Id);
            Assert.Equal(30m, Credit(lines, "VAT payable"));
            Assert.Equal(200m, Credit(lines, "Sales revenue"));
            AssertBalanced(lines);
        });

    // ── P3.2-D01: reversal journals ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task A_full_void_of_an_inclusive_VAT_sale_reverses_revenue_and_VAT_and_keeps_the_cash_payout_separate() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellLinesAsync(database, "p32-void", Inclusive10, [new(3201, 10, 100m, 0m)],
                payments: [new CreateSalePayment("cash", 1000m, 1000m)]);
            await VoidAsync(database, sale.Id, realCashRefund: true);
            // The physical payout is its own immutable cash fact, recorded by the void itself.
            Assert.Equal(-1000m, await ScalarAsync<decimal>(database,
                $"SELECT cash_effect FROM cash_session_movements WHERE original_sale_id = {sale.Id} AND movement_type = 'cash_refund'"));

            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 2);
            var lines = await JournalLinesAsync(database, "SaleRefund", sale.Id);
            Assert.Equal(909.09m, Debit(lines, "Sales return"));
            Assert.Equal(90.91m, Debit(lines, "VAT payable reversal"));
            Assert.Equal(1000m, Credit(lines, "Refund reversal via cash"));
            Assert.Equal(700m, Debit(lines, "Inventory restoration"));
            Assert.Equal(700m, Credit(lines, "COGS reversal"));
            AssertBalanced(lines);
            // Posting the GL reversal created no second cash movement.
            Assert.Equal(1, await CountAsync(database, $"cash_session_movements WHERE original_sale_id = {sale.Id}"));
        });

    [Fact]
    public Task A_full_refund_of_an_exclusive_VAT_sale_reverses_net_and_VAT_exactly() =>
        RunAsync(async (database, app) =>
        {
            await ExecuteAsync(database, "UPDATE products SET tax_rate = 15 WHERE id = 3101;");
            await OpenAsync(database);
            var sale = await SellLinesAsync(database, "p32-refund", Exclusive10, [new(3201, 2, 100m, 0m)],
                payments: [new CreateSalePayment("card", 230m, null)], expectedTax: 30m);
            await RefundAsync(database, sale.Id, [new RefundSaleLine(3201, 2)]);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 2);

            var lines = await JournalLinesAsync(database, "SaleRefund", sale.Id);
            Assert.Equal(200m, Debit(lines, "Sales return"));
            Assert.Equal(30m, Debit(lines, "VAT payable reversal"));
            Assert.Equal(230m, Credit(lines, "Refund reversal via card"));
            AssertBalanced(lines);
        });

    [Fact]
    public Task A_partial_refund_reverses_only_the_returned_share_split_over_the_original_payments_and_only_once() =>
        RunAsync(async (database, app) =>
        {
            await ExecuteAsync(database, "UPDATE products SET tax_rate = 15 WHERE id = 3101; UPDATE products SET tax_rate = 5 WHERE id = 3102;");
            await OpenAsync(database);
            var totals = SaleCheckoutCalculator.Calculate(
                [new SaleCheckoutLineInput(3201, 3, 100m, 0m, 15m), new SaleCheckoutLineInput(3202, 1, 100m, 0m, 5m)], 0m, Exclusive10);
            var sale = await SellLinesAsync(database, "p32-partial", Exclusive10, [new(3201, 3, 100m, 0m), new(3202, 1, 100m, 0m)],
                payments: [new CreateSalePayment("card", 150m, null), new CreateSalePayment("mobile", totals.TotalAmount - 150m, null)],
                expectedTax: totals.Tax);
            await RefundAsync(database, sale.Id, [new RefundSaleLine(3201, 1)]);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 2);

            var lines = await JournalLinesAsync(database, "SaleRefund", sale.Id);
            Assert.Equal(100m, Debit(lines, "Sales return"));                // one of three units of the 15% line
            Assert.Equal(15m, Debit(lines, "VAT payable reversal"));
            Assert.Equal(115m, lines.Where(line => line.Description.StartsWith("Refund reversal")).Sum(line => line.Credit));
            Assert.Equal(2, lines.Count(line => line.Description.StartsWith("Refund reversal")));
            AssertBalanced(lines);
            Assert.True(115m <= totals.Lines.Sum(line => line.TaxableAmount + line.VatAmount));

            // The existing flow allows one refund per sale: a second one is refused and creates no second reversal.
            await Assert.ThrowsAsync<InvalidOperationException>(() => RefundAsync(database, sale.Id, [new RefundSaleLine(3201, 2)]));
            Assert.Equal(1, await CountAsync(database, $"sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'"));
        });

    [Fact]
    public Task Missing_line_allocation_evidence_fails_the_reversal_explicitly_without_blocking_the_void() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellAsync(database, "p32-evidence");
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            await ExecuteAsync(database, $"UPDATE sale_items SET taxable_amount = NULL WHERE sale_id = {sale.Id}");

            await VoidAsync(database, sale.Id);
            Assert.Equal("voided", await ScalarAsync<string>(database, $"SELECT status FROM sales WHERE id = {sale.Id}"));
            Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);
            Assert.Equal(SaleFinancialPostingErrorCodes.AllocationEvidenceMissing, await ScalarAsync<string>(database,
                $"SELECT last_error_code FROM sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'"));
            Assert.Equal(0, await CountAsync(database, $"journal_entries WHERE reference_type = 'SaleRefund' AND reference_id = '{sale.Id}'"));
        });

    // ── P3.2-D02: reversal accounting date ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task A_reversal_posts_on_its_own_date_when_the_original_sale_period_is_closed() =>
        RunAsync(async (database, app) =>
        {
            await ProvisionAccountingAsync(app, 1);
            await SplitPeriodsAtDecemberAsync(database);
            await OpenAsync(database);
            var sale = await SellAsync(database, "p32-dates");
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            var saleBusinessDate = await ScalarAsync<DateTime>(database, $"SELECT business_date FROM sales WHERE id = {sale.Id}");
            await ExecuteAsync(database, "UPDATE accounting_periods SET status = 'Closed' WHERE period_number = 1");

            // 18:30 UTC on 5 December is 00:30 on 6 December in Asia/Dhaka.
            await VoidAsync(database, sale.Id, clock: new FixedClock(new DateTime(2026, 12, 5, 18, 30, 0, DateTimeKind.Utc)));
            await MakeSalePostingsDueAsync(database);   // the simulated server clock is ahead of the worker's
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);

            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT j.business_date = DATE '2026-12-06'
                   AND (j.posting_date AT TIME ZONE 'UTC')::date = DATE '2026-12-06'
                   AND j.transaction_date = TIMESTAMPTZ '2026-12-05 18:30:00+00'
                FROM journal_entries j WHERE j.reference_type = 'SaleRefund' AND j.reference_id = '{sale.Id}'
                """));
            // The original sale and its journal keep their own dates.
            Assert.Equal(saleBusinessDate, await ScalarAsync<DateTime>(database, $"SELECT business_date FROM sales WHERE id = {sale.Id}"));
            Assert.Equal(saleBusinessDate, await ScalarAsync<DateTime>(database,
                $"SELECT business_date FROM journal_entries WHERE reference_type = 'Sale' AND reference_id = '{sale.Id}'"));
        });

    [Fact]
    public Task A_closed_reversal_period_is_never_redated_by_retries_and_posts_on_the_same_date_once_reopened() =>
        RunAsync(async (database, app) =>
        {
            await ProvisionAccountingAsync(app, 1);
            await SplitPeriodsAtDecemberAsync(database);
            await ExecuteAsync(database, "UPDATE accounting_periods SET status = 'Closed' WHERE period_number = 2");
            await OpenAsync(database);
            var sale = await SellAsync(database, "p32-closed-reversal");
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            await VoidAsync(database, sale.Id, clock: new FixedClock(new DateTime(2026, 12, 10, 6, 0, 0, DateTimeKind.Utc)));

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await MakeSalePostingsDueAsync(database);
                Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);
            }

            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT status = 'failed' AND attempts = 3 AND last_error_code = 'accounting_period'
                FROM sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'
                """));
            Assert.Equal(0, await CountAsync(database, $"journal_entries WHERE reference_type = 'SaleRefund' AND reference_id = '{sale.Id}'"));

            await ExecuteAsync(database, "UPDATE accounting_periods SET status = 'Open' WHERE period_number = 2");
            await MakeSalePostingsDueAsync(database);
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            Assert.Equal(new DateTime(2026, 12, 10), await ScalarAsync<DateTime>(database,
                $"SELECT business_date FROM journal_entries WHERE reference_type = 'SaleRefund' AND reference_id = '{sale.Id}'"));
        });

    [Fact]
    public Task Concurrent_retries_of_a_reversal_produce_one_journal() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellAsync(database, "p32-concurrent-reversal");
            await AssertPostedAsync(database, await app.SaleRunner.RunOnceAsync(CancellationToken.None), 1);
            await VoidAsync(database, sale.Id);
            var reversalId = await ScalarAsync<long>(database,
                $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id} AND kind = 'reversal'");

            await using var secondInstance = new PostingApp(database.ConnectionString);
            var outcomes = await Task.WhenAll(
                ProcessAsync(app, reversalId, 1, Manager, skipLocked: false),
                ProcessAsync(secondInstance, reversalId, 1, Manager, skipLocked: false),
                ProcessAsync(app, reversalId, 1, Manager, skipLocked: false));
            Assert.Single(outcomes, outcome => outcome.Outcome == SaleFinancialPostingOutcome.Posted);
            Assert.All(outcomes, outcome => Assert.Contains(outcome.Outcome,
                new[] { SaleFinancialPostingOutcome.Posted, SaleFinancialPostingOutcome.AlreadyPosted }));
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_type = 'SaleRefund' AND reference_id = '{sale.Id}'"));
        });

    // ── P3.2-D03: permissions over HTTP ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task Financial_posting_endpoints_require_the_dedicated_permissions_and_stay_inside_the_Business() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            app.Accounting.Down = true;
            var sale = await SellAsync(database, "p32-http");
            Assert.Equal(1, (await app.SaleRunner.RunOnceAsync(CancellationToken.None)).Failed);
            app.Accounting.Down = false;
            var postingId = await ScalarAsync<long>(database, $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id}");

            var none = Client(app, "sales.view");
            var viewer = Client(app, "sales.financial_postings.view");
            var retrier = Client(app, "sales.financial_postings.retry");
            var foreign = Client(app, "sales.financial_postings.view,sales.financial_postings.retry", businessId: 2, userId: OtherCashier);

            Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("api/financial-postings/sales")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("api/financial-postings/sales?status=failed")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("api/financial-postings/summary")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"api/financial-postings/sales/{postingId}/retry", null)).StatusCode);
            // The retry permission does not grant manual journal posting.
            Assert.Equal(HttpStatusCode.Forbidden, (await retrier.PostAsync("api/accounting/journals/1/post", null)).StatusCode);
            // Another Business sees neither the posting nor its existence.
            var foreignList = await foreign.GetFromJsonAsync<System.Text.Json.JsonElement>("api/financial-postings/sales");
            Assert.Equal(0, foreignList.GetProperty("data").GetArrayLength());
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostAsync($"api/financial-postings/sales/{postingId}/retry", null)).StatusCode);
            Assert.Equal(SaleFinancialPostingStatuses.Failed, await PostingStatusAsync(database, sale.Id));

            var retried = await retrier.PostAsync($"api/financial-postings/sales/{postingId}/retry", null);
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            Assert.Equal(SaleFinancialPostingStatuses.Posted, await PostingStatusAsync(database, sale.Id));
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{sale.Id}'"));
            Assert.True(await ScalarAsync<bool>(database, $"""
                SELECT count(*) = 1 FROM audit_events
                WHERE primary_entity_type = 'SaleFinancialPosting' AND primary_entity_id = '{postingId}'
                  AND metadata->>'previousStatus' = 'failed' AND metadata->>'newStatus' = 'posted'
                  AND metadata->>'businessId' = '1' AND metadata ? 'retryRequestedAtUtc' AND real_user_id = {Manager}
                """));
        });

    // ── P3.2-D05: worker deployment modes ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public Task A_dedicated_worker_process_posts_pending_intents_and_records_a_durable_heartbeat() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var first = await SellAsync(database, "p32-worker-1");
            var second = await SellAsync(database, "p32-worker-2", variantId: 3202);
            Assert.Empty(app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<SaleFinancialPostingHostedService>());

            var workerSettings = new Dictionary<string, string?>
            {
                ["FinancialPosting:HostRole"] = "Worker",
                ["FinancialPosting:Sales:PollIntervalSeconds"] = "1",
                ["FinancialPosting:CashDifferences:PollIntervalSeconds"] = "1"
            };
            await using var workerA = new PostingApp(database.ConnectionString, workerSettings);
            await using var workerB = new PostingApp(database.ConnectionString, workerSettings);
            _ = workerA.Services;
            _ = workerB.Services;
            Assert.Single(workerA.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<SaleFinancialPostingHostedService>());

            await WaitUntilAsync(async () => await CountAsync(database, "sale_financial_postings WHERE status = 'posted'") == 2);
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{first.Id}'"));
            Assert.Equal(1, await CountAsync(database, $"journal_entries WHERE reference_id = '{second.Id}'"));
            await WaitUntilAsync(async () => await CountAsync(database,
                "financial_posting_worker_heartbeats WHERE host_role = 'Worker' AND last_run_completed_at_utc IS NOT NULL") >= 4);

            // The worker process exposes only its health endpoint.
            var health = await workerA.CreateClient().GetAsync("health/financial-posting");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            var noApi = await workerA.CreateClient().GetAsync("api/financial-postings/summary");
            Assert.Equal(HttpStatusCode.NotFound, noApi.StatusCode);

            // The API reads the dedicated workers' liveness from the shared table.
            var summary = await Client(app, "sales.financial_postings.view")
                .GetFromJsonAsync<System.Text.Json.JsonElement>("api/financial-postings/summary");
            Assert.Contains(summary.GetProperty("data").GetProperty("workers").EnumerateArray(),
                worker => worker.GetProperty("hostRole").GetString() == "Worker" && !worker.GetProperty("stale").GetBoolean());
        });

    [Fact]
    public void The_worker_plan_follows_the_host_role_and_the_enabled_flags()
    {
        Assert.Equal(new FinancialPostingWorkerPlan("Api", true, true), FinancialPostingWorkerPlan.Resolve(null, true, true, false));
        Assert.Equal(new FinancialPostingWorkerPlan("Api", false, true), FinancialPostingWorkerPlan.Resolve("Api", false, true, false));
        Assert.Equal(new FinancialPostingWorkerPlan("Api", false, false), FinancialPostingWorkerPlan.Resolve("Api", true, true, true));
        var worker = FinancialPostingWorkerPlan.Resolve("worker", true, false, true);
        Assert.True(worker.WorkerOnly);
        Assert.True(worker.RunSales);
        Assert.False(worker.RunCashDifferences);
        Assert.Throws<InvalidOperationException>(() => FinancialPostingWorkerPlan.Resolve("Worker", false, false, false));
        Assert.Throws<InvalidOperationException>(() => FinancialPostingWorkerPlan.Resolve("Both", true, true, false));
    }

    [Fact]
    public Task A_worker_survives_an_unavailable_database_and_reports_it_as_unhealthy() =>
        RunAsync(async (database, _) =>
        {
            var unreachable = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = database.Name + "_absent" }.ConnectionString;
            await using var worker = new PostingApp(unreachable, new Dictionary<string, string?>
            {
                ["FinancialPosting:HostRole"] = "Worker",
                ["FinancialPosting:Sales:PollIntervalSeconds"] = "1",
                ["FinancialPosting:CashDifferences:WorkerEnabled"] = "false"
            });
            var health = worker.Services.GetRequiredService<FinancialPostingWorkerHealth>();
            await WaitUntilAsync(() => Task.FromResult(health.Snapshot().Any(entry => entry.ConsecutiveRunFailures >= 3)));
            var response = await worker.CreateClient().GetAsync("health/financial-posting");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        });

    [Fact]
    public Task A_cancelled_posting_releases_its_row_lock_immediately() =>
        RunAsync(async (database, app) =>
        {
            await OpenAsync(database);
            var sale = await SellAsync(database, "p32-cancel");
            var postingId = await ScalarAsync<long>(database, $"SELECT id FROM sale_financial_postings WHERE sale_id = {sale.Id}");
            using var shutdown = new CancellationTokenSource();
            app.Accounting.HoldNextPosting();
            var stopping = app.SaleRunner.RunOnceAsync(shutdown.Token);
            await app.Accounting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            shutdown.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);

            // Another worker can claim the row at once (SKIP LOCKED would return nothing if it were still held).
            Assert.Equal(SaleFinancialPostingOutcome.Posted,
                (await ProcessAsync(app, postingId, 1, Cashier, skipLocked: true)).Outcome);
        });

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record SaleLine(long VariantId, int Quantity, decimal UnitPrice, decimal LineDiscount);

    private static async Task<RetailPOS.Application.Sales.ReadModels.SaleReadModel> SellLinesAsync(
        PostgresTestDatabase database,
        string key,
        SaleTaxPolicy policy,
        IReadOnlyList<SaleLine> lines,
        IReadOnlyList<CreateSalePayment> payments,
        decimal invoiceDiscount = 0m,
        decimal? expectedTax = null)
    {
        await using var db = Ctx(database);
        var validator = new SaleCashSessionValidator(
            new CashSessionStore(db), Mock.Of<IAuditService>(), Mock.Of<RetailPOS.Application.Pos.Abstractions.ICashSessionExpiryPublisher>(),
            TimeProvider.System, NullLogger<SaleCashSessionValidator>.Instance);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        var settings = SaleCreationCommandTestFactory.TaxPolicy(policy.RatePercent, policy.PricesIncludeTax);
        var tax = expectedTax ?? SaleCheckoutCalculator.Calculate(
            lines.Select(line => new SaleCheckoutLineInput(line.VariantId, line.Quantity, line.UnitPrice, line.LineDiscount)).ToArray(),
            invoiceDiscount, policy).Tax;
        var handler = SaleCreationCommandTestFactory.Create(
            db, validator, new SaleInvoiceNumberAllocator(db), Mock.Of<IAuditService>(),
            new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance),
            cache.Object, Mock.Of<ISaleEventPublisher>(), settings);
        return await handler.Handle(
            new CreateSaleCommand(
                OutletId, Terminal1, null,
                lines.Select(line => new CreateSaleLine(line.VariantId, line.Quantity, line.UnitPrice, line.LineDiscount, null, null)).ToArray(),
                invoiceDiscount, tax, payments[0].Method, payments, Cashier, null, null, key, null,
                CanApplyDiscount: true, CanOverrideDiscountLimit: true),
            CancellationToken.None);
    }

    private static async Task VoidAsync(PostgresTestDatabase database, long saleId, bool realCashRefund = false, TimeProvider? clock = null)
    {
        await using var db = Ctx(database);
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        ISaleCashRefundRecorder refunds = realCashRefund
            ? new SaleCashRefundRecorder(db, new CashSessionStore(db),
                new CashSessionMovementRecorder(db, new CashSessionStore(db), Events(db), TimeProvider.System))
            : new NoCashRefunds();
        var handler = new VoidSaleCommandHandler(
            new SaleVoidStore(db), new SaleReadStore(db), authorizer.Object,
            SaleCreationCommandTestFactory.FinancialPostings(db, clock),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(Mock.Of<IAuditService>()), new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance), refunds);
        await handler.Handle(new VoidSaleCommand(saleId, "customer changed mind", Manager, Terminal1), CancellationToken.None);
    }

    private static async Task RefundAsync(PostgresTestDatabase database, long saleId, IReadOnlyList<RefundSaleLine> lines)
    {
        await using var db = Ctx(database);
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var cache = new Mock<ISaleRefundCacheInvalidator>();
        var handler = new RefundSaleCommandHandler(
            new SaleVoidStore(db), new SaleReadStore(db), authorizer.Object,
            SaleCreationCommandTestFactory.FinancialPostings(db),
            new SaleRefundStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db, NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleRefundAuditWriter(Mock.Of<IAuditService>()), cache.Object, Mock.Of<ISaleRefundLogger>(), new NoCashRefunds());
        await handler.Handle(new RefundSaleCommand(saleId, lines, "returned", Manager), CancellationToken.None);
    }

    /// <summary>Period 1 ends on 30 November; period 2 covers December (both open).</summary>
    private static Task SplitPeriodsAtDecemberAsync(PostgresTestDatabase database) =>
        ExecuteAsync(database, """
            UPDATE accounting_periods SET end_date = TIMESTAMPTZ '2026-11-30 00:00:00+00', name = 'To November'
            WHERE period_number = 1;
            INSERT INTO accounting_periods (financial_year_id, period_number, name, start_date, end_date, status, created_at, updated_at)
            SELECT id, 2, 'December', TIMESTAMPTZ '2026-12-01 00:00:00+00', TIMESTAMPTZ '2026-12-31 00:00:00+00', 'Open', now(), now()
            FROM financial_years WHERE business_id = 1;
            """);

    private sealed record JournalLine(string Description, decimal Debit, decimal Credit);

    private static async Task<IReadOnlyList<JournalLine>> JournalLinesAsync(PostgresTestDatabase database, string referenceType, long saleId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT l.description, l.debit_amount, l.credit_amount
            FROM journal_entry_lines l JOIN journal_entries j ON j.id = l.journal_entry_id
            WHERE j.reference_type = '{referenceType}' AND j.reference_id = '{saleId}' AND j.status = 'Posted'
            """, connection);
        var lines = new List<JournalLine>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(new JournalLine(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2)));
        Assert.NotEmpty(lines);
        return lines;
    }

    private static decimal Debit(IReadOnlyList<JournalLine> lines, string description) =>
        lines.Where(line => line.Description == description).Sum(line => line.Debit);

    private static decimal Credit(IReadOnlyList<JournalLine> lines, string description) =>
        lines.Where(line => line.Description == description).Sum(line => line.Credit);

    private static void AssertBalanced(IReadOnlyList<JournalLine> lines) =>
        Assert.Equal(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));

    private static HttpClient Client(PostingApp app, string permissions, long businessId = 1, long userId = Manager)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Accountant");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition not reached within 60 seconds.");
            await Task.Delay(250);
        }
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }
}
