using RetailPOS.Application.Sales.Checkout;

namespace RetailPOS.Tests.Sales;

public sealed class SaleDiscountAuthorizerTests
{
    private static readonly SaleTaxPolicy NoTax = new(false, 0m, false);

    private static SaleCheckoutTotals Totals(IReadOnlyList<SaleCheckoutLineInput> lines, decimal invoiceDiscount = 0m) =>
        SaleCheckoutCalculator.Calculate(lines, invoiceDiscount, NoTax);

    private static SaleCheckoutLineInput Line(decimal unitPrice, int quantity = 1, decimal lineDiscount = 0m) =>
        new(1, quantity, unitPrice, lineDiscount);

    [Fact]
    public void NoManualDiscount_NeedsNoPermissionAndProducesNoEvidence()
    {
        var evidence = SaleDiscountAuthorizer.Authorize(
            Totals([Line(100m)]), new SaleDiscountPolicy(0m), canApply: false, canOverrideLimit: false, cashierId: 7);

        Assert.Null(evidence);
    }

    [Fact]
    public void ManualDiscount_WithoutThePermissionIsForbidden()
    {
        var exception = Assert.Throws<SaleCheckoutForbiddenException>(() => SaleDiscountAuthorizer.Authorize(
            Totals([Line(100m, lineDiscount: 1m)]), new SaleDiscountPolicy(50m), false, true, 7));

        Assert.Equal(SaleCheckoutErrorCodes.DiscountNotPermitted, exception.Code);
    }

    [Theory]
    [InlineData(10.00, true)]
    [InlineData(9.99, true)]
    [InlineData(10.01, false)]
    public void FixedLineDiscount_IsEvaluatedAsAnEffectivePercentageAgainstTheLimit(double discount, bool allowed)
    {
        var totals = Totals([Line(100m, lineDiscount: (decimal)discount)]);
        var policy = new SaleDiscountPolicy(10m);

        if (allowed)
        {
            var evidence = SaleDiscountAuthorizer.Authorize(totals, policy, true, false, 7);
            Assert.NotNull(evidence);
            Assert.False(evidence.OverrideUsed);
            Assert.Equal("line", evidence.Scope);
            Assert.Equal(10m, evidence.LimitPercent);
        }
        else
        {
            var exception = Assert.Throws<SaleCheckoutConflictException>(
                () => SaleDiscountAuthorizer.Authorize(totals, policy, true, false, 7));
            Assert.Equal(SaleCheckoutErrorCodes.DiscountLimitExceeded, exception.Code);
        }
    }

    [Fact]
    public void LineScope_UsesTheHighestLinePercentageNotTheSaleAverage()
    {
        // 1% of a 1000 line plus 50% of a 10 line: the sale average is tiny but one line is over the 20% limit.
        var totals = Totals([
            new SaleCheckoutLineInput(1, 1, 1000m, 10m),
            new SaleCheckoutLineInput(2, 1, 10m, 5m)]);

        Assert.Throws<SaleCheckoutConflictException>(() => SaleDiscountAuthorizer.Authorize(
            totals, new SaleDiscountPolicy(20m), true, false, 7));
    }

    [Fact]
    public void InvoiceDiscount_IsMeasuredAgainstTheItemsSubtotal()
    {
        var totals = Totals([Line(60m), Line(40m)], invoiceDiscount: 15m);

        var evidence = SaleDiscountAuthorizer.Authorize(totals, new SaleDiscountPolicy(15m), true, false, 7);

        Assert.Equal("invoice", evidence!.Scope);
        Assert.Equal(15m, evidence.EffectivePercent);
        Assert.Equal(15m, evidence.InvoiceDiscount);
    }

    [Fact]
    public void OverrideAllowsAnOverLimitDiscountAndFlagsItForAudit()
    {
        var evidence = SaleDiscountAuthorizer.Authorize(
            Totals([Line(100m, lineDiscount: 40m)]), new SaleDiscountPolicy(10m), true, true, cashierId: 9);

        Assert.True(evidence!.OverrideUsed);
        Assert.Equal(40m, evidence.EffectivePercent);
        Assert.Equal(9, evidence.CashierId);
        Assert.Equal(40m, evidence.LineDiscountTotal);
    }

    [Fact]
    public void ZeroLimit_MeansManualDiscountsAreNotEnabled()
    {
        var exception = Assert.Throws<SaleCheckoutConflictException>(() => SaleDiscountAuthorizer.Authorize(
            Totals([Line(100m, lineDiscount: 1m)]), new SaleDiscountPolicy(0m), true, false, 7));

        Assert.Contains("not enabled", exception.Message);
    }

    [Fact]
    public void Rejection_NeverReducesTheDiscountSilently()
    {
        var totals = Totals([Line(100m, lineDiscount: 30m)]);

        Assert.Throws<SaleCheckoutConflictException>(() => SaleDiscountAuthorizer.Authorize(
            totals, new SaleDiscountPolicy(10m), true, false, 7));
        Assert.Equal(30m, totals.Lines[0].LineDiscount);
    }
}
