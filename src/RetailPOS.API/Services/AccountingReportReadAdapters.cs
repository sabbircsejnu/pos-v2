using RetailPOS.API.DTOs.Reports;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.API.Services;

public sealed class AccountingReportTenantScope(ITenantAccessService tenantAccess)
    : IAccountingReportTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}

public sealed class CashBookPostingAccountResolver(
    IAccountingConfigurationService accountingConfigurationService)
    : ICashBookPostingAccountResolver
{
    public async Task<CashBookAccountResolution> ResolveAsync(
        string slot,
        long? outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = await accountingConfigurationService
            .ResolvePostingAccountAsync(slot, outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return new CashBookAccountResolution(resolution.Slot, resolution.AccountId);
    }
}

public sealed class CashFlowOwnerAccountsProvider(
    ITenantAccessService tenantAccess,
    IAccountingConfigurationService accountingConfigurationService)
    : ICashFlowOwnerAccountsProvider
{
    public async Task<CashFlowOwnerAccounts> GetAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException(
                "Cash Flow configuration is not available outside the current business.");
        var configuration = await accountingConfigurationService.GetBusinessConfigurationAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return new CashFlowOwnerAccounts(
            configuration.OwnerCapitalAccountId,
            configuration.OwnerDrawingsAccountId);
    }
}

public static class AccountingReportReadModelMapper
{
    public static ProfitabilityFilter ToApplicationFilter(
        ProfitabilityReportFilterDto filter) =>
        new(
            filter.DateFrom,
            filter.DateTo,
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo,
            filter.LocationId,
            filter.LocationType,
            filter.ProductId,
            filter.CategoryId);

    public static BusinessProfitabilityReportDto ToApi(
        BusinessProfitabilityReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            Revenue = report.Revenue,
            CostOfGoodsSold = report.CostOfGoodsSold,
            Expenses = report.Expenses,
            NetProfit = report.NetProfit,
            AuthoritativePnlNetProfit = report.AuthoritativePnlNetProfit,
            ReconciliationDifference = report.ReconciliationDifference
        };

    public static OutletProfitabilityReportDto ToApi(
        OutletProfitabilityReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            Outlets = report.Outlets.Select(ToApi).ToList(),
            Unallocated = ToApi(report.Unallocated),
            BusinessRevenue = report.BusinessRevenue,
            BusinessCostOfGoodsSold = report.BusinessCostOfGoodsSold,
            BusinessExpenses = report.BusinessExpenses,
            BusinessNetProfit = report.BusinessNetProfit,
            AttributedRevenue = report.AttributedRevenue,
            AttributedCostOfGoodsSold = report.AttributedCostOfGoodsSold,
            AttributedExpenses = report.AttributedExpenses,
            AttributedNetProfit = report.AttributedNetProfit,
            ReconciliationDifference = report.ReconciliationDifference
        };

    public static ComparativeBusinessProfitabilityReportDto ToApi(
        ComparativeBusinessProfitabilityReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    public static ComparativeOutletProfitabilityReportDto ToApi(
        ComparativeOutletProfitabilityReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    public static ProductContributionReportDto ToApi(
        ProductContributionReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            ContributionBasis = report.ContributionBasis,
            Products = report.Products.Select(ToApi).ToList(),
            Categories = report.Categories.Select(ToApi).ToList(),
            AttributedRevenue = report.AttributedRevenue,
            AttributedCostOfGoodsSold = report.AttributedCostOfGoodsSold,
            AttributedContribution = report.AttributedContribution,
            AuthoritativeRevenue = report.AuthoritativeRevenue,
            AuthoritativeCostOfGoodsSold = report.AuthoritativeCostOfGoodsSold,
            UnallocatedRevenue = report.UnallocatedRevenue,
            UnallocatedCostOfGoodsSold = report.UnallocatedCostOfGoodsSold,
            RevenueReconciliationDifference = report.RevenueReconciliationDifference,
            CogsReconciliationDifference = report.CogsReconciliationDifference,
            HistoricalLimitation = report.HistoricalLimitation
        };

    public static ComparativeProductContributionReportDto ToApi(
        ComparativeProductContributionReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    public static InventoryValuationFilter ToApplicationFilter(
        ValuationReportFilterDto filter) =>
        new(
            filter.DateTo,
            filter.OutletId,
            filter.WarehouseId,
            filter.LocationId,
            filter.LocationType,
            filter.ProductId,
            filter.VariantId);

    public static InventoryValuationReportDto ToApi(
        InventoryValuationReadModel report) =>
        new()
        {
            Rows = report.Rows.Select(row => new InventoryValuationRowDto
            {
                VariantId = row.VariantId,
                ProductId = row.ProductId,
                ProductName = row.ProductName,
                ProductCode = row.ProductCode,
                Sku = row.Sku,
                LocationId = row.LocationId,
                LocationType = row.LocationType,
                Quantity = row.Quantity,
                UnitCost = row.UnitCost,
                InventoryValue = row.InventoryValue
            }).ToList(),
            UnvaluedMovements = report.UnvaluedMovements
                .Select(movement => new UnvaluedStockMovementDto
                {
                    MovementId = movement.MovementId,
                    VariantId = movement.VariantId,
                    LocationId = movement.LocationId,
                    LocationType = movement.LocationType,
                    BusinessDate = movement.BusinessDate,
                    ReferenceType = movement.ReferenceType,
                    ReferenceId = movement.ReferenceId,
                    Remarks = movement.Remarks
                }).ToList(),
            TotalQuantity = report.TotalQuantity,
            TotalInventoryValue = report.TotalInventoryValue,
            GeneralLedgerBalance = report.GeneralLedgerBalance,
            ReconciliationDifference = report.ReconciliationDifference,
            InventoryAccountId = report.InventoryAccountId,
            AsOfDate = report.AsOfDate
        };

    public static CogsReportDto ToApi(CogsReportReadModel report) =>
        new()
        {
            Rows = report.Rows.Select(row => new CogsReportRowDto
            {
                JournalEntryId = row.JournalEntryId,
                JournalNumber = row.JournalNumber,
                PostingDate = row.PostingDate,
                JournalType = row.JournalType,
                ReferenceType = row.ReferenceType,
                ReferenceId = row.ReferenceId,
                ReferenceNumber = row.ReferenceNumber,
                OutletId = row.OutletId,
                WarehouseId = row.WarehouseId,
                DebitAmount = row.DebitAmount,
                CreditAmount = row.CreditAmount,
                NetCogs = row.NetCogs,
                Description = row.Description
            }).ToList(),
            TotalCogs = report.TotalCogs,
            GeneralLedgerBalance = report.GeneralLedgerBalance,
            ReconciliationDifference = report.ReconciliationDifference,
            CogsAccountId = report.CogsAccountId
        };

    public static CashFlowFilter ToApplicationFilter(CashFlowReportFilterDto filter) =>
        new(
            filter.DateFrom,
            filter.DateTo,
            filter.LocationId,
            filter.LocationType,
            filter.JournalType,
            filter.ReferenceType,
            filter.Reference,
            filter.ComparisonDateFrom,
            filter.ComparisonDateTo);

    public static CashFlowReportDto ToApi(CashFlowReportReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            Method = report.Method,
            OpeningLiquidity = report.OpeningLiquidity,
            ClosingLiquidity = report.ClosingLiquidity,
            OperatingNetMovement = report.OperatingNetMovement,
            InvestingNetMovement = report.InvestingNetMovement,
            FinancingNetMovement = report.FinancingNetMovement,
            UnclassifiedNetMovement = report.UnclassifiedNetMovement,
            TotalExternalNetMovement = report.TotalExternalNetMovement,
            GlDerivedMovement = report.GlDerivedMovement,
            ReconciliationDifference = report.ReconciliationDifference,
            InternalTransferNetMovement = report.InternalTransferNetMovement,
            CutoverAndNonCashNetMovement = report.CutoverAndNonCashNetMovement,
            Sections = report.Sections.Select(section => new CashFlowSectionDto
            {
                ActivityType = section.ActivityType,
                TotalInflows = section.TotalInflows,
                TotalOutflows = section.TotalOutflows,
                NetMovement = section.NetMovement,
                Rows = section.Rows.Select(ToApi).ToList()
            }).ToList(),
            InternalTransferRows = report.InternalTransferRows.Select(ToApi).ToList(),
            CutoverAndNonCashRows = report.CutoverAndNonCashRows.Select(ToApi).ToList()
        };

    public static ComparativeCashFlowReportDto ToApi(ComparativeCashFlowReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    public static CashBookReportDto ToApi(CashBookReadModel report) =>
        new()
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            Accounts = report.Accounts.Select(account => new CashBookAccountDto
            {
                AccountSlot = account.AccountSlot,
                AccountId = account.AccountId,
                AccountCode = account.AccountCode,
                AccountName = account.AccountName,
                OpeningBalance = account.OpeningBalance,
                TotalInflows = account.TotalInflows,
                TotalOutflows = account.TotalOutflows,
                ClosingBalance = account.ClosingBalance,
                GeneralLedgerBalance = account.GeneralLedgerBalance,
                ReconciliationDifference = account.ReconciliationDifference,
                Rows = account.Rows.Select(row => new CashBookRowDto
                {
                    JournalEntryId = row.JournalEntryId,
                    JournalEntryLineId = row.JournalEntryLineId,
                    PostingDate = row.PostingDate,
                    BusinessDate = row.BusinessDate,
                    JournalNumber = row.JournalNumber,
                    JournalType = row.JournalType,
                    ReferenceType = row.ReferenceType,
                    ReferenceId = row.ReferenceId,
                    ReferenceNumber = row.ReferenceNumber,
                    Narration = row.Narration,
                    Inflow = row.Inflow,
                    Outflow = row.Outflow,
                    RunningBalance = row.RunningBalance,
                    OutletId = row.OutletId,
                    WarehouseId = row.WarehouseId,
                    CustomerId = row.CustomerId,
                    SupplierId = row.SupplierId,
                    PaymentMethodId = row.PaymentMethodId,
                    CostCenterId = row.CostCenterId,
                    CounterpartAccounts = row.CounterpartAccounts,
                    CounterpartAccountIds = row.CounterpartAccountIds.ToList(),
                    ReversalOfJournalEntryId = row.ReversalOfJournalEntryId,
                    ReversedByJournalEntryId = row.ReversedByJournalEntryId
                }).ToList()
            }).ToList()
        };

    private static CashFlowRowDto ToApi(CashFlowRowReadModel row) =>
        new()
        {
            JournalEntryId = row.JournalEntryId,
            JournalEntryLineId = row.JournalEntryLineId,
            PostingDate = row.PostingDate,
            BusinessDate = row.BusinessDate,
            JournalNumber = row.JournalNumber,
            JournalType = row.JournalType,
            ReferenceType = row.ReferenceType,
            ReferenceId = row.ReferenceId,
            ReferenceNumber = row.ReferenceNumber,
            Narration = row.Narration,
            AccountSlot = row.AccountSlot,
            AccountId = row.AccountId,
            AccountCode = row.AccountCode,
            AccountName = row.AccountName,
            Inflow = row.Inflow,
            Outflow = row.Outflow,
            NetMovement = row.NetMovement,
            ClassificationReason = row.ClassificationReason,
            CounterpartAccounts = row.CounterpartAccounts,
            CounterpartAccountIds = row.CounterpartAccountIds.ToList(),
            OutletId = row.OutletId,
            WarehouseId = row.WarehouseId,
            CustomerId = row.CustomerId,
            SupplierId = row.SupplierId,
            PaymentMethodId = row.PaymentMethodId,
            CostCenterId = row.CostCenterId,
            ReversalOfJournalEntryId = row.ReversalOfJournalEntryId,
            ReversedByJournalEntryId = row.ReversedByJournalEntryId
        };

    private static OutletProfitabilityRowDto ToApi(
        OutletProfitabilityRowReadModel row) =>
        new()
        {
            OutletId = row.OutletId,
            OutletName = row.OutletName,
            Revenue = row.Revenue,
            CostOfGoodsSold = row.CostOfGoodsSold,
            Expenses = row.Expenses,
            NetProfit = row.NetProfit,
            UnallocatedReason = row.UnallocatedReason
        };

    private static ProductContributionRowDto ToApi(
        ProductContributionRowReadModel row) =>
        new()
        {
            ProductId = row.ProductId,
            ProductName = row.ProductName,
            ProductCode = row.ProductCode,
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName,
            Revenue = row.Revenue,
            CostOfGoodsSold = row.CostOfGoodsSold,
            Contribution = row.Contribution,
            MarginPercent = row.MarginPercent
        };

    private static CategoryContributionRowDto ToApi(
        CategoryContributionRowReadModel row) =>
        new()
        {
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName,
            Revenue = row.Revenue,
            CostOfGoodsSold = row.CostOfGoodsSold,
            Contribution = row.Contribution,
            MarginPercent = row.MarginPercent
        };

    public static GeneralLedgerReportDto ToApi(GeneralLedgerReportReadModel report) =>
        new()
        {
            Rows = report.Rows.Select(row => new GeneralLedgerRowDto
            {
                AccountCode = row.AccountCode,
                AccountName = row.AccountName,
                JournalNumber = row.JournalNumber,
                DebitAmount = row.DebitAmount,
                CreditAmount = row.CreditAmount,
                RunningBalance = row.RunningBalance
            }).ToList(),
            Metadata = new ReportMetadataDto()
        };

    public static TrialBalanceReportDto ToApi(TrialBalanceReportReadModel report) =>
        new()
        {
            Rows = report.Rows.Select(row => new TrialBalanceRowDto
            {
                AccountCode = row.AccountCode,
                AccountName = row.AccountName,
                DebitAmount = row.DebitAmount,
                CreditAmount = row.CreditAmount
            }).ToList(),
            TotalDebits = report.TotalDebits,
            TotalCredits = report.TotalCredits,
            Metadata = new ReportMetadataDto()
        };

    public static ProfitLossReportDto ToApi(ProfitLossReportReadModel report) =>
        new()
        {
            Revenue = report.Revenue,
            CostOfGoodsSold = report.CostOfGoodsSold,
            Expenses = report.Expenses,
            NetProfit = report.NetProfit,
            Metadata = new ReportMetadataDto()
        };

    public static ComparativeProfitLossReportDto ToApi(ComparativeProfitLossReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    public static EnhancedGeneralLedgerReportDto ToApi(EnhancedGeneralLedgerReadModel report) =>
        new()
        {
            AccountId = report.AccountId,
            AccountCode = report.AccountCode,
            AccountName = report.AccountName,
            NormalBalanceType = report.NormalBalanceType,
            OpeningBalance = report.OpeningBalance,
            TotalDebits = report.TotalDebits,
            TotalCredits = report.TotalCredits,
            ClosingBalance = report.ClosingBalance,
            Rows = report.Rows.Select(row => new EnhancedGeneralLedgerRowDto
            {
                JournalEntryId = row.JournalEntryId,
                JournalEntryLineId = row.JournalEntryLineId,
                PostingDate = row.PostingDate,
                BusinessDate = row.BusinessDate,
                JournalNumber = row.JournalNumber,
                JournalType = row.JournalType,
                ReferenceType = row.ReferenceType,
                ReferenceId = row.ReferenceId,
                ReferenceNumber = row.ReferenceNumber,
                Narration = row.Narration,
                DebitAmount = row.DebitAmount,
                CreditAmount = row.CreditAmount,
                RunningBalance = row.RunningBalance,
                OutletId = row.OutletId,
                WarehouseId = row.WarehouseId,
                CustomerId = row.CustomerId,
                SupplierId = row.SupplierId,
                PaymentMethodId = row.PaymentMethodId,
                CostCenterId = row.CostCenterId,
                ReversalOfJournalEntryId = row.ReversalOfJournalEntryId,
                ReversedByJournalEntryId = row.ReversedByJournalEntryId
            }).ToList()
        };

    public static BalanceSheetReportDto ToApi(BalanceSheetReportReadModel report) =>
        new()
        {
            AsOfDate = report.AsOfDate,
            IsManagementView = report.IsManagementView,
            CutoverWarning = report.CutoverWarning,
            Assets = ToApi(report.Assets),
            Liabilities = ToApi(report.Liabilities),
            Equity = ToApi(report.Equity),
            CurrentYearEarnings = report.CurrentYearEarnings,
            UnclosedPriorYearEarnings = report.UnclosedPriorYearEarnings,
            TotalAssets = report.TotalAssets,
            TotalLiabilitiesAndEquity = report.TotalLiabilitiesAndEquity,
            ReconciliationDifference = report.ReconciliationDifference
        };

    public static ComparativeBalanceSheetReportDto ToApi(ComparativeBalanceSheetReadModel report) =>
        new()
        {
            Current = ToApi(report.Current),
            Comparison = ToApi(report.Comparison)
        };

    private static BalanceSheetSectionDto ToApi(BalanceSheetSectionReadModel section) =>
        new()
        {
            Name = section.Name,
            Total = section.Total,
            Accounts = section.Accounts.Select(ToApi).ToList()
        };

    private static BalanceSheetAccountDto ToApi(BalanceSheetAccountReadModel account) =>
        new()
        {
            AccountId = account.AccountId,
            AccountCode = account.AccountCode,
            AccountName = account.AccountName,
            AccountType = account.AccountType,
            NormalBalanceType = account.NormalBalanceType,
            PresentationRole = account.PresentationRole,
            NormalBalance = account.NormalBalance,
            StatementBalance = account.StatementBalance,
            RollupNormalBalance = account.RollupNormalBalance,
            RollupStatementBalance = account.RollupStatementBalance,
            Children = account.Children.Select(ToApi).ToList()
        };
}
