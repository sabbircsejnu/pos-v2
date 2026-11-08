using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Exchanges;
using RetailPOS.Application.Exchanges;

namespace RetailPOS.Tests.Uom;

/// <summary>
/// Product Exchange on real PostgreSQL (isolated A2 test server, disposable template clone): atomicity (T-01), shared
/// returned quantity with refunds (C-03), concurrency (T-03 subset), void guard (D-14), legacy link (D-01), policy
/// (D-16, DN-01), supervisor approval (C-02, OC-01) and idempotency.
/// </summary>
public sealed partial class UomPostgresHttpTests
{
    private const decimal ExchangeVat = 0.10m;

    private async Task<int?> SaveExchangePolicyAsync(HttpClient owner, int window = 30, bool allowLower = true, decimal? threshold = null,
        string phoneMode = "optional", bool allowReExchange = true, bool expectSuccess = true)
    {
        var current = await Send(owner, HttpMethod.Get, "/api/pos/exchanges/policy", new { });
        int? version = current.GetProperty("isConfigured").GetBoolean() ? current.GetProperty("versionNo").GetInt32() : null;
        using var response = await owner.PutAsJsonAsync("/api/pos/exchanges/policy", new
        {
            expectedVersionNo = version,
            isEnabled = true,
            allowLowerPriceExchange = allowLower,
            exchangeWindowDays = window,
            allowReplacementReExchange = allowReExchange,
            replacementWindowDays = allowReExchange ? 7 : (int?)null,
            maxExchangeCountPerLineage = allowReExchange ? 2 : (int?)null,
            phoneVerificationMode = phoneMode,
            highValueApprovalThreshold = threshold,
            negativeTargetAdjustmentMode = "track_only"
        });
        var text = await response.Content.ReadAsStringAsync();
        if (expectSuccess)
            Assert.True(response.IsSuccessStatusCode, text);
        return (int)response.StatusCode;
    }

    private async Task<long> PricedVariantAsync(HttpClient owner, string code, decimal price, decimal stock = 20m, decimal cost = 40m)
    {
        var variant = await _fixture.CreateVariantAsync(code);
        await _fixture.WithDbAsync(async db =>
        {
            var product = await db.Products.SingleAsync(row => row.Id == variant);
            product.BasePrice = price;
            await db.SaveChangesAsync();
        });
        await ReceivePcsAsync(owner, variant, stock, cost);
        return variant;
    }

    private static object ExchangeBody(long saleId, long itemId, decimal returnQty, long replacementVariant, decimal replacementQty, bool sellable = true) => new
    {
        receivingOutletId = UomPostgresFixture.Outlet,
        terminalId = UomPostgresFixture.Terminal,
        sourceSaleIds = new[] { saleId },
        returns = new[] { new { sourceKind = "sale_item", sourceLineId = itemId, quantity = returnQty, conditionSellable = sellable, packagingReusable = false } },
        replacements = new[] { new { variantId = replacementVariant, quantity = replacementQty, discountAmount = 0m } },
        invoiceDiscount = 0m,
        reasonCode = "size_fitting"
    };

    private static object ConfirmBody(object exchange, string previewHash, string key, object[] settlements, string[]? tokens = null)
    {
        var json = JsonSerializer.SerializeToElement(exchange);
        var map = json.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value);
        map["previewHash"] = previewHash;
        map["idempotencyKey"] = key;
        map["settlements"] = settlements;
        map["approvalTokens"] = tokens ?? [];
        return map;
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostRawAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<(long SaleId, long ItemId)> SoldAsync(HttpClient owner, long variant, decimal quantity, decimal price)
    {
        var sale = await SellAsync(owner, new SaleLine(variant, quantity, price, null, null));
        return (sale.GetProperty("id").GetInt64(), sale.GetProperty("items")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Exchange_HigherValue_CommitsEveryEffectAtomically_OnTheExchangeDate()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "EXA", 100m);
        var jacket = await PricedVariantAsync(owner, "EXB", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 2m, 100m);
        var shirtBefore = await OnHandAsync(shirt);
        var jacketBefore = await OnHandAsync(jacket);

        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        Assert.Equal(110m, preview.GetProperty("returnGross").GetDecimal());       // 100 + 10% VAT, original allocation
        Assert.Equal(132m, preview.GetProperty("replacementGross").GetDecimal());  // 120 + 10% VAT, today's price
        Assert.Equal(22m, preview.GetProperty("netAdjustmentGross").GetDecimal());
        Assert.Equal("collect", preview.GetProperty("settlementDirection").GetString());
        Assert.Empty(preview.GetProperty("requiredApprovals").EnumerateArray());
        Assert.Equal("module_inactive", preview.GetProperty("loyaltyStatus").GetString());

        var key = Unique("ex");
        var (status, text) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, key,
            [new { direction = "collect", method = "cash", amount = 22m, tendered = 30m }]));
        Assert.True(status == HttpStatusCode.Created, text);
        using var result = JsonDocument.Parse(text);
        var exchangeId = result.RootElement.GetProperty("data").GetProperty("exchangeId").GetInt64();
        var number = result.RootElement.GetProperty("data").GetProperty("exchangeNumber").GetString()!;
        Assert.StartsWith($"UOMX{DateTime.UtcNow:yyMMdd}", number, StringComparison.Ordinal);

        Assert.Equal(shirtBefore + 1m, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore - 1m, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
        {
            var exchange = await db.Exchanges.AsNoTracking().Include(row => row.ReturnLines).Include(row => row.ReplacementLines)
                .Include(row => row.Settlements).SingleAsync(row => row.Id == exchangeId);
            Assert.Equal(ExchangeStatuses.Completed, exchange.Status);
            Assert.Equal(22m, exchange.NetAdjustmentGross);
            Assert.Equal(20m, exchange.NetAdjustmentNet);
            Assert.Equal("module_inactive", exchange.LoyaltyStatus);
            Assert.NotNull(exchange.JournalEntryId);
            Assert.Equal(40m, exchange.ReturnCost);   // the original cost snapshot, never today's
            Assert.Single(exchange.ReturnLines);
            Assert.Single(exchange.ReplacementLines);
            var settlement = Assert.Single(exchange.Settlements);
            Assert.Equal(8m, settlement.ChangeGiven);
            Assert.NotNull(settlement.CashSessionMovementId);

            // C-03 / D-14: the shared entitlement moved, the original sale itself did not.
            var item = await db.SaleItems.AsNoTracking().SingleAsync(row => row.Id == itemId);
            Assert.Equal(1m, item.ReturnedQuantity);
            Assert.Equal("completed", await db.Sales.AsNoTracking().Where(row => row.Id == saleId).Select(row => row.Status).SingleAsync());

            var movement = await db.CashSessionMovements.AsNoTracking().SingleAsync(row => row.SourceType == "exchange" && row.SourceId == exchangeId);
            Assert.Equal(CashSessionMovementTypes.ExchangeCashIn, movement.MovementType);
            Assert.Equal(22m, movement.CashEffect);

            var journal = await db.JournalEntries.AsNoTracking().SingleAsync(row => row.Id == exchange.JournalEntryId);
            Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
            Assert.Equal(DateTime.UtcNow.Date, journal.PostingDate.ToUniversalTime().Date);
            var lines = await db.JournalEntryLines.AsNoTracking().Where(row => row.JournalEntryId == journal.Id).ToListAsync();
            Assert.Equal(lines.Sum(row => row.DebitAmount), lines.Sum(row => row.CreditAmount));

            Assert.Equal(2, await db.StockLedgers.AsNoTracking().CountAsync(row => row.ReferenceType == "exchange" && row.ReferenceId == exchangeId));
            var fact = await db.ExchangeSalespersonAdjustments.AsNoTracking().SingleAsync(row => row.ExchangeId == exchangeId);
            Assert.Equal(20m, fact.PositiveAmountNet);
            Assert.Equal(1, await db.ExchangeReceiptSnapshots.AsNoTracking().CountAsync(row => row.ExchangeId == exchangeId));
            var returnLineIds = exchange.ReturnLines.Select(line => line.Id).ToList();
            Assert.Equal(1, await db.ExchangeLineageLinks.AsNoTracking().CountAsync(link => returnLineIds.Contains(link.ReturnLineId)));
        });

        // Idempotent replay; the same key for something else is refused.
        var (replayStatus, replayText) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, key,
            [new { direction = "collect", method = "cash", amount = 22m, tendered = 30m }]));
        Assert.Equal(HttpStatusCode.OK, replayStatus);
        Assert.Contains(number, replayText);
        var (mismatch, _) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, key,
            [new { direction = "collect", method = "card", amount = 22m }]));
        Assert.Equal(HttpStatusCode.Conflict, mismatch);

        // D-14: an exchanged sale can no longer be voided.
        var (voidStatus, voidText) = await PostRawAsync(owner, $"/api/sales/{saleId}/void", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Conflict, voidStatus);
        Assert.Contains("SALE_HAS_RETURNS", voidText);
    }

    [Fact]
    public async Task Exchange_RollsBackEveryWrite_WhenTheLastWriteFails()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "EXR", 100m);
        var jacket = await PricedVariantAsync(owner, "EXS", 120m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        var shirtBefore = await OnHandAsync(shirt);
        var jacketBefore = await OnHandAsync(jacket);
        long journalsBefore = 0, movementsBefore = 0, ledgerBefore = 0;
        await _fixture.WithDbAsync(async db =>
        {
            journalsBefore = await db.JournalEntries.LongCountAsync();
            movementsBefore = await db.CashSessionMovements.LongCountAsync();
            ledgerBefore = await db.StockLedgers.LongCountAsync();
        });

        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);

        // Fault injection in this disposable database: the salesperson fact is written after stock, cash and the posted journal.
        await _fixture.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION fn_test_fail_exchange_fact() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'injected failure'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_test_fail_exchange_fact BEFORE INSERT ON exchange_salesperson_adjustments
                FOR EACH ROW EXECUTE FUNCTION fn_test_fail_exchange_fact();
            """));
        try
        {
            var (status, _) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, Unique("rb"),
                [new { direction = "collect", method = "cash", amount = 22m }]));
            Assert.Equal(HttpStatusCode.InternalServerError, status);
        }
        finally
        {
            await _fixture.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS trg_test_fail_exchange_fact ON exchange_salesperson_adjustments; DROP FUNCTION IF EXISTS fn_test_fail_exchange_fact();"));
        }

        Assert.Equal(shirtBefore, await OnHandAsync(shirt));
        Assert.Equal(jacketBefore, await OnHandAsync(jacket));
        await _fixture.WithDbAsync(async db =>
        {
            Assert.Equal(0m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync());
            Assert.False(await db.ExchangeSourceInvoices.AnyAsync(row => row.SaleId == saleId));
            Assert.Equal(journalsBefore, await db.JournalEntries.LongCountAsync());
            Assert.Equal(movementsBefore, await db.CashSessionMovements.LongCountAsync());
            Assert.Equal(ledgerBefore, await db.StockLedgers.LongCountAsync());
        });

        // The same exchange succeeds afterwards: nothing was half-consumed.
        var again = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var (ok, okText) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, again.GetProperty("previewHash").GetString()!, Unique("rb"),
            [new { direction = "collect", method = "cash", amount = 22m }]));
        Assert.True(ok == HttpStatusCode.Created, okText);
    }

    [Fact]
    public async Task Exchange_SharesTheReturnableQuantityWithRefunds_AndOneConcurrentClaimWins()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner);
        var shirt = await PricedVariantAsync(owner, "EXQ", 100m);
        var jacket = await PricedVariantAsync(owner, "EXT", 100m);
        var sale = await SellAsync(owner, new SaleLine(shirt, 2m, 100m, null, null));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        await RefundAsync(owner, saleId, RefundBody(shirt, itemId, 1m, Unique("r")));
        var (over, overText) = await PostRawAsync(owner, "/api/pos/exchanges/preview", ExchangeBody(saleId, itemId, 2m, jacket, 2m));
        Assert.Equal(HttpStatusCode.Conflict, over);
        Assert.Contains("EXCHANGE_QUANTITY_EXCEEDS_REMAINING", overText);

        // The last unit: a refund and an even exchange race for it; exactly one wins.
        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        var exchangeTask = PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, preview.GetProperty("previewHash").GetString()!, Unique("cx"), []));
        var refundTask = PostRawAsync(owner, $"/api/sales/{saleId}/refund", RefundBody(shirt, itemId, 1m, Unique("r")));
        var outcomes = await Task.WhenAll(exchangeTask, refundTask);
        Assert.Equal(1, outcomes.Count(outcome => outcome.Status is HttpStatusCode.Created or HttpStatusCode.OK));
        await _fixture.WithDbAsync(async db =>
            Assert.Equal(2m, await db.SaleItems.AsNoTracking().Where(row => row.Id == itemId).Select(row => row.ReturnedQuantity).SingleAsync()));
    }

    [Fact]
    public async Task Exchange_Rules_LegacyLink_Condition_LowerPrice_And_RequiredPhoneMode()
    {
        var owner = Owner();
        var shirt = await PricedVariantAsync(owner, "EXL", 100m);
        var cheap = await PricedVariantAsync(owner, "EXM", 70m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);

        // D-01: ordinary checkout no longer creates exchange links.
        using (var linked = await owner.PostAsJsonAsync("/api/sales", new
               {
                   outletId = UomPostgresFixture.Outlet, terminalId = UomPostgresFixture.Terminal, discount = 0m, tax = 11m,
                   paymentMethod = "cash", payments = new[] { new { method = "cash", amount = 110m, tendered = 110m } },
                   items = new[] { new { variantId = shirt, quantity = 1m, unitPrice = 100m, discountAmount = 0m } },
                   originalSaleId = saleId, idempotencyKey = Unique("lk")
               }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, linked.StatusCode);
            Assert.Contains("SALE_EXCHANGE_LINK_DEPRECATED", await linked.Content.ReadAsStringAsync());
        }

        // DN-01: required phone verification cannot be switched on.
        Assert.Equal(422, await SaveExchangePolicyAsync(owner, phoneMode: "required", expectSuccess: false));

        // EX-05: damaged goods never enter the exchange.
        await SaveExchangePolicyAsync(owner, allowLower: false);
        var (damaged, damagedText) = await PostRawAsync(owner, "/api/pos/exchanges/preview", ExchangeBody(saleId, itemId, 1m, cheap, 1m, sellable: false));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, damaged);
        Assert.Contains("EXCHANGE_CONDITION_NOT_SELLABLE", damagedText);

        // EX-02: lower-priced replacement while lower-price exchange is off.
        var (lower, lowerText) = await PostRawAsync(owner, "/api/pos/exchanges/preview", ExchangeBody(saleId, itemId, 1m, cheap, 1m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lower);
        Assert.Contains("EXCHANGE_LOWER_PRICE_DISABLED", lowerText);

        // Lower price allowed: a cashier without refund permission needs a supervisor refund approval (OC-03 Case B).
        await SaveExchangePolicyAsync(owner, allowLower: true);
        var cashier = _fixture.Client(UomPostgresFixture.OwnerId, "Salesman", "pos.exchange.create,sales.view,sales.create");
        var preview = await Send(cashier, HttpMethod.Post, "/api/pos/exchanges/preview", ExchangeBody(saleId, itemId, 1m, cheap, 1m));
        Assert.Equal("refund", preview.GetProperty("settlementDirection").GetString());
        Assert.Contains("refund", preview.GetProperty("requiredApprovals").EnumerateArray().Select(value => value.GetString()));
        var (noApproval, noApprovalText) = await PostRawAsync(cashier, "/api/pos/exchanges",
            ConfirmBody(ExchangeBody(saleId, itemId, 1m, cheap, 1m), preview.GetProperty("previewHash").GetString()!, Unique("lp"),
                [new { direction = "refund", method = "cash", amount = 33m }]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noApproval);
        Assert.Contains("EXCHANGE_APPROVAL_REQUIRED", noApprovalText);

        // The owner holds pos.exchange.refund: the same exchange refunds 33.00 in cash (Case A).
        var ownerPreview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", ExchangeBody(saleId, itemId, 1m, cheap, 1m));
        var (refunded, refundedText) = await PostRawAsync(owner, "/api/pos/exchanges",
            ConfirmBody(ExchangeBody(saleId, itemId, 1m, cheap, 1m), ownerPreview.GetProperty("previewHash").GetString()!, Unique("lp"),
                [new { direction = "refund", method = "cash", amount = 33m }]));
        Assert.True(refunded == HttpStatusCode.Created, refundedText);
    }

    [Fact]
    public async Task Exchange_ExpiredWindow_NeedsAnIndependentSupervisor_WithOutletAccess_OnceOnly()
    {
        var owner = Owner();
        await SaveExchangePolicyAsync(owner, window: 7);
        var shirt = await PricedVariantAsync(owner, "EXW", 100m);
        var jacket = await PricedVariantAsync(owner, "EXX", 100m);
        var (saleId, itemId) = await SoldAsync(owner, shirt, 1m, 100m);
        await _fixture.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales SET sale_date = sale_date - interval '9 days' WHERE id = {saleId}"));

        const string password = "Supervisor#123";
        long supervisorId = 0, outsiderId = 0;
        await _fixture.WithDbAsync(async db =>
        {
            var role = new Role { Name = $"ExchangeSupervisor{Guid.NewGuid():N}"[..30], Permissions = "[\"pos.exchange.override_window\"]" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            var supervisor = new User { BusinessId = UomPostgresFixture.Business, RoleId = role.Id, Name = "Supervisor", Email = $"{Unique("sv")}@example.test",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4), IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                OutletId = UomPostgresFixture.Outlet };
            var outsider = new User { BusinessId = UomPostgresFixture.Business, RoleId = role.Id, Name = "Outsider", Email = $"{Unique("os")}@example.test",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4), IsActive = true, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                OutletId = UomPostgresFixture.OutletTwo };
            db.Users.AddRange(supervisor, outsider);
            await db.SaveChangesAsync();
            db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = supervisor.Id, OutletId = UomPostgresFixture.Outlet, BusinessId = UomPostgresFixture.Business, IsPrimary = true, IsActive = true });
            db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = outsider.Id, OutletId = UomPostgresFixture.OutletTwo, BusinessId = UomPostgresFixture.Business, IsPrimary = true, IsActive = true });
            await db.SaveChangesAsync();
            supervisorId = supervisor.Id;
            outsiderId = outsider.Id;
        });
        string Email(long id)
        {
            var email = string.Empty;
            _fixture.WithDbAsync(async db => email = await db.Users.Where(row => row.Id == id).Select(row => row.Email).SingleAsync()).GetAwaiter().GetResult();
            return email;
        }

        var body = ExchangeBody(saleId, itemId, 1m, jacket, 1m);
        var preview = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/preview", body);
        Assert.Contains("window", preview.GetProperty("requiredApprovals").EnumerateArray().Select(value => value.GetString()));
        var hash = preview.GetProperty("previewHash").GetString()!;
        object Approval(string login, string secret) => new
        {
            preview = body, previewHash = hash, approvals = new[] { new { type = "window", reason = "Customer was travelling" } },
            supervisorLogin = login, supervisorPassword = secret
        };

        var (noApproval, _) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, hash, Unique("w"), []));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noApproval);

        var (wrongPassword, _) = await PostRawAsync(owner, "/api/pos/exchanges/approvals", Approval(Email(supervisorId), "nope"));
        Assert.Equal(HttpStatusCode.Forbidden, wrongPassword);
        var (otherOutlet, otherText) = await PostRawAsync(owner, "/api/pos/exchanges/approvals", Approval(Email(outsiderId), password));
        Assert.Equal(HttpStatusCode.Forbidden, otherOutlet);
        Assert.Contains("EXCHANGE_APPROVAL_NOT_PERMITTED", otherText);

        var tokens = await Send(owner, HttpMethod.Post, "/api/pos/exchanges/approvals", Approval(Email(supervisorId), password));
        var token = tokens[0].GetProperty("approvalToken").GetString()!;
        Assert.DoesNotContain(password, tokens.GetRawText());

        var (approved, approvedText) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, hash, Unique("w"), [], [token]));
        Assert.True(approved == HttpStatusCode.Created, approvedText);

        // One-time: the token cannot authorise anything again.
        var (reused, _) = await PostRawAsync(owner, "/api/pos/exchanges", ConfirmBody(body, hash, Unique("w"), [], [token]));
        Assert.NotEqual(HttpStatusCode.Created, reused);
        await _fixture.WithDbAsync(async db =>
        {
            var approval = await db.ExchangeApprovals.AsNoTracking().SingleAsync(row => row.SupervisorUserId == supervisorId);
            Assert.Equal("consumed", approval.Status);
            Assert.Equal("assignment", approval.SupervisorOutletAccessBasis);
            Assert.NotEqual(approval.CashierUserId, approval.SupervisorUserId);
        });
    }
}
