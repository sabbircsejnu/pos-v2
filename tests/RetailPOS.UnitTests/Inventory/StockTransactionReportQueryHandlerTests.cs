using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.UnitTests.Inventory;

public sealed class StockTransactionReportQueryHandlerTests
{
    private static readonly InventoryReportReadScope Scope = new(4101, [5101], []);

    [Fact]
    public async Task CalendarDates_BecomeTheCompanyDayWindowInUtc()
    {
        var (handler, captured, _) = CreateHandler("Asia/Dhaka");

        await handler.Handle(
            Query(new DateTime(2026, 11, 5), new DateTime(2026, 11, 6)),
            CancellationToken.None);

        var filter = Assert.Single(captured);
        Assert.Equal(new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc), filter.DateFrom);
        Assert.Equal(DateTimeKind.Utc, filter.DateFrom!.Value.Kind);
        Assert.Equal(
            new DateTime(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc).AddTicks(-1),
            filter.DateTo);
        Assert.Equal(DateTimeKind.Utc, filter.DateTo!.Value.Kind);
    }

    [Fact]
    public async Task ThePageSeesTheDatesItAskedFor()
    {
        var (handler, _, _) = CreateHandler("Asia/Dhaka");
        var from = new DateTime(2026, 11, 5);
        var to = new DateTime(2026, 11, 6);

        var report = await handler.Handle(Query(from, to), CancellationToken.None);

        Assert.Equal(from, report.DateFrom);
        Assert.Equal(to, report.DateTo);
    }

    [Fact]
    public async Task ValuesCarryingAZone_KeepTheirExistingApiMeaning()
    {
        var (handler, captured, _) = CreateHandler("Asia/Dhaka");

        await handler.Handle(
            Query(
                new DateTime(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var filter = Assert.Single(captured);
        Assert.Equal(new DateTime(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc), filter.DateFrom);
        Assert.Equal(
            new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1),
            filter.DateTo);
    }

    [Fact]
    public async Task NoDates_NeedNoTimeZoneAndStayOpen()
    {
        var (handler, captured, timeZones) = CreateHandler("Asia/Dhaka");

        await handler.Handle(Query(null, null), CancellationToken.None);

        var filter = Assert.Single(captured);
        Assert.Null(filter.DateFrom);
        Assert.Null(filter.DateTo);
        timeZones.Verify(
            zone => zone.GetTimeZoneIdAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static GetStockTransactionReportQuery Query(DateTime? from, DateTime? to) =>
        new(new StockTransactionReportFilter(7101, null, 5101, "outlet", from, to));

    private static (
        GetStockTransactionReportQueryHandler Handler,
        List<StockTransactionReportFilter> Captured,
        Mock<ICurrentCompanyTimeZone> TimeZones) CreateHandler(string timeZone)
    {
        var captured = new List<StockTransactionReportFilter>();
        var scopeProvider = new Mock<IInventoryReportReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Scope);
        var store = new Mock<IStockTransactionReportReadStore>(MockBehavior.Strict);
        store.Setup(reader => reader.GetAsync(
                Scope,
                It.IsAny<StockTransactionReportFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<InventoryReportReadScope, StockTransactionReportFilter, CancellationToken>(
                (_, filter, _) => captured.Add(filter))
            .ReturnsAsync((InventoryReportReadScope _, StockTransactionReportFilter filter, CancellationToken _) =>
                new StockTransactionReportReadModel(
                    filter.ProductId,
                    "Product",
                    "CODE",
                    "product",
                    null,
                    null,
                    null,
                    null,
                    "SKU",
                    filter.LocationId,
                    filter.LocationType,
                    filter.DateFrom,
                    filter.DateTo,
                    new StockTransactionReportSummaryReadModel(0, 0, 0, 0, 0),
                    []));
        var timeZones = new Mock<ICurrentCompanyTimeZone>(MockBehavior.Strict);
        timeZones.Setup(zone => zone.GetTimeZoneIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(timeZone);

        return (
            new GetStockTransactionReportQueryHandler(scopeProvider.Object, store.Object, timeZones.Object),
            captured,
            timeZones);
    }
}
