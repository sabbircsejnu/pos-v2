using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Money;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class PurchaseAndReceiveStockLedgerWriterAdapter(IStockLedgerWriter stockLedger)
    : IPurchaseAndReceiveStockLedgerWriter
{
    public void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int quantityIn,
        int quantityOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string remarks,
        long? createdBy,
        GrnLedgerCost cost)
    {
        if (cost.ZeroCostVerified)
        {
            stockLedger.WriteVerifiedZeroCostEntry(
                variantId,
                locationId,
                locationType,
                transactionType,
                quantityIn,
                quantityOut,
                balanceAfter,
                referenceType,
                referenceId,
                remarks,
                createdBy);
            return;
        }

        stockLedger.WriteEntryWithValuation(
            variantId,
            locationId,
            locationType,
            transactionType,
            quantityIn,
            quantityOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            unitCost: cost.UnitCost,
            movementValue: cost.MovementValue);
    }
}

public sealed class PurchaseReceiptJournalPoster(
    ISender? sender = null,
    IAccountingConfigurationService? accountingConfigurationService = null,
    ICompanyTimeZoneProvider? timeZones = null)
    : IPurchaseReceiptJournalPoster
{
    public async Task PostAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long businessId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sender is null || accountingConfigurationService is null || timeZones is null)
            return;

        var totalAmount = MonetaryRounding.ToCurrency(
            grn.Items.Sum(item => item.ReceivedQty * item.UnitCost));
        if (totalAmount <= 0)
            return;

        var inventoryAccountId = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.Inventory);
        var payableAccountId = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.SupplierPayable);
        var createdBy = grn.CreatedBy ?? purchaseOrder.CreatedBy ?? 0;
        cancellationToken.ThrowIfCancellationRequested();
        var accountingDate = CompanyDates.DateFor(
            grn.ReceivedDate,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                createdBy,
                new ManualJournalDraftInput(
                    "PurchaseReceiptPosting",
                    grn.ReceivedDate,
                    accountingDate,
                    accountingDate,
                    "Grn",
                    grn.Id.ToString(),
                    $"GRN-{grn.Id:D6}",
                    $"Purchase receipt posting for GRN-{grn.Id:D6}",
                    purchaseOrder.OutletId,
                    purchaseOrder.WarehouseId,
                    null,
                    null,
                    [
                        new ManualJournalDraftLineInput(
                            inventoryAccountId,
                            "Inventory receipt",
                            totalAmount,
                            0,
                            purchaseOrder.OutletId,
                            purchaseOrder.WarehouseId,
                            null,
                            null,
                            null,
                            null),
                        new ManualJournalDraftLineInput(
                            payableAccountId,
                            "Supplier payable",
                            0,
                            totalAmount,
                            purchaseOrder.OutletId,
                            purchaseOrder.WarehouseId,
                            null,
                            null,
                            null,
                            null)
                    ]),
                $"grn:{grn.Id}"),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(businessId, createdBy, journalId),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<long> ResolveRequiredPostingAccountAsync(string slot)
    {
        var resolution = await accountingConfigurationService!.ResolvePostingAccountAsync(slot);
        return resolution.AccountId;
    }
}
