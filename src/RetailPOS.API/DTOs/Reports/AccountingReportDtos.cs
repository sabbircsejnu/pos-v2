namespace RetailPOS.API.DTOs.Reports;

public class GeneralLedgerReportDto
{
    public List<GeneralLedgerRowDto> Rows { get; set; } = new();
    public ReportMetadataDto Metadata { get; set; } = new();
}

public class GeneralLedgerRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string JournalNumber { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal RunningBalance { get; set; }
}

public class TrialBalanceReportDto
{
    public List<TrialBalanceRowDto> Rows { get; set; } = new();
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public ReportMetadataDto Metadata { get; set; } = new();
}

public class TrialBalanceRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
}

public class ProfitLossReportDto
{
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public ReportMetadataDto Metadata { get; set; } = new();
}

public class ReportMetadataDto
{
    public string Source { get; set; } = "posted-journals";
}

public class ValuationReportFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public long? ProductId { get; set; }
    public long? VariantId { get; set; }
}

public class CogsReportDto
{
    public List<CogsReportRowDto> Rows { get; set; } = new();
    public decimal TotalCogs { get; set; }
    public decimal GeneralLedgerBalance { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public long CogsAccountId { get; set; }
}

public class CogsReportRowDto
{
    public long JournalEntryId { get; set; }
    public string? JournalNumber { get; set; }
    public DateTime PostingDate { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal NetCogs { get; set; }
    public string? Description { get; set; }
}

public class InventoryValuationReportDto
{
    public List<InventoryValuationRowDto> Rows { get; set; } = new();
    public List<UnvaluedStockMovementDto> UnvaluedMovements { get; set; } = new();
    public decimal TotalQuantity { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public decimal GeneralLedgerBalance { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public long InventoryAccountId { get; set; }
    public DateTime AsOfDate { get; set; }
}

public class InventoryValuationRowDto
{
    public long VariantId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string Sku { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal InventoryValue { get; set; }
}

public class UnvaluedStockMovementDto
{
    public long MovementId { get; set; }
    public long VariantId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public string? Remarks { get; set; }
}

public class CashBookReportFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public string? JournalType { get; set; }
    public string? ReferenceType { get; set; }
    public string? Reference { get; set; }
    public List<string> AccountSlots { get; set; } = new();
}

public class CashBookReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<CashBookAccountDto> Accounts { get; set; } = new();
}

public class CashBookAccountDto
{
    public string AccountSlot { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal TotalInflows { get; set; }
    public decimal TotalOutflows { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal GeneralLedgerBalance { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public List<CashBookRowDto> Rows { get; set; } = new();
}

public class CashBookRowDto
{
    public long JournalEntryId { get; set; }
    public long JournalEntryLineId { get; set; }
    public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    public string? JournalNumber { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Narration { get; set; }
    public decimal Inflow { get; set; }
    public decimal Outflow { get; set; }
    public decimal RunningBalance { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? CustomerId { get; set; }
    public long? SupplierId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CostCenterId { get; set; }
    public string CounterpartAccounts { get; set; } = string.Empty;
    public List<long> CounterpartAccountIds { get; set; } = new();
    public long? ReversalOfJournalEntryId { get; set; }
    public long? ReversedByJournalEntryId { get; set; }
}

public class CashFlowReportFilterDto : CashBookReportFilterDto
{
    public DateTime? ComparisonDateFrom { get; set; }
    public DateTime? ComparisonDateTo { get; set; }
}

public class CashFlowReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string Method { get; set; } = "Direct";
    public decimal OpeningLiquidity { get; set; }
    public decimal ClosingLiquidity { get; set; }
    public decimal OperatingNetMovement { get; set; }
    public decimal InvestingNetMovement { get; set; }
    public decimal FinancingNetMovement { get; set; }
    public decimal UnclassifiedNetMovement { get; set; }
    public decimal TotalExternalNetMovement { get; set; }
    public decimal GlDerivedMovement { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public decimal InternalTransferNetMovement { get; set; }
    public decimal CutoverAndNonCashNetMovement { get; set; }
    public List<CashFlowSectionDto> Sections { get; set; } = new();
    public List<CashFlowRowDto> InternalTransferRows { get; set; } = new();
    public List<CashFlowRowDto> CutoverAndNonCashRows { get; set; } = new();
}

public class CashFlowSectionDto
{
    public string ActivityType { get; set; } = string.Empty;
    public decimal TotalInflows { get; set; }
    public decimal TotalOutflows { get; set; }
    public decimal NetMovement { get; set; }
    public List<CashFlowRowDto> Rows { get; set; } = new();
}

public class CashFlowRowDto
{
    public long JournalEntryId { get; set; }
    public long JournalEntryLineId { get; set; }
    public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    public string? JournalNumber { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Narration { get; set; }
    public string AccountSlot { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Inflow { get; set; }
    public decimal Outflow { get; set; }
    public decimal NetMovement { get; set; }
    public string ClassificationReason { get; set; } = string.Empty;
    public string CounterpartAccounts { get; set; } = string.Empty;
    public List<long> CounterpartAccountIds { get; set; } = new();
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? CustomerId { get; set; }
    public long? SupplierId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CostCenterId { get; set; }
    public long? ReversalOfJournalEntryId { get; set; }
    public long? ReversedByJournalEntryId { get; set; }
}

public class ComparativeCashFlowReportDto
{
    public CashFlowReportDto Current { get; set; } = new();
    public CashFlowReportDto Comparison { get; set; } = new();
}

public class FinancialStatementFilterDto
{
    public DateTime? AsOfDate { get; set; }
    public DateTime? ComparisonAsOfDate { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
}

public class EnhancedGeneralLedgerFilterDto
{
    public long AccountId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public string? JournalType { get; set; }
    public string? ReferenceType { get; set; }
    public string? Reference { get; set; }
}

public class ComparativeProfitLossFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public DateTime? ComparisonDateFrom { get; set; }
    public DateTime? ComparisonDateTo { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
}

public class BalanceSheetReportDto
{
    public DateTime AsOfDate { get; set; }
    public bool IsManagementView { get; set; }
    public string? CutoverWarning { get; set; }
    public BalanceSheetSectionDto Assets { get; set; } = new();
    public BalanceSheetSectionDto Liabilities { get; set; } = new();
    public BalanceSheetSectionDto Equity { get; set; } = new();
    public decimal CurrentYearEarnings { get; set; }
    public decimal UnclosedPriorYearEarnings { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilitiesAndEquity { get; set; }
    public decimal ReconciliationDifference { get; set; }
}

public class BalanceSheetSectionDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<BalanceSheetAccountDto> Accounts { get; set; } = new();
}

public class BalanceSheetAccountDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string NormalBalanceType { get; set; } = string.Empty;
    public string? PresentationRole { get; set; }
    public decimal NormalBalance { get; set; }
    public decimal StatementBalance { get; set; }
    public decimal RollupNormalBalance { get; set; }
    public decimal RollupStatementBalance { get; set; }
    public List<BalanceSheetAccountDto> Children { get; set; } = new();
}

public class ComparativeBalanceSheetReportDto
{
    public BalanceSheetReportDto Current { get; set; } = new();
    public BalanceSheetReportDto Comparison { get; set; } = new();
}

public class ComparativeProfitLossReportDto
{
    public ProfitLossReportDto Current { get; set; } = new();
    public ProfitLossReportDto Comparison { get; set; } = new();
}

public class ProfitabilityReportFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public DateTime? ComparisonDateFrom { get; set; }
    public DateTime? ComparisonDateTo { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public long? ProductId { get; set; }
    public long? CategoryId { get; set; }
}

public class BusinessProfitabilityReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal AuthoritativePnlNetProfit { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public string Source { get; set; } = "posted-journals";
}

public class OutletProfitabilityReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<OutletProfitabilityRowDto> Outlets { get; set; } = new();
    public OutletProfitabilityRowDto Unallocated { get; set; } = new() { OutletName = "Unallocated", UnallocatedReason = "Posted accounting activity lacks authoritative outlet attribution." };
    public decimal BusinessRevenue { get; set; }
    public decimal BusinessCostOfGoodsSold { get; set; }
    public decimal BusinessExpenses { get; set; }
    public decimal BusinessNetProfit { get; set; }
    public decimal AttributedRevenue { get; set; }
    public decimal AttributedCostOfGoodsSold { get; set; }
    public decimal AttributedExpenses { get; set; }
    public decimal AttributedNetProfit { get; set; }
    public decimal ReconciliationDifference { get; set; }
}

public class OutletProfitabilityRowDto
{
    public long? OutletId { get; set; }
    public string OutletName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public string? UnallocatedReason { get; set; }
}

public class ProductContributionReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string ContributionBasis { get; set; } = "Revenue minus COGS only; shared expenses are not allocated.";
    public List<ProductContributionRowDto> Products { get; set; } = new();
    public List<CategoryContributionRowDto> Categories { get; set; } = new();
    public decimal AttributedRevenue { get; set; }
    public decimal AttributedCostOfGoodsSold { get; set; }
    public decimal AttributedContribution { get; set; }
    public decimal AuthoritativeRevenue { get; set; }
    public decimal AuthoritativeCostOfGoodsSold { get; set; }
    public decimal UnallocatedRevenue { get; set; }
    public decimal UnallocatedCostOfGoodsSold { get; set; }
    public decimal RevenueReconciliationDifference { get; set; }
    public decimal CogsReconciliationDifference { get; set; }
    public string? HistoricalLimitation { get; set; }
}

public class ProductContributionRowDto
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Contribution { get; set; }
    public decimal MarginPercent { get; set; }
}

public class CategoryContributionRowDto
{
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal Contribution { get; set; }
    public decimal MarginPercent { get; set; }
}

public class ComparativeBusinessProfitabilityReportDto
{
    public BusinessProfitabilityReportDto Current { get; set; } = new();
    public BusinessProfitabilityReportDto Comparison { get; set; } = new();
}

public class ComparativeOutletProfitabilityReportDto
{
    public OutletProfitabilityReportDto Current { get; set; } = new();
    public OutletProfitabilityReportDto Comparison { get; set; } = new();
}

public class ComparativeProductContributionReportDto
{
    public ProductContributionReportDto Current { get; set; } = new();
    public ProductContributionReportDto Comparison { get; set; } = new();
}

public class EnhancedGeneralLedgerReportDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string NormalBalanceType { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebits { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<EnhancedGeneralLedgerRowDto> Rows { get; set; } = new();
}

public class EnhancedGeneralLedgerRowDto
{
    public long JournalEntryId { get; set; }
    public long JournalEntryLineId { get; set; }
    public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    public string? JournalNumber { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Narration { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal RunningBalance { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? CustomerId { get; set; }
    public long? SupplierId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CostCenterId { get; set; }
    public long? ReversalOfJournalEntryId { get; set; }
    public long? ReversedByJournalEntryId { get; set; }
}
