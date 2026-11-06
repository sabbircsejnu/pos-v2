using Moq;
using RetailPOS.Application.Common.Formatting;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.UnitTests.Sales;

public sealed class SaleReceiptThermalDocumentTests
{
    private static readonly SaleReceiptSettingsReadModel Settings =
        new("80mm", "80mm", false, false, false, null, "Thank You", "Company policy", "Powered by: Test",
            "AHIR BORKA BAZAR", null, "+8801898806900", "COMPANY-TAX")
        {
            ConcernOf = "PARENT GROUP",
            Website = "www.example.test",
            CurrencyCode = "BDT"
        };

    private static SaleReadModel Sale() => new(
        Id: 1,
        SaleNumber: "SLBL-1-C-10511266",
        OutletId: 5,
        OutletName: "Outlet Five",
        OutletAddress: "Bashundhara City",
        OutletContactNumber: "+8801898806901",
        TerminalId: 9,
        TerminalName: "Counter 1",
        CashSessionId: 3,
        BusinessDate: new DateTime(2026, 11, 5),
        SaleDateTime: null,
        CustomerId: 2,
        CustomerName: "MD ROMIJ ALOM",
        CashierId: 7,
        CashierName: "Cashier",
        SaleDate: new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc),
        TotalAmount: 1290m,
        Discount: 0m,
        Tax: 0m,
        NetTotal: 1290m,
        PaymentMethod: "cash",
        Status: "completed",
        CreatedAt: new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc),
        Items:
        [
            new SaleItemReadModel(1, 10, "Orna Hijab", "ABA1010P", 1, 1290m, null, 0m, 1290m, null, "ABA1010P", TaxInclusive: true)
        ],
        Payments: [new SalePaymentReadModel(1, "cash", 1290m, 1290m, 0m)])
    {
        CustomerPhone = "01711039823",
        EffectiveTimeZoneId = "Asia/Dhaka",
        OutletPrint = new SaleOutletPrintReadModel("IRANI MART-1", "0029807530203", "6.3", "No refunds.\nExchange in 15 days.", "S.R.O. No. 19\nVAT applies.")
    };

    private static Task<SaleReceiptDocumentReadModel> ComposeAsync(SaleReadModel sale, SaleReceiptSettingsReadModel? settings = null)
    {
        var store = new Mock<ISaleReadStore>();
        store.Setup(item => item.FindForReceiptAsync(sale.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sale);
        var provider = new Mock<ISaleReceiptSettingsProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings ?? Settings);
        var handler = new GenerateSaleReceiptDocumentCommandHandler(
            store.Object,
            provider.Object,
            Mock.Of<ISaleReceiptHistoryWriter>(),
            Mock.Of<ISaleReceiptActionAuditWriter>());
        return handler.Handle(new GenerateSaleReceiptDocumentCommand(sale.Id, "80mm", 7), CancellationToken.None);
    }

    [Fact]
    public async Task Header_ComesFromCompanyAndOutletSettings()
    {
        var header = (await ComposeAsync(Sale())).Header;

        Assert.Equal("AHIR BORKA BAZAR", header.BusinessName);
        Assert.Equal("PARENT GROUP", header.ConcernOf);
        Assert.Equal("IRANI MART-1", header.BranchName);
        Assert.Equal("Bashundhara City", header.OutletAddress);
        Assert.Equal("+8801898806901", header.ShowroomHotline);
        Assert.Equal("+8801898806900", header.CentralHotline);
        Assert.Equal("www.example.test", header.Website);
        Assert.Equal("BIN: 0029807530203, Mushak-6.3", header.TaxIdentityLine);
    }

    [Fact]
    public async Task EmptyOptionalSettings_AreLeftOffAndBranchDefaultsToOutletName()
    {
        var sale = Sale() with
        {
            OutletContactNumber = " ",
            CustomerPhone = null,
            OutletPrint = new SaleOutletPrintReadModel(" ", null, "", null, "  ")
        };
        var settings = Settings with { TaxNumber = "", CompanyPhone = "", ReturnPolicy = null };
        settings = settings with { ConcernOf = " ", Website = "" };

        var document = await ComposeAsync(sale, settings);

        Assert.Equal("Outlet Five", document.Header.BranchName);
        Assert.Null(document.Header.ConcernOf);
        Assert.Null(document.Header.ShowroomHotline);
        Assert.Null(document.Header.CentralHotline);
        Assert.Null(document.Header.Website);
        Assert.Null(document.Header.TaxIdentityLine);
        Assert.Null(document.SaleInfo.CustomerMobile);
        Assert.Null(document.Footer.ReturnPolicy);
        Assert.Null(document.Footer.VatLawNotification);
    }

    [Fact]
    public async Task OutletValues_WinOverCompanyValues_AndCompanyValuesRemainTheFallback()
    {
        var configured = await ComposeAsync(Sale());
        Assert.Equal("No refunds.\nExchange in 15 days.", configured.Footer.ReturnPolicy);
        Assert.Equal("S.R.O. No. 19\nVAT applies.", configured.Footer.VatLawNotification);
        Assert.Equal("0029807530203", configured.Header.BinOrVatNo);

        var fallback = await ComposeAsync(Sale() with { OutletPrint = null });
        Assert.Equal("Company policy", fallback.Footer.ReturnPolicy);
        Assert.Equal("COMPANY-TAX", fallback.Header.BinOrVatNo);
        Assert.Equal("BIN: COMPANY-TAX", fallback.Header.TaxIdentityLine);
    }

    [Theory]
    [InlineData("0029807530203", "6.3", "BIN: 0029807530203, Mushak-6.3")]
    [InlineData("0029807530203", "Mushak-6.3", "BIN: 0029807530203, Mushak-6.3")]
    [InlineData("0029807530203", null, "BIN: 0029807530203")]
    public async Task TaxIdentityLine_PrintsWhatIsConfigured(string bin, string? mushak, string expected)
    {
        var sale = Sale() with { OutletPrint = new SaleOutletPrintReadModel(null, bin, mushak, null, null) };

        Assert.Equal(expected, (await ComposeAsync(sale)).Header.TaxIdentityLine);
    }

    [Fact]
    public async Task MushakAlone_PrintsWithoutABin()
    {
        var sale = Sale() with { OutletPrint = new SaleOutletPrintReadModel(null, null, "6.3", null, null) };

        var document = await ComposeAsync(sale, Settings with { TaxNumber = null });

        Assert.Equal("Mushak-6.3", document.Header.TaxIdentityLine);
    }

    [Theory]
    [InlineData("ProBook Laptop 15 - Default", "ProBook Laptop 15 (SKU-1)")]
    [InlineData("ProBook Laptop 15", "ProBook Laptop 15 (SKU-1)")]
    [InlineData("SKU-1", "ProBook Laptop 15 (SKU-1)")]
    [InlineData("ProBook Laptop 15 - Black 128GB", "ProBook Laptop 15 - Black 128GB (SKU-1)")]
    [InlineData("Black 128GB", "ProBook Laptop 15 - Black 128GB (SKU-1)")]
    public async Task ItemPrintName_DoesNotRepeatTheProductName(string variantName, string expected)
    {
        var sale = Sale() with
        {
            Items = [new SaleItemReadModel(1, 10, "ProBook Laptop 15", "SKU-1", 1, 1290m, null, 0m, 1290m, null, variantName)]
        };

        Assert.Equal(expected, Assert.Single((await ComposeAsync(sale)).Items).PrintName);
    }

    [Theory]
    [InlineData("Asia/Dhaka", "05-Nov-2026 04:20 PM")]
    [InlineData("Asia/Karachi", "05-Nov-2026 03:20 PM")]
    [InlineData(null, "05-Nov-2026 04:20 PM")]
    public async Task InvoiceDate_UsesTheEffectiveTimeZone(string? timeZoneId, string expected)
    {
        var document = await ComposeAsync(Sale() with { EffectiveTimeZoneId = timeZoneId });

        Assert.Equal(expected, document.SaleInfo.InvoiceDateText);
    }

    [Fact]
    public async Task Amounts_AreTheSaleSnapshots_NeverRecalculated()
    {
        var sale = Sale() with
        {
            TotalAmount = 1705m,
            Tax = 155m,
            Discount = 100m,
            Items =
            [
                new SaleItemReadModel(1, 10, "Orna Hijab", "ABA1010P", 2, 1000m, null, 250m, 1750m, null, "Black", VatRate: 10m, VatAmount: 155m)
            ],
            Payments = [new SalePaymentReadModel(1, "cash", 1705m, 2000m, 295m)]
        };

        var document = await ComposeAsync(sale);
        var line = Assert.Single(document.Items);

        Assert.Equal("Orna Hijab - Black (ABA1010P)", line.PrintName);
        Assert.Equal(12.5m, line.DiscountPercent);
        Assert.Equal(1000m, line.UnitPrice);
        Assert.Equal(1750m, line.LineTotal);
        Assert.Equal(2000m, document.Summary.Subtotal);
        Assert.Equal(350m, document.Summary.Discount);
        Assert.Equal(155m, document.Summary.Tax);
        Assert.Equal(1705m, document.Summary.GrandTotal);
        Assert.Equal(1705m, document.Summary.PaidAmount);
        Assert.Equal(295m, document.Summary.Change);
        Assert.Equal(0m, document.Summary.RoundOff);
        Assert.False(document.TaxInclusive);
    }

    [Fact]
    public async Task PaymentLines_ShowOnlyMethodsWithARecordedAmountAboveZero()
    {
        var sale = Sale() with
        {
            Payments =
            [
                new SalePaymentReadModel(1, "cash", 200m, 200m, 0m),
                new SalePaymentReadModel(2, "cash", 90m, 90m, 0m),
                new SalePaymentReadModel(3, "mobile", 1000m, null, 0m),
                new SalePaymentReadModel(4, "card", 0m, null, 0m)
            ]
        };

        var document = await ComposeAsync(sale);

        Assert.Equal(
            [("Cash Amount", 290m), ("Mobile Banking Amount", 1000m)],
            document.PaymentLines.Select(line => (line.Label, line.Amount)));
        Assert.Equal(1290m, document.Summary.PaidAmount);
        Assert.Equal(1290m, document.Summary.GrandTotal);
    }

    [Theory]
    [InlineData("cash", "Cash Amount")]
    [InlineData("card", "Card Amount")]
    [InlineData("mobile", "Mobile Banking Amount")]
    public async Task PaymentLines_ASingleMethodSaleShowsOnlyThatRow(string method, string label)
    {
        var sale = Sale() with { Payments = [new SalePaymentReadModel(1, method, 1290m, null, 0m)] };

        var document = await ComposeAsync(sale);

        Assert.Equal([(label, 1290m)], document.PaymentLines.Select(line => (line.Label, line.Amount)));
    }

    [Fact]
    public async Task ChangeAndPaid_AreKeptEvenWhenPaymentRowsAreHidden()
    {
        var sale = Sale() with { Payments = [new SalePaymentReadModel(1, "cash", 1290m, 1500m, 210m)] };

        var document = await ComposeAsync(sale);

        Assert.Equal(1290m, document.Summary.PaidAmount);
        Assert.Equal(210m, document.Summary.Change);
    }

    [Fact]
    public async Task SaleWithoutPaymentRows_IsAttributedToItsPrimaryMethod()
    {
        var lines = (await ComposeAsync(Sale() with { Payments = [], PaymentMethod = "card" })).PaymentLines;

        Assert.Equal([("Card Amount", 1290m)], lines.Select(line => (line.Label, line.Amount)));
    }

    [Fact]
    public async Task Reprints_KeepTheInvoiceNumberAndDate()
    {
        var first = await ComposeAsync(Sale());
        var second = await ComposeAsync(Sale());

        Assert.Equal("SLBL-1-C-10511266", second.SaleInfo.InvoiceNumber);
        Assert.Equal(first.SaleInfo.InvoiceNumber, second.SaleInfo.InvoiceNumber);
        Assert.Equal(first.SaleInfo.InvoiceDateText, second.SaleInfo.InvoiceDateText);
    }

    [Fact]
    public async Task AmountInWords_FollowsTheConfiguredCurrency()
    {
        Assert.Equal("One Thousand Two Hundred Ninety Taka Only", (await ComposeAsync(Sale())).AmountInWords);

        var usd = Settings with { CurrencyCode = "USD", CurrencyName = "US Dollar" };
        Assert.Equal(
            "One Thousand Two Hundred Ninety US Dollar Only",
            (await ComposeAsync(Sale(), usd)).AmountInWords);
    }

    [Theory]
    [InlineData("0", "Zero Taka Only")]
    [InlineData("1290", "One Thousand Two Hundred Ninety Taka Only")]
    [InlineData("1290.50", "One Thousand Two Hundred Ninety Taka and Fifty Paisa Only")]
    [InlineData("100000", "One Lakh Taka Only")]
    [InlineData("12345678.05", "One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Taka and Five Paisa Only")]
    public void AmountInWordsFormatter_UsesLakhAndCrore(string amount, string expected) =>
        Assert.Equal(
            expected,
            AmountInWordsFormatter.Format(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "Taka", "Paisa"));

    [Fact]
    public void NormalizePrintText_KeepsLineBreaksAndSeparatesUnchangedFromCleared()
    {
        Assert.Null(OutletPosSetupRules.NormalizePrintText(null, 10, "Field"));
        Assert.Equal(string.Empty, OutletPosSetupRules.NormalizePrintText("   ", 10, "Field"));
        Assert.Equal("a\nb\nc", OutletPosSetupRules.NormalizePrintText(" a\r\nb\rc ", 10, "Field"));
    }

    [Fact]
    public void NormalizePrintText_RefusesTextOverTheLimit()
    {
        var exception = Assert.Throws<OutletSetupException>(() =>
            OutletPosSetupRules.NormalizePrintText(new string('x', 11), 10, "Field"));

        Assert.Equal(OutletSetupErrorCodes.InvalidPrintSetting, exception.Code);
    }
}
