using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Queries;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Tests.Grns;

public sealed class SearchGrnsFiltersTests
{
    private static readonly GrnReadScope WarehouseOnly = new(1, false, [], [301]);

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static GrnReceivedWindow DayWindow(DateOnly? from, DateOnly? to, string zone)
    {
        var (fromUtc, toExclusiveUtc) = CompanyDates.DayWindow(from, to, zone);
        return new GrnReceivedWindow(fromUtc, toExclusiveUtc);
    }

    [Fact]
    public void Window_BothDates_UsesExclusiveMidnightOfFollowingLocalDay()
    {
        var window = DayWindow(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10), "Asia/Dhaka");

        Assert.Equal(Utc(2026, 3, 9, 18), window.FromUtc);
        Assert.Equal(Utc(2026, 3, 10, 18), window.ToExclusiveUtc);
    }

    [Fact]
    public void Window_DatesAreIndependentlyOptional()
    {
        var fromOnly = DayWindow(new DateOnly(2026, 3, 10), null, "Asia/Dhaka");
        var toOnly = DayWindow(null, new DateOnly(2026, 3, 10), "Asia/Dhaka");

        Assert.Equal(Utc(2026, 3, 9, 18), fromOnly.FromUtc);
        Assert.Null(fromOnly.ToExclusiveUtc);
        Assert.Null(toOnly.FromUtc);
        Assert.Equal(Utc(2026, 3, 10, 18), toOnly.ToExclusiveUtc);
    }

    [Fact]
    public void Window_LocalMidnightSkippedByDst_StartsAtTheFirstInstantOfTheDay()
    {
        // Havana springs forward at 00:00 on 8 March 2026, so that day starts at 01:00 CDT (05:00 UTC).
        var window = DayWindow(new DateOnly(2026, 3, 8), null, "America/Havana");

        Assert.Equal(Utc(2026, 3, 8, 5), window.FromUtc);
    }

    [Fact]
    public void Window_LocalMidnightRepeatedByDst_StartsAtTheEarlierOccurrence()
    {
        // Havana falls back at 01:00 on 1 November 2026, so 00:00 happens twice; the first is 04:00 UTC.
        var window = DayWindow(new DateOnly(2026, 11, 1), null, "America/Havana");

        Assert.Equal(Utc(2026, 11, 1, 4), window.FromUtc);
    }

    [Fact]
    public void Window_DstDay_IsTwentyThreeHoursLong()
    {
        var day = new DateOnly(2026, 3, 8);
        var window = DayWindow(day, day, "America/New_York");

        Assert.Equal(Utc(2026, 3, 8, 5), window.FromUtc);
        Assert.Equal(Utc(2026, 3, 9, 4), window.ToExclusiveUtc);
    }

    [Theory]
    [InlineData(2026, 3, 9, 17, 59, 9)]
    [InlineData(2026, 3, 9, 18, 0, 10)]
    [InlineData(2026, 3, 10, 17, 59, 10)]
    [InlineData(2026, 3, 10, 18, 0, 11)]
    public void AccountingDate_IsTheCompanyZoneDate(int year, int month, int day, int hour, int minute, int expectedDay)
    {
        var receivedAt = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        var date = CompanyDates.DateFor(receivedAt, "Asia/Dhaka");

        Assert.Equal(new DateTime(2026, 3, expectedDay, 0, 0, 0, DateTimeKind.Utc), date);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Fact]
    public void AccountingDate_AgreesWithTheFilterWindowForTheSameDay()
    {
        const string zone = "America/New_York";
        var day = new DateOnly(2026, 3, 8);
        var window = DayWindow(day, day, zone);
        var expected = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, CompanyDates.DateFor(window.FromUtc!.Value, zone));
        Assert.Equal(expected, CompanyDates.DateFor(window.ToExclusiveUtc!.Value.AddSeconds(-1), zone));
        Assert.NotEqual(expected, CompanyDates.DateFor(window.ToExclusiveUtc!.Value, zone));
    }

    [Fact]
    public async Task Handle_FromDateAfterToDate_IsRejectedBeforeAnyRead()
    {
        var store = new Mock<IGrnReadStore>(MockBehavior.Strict);
        var provider = new Mock<IGrnReadScopeProvider>(MockBehavior.Strict);
        var handler = new SearchGrnsQueryHandler(store.Object, provider.Object, Mock.Of<ICompanyTimeZoneProvider>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            Query(fromDate: new DateOnly(2026, 3, 11), toDate: new DateOnly(2026, 3, 10)),
            CancellationToken.None));

        Assert.Equal("From Date cannot be later than To Date.", exception.Message);
    }

    [Fact]
    public async Task Handle_InvalidLocationType_IsRejected()
    {
        var handler = CreateHandler(new Mock<IGrnReadStore>(MockBehavior.Strict));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Query(locationType: "shop"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_LocationWithoutType_IsRejected()
    {
        var handler = CreateHandler(new Mock<IGrnReadStore>(MockBehavior.Strict));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(Query(locationId: 301), CancellationToken.None));

        Assert.Equal("Location Type is required when a Location is selected.", exception.Message);
    }

    [Theory]
    [InlineData("warehouse", 302)]
    [InlineData("outlet", 301)]
    public async Task Handle_LocationOutsideScope_IsDeniedWithoutReading(string locationType, long locationId)
    {
        var handler = CreateHandler(new Mock<IGrnReadStore>(MockBehavior.Strict));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(Query(locationType: locationType, locationId: locationId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthorizedFilters_ReachTheStoreWithNormalizedTypeAndCompanyWindow()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.SearchAsync(
                It.IsAny<GrnSearchCriteria>(),
                WarehouseOnly,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnSearchReadModel([], 0, 1, 10));
        var handler = CreateHandler(store);

        await handler.Handle(
            Query(
                status: "partial",
                locationType: " WAREHOUSE ",
                locationId: 301,
                fromDate: new DateOnly(2026, 3, 10),
                toDate: new DateOnly(2026, 3, 11)),
            CancellationToken.None);

        store.Verify(port => port.SearchAsync(
            It.Is<GrnSearchCriteria>(criteria =>
                criteria.Status == "partial"
                && criteria.LocationType == "warehouse"
                && criteria.LocationId == 301
                && criteria.ReceivedDate!.FromUtc == Utc(2026, 3, 9, 18)
                && criteria.ReceivedDate.ToExclusiveUtc == Utc(2026, 3, 11, 18)),
            WarehouseOnly,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TypeWithoutLocation_DoesNotRequireTheUserToHoldThatType()
    {
        var store = new Mock<IGrnReadStore>();
        store.Setup(port => port.SearchAsync(
                It.Is<GrnSearchCriteria>(criteria => criteria.LocationType == "outlet" && criteria.LocationId == null),
                WarehouseOnly,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnSearchReadModel([], 0, 1, 10));

        var result = await CreateHandler(store).Handle(Query(locationType: "outlet"), CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    private static SearchGrnsQuery Query(
        string? status = null,
        string? locationType = null,
        long? locationId = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null) =>
        new(null, status, null, null, 1, 10, locationType, locationId, fromDate, toDate);

    private static SearchGrnsQueryHandler CreateHandler(Mock<IGrnReadStore> store)
    {
        var provider = new Mock<IGrnReadScopeProvider>();
        provider.Setup(port => port.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(WarehouseOnly);
        var settings = new Mock<ICompanyTimeZoneProvider>();
        settings.Setup(port => port.GetTimeZoneIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync("Asia/Dhaka");
        return new SearchGrnsQueryHandler(store.Object, provider.Object, settings.Object);
    }
}
