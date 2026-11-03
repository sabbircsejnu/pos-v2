using MediatR;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Sales;

/// <summary>Phase 3.2 Workstream A: reproduces the two journal imbalances on the pre-3.2 composition.</summary>
public sealed class SaleFinancialJournalCharacterizationTests
{
    [Fact]
    public async Task Inclusive_vat_sale_journal_balances()
    {
        var totals = SaleCheckoutCalculator.Calculate(
            [new SaleCheckoutLineInput(1, 1, 1000m, 0m, 10m, null)], 0m, new SaleTaxPolicy(true, 10m, true));
        var sale = Sale(totals);
        var (sender, captured) = Sender();
        await new SaleFinancialJournalPoster(sender.Object, Accounts().Object)
            .PostSaleAsync(SaleFinancialIntentFactory.ReadSaleSnapshot(SaleFinancialIntentFactory.ForSale(sale, 1, DateTime.UtcNow)), default);
        var lines = captured.Single().Journal.Lines;
        Assert.Equal(lines.Sum(l => l.DebitAmount), lines.Sum(l => l.CreditAmount));
    }

    [Fact]
    public async Task Exclusive_vat_void_reversal_balances()
    {
        var totals = SaleCheckoutCalculator.Calculate(
            [new SaleCheckoutLineInput(1, 2, 100m, 0m, 15m, null)], 0m, new SaleTaxPolicy(true, 15m, false));
        var sale = Sale(totals);
        var (sender, captured) = Sender();
        var intent = SaleFinancialIntentFactory.ForReversal(
            sale, 1, [new SaleRefundLine(501, 1, 2)], SaleFinancialPostingReversalTypes.Void, 5, DateTime.UtcNow);
        await new SaleFinancialJournalPoster(sender.Object, Accounts().Object)
            .PostReversalAsync(SaleFinancialIntentFactory.ReadReversalSnapshot(intent), new Dictionary<long, long?>(), default);
        var lines = captured.Single().Journal.Lines;
        Assert.Equal(lines.Sum(l => l.DebitAmount), lines.Sum(l => l.CreditAmount));
    }

    private static Sale Sale(SaleCheckoutTotals totals)
    {
        var line = totals.Lines.Single();
        var item = new SaleItem
        {
            Id = 501, VariantId = line.VariantId, Quantity = line.Quantity, UnitPrice = line.UnitPrice,
            Subtotal = line.Subtotal, DiscountAmount = line.LineDiscount, TaxableAmount = line.TaxableAmount,
            VatAmount = line.VatAmount, VatRate = line.TaxRatePercent, TaxClassification = line.TaxClassification,
            TaxInclusive = totals.TaxInclusive
        };
        item.CaptureHistoricalUnitCost(40m);
        return new Sale
        {
            Id = 42, BusinessId = 1, OutletId = 11, CashierId = 5, SaleNumber = "S-42", BusinessDate = DateTime.UtcNow.Date,
            SaleDateTime = DateTime.UtcNow, SaleDate = DateTime.UtcNow, Discount = totals.InvoiceDiscount, Tax = totals.Tax,
            TotalAmount = totals.TotalAmount, Items = [item],
            Payments = [new SalePayment { Id = 601, Method = "cash", Amount = totals.TotalAmount }]
        };
    }

    private static (Mock<ISender> Sender, List<CreateOrGetSystemJournalDraftCommand> Captured) Sender()
    {
        var captured = new List<CreateOrGetSystemJournalDraftCommand>();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CreateOrGetSystemJournalDraftCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<long>, CancellationToken>((command, _) => captured.Add((CreateOrGetSystemJournalDraftCommand)command))
            .ReturnsAsync(900L);
        sender.Setup(s => s.Send(It.IsAny<PostJournalEntryCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(900L);
        return (sender, captured);
    }

    private static Mock<IAccountingConfigurationService> Accounts()
    {
        var slots = new List<string>();
        var service = new Mock<IAccountingConfigurationService>();
        service.Setup(s => s.ResolvePostingAccountAsync(It.IsAny<string>(), It.IsAny<long?>()))
            .ReturnsAsync((string slot, long? _) =>
            {
                if (!slots.Contains(slot))
                    slots.Add(slot);
                return new PostingAccountResolutionDto { Slot = slot, AccountId = 100 + slots.IndexOf(slot), Source = "test" };
            });
        service.Setup(s => s.GetBusinessConfigurationAsync()).ReturnsAsync(new AccountingConfigurationDto());
        return service;
    }
}
