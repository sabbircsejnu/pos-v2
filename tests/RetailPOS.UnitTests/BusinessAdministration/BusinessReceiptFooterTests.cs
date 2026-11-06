using MediatR;
using Moq;
using RetailPOS.Application.BusinessAdministration;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.Queries;
using RetailPOS.Application.BusinessAdministration.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings.Commands;

namespace RetailPOS.UnitTests.BusinessAdministration;

public sealed class BusinessReceiptFooterTests
{
    private static Mock<IBusinessTargetAccess> Denied()
    {
        var access = new Mock<IBusinessTargetAccess>();
        access.Setup(item => item.RequirePlatformSuperAdmin()).Throws<UnauthorizedAccessException>();
        return access;
    }

    private static CreateBusinessCommand CreateCommand(bool show = true, string? text = "Powered by: Test") => new(
        "Business", null, null, null, "Owner", "o@example.test", "Manager", "m@example.test",
        "Sales", "s@example.test", "Accounts", "a@example.test", "Main", false, null, null, null, show, text);

    [Fact]
    public async Task UpdateFooter_RefusesANonPlatformCallerBeforeTouchingTheStore()
    {
        var store = new Mock<IBusinessAdministrationWriteStore>(MockBehavior.Strict);
        var handler = new UpdateBusinessReceiptFooterCommandHandler(store.Object, Denied().Object, Mock.Of<ISender>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new UpdateBusinessReceiptFooterCommand(1, true, "Powered by: X"), CancellationToken.None));

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBusiness_RefusesANonPlatformCallerBeforeTouchingTheStore()
    {
        var store = new Mock<IBusinessOnboardingWriteStore>(MockBehavior.Strict);
        var handler = new CreateBusinessCommandHandler(store.Object, Denied().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(CreateCommand(), CancellationToken.None));

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateFooter_NormalizesTextAndReturnsTheBusiness()
    {
        var store = new Mock<IBusinessAdministrationWriteStore>();
        store.Setup(item => item.UpdateReceiptFooterAsync(7, true, "Powered by: X Y", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var summary = new BusinessSummaryReadModel(7, "B", null, null, true, null, null, null, null, null, 0, 0, 0, null);
        var sender = new Mock<ISender>();
        sender.Setup(item => item.Send(It.IsAny<GetBusinessByIdQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(summary);
        var handler = new UpdateBusinessReceiptFooterCommandHandler(store.Object, Mock.Of<IBusinessTargetAccess>(), sender.Object);

        var result = await handler.Handle(
            new UpdateBusinessReceiptFooterCommand(7, true, "  Powered by: X\r\nY  "),
            CancellationToken.None);

        Assert.Same(summary, result);
        store.VerifyAll();
    }

    [Fact]
    public async Task UpdateFooter_ReportsAMissingBusiness()
    {
        var store = new Mock<IBusinessAdministrationWriteStore>();
        var handler = new UpdateBusinessReceiptFooterCommandHandler(store.Object, Mock.Of<IBusinessTargetAccess>(), Mock.Of<ISender>());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new UpdateBusinessReceiptFooterCommand(404, false, null), CancellationToken.None));
    }

    [Fact]
    public void NormalizeText_TreatsBlankAsEmptyAndRefusesTextOverTheColumnLimit()
    {
        Assert.Null(BusinessReceiptFooterRules.NormalizePoweredByText("   "));
        Assert.Equal(new string('x', 150), BusinessReceiptFooterRules.NormalizePoweredByText(new string('x', 150)));
        Assert.Throws<ArgumentException>(() => BusinessReceiptFooterRules.NormalizePoweredByText(new string('x', 151)));
    }

    [Theory]
    [InlineData("Powered by: Someone", true)]
    [InlineData("  POWERED BY Someone", true)]
    [InlineData("powered-by someone", true)]
    [InlineData("Please come again\nPowered by: Someone", true)]
    [InlineData("Please come again", false)]
    [InlineData("Empowered by our customers", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsPoweredByLine_MatchesOnlyAPoweredByLine(string? footer, bool expected) =>
        Assert.Equal(expected, BusinessReceiptFooterRules.ContainsPoweredByLine(footer));

    [Fact]
    public async Task ReceiptSettings_RefuseAPoweredByFooterButKeepOtherFooters()
    {
        var store = new Mock<ISystemSettingsStore>();
        store.Setup(item => item.UpdateReceiptAsync(It.IsAny<ReceiptSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettings());
        var handler = new UpdateReceiptSettingsCommandHandler(store.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new UpdateReceiptSettingsCommand(new ReceiptSettings { Footer = "Powered by: Someone" }),
            CancellationToken.None));
        store.Verify(item => item.UpdateReceiptAsync(It.IsAny<ReceiptSettings>(), It.IsAny<CancellationToken>()), Times.Never);

        await handler.Handle(new UpdateReceiptSettingsCommand(new ReceiptSettings { Footer = "Please come again" }), CancellationToken.None);
        store.Verify(item => item.UpdateReceiptAsync(It.IsAny<ReceiptSettings>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<SaleReceiptFooterReadModel> ComposeFooterAsync(bool show, string? text, string? settingsFooter)
    {
        var sale = new SaleReadModel(
            1, "INV-1", 5, "Outlet", "Address", null, null, null, null, null, null, null, null, 7, "Cashier",
            new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc), 100m, 0m, 0m, 100m, "cash", "completed",
            DateTime.UtcNow, [], [])
        {
            ShowPoweredByOnReceipt = show,
            PoweredByText = text
        };
        var settings = new SaleReceiptSettingsReadModel(
            "80mm", "80mm", false, false, false, null, "Thanks", null, settingsFooter, "Shop", null, null, null);
        var readStore = new Mock<ISaleReadStore>();
        readStore.Setup(item => item.FindForReceiptAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(sale);
        var provider = new Mock<ISaleReceiptSettingsProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var handler = new GenerateSaleReceiptDocumentCommandHandler(
            readStore.Object,
            provider.Object,
            Mock.Of<ISaleReceiptHistoryWriter>(),
            Mock.Of<ISaleReceiptActionAuditWriter>());
        return (await handler.Handle(new GenerateSaleReceiptDocumentCommand(1, "80mm", 7), CancellationToken.None)).Footer;
    }

    [Theory]
    [InlineData(true, "Powered by: Irani Mart Technology", "Powered by: Irani Mart Technology")]
    [InlineData(true, "   ", null)]
    [InlineData(true, null, null)]
    [InlineData(false, "Powered by: Irani Mart Technology", null)]
    public async Task Receipt_PrintsThePlatformFooterOnlyWhenEnabledWithText(bool show, string? text, string? expected) =>
        Assert.Equal(expected, (await ComposeFooterAsync(show, text, "Please come again")).PoweredByText);

    [Fact]
    public async Task ExistingReceiptSettingsFooter_KeepsPrintingWhenThePlatformFooterIsOff()
    {
        var footer = await ComposeFooterAsync(false, null, "Please come again");

        Assert.Equal("Please come again", footer.FooterText);
        Assert.Null(footer.PoweredByText);
    }

    [Fact]
    public async Task ReceiptSettingsFooter_CannotPrintAPoweredByLineWhenThePlatformFooterIsOff()
    {
        var footer = await ComposeFooterAsync(false, "Platform text", "Please come again\nPowered by: Tenant");

        Assert.Equal("Please come again", footer.FooterText);
        Assert.Null(footer.PoweredByText);
        Assert.Null((await ComposeFooterAsync(false, null, "Powered by: Tenant")).FooterText);
    }

    [Fact]
    public async Task ReceiptSettingsFooter_CannotReplaceThePlatformText()
    {
        var footer = await ComposeFooterAsync(true, "Powered by: Platform", "Powered by: Tenant");

        Assert.Null(footer.FooterText);
        Assert.Equal("Powered by: Platform", footer.PoweredByText);
    }
}
