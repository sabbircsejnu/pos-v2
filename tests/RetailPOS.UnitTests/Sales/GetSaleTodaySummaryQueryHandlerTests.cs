using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Tests.Sales;

public sealed class GetSaleTodaySummaryQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsOutletUtcDayRangeAndCancellation()
    {
        var store = new FakeSaleTodaySummaryReadStore();
        var handler = new GetSaleTodaySummaryQueryHandler(store);
        using var cancellation = new CancellationTokenSource();
        var before = DateTime.UtcNow.Date;

        var result = await handler.Handle(
            new GetSaleTodaySummaryQuery(5001),
            cancellation.Token);

        Assert.Equal(2, result.TotalSales);
        Assert.Equal(30m, result.TotalAmount);
        Assert.Equal(2m, result.TotalDiscount);
        Assert.Equal(3m, result.TotalTax);
        Assert.Equal(20m, result.PaymentBreakdown["cash"]);
        Assert.Equal(10m, result.PaymentBreakdown["card"]);
        Assert.Equal(5001, store.OutletId);
        Assert.Equal(before, store.StartUtc);
        Assert.Equal(before.AddDays(1), store.EndExclusiveUtc);
        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    private sealed class FakeSaleTodaySummaryReadStore : ISaleTodaySummaryReadStore
    {
        public IReadOnlyList<SaleSummaryLineReadModel> Result { get; } =
        [
            new("cash", 20m, 1m, 2m),
            new("card", 10m, 1m, 1m)
        ];
        public long? OutletId { get; private set; }
        public DateTime StartUtc { get; private set; }
        public DateTime EndExclusiveUtc { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<SaleSummaryLineReadModel>> GetAsync(
            long? outletId,
            DateTime startUtc,
            DateTime endExclusiveUtc,
            CancellationToken cancellationToken)
        {
            OutletId = outletId;
            StartUtc = startUtc;
            EndExclusiveUtc = endExclusiveUtc;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
