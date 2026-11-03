using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingJournals.Queries;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class OpeningBalanceMappingValidator(
    ISender sender,
    ITenantAccessService tenantAccess)
    : IOpeningBalanceMappingValidator
{
    public async Task<IReadOnlyList<string>> ValidateBalanceSheetMappingsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBusinessScope(businessId);
        var errors = await sender.Send(
            new ValidateRequiredAccountingMappingsQuery(BalanceSheetOnly: true),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return errors;
    }

    private void EnsureBusinessScope(long businessId)
    {
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Opening balance Business scope does not match the active tenant.");
        }
    }
}

public sealed class OpeningBalanceJournalPoster(
    ISender sender,
    ITenantAccessService tenantAccess)
    : IOpeningBalanceJournalPoster
{
    public async Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        OpeningBalanceJournalInput journal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Opening balance Business scope does not match the active tenant.");
        }

        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                actorUserId,
                new ManualJournalDraftInput(
                    journal.JournalType,
                    journal.TransactionDate,
                    journal.PostingDate,
                    journal.BusinessDate,
                    journal.ReferenceType,
                    journal.ReferenceId,
                    journal.ReferenceNumber,
                    journal.Description,
                    null,
                    null,
                    null,
                    null,
                    journal.Lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        line.OutletId,
                        line.WarehouseId,
                        null,
                        null,
                        null,
                        null)).ToArray()),
                journal.IdempotencyDiscriminator),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var draft = await sender.Send(
            new GetJournalEntryByIdQuery(businessId, journalId),
            cancellationToken);
        if (string.Equals(
                draft.Status,
                JournalEntryStatuses.Posted,
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    journal.IdempotencyDiscriminator,
                    "opening-balance",
                    StringComparison.Ordinal))
                return journalId;

            throw new InvalidOperationException("Opening balance is already posted for this business.");
        }

        if (!string.Equals(
                draft.Status,
                JournalEntryStatuses.Draft,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Opening balance cannot be posted from journal status '{draft.Status}'.");
        }

        var posted = await sender.Send(
            new PostJournalEntryCommand(businessId, actorUserId, draft.Id),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return posted;
    }
}
