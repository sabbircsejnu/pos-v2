using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.Commands;

namespace RetailPOS.Tests.Sales;

public sealed class SaleCheckoutCalculatorTests
{
    private static readonly SaleTaxPolicy Vat10 = new(true, 10m, false);

    [Fact]
    public void ExclusiveTax_AddsVatToThePayable()
    {
        var totals = SaleCheckoutCalculator.Calculate([Line(1, 2, 100m)], 0m, Vat10);

        Assert.Equal(200m, totals.ItemsSubtotal);
        Assert.Equal(20m, totals.Tax);
        Assert.Equal(220m, totals.TotalAmount);
        var line = Assert.Single(totals.Lines);
        Assert.Equal(200m, line.TaxableAmount);
        Assert.Equal(20m, line.VatAmount);
    }

    [Fact]
    public void InclusiveTax_ExtractsVatWithoutChangingThePayable()
    {
        var totals = SaleCheckoutCalculator.Calculate([Line(1, 1, 110m)], 0m, new SaleTaxPolicy(true, 10m, true));

        Assert.Equal(110m, totals.TotalAmount);
        Assert.Equal(10m, totals.Tax);
        Assert.True(totals.TaxInclusive);
        Assert.Equal(100m, Assert.Single(totals.Lines).TaxableAmount);
    }

    [Fact]
    public void DisabledTax_ProducesNoVat()
    {
        var totals = SaleCheckoutCalculator.Calculate([Line(1, 1, 50m)], 0m, new SaleTaxPolicy(false, 10m, false));

        Assert.Equal(0m, totals.Tax);
        Assert.Equal(50m, totals.TotalAmount);
        Assert.Equal(0m, totals.TaxRate);
    }

    [Fact]
    public void InvoiceDiscount_LineVatSumsToSaleTaxExactly()
    {
        // The legacy allocation produced line VAT of 100 for a sale taxed at 90 (subtotal 1000, discount 100).
        var totals = SaleCheckoutCalculator.Calculate(
            [Line(1, 1, 400m), Line(2, 1, 600m)],
            100m,
            new SaleTaxPolicy(true, 10m, false));

        Assert.Equal(90m, totals.Tax);
        Assert.Equal(990m, totals.TotalAmount);
        Assert.Equal(totals.Tax, totals.Lines.Sum(line => line.VatAmount));
        Assert.Equal(900m, totals.Lines.Sum(line => line.TaxableAmount));
        Assert.Equal(100m, totals.Lines.Sum(line => line.InvoiceDiscountShare));
    }

    [Theory]
    [InlineData(1.00, 1.00, 1.00, 0.10)]
    [InlineData(0.01, 0.01, 0.01, 0.01)]
    [InlineData(33.33, 33.33, 33.34, 5.00)]
    [InlineData(10.00, 10.00, 10.00, 0.01)]
    public void RoundingNeverLeavesALineSnapshotOutOfBalance(double a, double b, double c, double discount)
    {
        var totals = SaleCheckoutCalculator.Calculate(
            [Line(1, 1, (decimal)a), Line(2, 1, (decimal)b), Line(3, 1, (decimal)c)],
            (decimal)discount,
            new SaleTaxPolicy(true, 7.5m, false));

        Assert.Equal(totals.Tax, totals.Lines.Sum(line => line.VatAmount));
        Assert.Equal(totals.InvoiceDiscount, totals.Lines.Sum(line => line.InvoiceDiscountShare));
        Assert.Equal(totals.ItemsSubtotal - totals.InvoiceDiscount + totals.Tax, totals.TotalAmount);
    }

    [Fact]
    public void MixedRates_TaxEachRateGroupAfterDiscountsAndKeepLineSnapshotsBalanced()
    {
        // 600 @ 10% and 400 @ 0% with a 100 invoice discount: shares 540/360, tax only on the taxable group.
        var totals = SaleCheckoutCalculator.Calculate(
            [Line(1, 1, 600m, 10m, "standard"), Line(2, 1, 400m, 0m, "zero_rated")],
            100m,
            new SaleTaxPolicy(true, 5m, false));

        Assert.Equal(54m, totals.Tax);
        Assert.Equal(954m, totals.TotalAmount);
        Assert.Equal(totals.Tax, totals.Lines.Sum(line => line.VatAmount));
        Assert.Equal(54m, totals.Lines[0].VatAmount);
        Assert.Equal(0m, totals.Lines[1].VatAmount);
        Assert.Equal(10m, totals.Lines[0].TaxRatePercent);
        Assert.Equal("zero_rated", totals.Lines[1].TaxClassification);
        Assert.Equal(10m, totals.TaxRate);
    }

    [Fact]
    public void MixedRates_WithInclusivePricesExtractTaxFromEachGroup()
    {
        var totals = SaleCheckoutCalculator.Calculate(
            [Line(1, 1, 110m, 10m, "standard"), Line(2, 1, 105m, 5m, "standard"), Line(3, 1, 20m, 0m, "exempt")],
            0m,
            new SaleTaxPolicy(true, 15m, true));

        Assert.Equal(235m, totals.TotalAmount);
        Assert.Equal(15m, totals.Tax);
        Assert.True(totals.TaxInclusive);
        Assert.Equal(10m, totals.Lines[0].VatAmount);
        Assert.Equal(5m, totals.Lines[1].VatAmount);
        Assert.Equal(100m, totals.Lines[0].TaxableAmount);
        Assert.Equal(20m, totals.Lines[2].TaxableAmount);
        Assert.Equal(0m, totals.TaxRate);
    }

    [Fact]
    public void LinesWithoutAnExplicitRateUseThePolicyRate()
    {
        var totals = SaleCheckoutCalculator.Calculate([Line(1, 1, 100m), Line(2, 1, 100m, 0m, "zero_rated")], 0m, Vat10);

        Assert.Equal(10m, totals.Lines[0].VatAmount);
        Assert.Equal(10m, totals.Lines[0].TaxRatePercent);
        Assert.Equal(0m, totals.Lines[1].VatAmount);
        Assert.Equal(10m, totals.Tax);
    }

    [Fact]
    public void RatesOutsideZeroToHundredAreRejected()
    {
        Assert.Throws<SaleCheckoutValidationException>(() =>
            SaleCheckoutCalculator.Calculate([Line(1, 1, 10m, 101m, "standard")], 0m, Vat10));
    }

    [Fact]
    public void LineAndInvoiceDiscountsCannotBeCombined()
    {
        var exception = Assert.Throws<SaleCheckoutValidationException>(() =>
            SaleCheckoutCalculator.Calculate([Line(1, 1, 100m, lineDiscount: 5m)], 5m, Vat10));

        Assert.Equal(SaleCheckoutErrorCodes.DiscountConflict, exception.Code);
    }

    [Fact]
    public void DiscountsAreBoundedByTheAmountTheyReduce()
    {
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidDiscount,
            Assert.Throws<SaleCheckoutValidationException>(() =>
                SaleCheckoutCalculator.Calculate([Line(1, 1, 100m, lineDiscount: 100.01m)], 0m, Vat10)).Code);
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidDiscount,
            Assert.Throws<SaleCheckoutValidationException>(() =>
                SaleCheckoutCalculator.Calculate([Line(1, 1, 100m)], 100.01m, Vat10)).Code);
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidDiscount,
            Assert.Throws<SaleCheckoutValidationException>(() =>
                SaleCheckoutCalculator.Calculate([Line(1, 1, 100m)], -1m, Vat10)).Code);
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidDiscount,
            Assert.Throws<SaleCheckoutValidationException>(() =>
                SaleCheckoutCalculator.Calculate([Line(1, 1, 100m, lineDiscount: -1m)], 0m, Vat10)).Code);
    }

    [Fact]
    public void FullInvoiceDiscountProducesAZeroPayable()
    {
        var totals = SaleCheckoutCalculator.Calculate([Line(1, 1, 100m)], 100m, Vat10);

        Assert.Equal(0m, totals.Tax);
        Assert.Equal(0m, totals.TotalAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuantitiesMustBePositive(int quantity)
    {
        var exception = Assert.Throws<SaleCheckoutValidationException>(() =>
            SaleCheckoutCalculator.Calculate([Line(1, quantity, 10m)], 0m, Vat10));

        Assert.Equal(SaleCheckoutErrorCodes.InvalidQuantity, exception.Code);
    }

    [Fact]
    public void AmountsWithSubCentPrecisionAreRejected()
    {
        Assert.Throws<SaleCheckoutValidationException>(() =>
            SaleCheckoutCalculator.Calculate([Line(1, 1, 10.005m)], 0m, Vat10));
    }

    [Fact]
    public void Allocate_DistributesRemainderCentsToTheLargestFractions()
    {
        var shares = SaleCheckoutCalculator.Allocate(0.05m, [1m, 1m, 1m]);

        Assert.Equal(0.05m, shares.Sum());
        Assert.Equal([0.02m, 0.02m, 0.01m], shares);
    }

    [Fact]
    public void InvoiceDiscount_OnEqualLines_SplitsEquallyWithTheRemainderCentOnTheFirstLine()
    {
        // The POS UI shows the same shares (pos-tax.ts allocateProportionally): 100 / 3 = 33.34 + 33.33 + 33.33.
        var totals = SaleCheckoutCalculator.Calculate(
            [Line(1, 1, 100m), Line(2, 1, 100m), Line(3, 1, 100m)], 100m, SaleTaxPolicy.None);

        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, totals.Lines.Select(line => line.InvoiceDiscountShare).ToArray());
        Assert.All(totals.Lines, line => Assert.True(line.TaxableAmount >= 0m));
        Assert.Equal(200m, totals.TotalAmount);
    }

    private static SaleCheckoutLineInput Line(long variantId, int quantity, decimal unitPrice, decimal lineDiscount = 0m) =>
        new(variantId, quantity, unitPrice, lineDiscount);

    private static SaleCheckoutLineInput Line(
        long variantId, int quantity, decimal unitPrice, decimal ratePercent, string classification) =>
        new(variantId, quantity, unitPrice, 0m, ratePercent, classification);
}

public sealed class SalePaymentPlannerTests
{
    [Fact]
    public void SingleCashPayment_RecordsTenderChangeAndRetainedCash()
    {
        var plan = Assert.Single(SalePaymentPlanner.Plan("Cash", [], 250m, 220m));

        Assert.Equal("cash", plan.Method);
        Assert.Equal(220m, plan.Amount);
        Assert.Equal(250m, plan.CashTendered);
        Assert.Equal(30m, plan.ChangeGiven);
        Assert.Equal(220m, plan.NetCashRetained);
    }

    [Fact]
    public void CashRequiresATenderThatCoversTheAmount()
    {
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidPayment,
            Assert.Throws<SaleCheckoutValidationException>(() => SalePaymentPlanner.Plan("cash", [], null, 100m)).Code);
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidPayment,
            Assert.Throws<SaleCheckoutValidationException>(() => SalePaymentPlanner.Plan("cash", [], 99.99m, 100m)).Code);
    }

    [Fact]
    public void NonCashPaymentsCarryNoCashFacts()
    {
        var plan = Assert.Single(SalePaymentPlanner.Plan("card", [], 500m, 100m));

        Assert.Equal("card", plan.Method);
        Assert.Null(plan.CashTendered);
        Assert.Null(plan.ChangeGiven);
        Assert.Null(plan.NetCashRetained);
    }

    [Fact]
    public void PosTenderTableShapes_AreAcceptedByThePlanner()
    {
        // One method: PaymentMethod + CashTendered (cash) or no tender (card/mobile).
        Assert.Equal(50m, Assert.Single(SalePaymentPlanner.Plan("cash", [], 150m, 100m)).ChangeGiven);
        Assert.Null(Assert.Single(SalePaymentPlanner.Plan("card", [], null, 100m)).CashTendered);

        // Several non-zero amounts: Payments[] with Tendered only on the cash row.
        var plans = SalePaymentPlanner.Plan(
            "cash",
            [new SalePaymentInput("cash", 100m, 200m), new SalePaymentInput("card", 100m, null), new SalePaymentInput("mobile", 50m, null)],
            null,
            250m);
        Assert.Equal(3, plans.Count);
        Assert.Equal(100m, plans[0].ChangeGiven);
        Assert.Equal(100m, plans[0].NetCashRetained);
    }

    [Fact]
    public void SplitPayments_MustAddUpExactlyAndOnlyCashMayBeOverTendered()
    {
        var plans = SalePaymentPlanner.Plan(
            "cash",
            [new SalePaymentInput("cash", 60m, 100m), new SalePaymentInput("mobile", 40m, null)],
            null,
            100m);

        Assert.Equal(2, plans.Count);
        Assert.Equal(40m, plans[0].ChangeGiven);
        Assert.Equal(60m, plans[0].NetCashRetained);
        Assert.Null(plans[1].ChangeGiven);

        Assert.Equal(
            SaleCheckoutErrorCodes.PaymentMismatch,
            Assert.Throws<SaleCheckoutValidationException>(() => SalePaymentPlanner.Plan(
                "cash",
                [new SalePaymentInput("cash", 60m, 60m), new SalePaymentInput("card", 39.99m, null)],
                null,
                100m)).Code);
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidPayment,
            Assert.Throws<SaleCheckoutValidationException>(() => SalePaymentPlanner.Plan(
                "cash",
                [new SalePaymentInput("cash", 60m, 60m), new SalePaymentInput("card", 40m, 50m)],
                null,
                100m)).Code);
    }

    [Theory]
    [InlineData("cheque")]
    [InlineData("loyalty")]
    [InlineData("")]
    public void UnsupportedMethodsAreRejected(string method)
    {
        Assert.Equal(
            SaleCheckoutErrorCodes.InvalidPayment,
            Assert.Throws<SaleCheckoutValidationException>(() => SalePaymentPlanner.Plan(method, [], null, 10m)).Code);
    }
}

public sealed class SaleRequestFingerprintTests
{
    [Fact]
    public void SameRequestProducesTheSameFingerprintRegardlessOfTheSaleDate()
    {
        var first = SaleRequestFingerprint.Compute(Command() with { SalesDate = DateTime.UtcNow });
        var second = SaleRequestFingerprint.Compute(Command() with { SalesDate = DateTime.UtcNow.AddMinutes(3) });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void AnyMaterialChangeProducesADifferentFingerprint()
    {
        var baseline = SaleRequestFingerprint.Compute(Command());

        Assert.NotEqual(baseline, SaleRequestFingerprint.Compute(Command() with { CashierId = 8 }));
        Assert.NotEqual(baseline, SaleRequestFingerprint.Compute(Command() with { Discount = 1m }));
        Assert.NotEqual(baseline, SaleRequestFingerprint.Compute(Command() with { CashTendered = 300m }));
        Assert.NotEqual(baseline, SaleRequestFingerprint.Compute(Command() with
        {
            Items = [new CreateSaleLine(1, 3, 100m, 0m, null, null)]
        }));
        Assert.NotEqual(baseline, SaleRequestFingerprint.Compute(Command() with
        {
            Payments = [new CreateSalePayment("card", 200m, null)]
        }));
    }

    private static CreateSaleCommand Command() => new(
        5001,
        8001,
        null,
        [new CreateSaleLine(1, 2, 100m, 0m, null, null)],
        0m,
        0m,
        "cash",
        [],
        7,
        null,
        null,
        "key-1",
        200m);
}
