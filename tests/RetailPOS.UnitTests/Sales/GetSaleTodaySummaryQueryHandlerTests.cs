using Moq;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Tests.Sales;

public sealed class GetSaleTodaySummaryQueryHandlerTests
{
    private static readonly SaleReadScope Scope = new(1, [5001]);

    private static ISaleReadScopeProvider ScopeProvider()
    {
        var provider = new Mock<ISaleReadScopeProvider>();
        provider.Setup(port => port.GetAsync(5001, It.IsAny<CancellationToken>())).ReturnsAsync(Scope);
        return provider.Object;
    }

    private static ICurrentCompanyTimeZone Zone(string zone)
    {
        var provider = new Mock<ICurrentCompanyTimeZone>();
        provider.Setup(port => port.GetTimeZoneIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        return provider.Object;
    }

    [Fact]
    public async Task Handle_UsesTheCompanyDay_NotTheUtcDay()
    {
        var store = new FakeSaleTodaySummaryReadStore();
        var handler = new GetSaleTodaySummaryQueryHandler(store, ScopeProvider(), Zone("Pacific/Kiritimati"));
        var today = DateOnly.FromDateTime(CompanyDates.DateFor(DateTime.UtcNow, "Pacific/Kiritimati"));
        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-14);

        await handler.Handle(new GetSaleTodaySummaryQuery(5001), CancellationToken.None);

        Assert.Equal(dayStart, store.StartUtc);
        Assert.Equal(dayStart.AddDays(1), store.EndExclusiveUtc);
    }

    [Fact]
    public async Task Handle_ForwardsOutletUtcDayRangeAndCancellation()
    {
        var store = new FakeSaleTodaySummaryReadStore();
        var handler = new GetSaleTodaySummaryQueryHandler(store, ScopeProvider(), Zone("Etc/UTC"));
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
        Assert.Same(Scope, store.Scope);
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
        public SaleReadScope? Scope { get; private set; }
        public DateTime StartUtc { get; private set; }
        public DateTime EndExclusiveUtc { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<SaleSummaryLineReadModel>> GetAsync(
            SaleReadScope scope,
            DateTime startUtc,
            DateTime endExclusiveUtc,
            CancellationToken cancellationToken)
        {
            Scope = scope;
            StartUtc = startUtc;
            EndExclusiveUtc = endExclusiveUtc;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
