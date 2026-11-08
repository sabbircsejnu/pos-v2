using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Application.SalesReports;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.OwnerDashboards;
using RetailPOS.Infrastructure.SalesReports;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// Exchange reporting on real PostgreSQL: owner dashboard net sales (EX-01, EX-20, D-12), exchange metrics (EX-20, D-06,
/// D-07), the mobile sales report without double counting (Stage 0 VI-19) and the re-exchange lookup (EX-04).
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private async Task<string> SessionBusinessDateAsync()
    {
        var date = string.Empty;
        await _fixture.WithDbAsync(async db => date = (await db.CashSessions.AsNoTracking()
                .Where(row => row.OutletId == UomPostgresFixture.Outlet && row.Status == "open")
                .Select(row => row.BusinessDate).FirstAsync())
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return date;
    }

    private async Task<decimal> DashboardNetSalesAsync(HttpClient owner, string date)
    {
        var summary = await Data(owner, $"/api/v1/owner-dashboard/sales/summary?from={date}&to={date}&outletId={UomPostgresFixture.Outlet}");
        var value = summary.GetProperty("netSales").GetProperty("value");
        return value.ValueKind == JsonValueKind.Null ? 0m : value.GetDecimal();
    }

    /// <summary>
    /// A fresh cashier of the outlet. The mobile report fails closed on any refunded sale in scope without a posted
    /// journal, and other tests of this shared database leave such sales behind; filtering by this cashier isolates it.
    /// </summary>
    private async Task<long> NewOutletCashierAsync()
    {
        long id = 0;
        await _fixture.WithDbAsync(async db =>
        {
            var role = new RetailPOS.Core.Entities.Role { Name = $"ExchangeCashier{Guid.NewGuid():N}"[..30], Permissions = "[\"*\"]" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            var user = new RetailPOS.Core.Entities.User
            {
                BusinessId = UomPostgresFixture.Business, RoleId = role.Id, Name = "Report cashier", Email = $"{Unique("rc")}@example.test",
                PasswordHash = "x", IsActive = true, InventoryLocationAccessScope = RetailPOS.Core.Entities.User.InventoryAccessAssignedOnly,
                OutletId = UomPostgresFixture.Outlet
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOutletAssignments.Add(new RetailPOS.Core.Entities.UserOutletAssignment
            {
                UserId = user.Id, OutletId = UomPostgresFixture.Outlet, BusinessId = UomPostgresFixture.Business, IsPrimary = true, IsActive = true
            });
            await db.SaveChangesAsync();
            id = user.Id;
        });
        return id;
    }

    private async Task<decimal> MobileNetRevenueAsync(DateTime startUtc, DateTime endUtc, long cashierId)
    {
        decimal net = 0m;
        await _fixture.WithDbAsync(async db =>
        {
            var result = await new SalesReportReadStore(db).GetMobileSalesDashboardAsync(
                new SalesReportReadScope([UomPostgresFixture.Outlet]),
                new SalesReportFilter(startUtc, endUtc, null, null, CashierId: cashierId),
                CancellationToken.None);
            net = result.NetRevenue;
        });
        return net;
    }

    [Fact]
    public async Task ExchangeReporting_DashboardMetricsAndMobileReport_CountTheExchangeOnce_AndItsReversalOnItsOwnDate()
    {
        await SaveExchangePolicyAsync(Owner());
        var cashierId = await NewOutletCashierAsync();
        var owner = _fixture.Client(cashierId, "BusinessOwner", "*");
        var shirt = await PricedVariantAsync(owner, "RPA", 100m);
        var jacket = await PricedVariantAsync(owner, "RPB", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 2m, 100m);
        var date = await SessionBusinessDateAsync();
        var metricsPath = $"/api/pos/exchanges/metrics?from={date}&to={date}&outletId={UomPostgresFixture.Outlet}";
        var windowStart = DateTime.UtcNow.AddHours(-1);

        var dashboardBefore = await DashboardNetSalesAsync(owner, date);
        var metricsBefore = await Data(owner, metricsPath);
        var mobileBefore = await MobileNetRevenueAsync(windowStart, DateTime.UtcNow.AddHours(1), cashierId);

        // Return 1 shirt (100 net, 110 gross) for a jacket (120 net, 132 gross): +20 net, +22 gross.
        var (exchangeId, _) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m),
            [new { direction = "collect", method = "cash", amount = 22m, tendered = 22m }]);

        Assert.Equal(dashboardBefore + 20m, await DashboardNetSalesAsync(owner, date));
        var metrics = await Data(owner, metricsPath);
        Assert.Equal(date, metrics.GetProperty("from").GetString());
        Assert.Equal(metricsBefore.GetProperty("exchangeCount").GetInt32() + 1, metrics.GetProperty("exchangeCount").GetInt32());
        Assert.Equal(metricsBefore.GetProperty("netAdjustmentNet").GetDecimal() + 20m, metrics.GetProperty("netAdjustmentNet").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("netAdjustmentGross").GetDecimal() + 22m, metrics.GetProperty("netAdjustmentGross").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("replacementGross").GetDecimal() + 132m, metrics.GetProperty("replacementGross").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("returnGross").GetDecimal() + 110m, metrics.GetProperty("returnGross").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("replacementNet").GetDecimal() + 120m, metrics.GetProperty("replacementNet").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("returnNet").GetDecimal() + 100m, metrics.GetProperty("returnNet").GetDecimal());
        var outlet = Assert.Single(metrics.GetProperty("byOutlet").EnumerateArray());
        Assert.Equal(UomPostgresFixture.Outlet, outlet.GetProperty("outletId").GetInt64());
        Assert.Equal(metrics.GetProperty("netAdjustmentNet").GetDecimal(), outlet.GetProperty("netAdjustmentNet").GetDecimal());
        Assert.NotEmpty(metrics.GetProperty("bySalesperson").EnumerateArray());

        // The exchange journal carries a "Sales return" debit, yet the mobile report moves by the gross adjustment only.
        var mobileAfter = await MobileNetRevenueAsync(windowStart, DateTime.UtcNow.AddHours(1), cashierId);
        Assert.Equal(mobileBefore + 22m, mobileAfter);

        var (status, text) = await PostRawAsync(owner, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(Unique("rv"), [new { direction = "refund", method = "cash", amount = 22m }]));
        Assert.True(status == HttpStatusCode.Created, text);

        // Same business date: the reversal offsets the exchange, which still counts as having happened.
        Assert.Equal(dashboardBefore, await DashboardNetSalesAsync(owner, date));
        var reversed = await Data(owner, metricsPath);
        Assert.Equal(metricsBefore.GetProperty("exchangeCount").GetInt32() + 1, reversed.GetProperty("exchangeCount").GetInt32());
        Assert.Equal(metricsBefore.GetProperty("reversalCount").GetInt32() + 1, reversed.GetProperty("reversalCount").GetInt32());
        Assert.Equal(metricsBefore.GetProperty("netAdjustmentNet").GetDecimal(), reversed.GetProperty("netAdjustmentNet").GetDecimal());
        Assert.Equal(metricsBefore.GetProperty("netAdjustmentGross").GetDecimal(), reversed.GetProperty("netAdjustmentGross").GetDecimal());
        Assert.Equal(
            metricsBefore.GetProperty("bySalesperson").EnumerateArray().Sum(row => row.GetProperty("signedNet").GetDecimal()),
            reversed.GetProperty("bySalesperson").EnumerateArray().Sum(row => row.GetProperty("signedNet").GetDecimal()));
        Assert.Equal(mobileBefore, await MobileNetRevenueAsync(windowStart, DateTime.UtcNow.AddHours(1), cashierId));

        // A reversal dated outside the range is not counted against it: the next day shows nothing for this exchange.
        var tomorrow = DateOnly.ParseExact(date, "yyyy-MM-dd").AddDays(1).ToString("yyyy-MM-dd");
        var nextDay = await Data(owner, $"/api/pos/exchanges/metrics?from={tomorrow}&to={tomorrow}");
        Assert.Equal(0, nextDay.GetProperty("reversalCount").GetInt32());

        Assert.Equal("EXCHANGE_INVALID_REQUEST", await ErrorCodeAsync(owner, HttpMethod.Get,
            $"/api/pos/exchanges/metrics?from={tomorrow}&to={date}", null, HttpStatusCode.BadRequest));
        var noSales = _fixture.Client(UomPostgresFixture.OwnerId, "Salesman", "pos.exchange.create");
        await ErrorCodeAsync(noSales, HttpMethod.Get, metricsPath, null, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExchangeLookup_FindsAnExchangeByItsNumber_ForReExchange_AndTheThirdExchangeNeedsChainApproval()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner, allowReExchange: true);   // maxExchangeCountPerLineage = 2
        var shirt = await PricedVariantAsync(owner, "RLA", 100m);
        var jacket = await PricedVariantAsync(owner, "RLB", 100m);
        var coat = await PricedVariantAsync(owner, "RLC", 100m);
        var hat = await PricedVariantAsync(owner, "RLD", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var saleNumber = string.Empty;
        await _fixture.WithDbAsync(async db => saleNumber = await db.Sales.AsNoTracking().Where(row => row.Id == saleId).Select(row => row.SaleNumber).SingleAsync());

        var sale = await Data(owner, $"/api/pos/exchanges/source-invoice?invoiceNumber={Uri.EscapeDataString(saleNumber)}");
        Assert.Equal("sale", sale.GetProperty("kind").GetString());
        Assert.Equal(saleId, sale.GetProperty("saleId").GetInt64());
        Assert.Equal("sale_item", sale.GetProperty("lines")[0].GetProperty("sourceKind").GetString());

        var (first, firstNumber) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m), []);
        var source = await Data(owner, $"/api/pos/exchanges/source-invoice?invoiceNumber={Uri.EscapeDataString(firstNumber)}");
        Assert.Equal("exchange", source.GetProperty("kind").GetString());
        Assert.Equal(0L, source.GetProperty("saleId").GetInt64());
        Assert.Equal(firstNumber, source.GetProperty("invoiceNumber").GetString());
        Assert.True(source.GetProperty("eligible").GetBoolean());
        var line = Assert.Single(source.GetProperty("lines").EnumerateArray());
        Assert.Equal("exchange_replacement_line", line.GetProperty("sourceKind").GetString());
        Assert.Equal(jacket, line.GetProperty("variantId").GetInt64());
        Assert.Equal(1m, line.GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(110m, line.GetProperty("paidGross").GetDecimal());
        Assert.True(line.GetProperty("evidenceAvailable").GetBoolean());
        long expectedLineId = 0;
        await _fixture.WithDbAsync(async db => expectedLineId = await db.ExchangeReplacementLines.AsNoTracking()
            .Where(row => row.ExchangeId == first).Select(row => row.Id).SingleAsync());
        Assert.Equal(expectedLineId, line.GetProperty("saleItemId").GetInt64());

        // Second exchange in the lineage, from the looked-up line: within the limit, no chain approval.
        var secondBody = ReExchangeBody(line.GetProperty("saleItemId").GetInt64(), coat);
        var secondPreview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", secondBody);
        Assert.DoesNotContain("chain", secondPreview.GetProperty("requiredApprovals").EnumerateArray().Select(value => value.GetString()));
        var (_, secondNumber) = await ConfirmExchangeAsync(owner, secondBody, []);

        var consumed = await Data(owner, $"/api/pos/exchanges/source-invoice?invoiceNumber={Uri.EscapeDataString(firstNumber)}");
        Assert.Equal(0m, consumed.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());

        // Third exchange beyond MaxExchangeCountPerLineage = 2: a chain approval is required.
        var second = await Data(owner, $"/api/pos/exchanges/source-invoice?invoiceNumber={Uri.EscapeDataString(secondNumber)}");
        var thirdBody = ReExchangeBody(second.GetProperty("lines")[0].GetProperty("saleItemId").GetInt64(), hat);
        var thirdPreview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", thirdBody);
        Assert.Contains("chain", thirdPreview.GetProperty("requiredApprovals").EnumerateArray().Select(value => value.GetString()));

        // Unknown numbers stay not found.
        Assert.Equal("EXCHANGE_SOURCE_NOT_FOUND", await ErrorCodeAsync(owner, HttpMethod.Get,
            "/api/pos/exchanges/source-invoice?invoiceNumber=NO-SUCH-NUMBER", null, HttpStatusCode.NotFound));
    }

    private async Task<decimal> DashboardCogsAsync(string date)
    {
        var day = DateOnly.ParseExact(date, "yyyy-MM-dd");
        var cogs = 0m;
        await _fixture.WithDbAsync(async db =>
        {
            var scope = new OwnerDashboardScope(UomPostgresFixture.Business, "UTC", day, [], [], [], null, false, false);
            var facts = await new SalesFinanceReadStore(db).GetSalesFactsAsync(scope, [UomPostgresFixture.Outlet], day, day, CancellationToken.None);
            cogs = facts.Days.Sum(row => row.Cogs);
        });
        return cogs;
    }

    /// <summary>
    /// L4: packaging issued in a fraction cannot come back on reversal (I-07), so its value stays expensed. The dashboard
    /// must keep exactly that value in COGS, the same as the stock ledger and the two journals.
    /// </summary>
    [Fact]
    public async Task ExchangeReversal_KeepsFractionalPackagingInDashboardCogs_ReconcilingWithLedgerAndJournals()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var wrap = await _fixture.CreateVariantAsync("RCW");
        var wrapConfig = await SaveConfigAsync(owner, wrap, "ml", Jar500(owner), [CustomMl(1m)]);
        await PurchaseAsync(owner, wrap, wrapConfig.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 1000m); // 2 per ml
        var (attar, config) = await StockedAttarAsync(owner, "RCA",
            [Pack("P12", 12m, 100m, [new { componentVariantId = wrap, quantity = 2.5m, isRequired = true }]), CustomMl(5m)]);
        var shirt = await PricedVariantAsync(owner, "RCS", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var date = await SessionBusinessDateAsync();
        var cogsBefore = await DashboardCogsAsync(date);
        var wrapBefore = await OnHandAsync(wrap);

        // Return the shirt (100 net) for one 12 ml pack (100 net): an even exchange that issues 2.5 ml of wrap.
        var body = new
        {
            receivingOutletId = UomPostgresFixture.Outlet,
            terminalId = UomPostgresFixture.Terminal,
            sourceSaleIds = new[] { saleId },
            returns = new[] { new { sourceKind = "sale_item", sourceLineId = itemId, quantity = 1m, conditionSellable = true, packagingReusable = false } },
            replacements = new[] { new { variantId = attar, quantity = 1m, discountAmount = 0m, sellingOptionId = (long?)Option(config, "P12"), expectedConfigVersionId = (long?)config.GetProperty("configId").GetInt64() } },
            invoiceDiscount = 0m,
            reasonCode = "size_fitting"
        };
        var (exchangeId, _) = await ConfirmExchangeAsync(owner, body, []);
        Assert.Equal(wrapBefore - 2.5m, await OnHandAsync(wrap));
        var cogsAfterExchange = await DashboardCogsAsync(date);

        var (status, text) = await PostRawAsync(owner, $"/api/pos/exchanges/{exchangeId}/reverse", ReverseBody(Unique("rv"), []));
        Assert.True(status == HttpStatusCode.Created, text);
        using var result = JsonDocument.Parse(text);
        var reversalId = result.RootElement.GetProperty("data").GetProperty("reversalId").GetInt64();
        Assert.Equal(wrapBefore - 2.5m, await OnHandAsync(wrap));   // half-used wrap cannot go back on the shelf

        decimal ledgerCogs = 0m, retained = 0m, journalCogsNet = 0m, exchangeCogs = 0m;
        await _fixture.WithDbAsync(async db =>
        {
            var exchange = await db.Exchanges.AsNoTracking().SingleAsync(row => row.Id == exchangeId);
            exchangeCogs = exchange.ReplacementCost - exchange.ReturnCost;
            retained = (await db.ExchangeReversals.AsNoTracking().SingleAsync(row => row.Id == reversalId)).RetainedPackagingCost;

            // COGS by the stock ledger: value that left the shelf minus value that came back, over both documents.
            var rows = await db.StockLedgers.AsNoTracking()
                .Where(row => (row.ReferenceType == StockLedgerReferenceType.Exchange && row.ReferenceId == exchangeId)
                    || (row.ReferenceType == StockLedgerReferenceType.ExchangeReversal && row.ReferenceId == reversalId))
                .ToListAsync();
            ledgerCogs = rows.Sum(row => row.QtyOut > 0m ? row.MovementValue : -row.MovementValue);

            // The two journals leave exactly the retained value on one account (COGS) and its opposite on another (inventory).
            var reversal = await db.ExchangeReversals.AsNoTracking().SingleAsync(row => row.Id == reversalId);
            var lines = await db.JournalEntryLines.AsNoTracking()
                .Where(row => row.JournalEntryId == exchange.JournalEntryId || row.JournalEntryId == reversal.JournalEntryId)
                .ToListAsync();
            var byAccount = lines.GroupBy(row => row.AccountId).Select(group => group.Sum(row => row.DebitAmount - row.CreditAmount)).Where(net => net != 0m).ToList();
            Assert.Equal(2, byAccount.Count);
            journalCogsNet = byAccount.Max();
        });

        Assert.Equal(5m, retained);                                         // 2.5 ml at 2 per ml
        Assert.Equal(cogsBefore + exchangeCogs, cogsAfterExchange);
        Assert.Equal(retained, ledgerCogs);
        Assert.Equal(retained, journalCogsNet);
        Assert.Equal(cogsBefore + retained, await DashboardCogsAsync(date));
    }
}
