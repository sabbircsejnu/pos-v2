using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities;
using AccountingConfigurationEntity = RetailPOS.Core.Entities.AccountingConfiguration;

namespace RetailPOS.Application.AccountingConfiguration.Queries;

public sealed record PostingAccountResolution(
    string Slot,
    long AccountId,
    long? OutletId,
    string Source);

public sealed record ResolvePostingAccountQuery(string Slot, long? OutletId)
    : IQuery<PostingAccountResolution>;

public sealed class ResolvePostingAccountQueryHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingConfigurationInitializationStore initializationStore,
    IAccountingPostingAccountReadStore readStore)
    : IRequestHandler<ResolvePostingAccountQuery, PostingAccountResolution>
{
    public async Task<PostingAccountResolution> Handle(
        ResolvePostingAccountQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Slot))
        {
            throw new InvalidOperationException("Posting account slot is required.");
        }

        var slot = request.Slot.Trim().ToLowerInvariant();
        if (!PostingAccountResolutionPolicy.IsSupportedSlot(slot))
        {
            throw new InvalidOperationException($"Unsupported posting account slot '{request.Slot}'.");
        }

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (request.OutletId.HasValue &&
            PostingAccountResolutionPolicy.SupportsOutletOverride(slot))
        {
            var outletId = request.OutletId.Value;
            if (!await readStore.OutletExistsForBusinessAsync(businessId, outletId, cancellationToken))
            {
                throw new InvalidOperationException("Outlet not found in this business.");
            }

            var outletConfiguration = await readStore.GetOutletConfigurationAsync(
                businessId,
                outletId,
                cancellationToken);
            var outletAccountId = outletConfiguration is null
                ? null
                : PostingAccountResolutionPolicy.GetOutletAccountId(slot, outletConfiguration);
            if (outletAccountId.HasValue)
            {
                await ValidateAccountAsync(businessId, slot, outletAccountId.Value, cancellationToken);
                return new PostingAccountResolution(
                    slot,
                    outletAccountId.Value,
                    request.OutletId,
                    "OutletOverride");
            }
        }

        var configuration = await initializationStore.EnsureInitializedAsync(
            businessId,
            actorUserId: null,
            cancellationToken);
        var accountId = PostingAccountResolutionPolicy.GetGlobalAccountId(slot, configuration)
            ?? throw new InvalidOperationException(
                $"No posting account mapping configured for slot '{slot}'.");

        await ValidateAccountAsync(businessId, slot, accountId, cancellationToken);
        return new PostingAccountResolution(
            slot,
            accountId,
            request.OutletId,
            "GlobalConfiguration");
    }

    private async Task ValidateAccountAsync(
        long businessId,
        string slot,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await readStore.GetChartAccountAsync(businessId, accountId, cancellationToken);
        if (account is null)
        {
            throw new InvalidOperationException($"Invalid chart account IDs: {accountId}");
        }

        if (!account.IsActive)
        {
            throw new InvalidOperationException(
                $"Mapped account {account.Id} for slot '{slot}' must be active.");
        }

        var allowedTypes = PostingAccountResolutionPolicy.GetAllowedAccountTypes(slot);
        if (!allowedTypes.Any(type =>
                string.Equals(type, account.AccountType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Mapped account {account.Id} for slot '{slot}' must be one of: {string.Join(", ", allowedTypes)}.");
        }
    }
}

public static class PostingAccountResolutionPolicy
{
    public static bool IsSupportedSlot(string slot) =>
        slot is "cash" or "petty_cash" or "bank" or "inventory" or "sales" or "sales_discount" or
            "sales_return" or "cogs" or "inventory_shortage" or "inventory_gain" or
            "supplier_payable" or "supplier_advance" or "customer_advance" or "other_income" or
            "owner_capital" or "owner_drawings" or "retained_earnings" or "rounding_difference" or
            "cash_difference_gain" or "cash_difference_loss" or "vat_payable" or "vat_receivable" or
            "card_clearing" or "mobile_mfs_clearing" or "settlement_fee_expense" or
            "bank_fee_expense" or "bank_interest_income";

    public static bool SupportsOutletOverride(string slot) =>
        slot is "cash" or "petty_cash" or "bank";

    public static long? GetOutletAccountId(string slot, AccountingOutletConfiguration configuration) =>
        slot switch
        {
            "cash" => configuration.OutletCashAccountId,
            "petty_cash" => configuration.OutletPettyCashAccountId,
            "bank" => configuration.OutletBankAccountId,
            _ => null
        };

    public static long? GetGlobalAccountId(string slot, AccountingConfigurationEntity? configuration)
    {
        if (configuration is null)
        {
            return null;
        }
        return slot switch
        {
            "cash" => configuration.CashAccountId,
            "petty_cash" => configuration.PettyCashAccountId,
            "bank" => configuration.DefaultBankAccountId,
            "inventory" => configuration.InventoryAccountId,
            "sales" => configuration.SalesAccountId,
            "sales_discount" => configuration.SalesDiscountAccountId,
            "sales_return" => configuration.SalesReturnAccountId,
            "cogs" => configuration.CogsAccountId,
            "inventory_shortage" => configuration.InventoryShortageAccountId,
            "inventory_gain" => configuration.InventoryGainAccountId,
            "supplier_payable" => configuration.SupplierPayableAccountId,
            "supplier_advance" => configuration.SupplierAdvanceAccountId,
            "customer_advance" => configuration.CustomerAdvanceAccountId,
            "other_income" => configuration.OtherIncomeAccountId,
            "owner_capital" => configuration.OwnerCapitalAccountId,
            "owner_drawings" => configuration.OwnerDrawingsAccountId,
            "retained_earnings" => configuration.RetainedEarningsAccountId,
            "rounding_difference" => configuration.RoundingDifferenceAccountId,
            "cash_difference_gain" => configuration.CashDifferenceGainAccountId,
            "cash_difference_loss" => configuration.CashDifferenceLossAccountId,
            "vat_payable" => configuration.VatPayableAccountId,
            "vat_receivable" => configuration.VatReceivableAccountId,
            "card_clearing" => configuration.CardClearingAccountId,
            "mobile_mfs_clearing" => configuration.MobileMfsClearingAccountId,
            "settlement_fee_expense" => configuration.SettlementFeeExpenseAccountId,
            "bank_fee_expense" => configuration.BankFeeExpenseAccountId,
            "bank_interest_income" => configuration.BankInterestIncomeAccountId,
            _ => null
        };
    }

    public static IReadOnlyList<(string Slot, long? AccountId)> GetGlobalAccountMappings(
        AccountingConfigurationEntity configuration) =>
    [
        ("cash", configuration.CashAccountId),
        ("petty_cash", configuration.PettyCashAccountId),
        ("bank", configuration.DefaultBankAccountId),
        ("inventory", configuration.InventoryAccountId),
        ("sales", configuration.SalesAccountId),
        ("sales_discount", configuration.SalesDiscountAccountId),
        ("sales_return", configuration.SalesReturnAccountId),
        ("cogs", configuration.CogsAccountId),
        ("inventory_shortage", configuration.InventoryShortageAccountId),
        ("inventory_gain", configuration.InventoryGainAccountId),
        ("supplier_payable", configuration.SupplierPayableAccountId),
        ("supplier_advance", configuration.SupplierAdvanceAccountId),
        ("customer_advance", configuration.CustomerAdvanceAccountId),
        ("other_income", configuration.OtherIncomeAccountId),
        ("owner_capital", configuration.OwnerCapitalAccountId),
        ("owner_drawings", configuration.OwnerDrawingsAccountId),
        ("retained_earnings", configuration.RetainedEarningsAccountId),
        ("rounding_difference", configuration.RoundingDifferenceAccountId),
        ("cash_difference_gain", configuration.CashDifferenceGainAccountId),
        ("cash_difference_loss", configuration.CashDifferenceLossAccountId),
        ("vat_payable", configuration.VatPayableAccountId),
        ("vat_receivable", configuration.VatReceivableAccountId),
        ("card_clearing", configuration.CardClearingAccountId),
        ("mobile_mfs_clearing", configuration.MobileMfsClearingAccountId),
        ("settlement_fee_expense", configuration.SettlementFeeExpenseAccountId),
        ("bank_fee_expense", configuration.BankFeeExpenseAccountId),
        ("bank_interest_income", configuration.BankInterestIncomeAccountId)
    ];

    public static IReadOnlyList<(string Slot, long? AccountId)> GetOutletAccountMappings(
        AccountingOutletConfiguration configuration) =>
    [
        ("cash", configuration.OutletCashAccountId),
        ("petty_cash", configuration.OutletPettyCashAccountId),
        ("bank", configuration.OutletBankAccountId)
    ];

    public static IReadOnlyList<string> GetRequiredSlots(bool balanceSheetOnly) =>
        balanceSheetOnly
            ?
            [
                "cash",
                "petty_cash",
                "bank",
                "inventory",
                "supplier_payable",
                "customer_advance",
                "owner_capital",
                "owner_drawings"
            ]
            :
            [
                "cash",
                "petty_cash",
                "bank",
                "inventory",
                "sales",
                "sales_discount",
                "sales_return",
                "cogs",
                "inventory_shortage",
                "inventory_gain",
                "supplier_payable",
                "supplier_advance",
                "customer_advance",
                "other_income",
                "owner_capital",
                "owner_drawings",
                "retained_earnings",
                "rounding_difference",
                "cash_difference_gain",
                "cash_difference_loss"
            ];

    public static IReadOnlyList<string> GetAllowedAccountTypes(string slot) =>
        slot switch
        {
            "cash" or "petty_cash" or "bank" or "inventory" or "vat_receivable" or
                "card_clearing" or "mobile_mfs_clearing" => [ChartAccountTypes.Asset],
            "sales" or "inventory_gain" or "other_income" or "cash_difference_gain" or
                "bank_interest_income" => [ChartAccountTypes.Income],
            "sales_discount" or "sales_return" or "inventory_shortage" or
                "rounding_difference" or "settlement_fee_expense" or "bank_fee_expense" or
                "cash_difference_loss" => [ChartAccountTypes.Expense],
            "cogs" => [ChartAccountTypes.CostOfGoodsSold],
            "supplier_payable" or "supplier_advance" or "customer_advance" or
                "vat_payable" => [ChartAccountTypes.Liability],
            "owner_capital" or "owner_drawings" or "retained_earnings" => [ChartAccountTypes.Equity],
            _ => throw new InvalidOperationException($"Unsupported posting account slot '{slot}'.")
        };
}
