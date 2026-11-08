using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Exchanges;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// Exchange reversal (EX-14, C-05), history and receipts (EX-30, D-11) and the period/new-row races (T-02, T-05) on real
/// PostgreSQL (isolated test server, disposable template clone).
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private async Task<(long Id, string Number)> ConfirmExchangeAsync(HttpClient client, object body, object[] settlements)
    {
        var preview = await Send(client, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var (status, text) = await PostRawAsync(client, "/api/pos/exchanges",
            ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, Unique("ex"), settlements));
        Assert.True(status == HttpStatusCode.Created, text);
        using var json = JsonDocument.Parse(text);
        var data = json.RootElement.GetProperty("data");
        return (data.GetProperty("exchangeId").GetInt64(), data.GetProperty("exchangeNumber").GetString()!);
    }

    private static object ReverseBody(string key, object[] settlements, string reason = "Customer changed their mind about the exchange") =>
        new { reason, idempotencyKey = key, settlements };

    private static object ReExchangeBody(long replacementLineId, long replacementVariant) => new
    {
        receivingOutletId = UomPostgresFixture.Outlet,
        terminalId = UomPostgresFixture.Terminal,
        sourceSaleIds = Array.Empty<long>(),
        returns = new[] { new { sourceKind = "exchange_replacement_line", sourceLineId = replacementLineId, quantity = 1m, conditionSellable = true, packagingReusable = false } },
        replacements = new[] { new { variantId = replacementVariant, quantity = 1m, discountAmount = 0m } },
        invoiceDiscount = 0m,
        reasonCode = "size_fitting"
    };

    private async Task<string> ErrorCodeAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{method} {path}: {response.StatusCode} {text}");
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        using var json = JsonDocument.Parse(text);
        return json.RootElement.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : string.Empty;
    }

    [Fact]
    public async Task ExchangeReversal_RefundsTheCollectedCash_MirrorsStockAndJournal_AndIsIdempotent()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "RVA", 100m);
        var jacket = await PricedVariantAsync(owner, "RVB", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 2m, 100m);
        var shirtBefore = await OnHandAsync(shirt);
        var jacketBefore = await OnHandAsync(jacket);
        var (exchangeId, number) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m),
            [new { direction = "collect", method = "cash", amount = 22m, tendered = 22m }]);

        var eligibility = await Data(owner, $"/api/pos/exchanges/{exchangeId}/reversal-eligibility");
        Assert.True(eligibility.GetProperty("canReverse").GetBoolean());
        Assert.Empty(eligibility.GetProperty("blockingReasons").EnumerateArray());
        Assert.Equal("refund", eligibility.GetProperty("settlementDirection").GetString());
        Assert.Equal(22m, eligibility.GetProperty("settlementAmount").GetDecimal());

        // The reversal refunds, so a collection is refused.
        Assert.Equal("EXCHANGE_SETTLEMENT_MISMATCH", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(Unique("rv"), [new { direction = "collect", method = "cash", amount = 22m }]), HttpStatusCode.BadRequest));
        Assert.Equal("EXCHANGE_REASON_REQUIRED", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(Unique("rv"), [new { direction = "refund", method = "cash", amount = 22m }], reason: " "), HttpStatusCode.BadRequest));

        var key = Unique("rv");
        var body = ReverseBody(key, [new { direction = "refund", method = "cash", amount = 22m }]);
        var (status, text) = await PostRawAsync(owner, $"/api/pos/exchanges/{exchangeId}/reverse", body);
        Assert.True(status == HttpStatusCode.Created, text);
        using var result = JsonDocument.Parse(text);
        var reversalId = result.RootElement.GetProperty("data").GetProperty("reversalId").GetInt64();
        Assert.Equal($"{number}-RV", result.RootElement.GetProperty("data").GetProperty("reversalNumber").GetString());
        Assert.False(result.RootElement.GetProperty("data").GetProperty("replayed").GetBoolean());

        // Stock both ways and the shared returned quantity are back where they were before the exchange.
        Assert.Equal(shirtBefore, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(0m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync());
            var exchange = await db.Exchanges.AsNoTracking().SingleAsync(row => row.Id == exchangeId);
            Assert.Equal(ExchangeStatuses.Reversed, exchange.Status);
            Assert.Equal(reversalId, exchange.ReversalId);
            Assert.Equal(22m, exchange.NetAdjustmentGross);   // the original record is untouched

            var reversal = await db.ExchangeReversals.AsNoTracking().SingleAsync(row => row.Id == reversalId);
            Assert.Equal(-22m, reversal.NetAdjustmentGross);
            Assert.NotNull(reversal.JournalEntryId);

            // Journal: every account moves by exactly the opposite of the original, and both journals balance.
            var original = await db.JournalEntryLines.AsNoTracking().Where(row => row.JournalEntryId == exchange.JournalEntryId).ToListAsync();
            var mirror = await db.JournalEntryLines.AsNoTracking().Where(row => row.JournalEntryId == reversal.JournalEntryId).ToListAsync();
            Assert.Equal(mirror.Sum(row => row.DebitAmount), mirror.Sum(row => row.CreditAmount));
            Assert.Equal(original.Count, mirror.Count);
            foreach (var account in original.Concat(mirror).Select(row => row.AccountId).Distinct())
                Assert.Equal(0m, original.Concat(mirror).Where(row => row.AccountId == account).Sum(row => row.DebitAmount - row.CreditAmount));
            var journal = await db.JournalEntries.AsNoTracking().SingleAsync(row => row.Id == reversal.JournalEntryId);
            Assert.Equal(JournalEntryStatuses.Posted, journal.Status);

            // Cash left the drawer once, as an exchange cash-out for the reversal.
            var movement = await db.CashSessionMovements.AsNoTracking().SingleAsync(row => row.SourceType == "exchange_reversal" && row.SourceId == reversalId);
            Assert.Equal(CashSessionMovementTypes.ExchangeCashOut, movement.MovementType);
            Assert.Equal(-22m, movement.CashEffect);
            var settlement = await db.ExchangeSettlements.AsNoTracking().SingleAsync(row => row.ExchangeReversalId == reversalId);
            Assert.Equal("refund", settlement.Direction);
            Assert.Equal(exchangeId, settlement.ExchangeId);

            // Stock ledger: one mirror row per original row, linked to it.
            var originalRows = await db.StockLedgers.AsNoTracking().Where(row => row.ReferenceType == "exchange" && row.ReferenceId == exchangeId).ToListAsync();
            var mirrorRows = await db.StockLedgers.AsNoTracking().Where(row => row.ReferenceType == "exchange_reversal" && row.ReferenceId == reversalId).ToListAsync();
            Assert.Equal(originalRows.Count, mirrorRows.Count);
            foreach (var row in mirrorRows)
            {
                var source = originalRows.Single(item => item.Id == row.ReversalOfMovementId);
                Assert.Equal(source.QtyIn, row.QtyOut);
                Assert.Equal(source.QtyOut, row.QtyIn);
                Assert.Equal(source.MovementValue, row.MovementValue);
            }

            // Salesperson facts net to zero; receipt and audit evidence exist.
            var facts = await db.ExchangeSalespersonAdjustments.AsNoTracking().Where(row => row.ExchangeId == exchangeId).ToListAsync();
            Assert.Equal(2, facts.Count);
            Assert.Equal(0m, facts.Sum(row => row.SignedAmountNet));
            Assert.Equal(0m, facts.Sum(row => row.TargetEffectAmountNet));
            Assert.Equal(1, await db.ExchangeReceiptSnapshots.AsNoTracking().CountAsync(row => row.ExchangeReversalId == reversalId));
        });

        // Replay answers with the same reversal; the same key for something else is refused; a second reversal is refused.
        var (replay, replayText) = await PostRawAsync(owner, $"/api/pos/exchanges/{exchangeId}/reverse", body);
        Assert.Equal(HttpStatusCode.OK, replay);
        Assert.Contains($"\"reversalId\":{reversalId}", replayText);
        Assert.Equal("EXCHANGE_IDEMPOTENCY_MISMATCH", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(key, [new { direction = "refund", method = "cash", amount = 22m }], reason: "Another reason"), HttpStatusCode.Conflict));
        Assert.Equal("EXCHANGE_ALREADY_REVERSED", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(Unique("rv"), [new { direction = "refund", method = "cash", amount = 22m }]), HttpStatusCode.Conflict));

        var detail = await Data(owner, $"/api/pos/exchanges/{exchangeId}");
        Assert.Equal("reversed", detail.GetProperty("status").GetString());
        Assert.Equal($"{number}-RV", detail.GetProperty("reversal").GetProperty("reversalNumber").GetString());
        Assert.Equal(2, detail.GetProperty("settlements").GetArrayLength());
        Assert.Single(detail.GetProperty("settlements").EnumerateArray(), item => item.GetProperty("isReversal").GetBoolean());

        // The sale can be refunded normally again: the returned quantity was given back.
        await RefundAsync(owner, saleId, RefundBody(shirt, itemId, 2m, Unique("r")));
    }

    [Fact]
    public async Task ExchangeReversal_IsBlockedWhileTheReplacementIsReExchanged_AndAllowedAfterThatIsReversed()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner, allowReExchange: true);
        var shirt = await PricedVariantAsync(owner, "RVC", 100m);
        var jacket = await PricedVariantAsync(owner, "RVD", 100m);
        var coat = await PricedVariantAsync(owner, "RVE", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var (first, firstNumber) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m), []);
        long replacementLineId = 0;
        await _fixture.WithDbAsync(async db => replacementLineId = await db.ExchangeReplacementLines.AsNoTracking()
            .Where(row => row.ExchangeId == first).Select(row => row.Id).SingleAsync());
        var (second, _) = await ConfirmExchangeAsync(owner, ReExchangeBody(replacementLineId, coat), []);
        var jacketBefore = await OnHandAsync(jacket);
        var shirtBefore = await OnHandAsync(shirt);

        var eligibility = await Data(owner, $"/api/pos/exchanges/{first}/reversal-eligibility");
        Assert.False(eligibility.GetProperty("canReverse").GetBoolean());
        Assert.Equal("none", eligibility.GetProperty("settlementDirection").GetString());
        Assert.Equal("EXCHANGE_REVERSAL_BLOCKED_DEPENDENCY", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{first}/reverse",
            ReverseBody(Unique("rv"), []), HttpStatusCode.Conflict));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));
        Assert.Equal(shirtBefore, await OnHandAsync(shirt));
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(ExchangeStatuses.Completed, await db.Exchanges.AsNoTracking().Where(row => row.Id == first).Select(row => row.Status).SingleAsync());
            Assert.False(await db.ExchangeReversals.AsNoTracking().AnyAsync(row => row.ExchangeId == first));
        });

        // No cascade: the later exchange is reversed on its own, which gives the replacement line its quantity back.
        var (secondStatus, secondText) = await PostRawAsync(owner, $"/api/pos/exchanges/{second}/reverse", ReverseBody(Unique("rv"), []));
        Assert.True(secondStatus == HttpStatusCode.Created, secondText);
        await _fixture.WithDbAsync(async db => Assert.Equal(0m, await db.ExchangeReplacementLines.AsNoTracking()
            .Where(row => row.Id == replacementLineId).Select(row => row.ReturnedQuantity).SingleAsync()));
        var (firstStatus, firstText) = await PostRawAsync(owner, $"/api/pos/exchanges/{first}/reverse", ReverseBody(Unique("rv"), []));
        Assert.True(firstStatus == HttpStatusCode.Created, firstText);
        await _fixture.WithDbAsync(async db =>
            Assert.Equal(0m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync()));

        // I-12 item 3: the reversed exchange is not a re-exchange source; the lookup says why, like Preview does.
        Assert.Equal("EXCHANGE_SOURCE_INELIGIBLE", await ErrorCodeAsync(owner, HttpMethod.Get,
            $"/api/pos/exchanges/source-invoice?invoiceNumber={Uri.EscapeDataString(firstNumber)}", null, HttpStatusCode.UnprocessableEntity));
        Assert.Equal("EXCHANGE_SOURCE_INELIGIBLE", await ErrorCodeAsync(owner, HttpMethod.Post, "/api/pos/exchanges/preview",
            ReExchangeBody(replacementLineId, coat), HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task ExchangeReversal_IsRefused_WhenTheReturnedGoodsWereSoldAgain_AndChangesNothing()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "RVF", 100m, stock: 1m);
        var jacket = await PricedVariantAsync(owner, "RVG", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var (exchangeId, _) = await ConfirmExchangeAsync(owner, ExchangeBody(saleId, itemId, 1m, jacket, 1m), []);
        Assert.Equal(1m, await OnHandAsync(shirt));
        await SoldAsync(owner, shirt, 1m, 100m);   // the returned unit is sold to someone else
        Assert.Equal(0m, await OnHandAsync(shirt));
        var jacketBefore = await OnHandAsync(jacket);
        long journalsBefore = 0, ledgerBefore = 0;
        await _fixture.WithDbAsync(async db =>
        {
            journalsBefore = await db.JournalEntries.LongCountAsync();
            ledgerBefore = await db.StockLedgers.LongCountAsync();
        });

        var eligibility = await Data(owner, $"/api/pos/exchanges/{exchangeId}/reversal-eligibility");
        Assert.False(eligibility.GetProperty("canReverse").GetBoolean());
        Assert.NotEmpty(eligibility.GetProperty("blockingReasons").EnumerateArray());
        Assert.Equal("EXCHANGE_REVERSAL_STOCK_UNAVAILABLE", await ErrorCodeAsync(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse",
            ReverseBody(Unique("rv"), []), HttpStatusCode.UnprocessableEntity));

        Assert.Equal(0m, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(ExchangeStatuses.Completed, await db.Exchanges.AsNoTracking().Where(row => row.Id == exchangeId).Select(row => row.Status).SingleAsync());
            Assert.Equal(1m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync());
            Assert.False(await db.ExchangeReversals.AsNoTracking().AnyAsync(row => row.ExchangeId == exchangeId));
            Assert.Equal(journalsBefore, await db.JournalEntries.LongCountAsync());
            Assert.Equal(ledgerBefore, await db.StockLedgers.LongCountAsync());
        });
    }

    [Fact]
    public async Task ExchangeHistory_DetailBySaleAndReceipts_FollowBusinessAndOutletScope()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "RVH", 100m);
        var jacket = await PricedVariantAsync(owner, "RVI", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        string invoiceNumber = string.Empty;
        await _fixture.WithDbAsync(async db => invoiceNumber = await db.Sales.AsNoTracking().Where(row => row.Id == saleId).Select(row => row.SaleNumber).SingleAsync());
        var body = JsonSerializer.SerializeToElement(ExchangeBody(saleId, itemId, 1m, jacket, 1m))
            .EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value);
        body["internalNote"] = "Staff-only remark";
        var (exchangeId, number) = await ConfirmExchangeAsync(owner, body, [new { direction = "collect", method = "card", amount = 22m, externalReference = "CARD-998877" }]);

        // Owner: list by number and by original invoice, detail with the internal note, by-sale, receipt document and PDFs.
        var byNumber = await Data(owner, $"/api/pos/exchanges?search={number}&pageSize=500");
        Assert.Equal(100, byNumber.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(byNumber.GetProperty("items").EnumerateArray());
        Assert.Equal(exchangeId, item.GetProperty("id").GetInt64());
        Assert.Equal(invoiceNumber, item.GetProperty("originalInvoiceNumbers")[0].GetString());
        Assert.Equal("collect", item.GetProperty("settlementDirection").GetString());
        var byInvoice = await Data(owner, $"/api/pos/exchanges?search={invoiceNumber}&status=completed&outletId={UomPostgresFixture.Outlet}");
        Assert.Contains(byInvoice.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetInt64() == exchangeId);

        var detail = await Data(owner, $"/api/pos/exchanges/{exchangeId}");
        Assert.Equal("Staff-only remark", detail.GetProperty("internalNote").GetString());
        Assert.Equal(110m, detail.GetProperty("returnGross").GetDecimal());
        Assert.Equal("8877", detail.GetProperty("settlements")[0].GetProperty("referenceLastFour").GetString());
        Assert.Equal(shirt, detail.GetProperty("returns")[0].GetProperty("variantId").GetInt64());
        Assert.Equal(jacket, detail.GetProperty("replacements")[0].GetProperty("variantId").GetInt64());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("reversal").ValueKind);

        var bySale = await Data(owner, $"/api/pos/exchanges/by-sale/{saleId}");
        Assert.Contains(bySale.EnumerateArray(), row => row.GetProperty("id").GetInt64() == exchangeId);

        var receipt = await Data(owner, $"/api/pos/exchanges/{exchangeId}/receipt-document");
        Assert.Equal(number, receipt.GetProperty("document").GetProperty("exchangeNumber").GetString());
        Assert.True(receipt.GetProperty("variantNames").TryGetProperty(jacket.ToString(), out _));
        Assert.DoesNotContain("Staff-only remark", receipt.GetRawText());
        foreach (var mode in new[] { "thermal", "a4" })
        {
            using var pdf = await owner.GetAsync($"/api/pos/exchanges/{exchangeId}/receipt-pdf?mode={mode}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }

        await Send(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reprint", new { terminalId = UomPostgresFixture.Terminal });
        await Send(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/receipt-print-attempt", new { terminalId = UomPostgresFixture.Terminal, succeeded = true });
        await Send(owner, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/receipt-print-attempt", new { succeeded = false });
        await _fixture.WithDbAsync(async db =>
        {
            var actions = await db.ExchangeReceiptPrintHistory.AsNoTracking().Where(row => row.ExchangeId == exchangeId)
                .OrderBy(row => row.Id).Select(row => row.ActionType).ToListAsync();
            Assert.Equal(["reprint", "print", "print_attempt_failed"], actions);
        });

        // A cashier who can only read Outlet two never sees this Outlet's exchange.
        long outletTwoUser = 0;
        await _fixture.WithDbAsync(async db =>
        {
            var user = new User { BusinessId = UomPostgresFixture.Business, Name = "Outlet two cashier", Email = $"{Unique("o2")}@example.test",
                PasswordHash = "test-only", IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, OutletId = UomPostgresFixture.OutletTwo };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = user.Id, OutletId = UomPostgresFixture.OutletTwo, BusinessId = UomPostgresFixture.Business, IsPrimary = true, IsActive = true });
            await db.SaveChangesAsync();
            outletTwoUser = user.Id;
        });
        var scoped = _fixture.Client(outletTwoUser, "Cashier", "sales.view");
        var scopedList = await Data(scoped, $"/api/pos/exchanges?search={number}");
        Assert.Equal(0, scopedList.GetProperty("totalCount").GetInt32());
        // L3: a sale of an outlet the user cannot see answers exactly like a sale that does not exist.
        var outOfScope = await ErrorCodeAsync(scoped, HttpMethod.Get, $"/api/pos/exchanges/by-sale/{saleId}", null, HttpStatusCode.NotFound);
        var missing = await ErrorCodeAsync(scoped, HttpMethod.Get, "/api/pos/exchanges/by-sale/987654321", null, HttpStatusCode.NotFound);
        Assert.Equal("EXCHANGE_SOURCE_NOT_FOUND", outOfScope);
        Assert.Equal(missing, outOfScope);
        await ErrorCodeAsync(scoped, HttpMethod.Get, $"/api/pos/exchanges?outletId={UomPostgresFixture.Outlet}", null, HttpStatusCode.Forbidden);
        Assert.Equal("EXCHANGE_NOT_FOUND", await ErrorCodeAsync(scoped, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}", null, HttpStatusCode.NotFound));
        await ErrorCodeAsync(scoped, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}/receipt-document", null, HttpStatusCode.NotFound);
        await ErrorCodeAsync(scoped, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}/receipt-pdf", null, HttpStatusCode.NotFound);
        await ErrorCodeAsync(scoped, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reprint", new { }, HttpStatusCode.NotFound);

        // The restricted user (no readable outlet) and another business see nothing; a missing permission is 403.
        var restricted = _fixture.Client(UomPostgresFixture.RestrictedUserId, "Cashier", "sales.view");
        await ErrorCodeAsync(restricted, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}", null, HttpStatusCode.NotFound);
        await ErrorCodeAsync(restricted, HttpMethod.Get, "/api/pos/exchanges", null, HttpStatusCode.Forbidden);
        var foreign = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        await ErrorCodeAsync(foreign, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}", null, HttpStatusCode.NotFound);
        await ErrorCodeAsync(foreign, HttpMethod.Get, $"/api/pos/exchanges/by-sale/{saleId}", null, HttpStatusCode.NotFound);
        await ErrorCodeAsync(foreign, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}/reversal-eligibility", null, HttpStatusCode.NotFound);
        var noPermission = _fixture.Client(UomPostgresFixture.OwnerId, "Cashier", "pos.exchange.create");
        await ErrorCodeAsync(noPermission, HttpMethod.Get, $"/api/pos/exchanges/{exchangeId}", null, HttpStatusCode.Forbidden);
        await ErrorCodeAsync(scoped, HttpMethod.Post, $"/api/pos/exchanges/{exchangeId}/reverse", ReverseBody(Unique("rv"), []), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Exchange_T02_ALockedAccountingPeriod_RefusesTheExchange_WithNoMutation()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "RVJ", 100m);
        var jacket = await PricedVariantAsync(owner, "RVK", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var shirtBefore = await OnHandAsync(shirt);
        var jacketBefore = await OnHandAsync(jacket);
        long journalsBefore = 0, movementsBefore = 0, exchangesBefore = 0;
        var periodIds = new List<long>();
        await _fixture.WithDbAsync(async db =>
        {
            journalsBefore = await db.JournalEntries.LongCountAsync();
            movementsBefore = await db.CashSessionMovements.LongCountAsync();
            exchangesBefore = await db.Exchanges.LongCountAsync();
            var today = DateTime.UtcNow.Date;
            var years = await db.FinancialYears.AsNoTracking().Where(year => year.BusinessId == UomPostgresFixture.Business).Select(year => year.Id).ToListAsync();
            periodIds = await db.AccountingPeriods.Where(period => years.Contains(period.FinancialYearId)
                    && period.StartDate.Date <= today && period.EndDate.Date >= today && period.Status == AccountingPeriodStatuses.Open)
                .Select(period => period.Id).ToListAsync();
            Assert.NotEmpty(periodIds);
            await db.AccountingPeriods.Where(period => periodIds.Contains(period.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.Status, AccountingPeriodStatuses.Locked));
        });
        try
        {
            Assert.Equal("EXCHANGE_ACCOUNTING_PERIOD_CLOSED", await ErrorCodeAsync(owner, HttpMethod.Post, "/api/pos/exchanges",
                ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, Unique("pl"),
                    [new { direction = "collect", method = "cash", amount = 22m }]), HttpStatusCode.UnprocessableEntity));
        }
        finally
        {
            await _fixture.WithDbAsync(db => db.AccountingPeriods.Where(period => periodIds.Contains(period.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(period => period.Status, AccountingPeriodStatuses.Open)));
        }

        Assert.Equal(shirtBefore, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(0m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync());
            Assert.Equal(journalsBefore, await db.JournalEntries.LongCountAsync());
            Assert.Equal(movementsBefore, await db.CashSessionMovements.LongCountAsync());
            Assert.Equal(exchangesBefore, await db.Exchanges.LongCountAsync());
        });
    }

    [Fact]
    public async Task Exchange_T05_ConcurrentExchanges_CreateTheMissingReceivingOutletRowExactlyOnce()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "RVL", 100m);
        var jacket = await PricedVariantAsync(owner, "RVM", 100m);
        var first = await SoldAsync(owner, shirt, 1m, 100m);
        var second = await SoldAsync(owner, shirt, 1m, 100m);

        // Outlet two receives the exchanges: a terminal, an open session, an invoice prefix and replacement stock.
        string? previousPrefix = null;
        await _fixture.WithDbAsync(async db =>
        {
            if (!await db.PosTerminals.AnyAsync(row => row.Id == UomPostgresFixture.TerminalTwo))
            {
                db.PosTerminals.Add(new PosTerminal { Id = UomPostgresFixture.TerminalTwo, OutletId = UomPostgresFixture.OutletTwo, Name = "T2", Code = "T2", IsActive = true, IsDefault = true });
                await db.SaveChangesAsync();
            }

            if (!await db.CashSessions.AnyAsync(row => row.TerminalId == UomPostgresFixture.TerminalTwo && row.Status == "open"))
                db.CashSessions.Add(new CashSession
                {
                    BusinessId = UomPostgresFixture.Business, OutletId = UomPostgresFixture.OutletTwo, TerminalId = UomPostgresFixture.TerminalTwo,
                    BusinessDate = DateTime.UtcNow.Date, OpenedAt = DateTime.UtcNow.AddHours(-1), OpeningCashAmount = 1000m, Status = "open",
                    OpenedByUserId = UomPostgresFixture.OwnerId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });
            var outlet = await db.Outlets.SingleAsync(row => row.Id == UomPostgresFixture.OutletTwo);
            previousPrefix = outlet.InvoicePrefix;
            outlet.InvoicePrefix = "UO2";
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory { VariantId = jacket, LocationId = UomPostgresFixture.OutletTwo, LocationType = "outlet", Quantity = 5m });
            await db.SaveChangesAsync();
            Assert.False(await db.Inventories.AnyAsync(row => row.VariantId == shirt && row.LocationId == UomPostgresFixture.OutletTwo));
        });

        try
        {
            object Body((long SaleId, long ItemId) sale) => new
            {
                receivingOutletId = UomPostgresFixture.OutletTwo,
                terminalId = UomPostgresFixture.TerminalTwo,
                sourceSaleIds = new[] { sale.SaleId },
                returns = new[] { new { sourceKind = "sale_item", sourceLineId = sale.ItemId, quantity = 1m, conditionSellable = true, packagingReusable = false } },
                replacements = new[] { new { variantId = jacket, quantity = 1m, discountAmount = 0m } },
                invoiceDiscount = 0m,
                reasonCode = "size_fitting"
            };
            var bodies = new[] { Body(first), Body(second) };
            var hashes = new List<string>();
            foreach (var item in bodies)
                hashes.Add((await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", item)).GetProperty("previewHash").GetString()!);

            var outcomes = await Task.WhenAll(bodies.Select((item, index) =>
                PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(item, hashes[index], Unique("t5"), []))));
            var succeeded = outcomes.Count(outcome => outcome.Status == HttpStatusCode.Created);
            Assert.True(succeeded >= 1, string.Join(" | ", outcomes.Select(outcome => outcome.Body)));
            foreach (var loser in outcomes.Where(outcome => outcome.Status != HttpStatusCode.Created))
            {
                Assert.Equal(HttpStatusCode.Conflict, loser.Status);
                Assert.Contains("EXCHANGE_CONCURRENCY_CONFLICT", loser.Body);
            }

            await _fixture.WithDbAsync(async db =>
            {
                var rows = await db.Inventories.AsNoTracking().Where(row => row.VariantId == shirt && row.LocationId == UomPostgresFixture.OutletTwo && row.LocationType == "outlet").ToListAsync();
                Assert.Equal(succeeded, (int)Assert.Single(rows).Quantity);
                Assert.Equal(5m - succeeded, await db.Inventories.AsNoTracking().Where(row => row.VariantId == jacket && row.LocationId == UomPostgresFixture.OutletTwo).Select(row => row.Quantity).SingleAsync());
                var itemIds = new List<long> { first.ItemId, second.ItemId };
                Assert.Equal(succeeded, (int)await db.SaleItems.AsNoTracking().Where(row => itemIds.Contains(row.Id)).SumAsync(row => row.ReturnedQuantity));
                var saleIds = new List<long> { first.SaleId, second.SaleId };
                Assert.Equal(succeeded, await db.ExchangeSourceInvoices.AsNoTracking().CountAsync(row => saleIds.Contains(row.SaleId)));
            });
        }
        finally
        {
            await _fixture.WithDbAsync(async db =>
            {
                var outlet = await db.Outlets.SingleAsync(row => row.Id == UomPostgresFixture.OutletTwo);
                outlet.InvoicePrefix = previousPrefix;
                await db.SaveChangesAsync();
            });
        }
    }
}
