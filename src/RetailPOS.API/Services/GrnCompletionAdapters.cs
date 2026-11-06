using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingJournals.Queries;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Money;
using RetailPOS.Infrastructure.Audit;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class GrnTenantScopeAdapter(ITenantAccessService tenantAccess)
    : IGrnTenantScope
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long? businessId = tenantAccess.RequireBusinessId();
        return Task.FromResult(businessId);
    }
}

public sealed class GrnReadScopeProviderAdapter(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService locationAccess) : IGrnReadScopeProvider
{
    public async Task<GrnReadScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var businessId = tenantAccess.RequireBusinessId();
        var authorized = await locationAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.GrnView);
        return new GrnReadScope(
            businessId,
            authorized.IsGlobalAccess,
            authorized.Outlets.Select(location => location.Id).ToList(),
            authorized.Warehouses.Select(location => location.Id).ToList());
    }
}

public sealed class GrnReceiptLocationAuthorizerAdapter(IUserOutletAccessService outletAccess)
    : IGrnReceiptLocationAuthorizer
{
    public async Task AuthorizeAsync(
        string locationType,
        long locationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await outletAccess.EnforceWriteLocationAsync(LocationAccessPolicy.GrnView, locationId, locationType);
        }
        catch (UnauthorizedAccessException)
        {
            // The location can come from the stored document, so the refusal must not name it.
            throw new UnauthorizedAccessException("You are not assigned to this GRN location.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}

public sealed class GrnCompletionAuthorizerAdapter(
    ITenantAccessService tenantAccess,
    IGrnReceiptLocationAuthorizer locationAuthorizer) : IGrnCompletionAuthorizer
{
    public async Task<long?> AuthorizeAsync(
        long? businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        tenantAccess.EnsureBusinessMatch(businessId, "GRN");
        long? authorizedBusinessId = tenantAccess.RequireBusinessId();
        await locationAuthorizer.AuthorizeAsync(locationType, locationId, cancellationToken);
        return authorizedBusinessId;
    }
}

public sealed class GrnCompletionStockLedgerWriterAdapter(IStockLedgerWriter stockLedger)
    : IGrnCompletionStockLedgerWriter
{
    public void WriteEntryWithValuation(
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
        long createdBy,
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

public sealed class GrnCompletionAccountingPosterAdapter(
    ISender sender,
    IAccountingConfigurationService accountingConfigurationService,
    ISupplierPayableRecognitionWriter supplierPayableWriter,
    ICompanyTimeZoneProvider timeZones)
    : IGrnCompletionAccountingPoster
{
    public async Task<long?> PostReceiptAndCreatePayableAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var postedJournal = await PostPurchaseReceiptAccountingAsync(
            grn,
            purchaseOrder,
            businessId,
            actorUserId,
            cancellationToken);
        if (postedJournal is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = await accountingConfigurationService.GetBusinessConfigurationAsync();
            await supplierPayableWriter.RecordFromPostedGrnAsync(
                grn,
                purchaseOrder,
                new SupplierPayableRecognitionJournal(
                    postedJournal.Id,
                    postedJournal.BusinessId,
                    postedJournal.Status,
                    postedJournal.PostingDate,
                    postedJournal.ReferenceNumber,
                    postedJournal.Lines
                        .Select(line => new SupplierPayableRecognitionJournalLine(
                            line.AccountId,
                            line.DebitAmount,
                            line.CreditAmount))
                        .ToArray()),
                configuration.SupplierPayableAccountId,
                configuration.DefaultSupplierPaymentTermsDays,
                actorUserId,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return postedJournal?.Id;
    }

    private async Task<JournalEntryReadModel?> PostPurchaseReceiptAccountingAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long businessId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var netAmount = MonetaryRounding.ToCurrency(
            grn.Items.Sum(item => item.TaxableAmount ?? item.ReceivedQty * item.UnitCost));
        var vatAmount = MonetaryRounding.ToCurrency(
            grn.Items.Sum(item => item.VatAmount ?? 0m));
        var totalAmount = netAmount + vatAmount;
        if (totalAmount <= 0)
            return null;

        var inventoryAccountId = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.Inventory);
        var payableAccountId = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.SupplierPayable);
        var lines = new List<ManualJournalDraftLineInput>
        {
            new(
                inventoryAccountId,
                "Inventory receipt",
                netAmount,
                0,
                purchaseOrder.OutletId,
                purchaseOrder.WarehouseId,
                null,
                null,
                null,
                null),
            new(
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
        };

        if (vatAmount > 0m)
        {
            lines.Add(new ManualJournalDraftLineInput(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.VatReceivable),
                "Input VAT",
                vatAmount,
                0,
                purchaseOrder.OutletId,
                purchaseOrder.WarehouseId,
                null,
                null,
                null,
                null));
        }

        var roundingDifference = MonetaryRounding.ToCurrency(
            lines.Sum(line => line.DebitAmount) - lines.Sum(line => line.CreditAmount));
        if (Math.Abs(roundingDifference) > 0.005m)
        {
            var roundingAccountId = await ResolveRequiredPostingAccountAsync(
                PostingAccountSlots.RoundingDifference);
            lines.Add(roundingDifference > 0
                ? new ManualJournalDraftLineInput(
                    roundingAccountId,
                    "Rounding difference",
                    roundingDifference,
                    0,
                    purchaseOrder.OutletId,
                    purchaseOrder.WarehouseId,
                    null,
                    null,
                    null,
                    null)
                : new ManualJournalDraftLineInput(
                    roundingAccountId,
                    "Rounding difference",
                    0,
                    Math.Abs(roundingDifference),
                    purchaseOrder.OutletId,
                    purchaseOrder.WarehouseId,
                    null,
                    null,
                    null,
                    null));
        }

        var accountingDate = CompanyDates.DateFor(
            grn.ReceivedDate,
            await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken));
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                actorUserId,
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
                    lines),
                $"grn:{grn.Id}"),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(businessId, actorUserId, journalId),
            cancellationToken);
        return await sender.Send(
            new GetJournalEntryByIdQuery(businessId, journalId),
            cancellationToken);
    }

    private async Task<long> ResolveRequiredPostingAccountAsync(string slot)
    {
        var resolution = await accountingConfigurationService!.ResolvePostingAccountAsync(slot);
        return resolution.AccountId;
    }
}

public sealed class GrnCompletionAuditWriterAdapter(IAuditService? auditService = null)
    : IGrnCompletionAuditWriter
{
    public Task RecordCompletedAsync(
        Grn grn,
        PurchaseOrder purchaseOrder,
        long actorUserId,
        long? businessId,
        long? journalEntryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (auditService is null)
            return Task.CompletedTask;

        return auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Approve,
            Module = AuditModule.Purchase,
            Summary = $"GRN completed and purchase receipt posted: {grn.Id}",
            PrimaryEntity = ("Grn", grn.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["warehouseId"] = purchaseOrder.WarehouseId,
                ["outletId"] = purchaseOrder.OutletId,
                ["locationType"] = purchaseOrder.LocationType,
                ["locationId"] = purchaseOrder.LocationId,
                ["supplierId"] = purchaseOrder.SupplierId,
                ["poId"] = purchaseOrder.Id,
                ["grnId"] = grn.Id,
                ["journalEntryId"] = journalEntryId,
                ["actorUserId"] = actorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "Grn",
                    grn.Id.ToString(),
                    AuditOperationType.Update,
                    [
                        ("Status", "full", grn.Status),
                        ("PurchaseOrderStatus", purchaseOrder.Status, purchaseOrder.Status),
                        ("ReceivedQuantity", null, grn.Items.Sum(item => item.ReceivedQty)),
                        ("ReceiptAmount", null, grn.Items.Sum(
                            item => item.TaxableAmount ?? item.ReceivedQty * item.UnitCost)),
                        ("VatAmount", null, grn.Items.Sum(item => item.VatAmount ?? 0m)),
                        ("JournalEntryId", null, journalEntryId)
                    ])
            ]
        }, cancellationToken);
    }
}
