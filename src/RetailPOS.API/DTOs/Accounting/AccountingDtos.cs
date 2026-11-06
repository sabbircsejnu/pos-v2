using System.ComponentModel.DataAnnotations;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.DTOs.Accounting;

public class ChartAccountDto
{
    public long Id { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string NormalBalanceType { get; set; } = string.Empty;
    public long? ParentAccountId { get; set; }
    public bool IsSystemAccount { get; set; }
    public bool IsControlAccount { get; set; }
    public bool AllowManualPosting { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsExpensePaymentAccount { get; set; }
}

public class ChartAccountSearchDto
{
    public string? Query { get; set; }
    public string? AccountType { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateChartAccountDto
{
    [Required, MaxLength(30)] public string AccountCode { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string AccountType { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string NormalBalanceType { get; set; } = string.Empty;
    public long? ParentAccountId { get; set; }
    public bool IsControlAccount { get; set; }
    public bool AllowManualPosting { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public bool IsExpensePaymentAccount { get; set; }
}

public class UpdateChartAccountDto
{
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    public long? ParentAccountId { get; set; }
    public bool IsControlAccount { get; set; }
    public bool AllowManualPosting { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>Omitted by older clients, which leaves the flag unchanged.</summary>
    public bool? IsExpensePaymentAccount { get; set; }
}

public class AccountingConfigurationDto
{
    public long? CashAccountId { get; set; }
    public long? PettyCashAccountId { get; set; }
    public long? DefaultBankAccountId { get; set; }
    public long? InventoryAccountId { get; set; }
    public long? SalesAccountId { get; set; }
    public long? SalesDiscountAccountId { get; set; }
    public long? SalesReturnAccountId { get; set; }
    public long? CogsAccountId { get; set; }
    public long? InventoryShortageAccountId { get; set; }
    public long? InventoryGainAccountId { get; set; }
    public long? SupplierPayableAccountId { get; set; }
    public long? SupplierAdvanceAccountId { get; set; }
    public long? CustomerAdvanceAccountId { get; set; }
    public long? OtherIncomeAccountId { get; set; }
    public long? OwnerCapitalAccountId { get; set; }
    public long? OwnerDrawingsAccountId { get; set; }
    public long? RetainedEarningsAccountId { get; set; }
    public long? RoundingDifferenceAccountId { get; set; }
    public long? CashDifferenceGainAccountId { get; set; }
    public long? CashDifferenceLossAccountId { get; set; }
    public long? VatPayableAccountId { get; set; }
    public long? VatReceivableAccountId { get; set; }
    public long? CardClearingAccountId { get; set; }
    public long? MobileMfsClearingAccountId { get; set; }
    public long? SettlementFeeExpenseAccountId { get; set; }
    public long? BankFeeExpenseAccountId { get; set; }
    public long? BankInterestIncomeAccountId { get; set; }
    public DateTime? BankClearingActivationDate { get; set; }
    public int DefaultSupplierPaymentTermsDays { get; set; } = 30;
    public string JournalNumberPrefix { get; set; } = "JR";
    public int JournalNumberPadding { get; set; } = 6;
    public string JournalSequenceResetPolicy { get; set; } = AccountingSequenceResetPolicies.PerFinancialYear;
    public string ManualJournalWorkflowMode { get; set; } = ManualJournalWorkflowModes.DraftPost;
    public bool ManualJournalAllowSelfApproval { get; set; }
    public long ManualJournalAttachmentMaxBytes { get; set; } = 10 * 1024 * 1024;
}

public class AccountingOutletConfigurationDto
{
    [Required] public long OutletId { get; set; }
    public long? OutletCashAccountId { get; set; }
    public long? OutletPettyCashAccountId { get; set; }
    public long? OutletBankAccountId { get; set; }
}

public static class PostingAccountSlots
{
    public const string Cash = "cash";
    public const string PettyCash = "petty_cash";
    public const string Bank = "bank";
    public const string Inventory = "inventory";
    public const string Sales = "sales";
    public const string SalesDiscount = "sales_discount";
    public const string SalesReturn = "sales_return";
    public const string Cogs = "cogs";
    public const string InventoryShortage = "inventory_shortage";
    public const string InventoryGain = "inventory_gain";
    public const string SupplierPayable = "supplier_payable";
    public const string SupplierAdvance = "supplier_advance";
    public const string CustomerAdvance = "customer_advance";
    public const string OtherIncome = "other_income";
    public const string OwnerCapital = "owner_capital";
    public const string OwnerDrawings = "owner_drawings";
    public const string RetainedEarnings = "retained_earnings";
    public const string RoundingDifference = "rounding_difference";
    public const string CashDifferenceGain = "cash_difference_gain";
    public const string CashDifferenceLoss = "cash_difference_loss";
    public const string VatPayable = "vat_payable";
    public const string VatReceivable = "vat_receivable";
    public const string CardClearing = "card_clearing";
    public const string MobileMfsClearing = "mobile_mfs_clearing";
    public const string SettlementFeeExpense = "settlement_fee_expense";
    public const string BankFeeExpense = "bank_fee_expense";
    public const string BankInterestIncome = "bank_interest_income";
}

public class PostingAccountResolutionDto
{
    public string Slot { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public string Source { get; set; } = string.Empty;
    public long? OutletId { get; set; }
}

public class OpeningBalanceLineDto
{
    [Required] public long AccountId { get; set; }
    [Range(0, double.MaxValue)] public decimal DebitAmount { get; set; }
    [Range(0, double.MaxValue)] public decimal CreditAmount { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
}

public class CreateOpeningBalanceRequestDto
{
    [Required] public DateTime AccountingStartDate { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MinLength(2)] public List<OpeningBalanceLineDto> Lines { get; set; } = new();
}

public class CreateOpeningBalanceAdjustmentRequestDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required] public DateTime AdjustmentBusinessDate { get; set; }
    [Required] public DateTime PostingDate { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MinLength(2)] public List<OpeningBalanceLineDto> Lines { get; set; } = new();
}

public class OpeningBalanceStatusDto
{
    public bool HasPostedOpeningBalance { get; set; }
    public long? OpeningBalanceJournalEntryId { get; set; }
    public string? OpeningBalanceJournalNumber { get; set; }
    public DateTime? OpeningBalancePostedAt { get; set; }
    public bool IsOpeningBalanceLocked { get; set; }
    public long? LockingJournalEntryId { get; set; }
    public string? LockingJournalNumber { get; set; }
    public string? LockingJournalType { get; set; }
    public DateTime? LockedAt { get; set; }
    public bool CanPostOpeningBalance { get; set; }
}

public class JournalEntryLineDto
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string? Description { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? SupplierId { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CostCenterId { get; set; }
}

public class JournalEntryDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string? JournalNumber { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public DateTime? BusinessDate { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime PostingDate { get; set; }
    public long? FinancialYearId { get; set; }
    public long? AccountingPeriodId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsSystemGenerated { get; set; }
    public long? ReversalOfJournalEntryId { get; set; }
    public long? ReversedByJournalEntryId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public long? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public List<JournalEntryLineDto> Lines { get; set; } = new();
    public List<JournalEntryAttachmentDto> Attachments { get; set; } = new();
}

public class JournalEntryAttachmentDto
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}

public class CreateJournalEntryLineDto
{
    [Required] public long AccountId { get; set; }
    public string? Description { get; set; }
    [Range(0, double.MaxValue)] public decimal DebitAmount { get; set; }
    [Range(0, double.MaxValue)] public decimal CreditAmount { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? SupplierId { get; set; }
    public long? CustomerId { get; set; }
    [MaxLength(50)] public string? PaymentMethodId { get; set; }
    [MaxLength(50)] public string? CostCenterId { get; set; }
}

public class CreateJournalEntryDto
{
    [Required, MaxLength(50)] public string JournalType { get; set; } = "ManualJournal";
    [Required] public DateTime TransactionDate { get; set; }
    [Required] public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    [MaxLength(50)] public string? ReferenceType { get; set; }
    [MaxLength(80)] public string? ReferenceId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    [MinLength(2)] public List<CreateJournalEntryLineDto> Lines { get; set; } = new();
}

public class UpdateJournalEntryDto
{
    [Required, MaxLength(50)] public string JournalType { get; set; } = "ManualJournal";
    [Required] public DateTime TransactionDate { get; set; }
    [Required] public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    [MaxLength(50)] public string? ReferenceType { get; set; }
    [MaxLength(80)] public string? ReferenceId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    [MinLength(2)] public List<CreateJournalEntryLineDto> Lines { get; set; } = new();
}

public class CancelJournalEntryDto
{
    [MaxLength(500)] public string? Reason { get; set; }
}

public class ApproveJournalEntryDto
{
}

public class JournalSearchDto
{
    public DateTime? PostingDateStart { get; set; }
    public DateTime? PostingDateEnd { get; set; }
    public DateTime? BusinessDateStart { get; set; }
    public DateTime? BusinessDateEnd { get; set; }
    public string? Status { get; set; }
    public long? OutletId { get; set; }
    public string? JournalType { get; set; }
    public string? JournalNumber { get; set; }
    public long? AccountId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public long? CreatedBy { get; set; }
    public long? PostedBy { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class JournalListDto
{
    public List<JournalEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class ReverseJournalEntryDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public DateTime? PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
}

public class FinancialYearDto
{
    public long Id { get; set; }
    public string YearCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
}

public class CreateFinancialYearDto
{
    [Required, MaxLength(20)] public string YearCode { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [Required] public DateTime StartDate { get; set; }
    [Required] public DateTime EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class UpdateFinancialYearDto
{
    [Required, MaxLength(20)] public string YearCode { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [Required] public DateTime StartDate { get; set; }
    [Required] public DateTime EndDate { get; set; }
}

public class FinancialYearActionDto
{
    [MaxLength(500)] public string? Reason { get; set; }
}

public class RequiredReasonActionDto
{
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public class AccountingPeriodDto
{
    public long Id { get; set; }
    public long FinancialYearId { get; set; }
    public int PeriodNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public DateTime? LockedAt { get; set; }
    public long? LockedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public long? ReopenedBy { get; set; }
}

public class CreateAccountingPeriodDto
{
    [Required] public int PeriodNumber { get; set; }
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [Required] public DateTime StartDate { get; set; }
    [Required] public DateTime EndDate { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = AccountingPeriodStatuses.Open;
}

public class UpdateAccountingPeriodDto
{
    [Required] public int PeriodNumber { get; set; }
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [Required] public DateTime StartDate { get; set; }
    [Required] public DateTime EndDate { get; set; }
}

public class AccountingPeriodStatusActionDto
{
    [MaxLength(500)] public string? Reason { get; set; }
}

public class OtherIncomeEntryDto
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public DateTime IncomeDate { get; set; }
    public string IncomeCategory { get; set; } = string.Empty;
    public long? IncomeAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public long? PaymentAccountId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? OutletId { get; set; }
    public long? JournalEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}

public class CreateOtherIncomeEntryDto
{
    [Required] public DateTime IncomeDate { get; set; }
    [Required, MaxLength(120)] public string IncomeCategory { get; set; } = string.Empty;
    public long? IncomeAccountId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    [MaxLength(40)] public string? PaymentMethod { get; set; }
    public long? PaymentAccountId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public long? OutletId { get; set; }
}

public class UpdateOtherIncomeEntryDto
{
    [Required] public DateTime IncomeDate { get; set; }
    [Required, MaxLength(120)] public string IncomeCategory { get; set; } = string.Empty;
    public long? IncomeAccountId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    [MaxLength(40)] public string? PaymentMethod { get; set; }
    public long? PaymentAccountId { get; set; }
    [MaxLength(80)] public string? ReferenceNumber { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public long? OutletId { get; set; }
}

public class OtherIncomeSearchDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public long? OutletId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class OtherIncomeListDto
{
    public List<OtherIncomeEntryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class ApproveOtherIncomeEntryDto
{
    public long? IncomeAccountId { get; set; }
}
