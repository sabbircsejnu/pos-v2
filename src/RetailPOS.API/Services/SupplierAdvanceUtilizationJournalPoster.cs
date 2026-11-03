using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class SupplierAdvanceUtilizationJournalPoster(
    ISender sender,
    IAccountingConfigurationService configurationService)
    : ISupplierAdvanceUtilizationJournalPoster
{
    public async Task<SupplierAdvanceUtilizationPostingAccounts> ResolvePostingAccountsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payableAccount = await configurationService.ResolvePostingAccountAsync(
            PostingAccountSlots.SupplierPayable);
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await configurationService.GetBusinessConfigurationAsync();
        if (!configuration.SupplierAdvanceAccountId.HasValue)
            throw new InvalidOperationException("Supplier advance account mapping is required.");

        return new SupplierAdvanceUtilizationPostingAccounts(
            payableAccount.AccountId,
            configuration.SupplierAdvanceAccountId.Value);
    }

    public async Task<long> PostAsync(
        SupplierAdvanceUtilizationJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reference = request.ReferenceNumber.Trim();
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                request.BusinessId,
                actorUserId,
                new ManualJournalDraftInput(
                    AccountingJournalTypes.SystemJournal,
                    request.EffectiveDate,
                    request.EffectiveDate,
                    request.EffectiveDate,
                    "SupplierAdvanceUtilization",
                    reference,
                    reference,
                    request.Notes,
                    null,
                    null,
                    null,
                    null,
                    [
                        new ManualJournalDraftLineInput(
                            request.SupplierPayableAccountId,
                            null,
                            request.Amount,
                            0,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null),
                        new ManualJournalDraftLineInput(
                            request.SupplierAdvanceAccountId,
                            null,
                            0,
                            request.Amount,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                    ]),
                $"supplier-advance-utilization:{request.AdvanceId}:{request.PayableDocumentId}:{reference}"),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(request.BusinessId, actorUserId, journalId),
            cancellationToken);
        return journalId;
    }
}
