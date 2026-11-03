namespace RetailPOS.Core.Entities;

public class AccountingConfiguration
{
    public long Id { get; set; }
    public long BusinessId { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ChartAccount? CashAccount { get; set; }
    public virtual ChartAccount? PettyCashAccount { get; set; }
    public virtual ChartAccount? DefaultBankAccount { get; set; }
    public virtual ChartAccount? InventoryAccount { get; set; }
    public virtual ChartAccount? SalesAccount { get; set; }
    public virtual ChartAccount? SalesDiscountAccount { get; set; }
    public virtual ChartAccount? SalesReturnAccount { get; set; }
    public virtual ChartAccount? CogsAccount { get; set; }
    public virtual ChartAccount? InventoryShortageAccount { get; set; }
    public virtual ChartAccount? InventoryGainAccount { get; set; }
    public virtual ChartAccount? SupplierPayableAccount { get; set; }
    public virtual ChartAccount? SupplierAdvanceAccount { get; set; }
    public virtual ChartAccount? CustomerAdvanceAccount { get; set; }
    public virtual ChartAccount? OtherIncomeAccount { get; set; }
    public virtual ChartAccount? OwnerCapitalAccount { get; set; }
    public virtual ChartAccount? OwnerDrawingsAccount { get; set; }
    public virtual ChartAccount? RetainedEarningsAccount { get; set; }
    public virtual ChartAccount? RoundingDifferenceAccount { get; set; }
    public virtual ChartAccount? CashDifferenceGainAccount { get; set; }
    public virtual ChartAccount? CashDifferenceLossAccount { get; set; }
    public virtual ChartAccount? VatPayableAccount { get; set; }
    public virtual ChartAccount? VatReceivableAccount { get; set; }
    public virtual ChartAccount? CardClearingAccount { get; set; }
    public virtual ChartAccount? MobileMfsClearingAccount { get; set; }
}
