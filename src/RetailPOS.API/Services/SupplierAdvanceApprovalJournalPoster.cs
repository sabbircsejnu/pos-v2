using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class SupplierAdvanceApprovalJournalPoster(
    ISender sender,
    IAccountingConfigurationService configurationService)
    : ISupplierAdvanceApprovalJournalPoster
{
    public async Task<long> ResolveAdvanceAccountIdAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = await configurationService.GetBusinessConfigurationAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (!configuration.SupplierAdvanceAccountId.HasValue)
            throw new InvalidOperationException("Supplier advance account mapping is required.");

        return configuration.SupplierAdvanceAccountId.Value;
    }

    public async Task<long> PostAsync(
        SupplierAdvanceApprovalJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                request.BusinessId,
                actorUserId,
                new ManualJournalDraftInput(
                    AccountingJournalTypes.SystemJournal,
                    request.PaymentDate,
                    request.PaymentDate,
                    request.PaymentDate,
                    "SupplierAdvance",
                    request.AdvanceId.ToString(),
                    request.ReferenceNumber,
                    string.IsNullOrWhiteSpace(request.Notes)
                        ? $"Supplier advance {request.AdvanceId}"
                        : request.Notes,
                    request.OutletId,
                    null,
                    null,
                    null,
                    [
                        new ManualJournalDraftLineInput(
                            request.AdvanceAccountId,
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
