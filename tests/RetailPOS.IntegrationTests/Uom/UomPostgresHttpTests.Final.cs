using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Uom;

/// <summary>Final gap closure: cost redaction, refund VAT reporting, exact refund cost, posting immutability, I06.</summary>
public sealed partial class UomPostgresHttpTests
{
    [Fact]
    public async Task Security_SaleCost_IsAbsentFromTheResponse_WithoutViewCost()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "SEC");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        Assert.Equal(2m, sale.GetProperty("items")[0].GetProperty("historicalUnitCost").GetDecimal());

        // A cashier reads the sale, its prices and quantities, and gets no cost at all: null, not a misleading 0.
        var cashier = _fixture.Client(UomPostgresFixture.OwnerId, "Cashier", "sales.view,sales.create,sales.refund");
        var seen = await Data(cashier, $"/api/sales/{saleId}");
        var line = seen.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, line.GetProperty("historicalUnitCost").ValueKind);
        Assert.Equal(100m, line.GetProperty("enteredUnitPrice").GetDecimal());
        Assert.Equal(12m, line.GetProperty("quantity").GetDecimal());

        // The same holds for the refund response and the sales list.
        var refunded = await RefundAsync(cashier, saleId, RefundBody(attar, line.GetProperty("id").GetInt64(), 2m, Unique("r")));
        Assert.Equal(JsonValueKind.Null, refunded.GetProperty("items")[0].GetProperty("historicalUnitCost").ValueKind);
        using var list = await cashier.PostAsJsonAsync("/api/sales/search", new { pageNumber = 1, pageSize = 50 });
        using (var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var items = listJson.RootElement.GetProperty("data").GetProperty("sales").EnumerateArray()
                .SelectMany(row => row.GetProperty("items").EnumerateArray()).ToArray();
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("historicalUnitCost").ValueKind));
        }

        // The stock ledger, readable with inventory.view alone, shows the movement but neither its cost nor a remark quoting it.
        var storekeeper = _fixture.Client(UomPostgresFixture.OwnerId, "Warehouse", "inventory.view");
        using (var ledger = await storekeeper.GetAsync($"/api/stock-ledger/variant/{attar}/location/{UomPostgresFixture.Outlet}?locationType=outlet"))
        {
            var text = await ledger.Content.ReadAsStringAsync();
            Assert.True(ledger.IsSuccessStatusCode, text);
            using var ledgerJson = JsonDocument.Parse(text);
            var rows = ledgerJson.RootElement.GetProperty("data").EnumerateArray().ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, row =>
            {
                Assert.Equal(JsonValueKind.Null, row.GetProperty("unitCost").ValueKind);
                Assert.Equal(JsonValueKind.Null, row.GetProperty("movementValue").ValueKind);
                Assert.DoesNotContain("historical unit cost", row.GetProperty("remarks").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Granted the permission, the cost is there.
        var finance = _fixture.Client(UomPostgresFixture.OwnerId, "Accountant", "sales.view,products.view_cost");
        Assert.Equal(2m, (await Data(finance, $"/api/sales/{saleId}")).GetProperty("items")[0].GetProperty("historicalUnitCost").GetDecimal());

        // Another business's owner cannot read the sale at all.
        var foreign = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        using var foreignRead = await foreign.GetAsync($"/api/sales/{saleId}");
        Assert.NotEqual(HttpStatusCode.OK, foreignRead.StatusCode);
    }

    [Fact]
    public async Task Vat_PostedPartialRefunds_ReduceOutputVat_OnlyOnceEach_AndAnUnpostedOneDoesNot()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "VAT");
        // 10 packs at ৳100 + 10% VAT: net ৳1,000, VAT ৳100.
        var sale = await SellAsync(owner, new SaleLine(attar, 10m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();
        var saleNumber = sale.GetProperty("saleNumber").GetString()!;

        // Two packs back (net ৳200, VAT ৳20), then one more (৳100, ৳10), and a third that is not posted yet.
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 24m, Unique("r")));
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));
        await PostAllAsync(owner, saleId);
        await PostAllAsync(owner, saleId);
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r")));

        var today = DateTime.UtcNow.Date;
        var details = await Data(owner, $"/api/accounting/reports/vat-details?dateFrom={today.AddDays(-2):yyyy-MM-dd}&dateTo={today.AddDays(2):yyyy-MM-dd}");
        var rows = details.EnumerateArray()
            .Where(row => row.GetProperty("sourceReference").GetString()!.StartsWith(saleNumber, StringComparison.Ordinal))
            .ToArray();
        decimal Vat(string type) => rows.Where(row => row.GetProperty("sourceType").GetString() == type).Sum(row => row.GetProperty("vatAmount").GetDecimal());

        Assert.Equal(100m, Vat("Sale"));
        Assert.Equal(-30m, Vat("SaleRefund"));
        Assert.Equal(2, rows.Count(row => row.GetProperty("sourceType").GetString() == "SaleRefund"));
        Assert.Equal(70m, rows.Sum(row => row.GetProperty("vatAmount").GetDecimal()));
    }

    [Fact]
    public async Task Vat_APostedVoid_CancelsTheSalesVat()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "VOV");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        await Send(owner, HttpMethod.Post, $"/api/sales/{saleId}/void", new { reason = "Wrong item" });
        await PostAllAsync(owner, saleId);

        var today = DateTime.UtcNow.Date;
        var details = await Data(owner, $"/api/accounting/reports/vat-details?dateFrom={today.AddDays(-2):yyyy-MM-dd}&dateTo={today.AddDays(2):yyyy-MM-dd}");
        var rows = details.EnumerateArray()
            .Where(row => row.GetProperty("sourceReference").GetString() == sale.GetProperty("saleNumber").GetString())
            .ToArray();
        Assert.Contains(rows, row => row.GetProperty("sourceType").GetString() == "SaleVoid");
        Assert.Equal(0m, rows.Sum(row => row.GetProperty("vatAmount").GetDecimal()));
    }

    [Fact]
    public async Task Refunds_ThreeAwkwardPartials_LedgerAndJournalRestoreExactlyTheSameCost()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("RND");
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)]);
        // ৳1,000 for 3 jars of 500 ml: ৳0.6666… per ml, a cost that never divides evenly.
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 3m, 1000m / 3m);
        var sale = await SellAsync(owner, new SaleLine(attar, 7m, 5m, Option(config, "ML"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();

        foreach (var quantity in new[] { 1m, 3m, 3m })
            await RefundAsync(owner, saleId, RefundBody(attar, itemId, quantity, Unique("r")));

        await _fixture.WithDbAsync(async db =>
        {
            var item = await db.SaleItems.AsNoTracking().SingleAsync(row => row.Id == itemId);
            var soldCost = Math.Round(item.HistoricalUnitCost!.Value * item.Quantity, 2, MidpointRounding.AwayFromZero);
            var ledger = await db.StockLedgers.AsNoTracking()
                .Where(row => row.VariantId == attar && row.ReferenceId == saleId && row.TransactionType == StockLedgerTransactionType.Return)
                .Select(row => row.MovementValue)
                .ToListAsync();
            var journal = new List<decimal>();
            foreach (var posting in await db.SaleFinancialPostings.AsNoTracking().Where(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Reversal).ToListAsync())
                journal.Add(SnapshotAmount(posting, "cost"));

            Assert.Equal(3, ledger.Count);
            Assert.Equal(journal.OrderBy(value => value), ledger.OrderBy(value => value));
            Assert.Equal(soldCost, journal.Sum());
        });
    }

    [Fact]
    public async Task Immutability_ARefundIntentCannotBeMovedToAnotherRefund()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "IMM");
        var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64()));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 4m, Unique("r")));
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 4m, Unique("r")));
        await PostAllAsync(owner, saleId);

        await _fixture.WithDbAsync(async db =>
        {
            var postings = await db.SaleFinancialPostings.AsNoTracking()
                .Where(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Reversal)
                .OrderBy(row => row.Id)
                .ToListAsync();
            Assert.All(postings, row => Assert.Equal(SaleFinancialPostingStatuses.Posted, row.Status));
            var first = postings[0];
            var other = postings[1].SaleRefundId!.Value;

            var moved = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sale_financial_postings SET sale_refund_id = {other} WHERE id = {first.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", moved.Message);
            var cleared = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE sale_financial_postings SET sale_refund_id = NULL WHERE id = {first.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", cleared.Message);
            var deleted = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM sale_financial_postings WHERE id = {first.Id}"));
            Assert.Contains("SALE_FINANCIAL_POSTING_IMMUTABLE", deleted.Message);
        });
    }

    [Fact]
    public async Task I06_OpeningPacksOneAtATime_ConcurrentlyAndBeyondStock_KeepsEveryValueExact()
    {
        var owner = Owner();
        var bottle = await _fixture.CreateVariantAsync("I6B");
        await ReceivePcsAsync(owner, bottle, 3m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "I6A", bottle);
        var job = await CreateJobAsync(owner, attar, config, 3, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });
        var before = await ValuationReportAsync(owner);
        object Body(string key, long location = UomPostgresFixture.Outlet) => new
        {
            variantId = attar, locationId = location, locationType = "outlet", packCode = "P12", baseQtyPerPack = 12m,
            count = 1, packagingReusable = true, idempotencyKey = key
        };

        // Two terminals open one pack each at the same moment, and a third retries the first one's request.
        var key = Unique("op");
        var outcomes = await Task.WhenAll(new[] { key, Unique("op"), key }.Select(async requestKey =>
        {
            using var client = Owner();
            using var response = await client.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", Body(requestKey));
            return response.StatusCode;
        }));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, outcomes);

        var state = await PackStateAsync(owner, attar);
        var prepared = state.GetProperty("balances").EnumerateArray().Single(row => row.GetProperty("state").GetString() == "prepared");
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(1, prepared.GetProperty("packCount").GetInt32());
        Assert.Equal(8m, prepared.GetProperty("capitalizedValue").GetDecimal());
        await _fixture.WithDbAsync(async db => Assert.Equal(2m, await db.Inventories.AsNoTracking()
            .Where(row => row.VariantId == bottle && row.LocationId == UomPostgresFixture.Outlet).Select(row => row.Quantity).SingleAsync()));

        // More packs than are left, or packs at a location the caller cannot reach, are refused.
        using (var tooMany = await owner.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", new
        {
            variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "P12", baseQtyPerPack = 12m,
            count = 2, packagingReusable = true, idempotencyKey = Unique("op")
        }))
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        var restricted = _fixture.Client(UomPostgresFixture.RestrictedUserId, "Manager", "inventory.pack_state.manage,uom.view");
        using (var elsewhere = await restricted.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", Body(Unique("op"))))
            Assert.NotEqual(HttpStatusCode.OK, elsewhere.StatusCode);
        var foreign = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        using (var otherBusiness = await foreign.PostAsJsonAsync("/api/v1/uom/pack-state/open-prepared", Body(Unique("op"))))
            Assert.NotEqual(HttpStatusCode.OK, otherBusiness.StatusCode);

        var after = await ValuationReportAsync(owner);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(before.Gl, after.Gl);
        Assert.Equal(before.Capitalized - 16m, after.Capitalized);
    }

    [Fact]
    public async Task Refund_OfAFractionOfACent_NeverRecordsAZeroValueThatWouldUnvalueTheStock()
    {
        var owner = Owner();
        var attar = await _fixture.CreateVariantAsync("TNY");
        var config = await SaveConfigAsync(owner, attar, "ml", Jar500(owner), [Pack("P12", 12m, 100m), CustomMl(5m)]);
        // ৳0.50 for 500 ml: ৳0.001 an ml, so 1 ml's cost rounds to ৳0.00.
        await PurchaseAsync(owner, attar, config.GetProperty("purchaseOptions")[0].GetProperty("id").GetInt64(), 1m, 0.5m);
        var sale = await SellAsync(owner, new SaleLine(attar, 2m, 5m, Option(config, "ML"), config.GetProperty("configId").GetInt64()));
        await RefundAsync(owner, sale.GetProperty("id").GetInt64(), RefundBody(attar, sale.GetProperty("items")[0].GetProperty("id").GetInt64(), 1m, Unique("r")));

        await _fixture.WithDbAsync(async db =>
        {
            var returned = await db.StockLedgers.AsNoTracking()
                .SingleAsync(row => row.VariantId == attar && row.TransactionType == StockLedgerTransactionType.Return);
            Assert.True(returned.MovementValue > 0m);
        });
        Assert.True(await ValuationAsync(attar) > 0m);
    }
}
