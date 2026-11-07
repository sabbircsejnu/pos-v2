using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Uom;

/// <summary>I05 (Pattern P1) and Sequence E: the proofs the earlier suite did not yet make.</summary>
public sealed partial class UomPostgresHttpTests
{
    private async Task PostSaleJournalsAsync(HttpClient owner, long saleId) => await PostAllAsync(owner, saleId);

    [Fact]
    public async Task P1_PreparedPackReturn_ReusableBottleRestoresItsShare_BrokenBottleStaysExpensed_BooksStayInStep()
    {
        var owner = Owner();
        var before = await ValuationReportAsync(owner);
        var bottle = await _fixture.CreateVariantAsync("RPB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "RPA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });

        var sale = await SellAsync(owner, new SaleLine(attar, 3m, 100m, Option(config, "P12"), config.GetProperty("configId").GetInt64(), "prepared"));
        var saleId = sale.GetProperty("id").GetInt64();
        var itemId = sale.GetProperty("items")[0].GetProperty("id").GetInt64();
        Assert.Equal(24m, await PackagingCostAsync(itemId));

        // One pack back sealed and reusable: it returns as a prepared pack carrying its ৳8.
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r"), packagingReusable: true));
        var state = await PackStateAsync(owner, attar);
        Assert.Equal(18, Balance(state, "prepared").GetProperty("packCount").GetInt32());
        Assert.Equal(144m, Balance(state, "prepared").GetProperty("capitalizedValue").GetDecimal());

        // One pack back resellable but its bottle unusable: 12 ml comes back loose, the bottle's ৳8 stays in COGS.
        await RefundAsync(owner, saleId, RefundBody(attar, itemId, 12m, Unique("r"), packagingReusable: false));
        state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.Equal(18, Balance(state, "prepared").GetProperty("packCount").GetInt32());

        var reversals = await ReversalsAsync(saleId);
        Assert.Equal([32m, 24m], reversals.Select(row => SnapshotAmount(row, "cost")));

        await PostSaleJournalsAsync(owner, saleId);
        var after = await ValuationReportAsync(owner);
        Assert.Equal(before.Value - before.Gl, after.Value - after.Gl);
        // ৳1160 bought, ৳96 sold out, ৳56 back.
        Assert.Equal(before.Value + 1160m - 96m + 56m, after.Value);
        Assert.Equal(before.Capitalized + 160m - 24m + 8m, after.Capitalized);
    }

    [Fact]
    public async Task P1_CapitalizedValueThatDoesNotDivideEvenly_IsReleasedToTheCentAndNeverInvented()
    {
        var owner = Owner();
        var before = await ValuationReportAsync(owner);
        var bottle = await _fixture.CreateVariantAsync("RNB");
        // Three bottles for ৳10: ৳3.33 a bottle does not exist, so the value must never round into or out of being.
        await ReceivePcsAsync(owner, bottle, 3m, 10m / 3m);
        var (attar, config) = await RepackableAttarAsync(owner, "RNA", bottle);
        var job = await CreateJobAsync(owner, attar, config, 3, Unique("job"));
        await Send(owner, HttpMethod.Post, $"/api/v1/uom/repacking/{job.GetProperty("id").GetInt64()}/execute", new { });
        var repacked = await PackStateAsync(owner, attar);
        var capitalized = Balance(repacked, "prepared").GetProperty("capitalizedValue").GetDecimal();

        var version = config.GetProperty("configId").GetInt64();
        decimal released = 0m;
        foreach (var _ in Enumerable.Range(0, 3))
        {
            var sale = await SellAsync(owner, new SaleLine(attar, 1m, 100m, Option(config, "P12"), version, "prepared"));
            released += await PackagingCostAsync(sale.GetProperty("items")[0].GetProperty("id").GetInt64());
            await PostSaleJournalsAsync(owner, sale.GetProperty("id").GetInt64());
        }

        Assert.Equal(capitalized, released);
        var empty = await PackStateAsync(owner, attar);
        // The emptied prepared balance carries nothing: no row, or a row at zero.
        Assert.All(
            empty.GetProperty("balances").EnumerateArray().Where(row => row.GetProperty("state").GetString() == "prepared"),
            row => Assert.Equal(0m, row.GetProperty("capitalizedValue").GetDecimal()));
        var after = await ValuationReportAsync(owner);
        Assert.Equal(before.Value - before.Gl, after.Value - after.Gl);
    }

    [Fact]
    public async Task SequenceE_SalesRefundsAndARepackCompeteForOneStock_NothingNegativeDuplicatedOrInvented()
    {
        var owner = Owner();
        var before = await ValuationReportAsync(owner);
        var bottle = await _fixture.CreateVariantAsync("SEB");
        await ReceivePcsAsync(owner, bottle, 20m, 8m);
        var (attar, config) = await RepackableAttarAsync(owner, "SEA", bottle);
        var version = config.GetProperty("configId").GetInt64();
        var first = await SellAsync(owner, new SaleLine(attar, 100m, 5m, Option(config, "ML"), version, "loose"));
        var firstId = first.GetProperty("id").GetInt64();
        var firstItem = first.GetProperty("items")[0].GetProperty("id").GetInt64();
        var job = await CreateJobAsync(owner, attar, config, 20, Unique("job"));
        var jobId = job.GetProperty("id").GetInt64();

        // 400 ml loose is left. A 240 ml repack, refunds and loose sales of 30 ml race for it.
        var work = new List<Task<HttpStatusCode>>
        {
            Task.Run(async () =>
            {
                using var client = Owner();
                using var response = await client.PostAsync($"/api/v1/uom/repacking/{jobId}/execute", EmptyJson());
                return response.StatusCode;
            })
        };
        for (var index = 0; index < 8; index++)
            work.Add(Task.Run(async () =>
            {
                using var client = Owner();
                return await TrySellAsync(client, new SaleLine(attar, 30m, 5m, Option(config, "ML"), version, "loose"))
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Conflict;
            }));
        for (var index = 0; index < 4; index++)
            work.Add(Task.Run(async () =>
            {
                using var client = Owner();
                using var response = await client.PostAsync(
                    $"/api/sales/{firstId}/refund",
                    System.Net.Http.Json.JsonContent.Create(RefundBody(attar, firstItem, 10m, Unique("r"))));
                return response.StatusCode;
            }));
        var outcomes = await Task.WhenAll(work);
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, outcomes);

        var state = await PackStateAsync(owner, attar);
        Assert.True(state.GetProperty("isReconciled").GetBoolean());
        Assert.True(state.GetProperty("onHand").GetDecimal() >= 0m);
        Assert.True(Balance(state, "loose").GetProperty("baseQuantity").GetDecimal() >= 0m);
        await _fixture.WithDbAsync(async db =>
        {
            var item = await db.SaleItems.AsNoTracking().SingleAsync(row => row.Id == firstItem);
            Assert.Equal(10m * outcomes.Skip(9).Count(status => status == HttpStatusCode.OK), item.ReturnedQuantity);
            var executed = await db.Set<RetailPOS.Core.Entities.Uom.RepackJob>().AsNoTracking().SingleAsync(row => row.Id == jobId);
            var consumed = await db.StockLedgers.AsNoTracking()
                .Where(row => row.VariantId == bottle && row.ReferenceId == jobId && row.QtyOut > 0m)
                .SumAsync(row => row.QtyOut);
            Assert.Equal(executed.Status == "executed" ? 20m : 0m, consumed);
        });

        var sales = new List<long>();
        await _fixture.WithDbAsync(async db => sales = await db.Sales.AsNoTracking()
            .Where(row => row.Items.Any(item => item.VariantId == attar)).Select(row => row.Id).ToListAsync());
        foreach (var saleId in sales)
            await PostSaleJournalsAsync(owner, saleId);
        var after = await ValuationReportAsync(owner);
        Assert.Equal(before.Value - before.Gl, after.Value - after.Gl);
    }

    private async Task<decimal> PackagingCostAsync(long saleItemId)
    {
        decimal cost = 0m;
        await _fixture.WithDbAsync(async db => cost = await db.SaleItems.AsNoTracking()
            .Where(row => row.Id == saleItemId).Select(row => row.PackagingCost).SingleAsync());
        return cost;
    }

    private static StringContent EmptyJson() => new("{}", System.Text.Encoding.UTF8, "application/json");
}
