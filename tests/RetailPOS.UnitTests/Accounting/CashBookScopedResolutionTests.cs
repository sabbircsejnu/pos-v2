using Moq;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.UnitTests.Accounting;

public sealed class CashBookScopedResolutionTests
{
    [Fact]
    public async Task ResolvesLiquiditySlotsSequentiallyWithinSharedPersistenceScope()
    {
        var active = 0;
        var resolved = new List<string>();
        var resolver = new Mock<ICashBookPostingAccountResolver>();
        resolver.Setup(service => service.ResolveAsync(
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Returns(async (string slot, long? outlet, CancellationToken token) =>
            {
                Assert.Equal(1, Interlocked.Increment(ref active));
                await Task.Delay(10, token);
                resolved.Add(slot);
                Interlocked.Decrement(ref active);
                return new CashBookAccountResolution(slot, resolved.Count);
            });
        var tenant = new Mock<IAccountingReportTenantScope>();
        tenant.Setup(service => service.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        var store = new Mock<ICashBookReadStore>();
        store.Setup(service => service.GetAsync(
                It.IsAny<CashBookFilter>(), 4,
                It.IsAny<IReadOnlyList<CashBookAccountResolution>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashBookReadModel(DateTime.UtcNow.Date, DateTime.UtcNow.Date, []));

        await new GetCashBookQueryHandler(store.Object, resolver.Object, tenant.Object, Mock.Of<RetailPOS.Application.Settings.Abstractions.ICompanyTimeZoneProvider>(provider => provider.GetTimeZoneIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()) == Task.FromResult("Asia/Dhaka")))
            .Handle(new GetCashBookQuery(
                new CashBookFilter(null, null, null, null, null, null, null), []),
                CancellationToken.None);

        Assert.Equal(new[] { "cash", "petty_cash", "bank" }, resolved);
        Assert.Equal(0, active);
        store.Verify(service => service.GetAsync(
            It.IsAny<CashBookFilter>(), 4,
            It.Is<IReadOnlyList<CashBookAccountResolution>>(items => items.Count == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
