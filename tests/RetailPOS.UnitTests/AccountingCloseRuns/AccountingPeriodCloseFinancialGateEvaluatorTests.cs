using MediatR;
using Moq;
using RetailPOS.Application.AccountingCloseRuns.Queries;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Application.AccountingReports.Queries;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Application.ApReports.Queries;
using RetailPOS.Application.ApReports.ReadModels;
using RetailPOS.Application.VatReports.Queries;
using RetailPOS.Application.VatReports.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.AccountingCloseRuns;

public sealed class AccountingPeriodCloseFinancialGateEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ReturnsAllGatesInLegacyOrderAndPreservesWarnings()
    {
        var sender = new Mock<ISender>();
        var periodStart = new DateTime(2026, 1, 1);
        var periodEnd = new DateTime(2026, 1, 31);
        using var cancellation = new CancellationTokenSource();
        SetupPassingConfiguration(sender, cancellation.Token);
        sender.Setup(querySender => querySender.Send(
                It.Is<GetTrialBalanceQuery>(query =>
                    query.Filter.DateFrom == periodStart &&
                    query.Filter.DateTo == periodEnd &&
                    query.Filter.FinancialYearId == 4 &&
                    query.Filter.AccountingPeriodId == 8),
                cancellation.Token))
            .ReturnsAsync(new TrialBalanceReportReadModel(
                Array.Empty<TrialBalanceRowReadModel>(),
                12m,
                12m));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetBalanceSheetQuery>(query =>
                    query.Filter.AsOfDate == periodEnd &&
                    query.Filter.LocationId == null &&
                    query.Filter.LocationType == null),
                cancellation.Token))
            .ReturnsAsync(BalanceSheet(0m));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetApReconciliationQuery>(query =>
                    query.Filter.AsOfDate == periodEnd),
                cancellation.Token))
            .ReturnsAsync(ApReconciliation(-3m));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetSupplierAdvanceReconciliationQuery>(query =>
                    query.Filter.AsOfDate == periodEnd),
                cancellation.Token))
            .ReturnsAsync(ApReconciliation(0m));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetVatReconciliationQuery>(query =>
                    query.Filter.DateFrom == periodStart &&
                    query.Filter.DateTo == periodEnd),
                cancellation.Token))
            .ReturnsAsync(new VatReconciliationReadModel(
                10m, 10m, 0m, "reconciled",
                5m, 5m, 0m, "reconciled",
                5m, 5m, 2m));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetInventoryValuationQuery>(query =>
                    query.Filter.DateTo == periodEnd &&
                    query.Filter.LocationId == null &&
                    query.Filter.LocationType == null),
                cancellation.Token))
            .ReturnsAsync(new InventoryValuationReadModel(
                Array.Empty<InventoryValuationRowReadModel>(),
                new[]
                {
                    new UnvaluedStockMovementReadModel(1, 2, 3, "outlet", periodEnd, "sale", 4, null),
                    new UnvaluedStockMovementReadModel(2, 3, 4, "warehouse", periodEnd, "grn", 5, null)
                },
                0m,
                0m,
                0m,
                0m,
                1,
                periodEnd));
        sender.Setup(querySender => querySender.Send(
                It.Is<GetVatExceptionsQuery>(query =>
                    query.Filter.DateFrom == periodStart &&
                    query.Filter.DateTo == periodEnd),
                cancellation.Token))
            .ReturnsAsync(new[]
            {
                new VatExceptionReadModel(
                    periodEnd,
                    "Sale",
                    "legacy",
                    null,
                    null,
                    "Incomplete VAT snapshot",
                    1m)
            });

        var evaluator = new AccountingPeriodCloseFinancialGateEvaluator(sender.Object);

        var gates = await evaluator.EvaluateAsync(
            periodStart,
            periodEnd,
            4,
            8,
            cancellation.Token);

        Assert.Equal(
            new[]
            {
                "configuration",
                "trial_balance",
                "balance_sheet",
                "supplier_payable",
                "supplier_advance",
                "vat",
                "inventory",
                "inventory_unvalued",
                "vat_legacy"
            },
            gates.Select(gate => gate.GateCode));
        Assert.Equal(AccountingCloseGateStatuses.Blocking, gates[3].Status);
        Assert.Equal(-3m, gates[3].ActualAmount);
        Assert.Equal(AccountingCloseGateStatuses.Blocking, gates[5].Status);
        Assert.Equal(2m, gates[5].ActualAmount);
        Assert.Equal(AccountingCloseGateStatuses.Warning, gates[7].Status);
        Assert.Equal(2m, gates[7].ActualAmount);
        Assert.True(gates[7].IsHistoricalCutoverLimitation);
        Assert.Equal(AccountingCloseGateStatuses.Warning, gates[8].Status);
        Assert.Equal(1m, gates[8].ActualAmount);
        Assert.True(gates[8].IsHistoricalCutoverLimitation);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsFinancialReportsWhenRequiredMappingsAreInvalid()
    {
        var sender = new Mock<ISender>();
        sender.Setup(querySender => querySender.Send(
                It.IsAny<ValidateRequiredAccountingMappingsQuery>(),
                CancellationToken.None))
            .ReturnsAsync(new[] { "cash mapping is invalid.", "bank mapping is invalid." });
        var evaluator = new AccountingPeriodCloseFinancialGateEvaluator(sender.Object);

        var gates = await evaluator.EvaluateAsync(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31),
            4,
            8,
            CancellationToken.None);

        var gate = Assert.Single(gates);
        Assert.Equal("configuration", gate.GateCode);
        Assert.Equal(AccountingCloseGateStatuses.Blocking, gate.Status);
        Assert.Equal(2m, gate.ActualAmount);
        Assert.Equal("cash mapping is invalid. bank mapping is invalid.", gate.Details);
        sender.Verify(querySender => querySender.Send(
            It.IsAny<GetTrialBalanceQuery>(),
            It.IsAny<CancellationToken>()), Times.Never);
        sender.Verify(querySender => querySender.Send(
            It.IsAny<GetBalanceSheetQuery>(),
            It.IsAny<CancellationToken>()), Times.Never);
        sender.Verify(querySender => querySender.Send(
            It.IsAny<GetVatReconciliationQuery>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void SetupPassingConfiguration(
        Mock<ISender> sender,
        CancellationToken cancellationToken)
    {
        sender.Setup(querySender => querySender.Send(
                It.IsAny<ValidateRequiredAccountingMappingsQuery>(),
                cancellationToken))
            .ReturnsAsync(Array.Empty<string>());
    }

    private static BalanceSheetReportReadModel BalanceSheet(decimal difference) =>
        new(
            new DateTime(2026, 1, 31),
            false,
            null,
            new BalanceSheetSectionReadModel("Assets", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            new BalanceSheetSectionReadModel("Liabilities", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            new BalanceSheetSectionReadModel("Equity", 0m, Array.Empty<BalanceSheetAccountReadModel>()),
            0m,
            0m,
            0m,
            0m,
            difference);

    private static ApReconciliationReadModel ApReconciliation(decimal difference) =>
        new(
            new DateTime(2026, 1, 31),
            1,
            "test",
            0m,
            difference,
            difference,
            Array.Empty<string>());
}
