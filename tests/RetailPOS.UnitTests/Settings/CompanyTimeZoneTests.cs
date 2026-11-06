using Moq;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings.Commands;
using RetailPOS.Application.Settings.Queries;

namespace RetailPOS.Tests.Settings;

public sealed class CompanyTimeZoneTests
{
    private const string SharedZone = "Asia/Dhaka";
    private const long BusinessId = 7;

    private static SystemSettings Shared()
    {
        var settings = new SystemSettings();
        settings.Company.TimeZone = SharedZone;
        return settings;
    }

    private static BusinessPosSettingsSnapshot Business(string zone, bool persisted) =>
        new(BusinessId, 24, zone, false, 0m, "VAT", false, 0m, 1, null, null, persisted);

    private static Mock<ISystemSettingsStore> SystemStore(SystemSettings settings)
    {
        var store = new Mock<ISystemSettingsStore>();
        store.Setup(port => port.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        return store;
    }

    private static Mock<IBusinessPosSettingsStore> BusinessStore(string zone, bool persisted)
    {
        var store = new Mock<IBusinessPosSettingsStore>();
        store.Setup(port => port.GetAsync(BusinessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Business(zone, persisted));
        return store;
    }

    private static Mock<ICompanyTimeZoneProvider> Provider(string zone)
    {
        var provider = new Mock<ICompanyTimeZoneProvider>();
        provider.Setup(port => port.GetTimeZoneIdAsync(BusinessId, It.IsAny<CancellationToken>())).ReturnsAsync(zone);
        return provider;
    }

    private static Mock<IBusinessScope> Scope(long? businessId)
    {
        var scope = new Mock<IBusinessScope>();
        scope.SetupGet(port => port.EffectiveBusinessId).Returns(businessId);
        return scope;
    }

    [Fact]
    public void ADateWithoutAZone_IsACalendarDate_AndAnInstantIsConverted()
    {
        Assert.Equal(new DateOnly(2026, 4, 1), CompanyDates.CalendarDateOf(new DateTime(2026, 4, 1), SharedZone));
        Assert.Equal(
            new DateOnly(2026, 4, 1),
            CompanyDates.CalendarDateOf(new DateTime(2026, 3, 31, 18, 30, 0, DateTimeKind.Utc), SharedZone));
        Assert.Equal(
            new DateOnly(2026, 3, 31),
            CompanyDates.CalendarDateOf(new DateTime(2026, 3, 31, 17, 59, 0, DateTimeKind.Utc), SharedZone));
    }

    [Fact]
    public void RangeBounds_CoverTheWholeCompanyDay_AndKeepInstants()
    {
        var day = new DateTime(2026, 4, 1);
        var instant = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 3, 31, 18, 0, 0, DateTimeKind.Utc), CompanyDates.StartUtc(day, SharedZone));
        Assert.Equal(
            new DateTime(2026, 4, 1, 18, 0, 0, DateTimeKind.Utc).AddTicks(-1),
            CompanyDates.EndInclusiveUtc(day, SharedZone));
        Assert.Equal(instant, CompanyDates.StartUtc(instant, SharedZone));
        Assert.Equal(instant, CompanyDates.EndInclusiveUtc(instant, SharedZone));
    }

    [Fact]
    public async Task CurrentZone_WithoutABusiness_IsTheSharedValue()
    {
        var shared = Shared();
        shared.Company.TimeZone = "Europe/London";
        var current = new CurrentCompanyTimeZone(
            Scope(null).Object,
            new Mock<ICompanyTimeZoneProvider>(MockBehavior.Strict).Object,
            SystemStore(shared).Object);

        Assert.Equal("Europe/London", await current.GetTimeZoneIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CurrentZone_InsideABusiness_IsThatBusinessZone()
    {
        var current = new CurrentCompanyTimeZone(
            Scope(BusinessId).Object,
            Provider("Asia/Tokyo").Object,
            SystemStore(Shared()).Object);

        Assert.Equal("Asia/Tokyo", await current.GetTimeZoneIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Provider_UsesTheZoneSavedForTheBusiness()
    {
        var provider = new CompanyTimeZoneProvider(
            BusinessStore("Asia/Tokyo", persisted: true).Object,
            SystemStore(Shared()).Object);

        Assert.Equal("Asia/Tokyo", await provider.GetTimeZoneIdAsync(BusinessId, CancellationToken.None));
    }

    [Fact]
    public async Task Provider_BusinessThatNeverSavedAZone_KeepsTheSharedValue()
    {
        var shared = Shared();
        shared.Company.TimeZone = "Europe/London";
        var provider = new CompanyTimeZoneProvider(
            BusinessStore("Asia/Dhaka", persisted: false).Object,
            SystemStore(shared).Object);

        Assert.Equal("Europe/London", await provider.GetTimeZoneIdAsync(BusinessId, CancellationToken.None));
    }

    [Fact]
    public async Task SettingsQuery_InsideABusiness_ReturnsItsZone_WithoutChangingTheSharedSettings()
    {
        var shared = Shared();
        var handler = new GetSystemSettingsQueryHandler(
            SystemStore(shared).Object,
            Scope(BusinessId).Object,
            Provider("Asia/Tokyo").Object);

        var result = await handler.Handle(new GetSystemSettingsQuery(), CancellationToken.None);

        Assert.Equal("Asia/Tokyo", result.Company.TimeZone);
        Assert.Equal(SharedZone, shared.Company.TimeZone);
        Assert.NotSame(shared, result);
    }

    [Fact]
    public async Task SettingsQuery_WithoutABusiness_ReturnsTheSharedSettings()
    {
        var shared = Shared();
        var provider = new Mock<ICompanyTimeZoneProvider>(MockBehavior.Strict);
        var handler = new GetSystemSettingsQueryHandler(
            SystemStore(shared).Object,
            Scope(null).Object,
            provider.Object);

        Assert.Same(shared, await handler.Handle(new GetSystemSettingsQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCompany_InsideABusiness_ChangesOnlyThatBusinessZone_AndAudits()
    {
        var system = SystemStore(Shared());
        system.Setup(port => port.UpdateCompanyAsync(It.IsAny<CompanySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompanySettings company, CancellationToken _) => new SystemSettings { Company = company });
        var before = Business(SharedZone, persisted: true);
        var after = Business("Asia/Tokyo", persisted: true);
        var business = new Mock<IBusinessPosSettingsStore>();
        business.Setup(port => port.UpdateAsync(
                BusinessId,
                It.Is<BusinessPosSettingsUpdate>(update => update.DefaultTimeZoneId == "Asia/Tokyo"),
                11,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((before, after));
        var audit = new Mock<IBusinessPosSettingsAuditWriter>();
        var handler = new UpdateCompanySettingsCommandHandler(
            system.Object,
            business.Object,
            audit.Object,
            Provider(SharedZone).Object);

        var result = await handler.Handle(
            new UpdateCompanySettingsCommand(new CompanySettings { TimeZone = "Asia/Tokyo" }, BusinessId, 11),
            CancellationToken.None);

        Assert.Equal("Asia/Tokyo", result.Company.TimeZone);
        system.Verify(port => port.UpdateCompanyAsync(
            It.Is<CompanySettings>(company => company.TimeZone == SharedZone),
            It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(port => port.RecordUpdatedAsync(before, after, 11, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCompany_UnchangedZone_CreatesNoBusinessSettings()
    {
        var system = SystemStore(Shared());
        system.Setup(port => port.UpdateCompanyAsync(It.IsAny<CompanySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompanySettings company, CancellationToken _) => new SystemSettings { Company = company });
        var business = new Mock<IBusinessPosSettingsStore>(MockBehavior.Strict);
        var handler = new UpdateCompanySettingsCommandHandler(
            system.Object,
            business.Object,
            Mock.Of<IBusinessPosSettingsAuditWriter>(),
            Provider(SharedZone).Object);

        await handler.Handle(
            new UpdateCompanySettingsCommand(new CompanySettings { TimeZone = SharedZone }, BusinessId, 11),
            CancellationToken.None);

        business.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateCompany_WithoutABusiness_ChangesTheSharedValue()
    {
        var system = SystemStore(Shared());
        system.Setup(port => port.UpdateCompanyAsync(It.IsAny<CompanySettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompanySettings company, CancellationToken _) => new SystemSettings { Company = company });
        var business = new Mock<IBusinessPosSettingsStore>(MockBehavior.Strict);
        var handler = new UpdateCompanySettingsCommandHandler(
            system.Object,
            business.Object,
            Mock.Of<IBusinessPosSettingsAuditWriter>(),
            new Mock<ICompanyTimeZoneProvider>(MockBehavior.Strict).Object);

        await handler.Handle(
            new UpdateCompanySettingsCommand(new CompanySettings { TimeZone = "Asia/Tokyo" }),
            CancellationToken.None);

        system.Verify(port => port.UpdateCompanyAsync(
            It.Is<CompanySettings>(company => company.TimeZone == "Asia/Tokyo"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reversal_WithoutAChosenDate_PostsOnTodayInTheCompanyZone()
    {
        var store = new Mock<IManualJournalDraftStore>();
        store.Setup(port => port.ReverseAsync(
                BusinessId,
                3,
                40,
                It.IsAny<ManualJournalReversalInput>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(41);
        var handler = new ReverseJournalEntryCommandHandler(store.Object, Provider("Pacific/Kiritimati").Object);
        var expected = CompanyDates.DateFor(DateTime.UtcNow, "Pacific/Kiritimati");

        await handler.Handle(
            new ReverseJournalEntryCommand(BusinessId, 3, 40, new ManualJournalReversalInput("Mistake", null, null)),
            CancellationToken.None);

        store.Verify(port => port.ReverseAsync(
            BusinessId,
            3,
            40,
            It.Is<ManualJournalReversalInput>(input => input.PostingDate == expected),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reversal_KeepsAChosenDate()
    {
        var chosen = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var store = new Mock<IManualJournalDraftStore>();
        var provider = new Mock<ICompanyTimeZoneProvider>(MockBehavior.Strict);
        var handler = new ReverseJournalEntryCommandHandler(store.Object, provider.Object);

        await handler.Handle(
            new ReverseJournalEntryCommand(BusinessId, 3, 40, new ManualJournalReversalInput("Mistake", chosen, null)),
            CancellationToken.None);

        store.Verify(port => port.ReverseAsync(
            BusinessId,
            3,
            40,
            It.Is<ManualJournalReversalInput>(input => input.PostingDate == chosen),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
