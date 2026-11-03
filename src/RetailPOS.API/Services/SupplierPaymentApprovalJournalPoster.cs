using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class SupplierPaymentApprovalJournalPoster(
    ISender sender,
    IAccountingConfigurationService configurationService)
    : ISupplierPaymentApprovalJournalPoster
{
    public async Task<SupplierPaymentApprovalPostingAccounts> ResolvePostingAccountsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payableAccount = await configurationService.ResolvePostingAccountAsync(
            PostingAccountSlots.SupplierPayable);
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await configurationService.GetBusinessConfigurationAsync();
        if (!configuration.SupplierAdvanceAccountId.HasValue)
            throw new InvalidOperationException("Supplier advance account mapping is required.");

        return new SupplierPaymentApprovalPostingAccounts(
            payableAccount.AccountId,
            configuration.SupplierAdvanceAccountId.Value);
    }

    public Task<long> PostPaymentAsync(
        SupplierPaymentApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken) =>
        CreateAndPostAsync(
            request,
            actorUserId,
            "SupplierPayment",
            request.PaymentId.ToString(),
            cancellationToken);

    public Task<long> PostAdvanceAsync(
        SupplierPaymentApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken) =>
        CreateAndPostAsync(
            request,
            actorUserId,
            "SupplierAdvance",
            $"{request.PaymentId}:{request.ReferenceNumber}",
            cancellationToken);

    private async Task<long> CreateAndPostAsync(
        SupplierPaymentApprovalJournalRequest request,
        long actorUserId,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var description = string.IsNullOrWhiteSpace(request.Notes)
            ? referenceType == "SupplierPayment"
                ? $"Supplier payment {request.PaymentId}"
                : $"Supplier advance {request.PaymentId}"
            : request.Notes;
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                request.BusinessId,
                actorUserId,
                new ManualJournalDraftInput(
                    AccountingJournalTypes.SystemJournal,
                    request.PaymentDate,
                    request.PaymentDate,
                    request.PaymentDate,
                    referenceType,
                    referenceId,
                    request.ReferenceNumber,
                    description,
                    request.OutletId,
                    null,
                    null,
                    null,
                    [
                        new ManualJournalDraftLineInput(
                            request.OffsetAccountId,
                            request.Notes,
                            request.Amount,
                            0,
                            request.OutletId,
                            null,
                            null,
                            null,
                            null,
                            null),
                        new ManualJournalDraftLineInput(
                            request.PaymentAccountId,
                            request.Notes,
                            0,
                            request.Amount,
                            request.OutletId,
                            null,
                            null,
                            null,
                            null,
                            null)
                    ]),
                null),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(request.BusinessId, actorUserId, journalId),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return journalId;
    }
}
