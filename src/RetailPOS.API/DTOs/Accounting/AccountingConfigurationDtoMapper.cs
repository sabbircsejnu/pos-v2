using RetailPOS.Core.Entities;

namespace RetailPOS.API.DTOs.Accounting;

internal static class AccountingConfigurationDtoMapper
{
    internal static AccountingConfigurationDto ToDto(AccountingConfiguration row) => new()
    {
        CashAccountId = row.CashAccountId,
        PettyCashAccountId = row.PettyCashAccountId,
        DefaultBankAccountId = row.DefaultBankAccountId,
        InventoryAccountId = row.InventoryAccountId,
        SalesAccountId = row.SalesAccountId,
        SalesDiscountAccountId = row.SalesDiscountAccountId,
        SalesReturnAccountId = row.SalesReturnAccountId,
        CogsAccountId = row.CogsAccountId,
        InventoryShortageAccountId = row.InventoryShortageAccountId,
        InventoryGainAccountId = row.InventoryGainAccountId,
        SupplierPayableAccountId = row.SupplierPayableAccountId,
        SupplierAdvanceAccountId = row.SupplierAdvanceAccountId,
        CustomerAdvanceAccountId = row.CustomerAdvanceAccountId,
        OtherIncomeAccountId = row.OtherIncomeAccountId,
        OwnerCapitalAccountId = row.OwnerCapitalAccountId,
        OwnerDrawingsAccountId = row.OwnerDrawingsAccountId,
        RetainedEarningsAccountId = row.RetainedEarningsAccountId,
        RoundingDifferenceAccountId = row.RoundingDifferenceAccountId,
        CashDifferenceGainAccountId = row.CashDifferenceGainAccountId,
        CashDifferenceLossAccountId = row.CashDifferenceLossAccountId,
        VatPayableAccountId = row.VatPayableAccountId,
        VatReceivableAccountId = row.VatReceivableAccountId,
        CardClearingAccountId = row.CardClearingAccountId,
        MobileMfsClearingAccountId = row.MobileMfsClearingAccountId,
        SettlementFeeExpenseAccountId = row.SettlementFeeExpenseAccountId,
        BankFeeExpenseAccountId = row.BankFeeExpenseAccountId,
        BankInterestIncomeAccountId = row.BankInterestIncomeAccountId,
        BankClearingActivationDate = row.BankClearingActivationDate,
        DefaultSupplierPaymentTermsDays = row.DefaultSupplierPaymentTermsDays,
        JournalNumberPrefix = row.JournalNumberPrefix,
        JournalNumberPadding = row.JournalNumberPadding,
        JournalSequenceResetPolicy = row.JournalSequenceResetPolicy,
        ManualJournalWorkflowMode = row.ManualJournalWorkflowMode,
        ManualJournalAllowSelfApproval = row.ManualJournalAllowSelfApproval,
        ManualJournalAttachmentMaxBytes = row.ManualJournalAttachmentMaxBytes
    };

    internal static AccountingConfiguration ToEntity(AccountingConfigurationDto dto) => new()
    {
        CashAccountId = dto.CashAccountId,
        PettyCashAccountId = dto.PettyCashAccountId,
        DefaultBankAccountId = dto.DefaultBankAccountId,
        InventoryAccountId = dto.InventoryAccountId,
        SalesAccountId = dto.SalesAccountId,
        SalesDiscountAccountId = dto.SalesDiscountAccountId,
        SalesReturnAccountId = dto.SalesReturnAccountId,
        CogsAccountId = dto.CogsAccountId,
        InventoryShortageAccountId = dto.InventoryShortageAccountId,
        InventoryGainAccountId = dto.InventoryGainAccountId,
        SupplierPayableAccountId = dto.SupplierPayableAccountId,
        SupplierAdvanceAccountId = dto.SupplierAdvanceAccountId,
        CustomerAdvanceAccountId = dto.CustomerAdvanceAccountId,
        OtherIncomeAccountId = dto.OtherIncomeAccountId,
        OwnerCapitalAccountId = dto.OwnerCapitalAccountId,
        OwnerDrawingsAccountId = dto.OwnerDrawingsAccountId,
        RetainedEarningsAccountId = dto.RetainedEarningsAccountId,
        RoundingDifferenceAccountId = dto.RoundingDifferenceAccountId,
        CashDifferenceGainAccountId = dto.CashDifferenceGainAccountId,
        CashDifferenceLossAccountId = dto.CashDifferenceLossAccountId,
        VatPayableAccountId = dto.VatPayableAccountId,
        VatReceivableAccountId = dto.VatReceivableAccountId,
        CardClearingAccountId = dto.CardClearingAccountId,
        MobileMfsClearingAccountId = dto.MobileMfsClearingAccountId,
        SettlementFeeExpenseAccountId = dto.SettlementFeeExpenseAccountId,
        BankFeeExpenseAccountId = dto.BankFeeExpenseAccountId,
        BankInterestIncomeAccountId = dto.BankInterestIncomeAccountId,
        BankClearingActivationDate = dto.BankClearingActivationDate,
        DefaultSupplierPaymentTermsDays = dto.DefaultSupplierPaymentTermsDays,
        JournalNumberPrefix = dto.JournalNumberPrefix,
        JournalNumberPadding = dto.JournalNumberPadding,
        JournalSequenceResetPolicy = dto.JournalSequenceResetPolicy,
        ManualJournalWorkflowMode = dto.ManualJournalWorkflowMode,
        ManualJournalAllowSelfApproval = dto.ManualJournalAllowSelfApproval,
        ManualJournalAttachmentMaxBytes = dto.ManualJournalAttachmentMaxBytes
    };

    internal static AccountingOutletConfigurationDto ToDto(AccountingOutletConfiguration row) => new()
    {
        OutletId = row.OutletId,
        OutletCashAccountId = row.OutletCashAccountId,
        OutletPettyCashAccountId = row.OutletPettyCashAccountId,
        OutletBankAccountId = row.OutletBankAccountId
    };

    internal static AccountingOutletConfiguration ToOutletEntity(
        AccountingOutletConfigurationDto dto) => new()
    {
        OutletId = dto.OutletId,
        OutletCashAccountId = dto.OutletCashAccountId,
        OutletPettyCashAccountId = dto.OutletPettyCashAccountId,
        OutletBankAccountId = dto.OutletBankAccountId
    };
}
