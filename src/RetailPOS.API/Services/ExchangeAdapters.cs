using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Exchanges;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Services;

/// <summary>
/// Posts the exchange journal synchronously, inside the exchange transaction, with the existing journal engine (C-04).
/// Accounts come from the business's existing COA slots and payment-method policy (C-06): an unmapped slot fails the
/// exchange with a typed error; there is no generic or suspense fallback.
/// </summary>
public sealed class ExchangeJournalPoster(ISender sender, IAccountingConfigurationService accounting) : IExchangeJournalPoster
{
    private static readonly IReadOnlyDictionary<string, string> Slots = new Dictionary<string, string>
    {
        [ExchangeJournalComposer.SalesReturn] = PostingAccountSlots.SalesReturn,
        [ExchangeJournalComposer.Sales] = PostingAccountSlots.Sales,
        [ExchangeJournalComposer.SalesDiscount] = PostingAccountSlots.SalesDiscount,
        [ExchangeJournalComposer.VatPayable] = PostingAccountSlots.VatPayable,
        [ExchangeJournalComposer.Cogs] = PostingAccountSlots.Cogs,
        [ExchangeJournalComposer.Inventory] = PostingAccountSlots.Inventory
    };

    public async Task EnsureAccountsAsync(
        long outletId, IReadOnlyCollection<string> slots, IReadOnlyCollection<string> settlementMethods, DateTime businessDate, CancellationToken cancellationToken)
    {
        foreach (var slot in slots)
            await ResolveSlotAsync(Slots[slot], outletId, ExchangeErrorCodes.AccountingConfigurationMissing);
        foreach (var method in settlementMethods)
            await ResolveMethodAsync(method, outletId, businessDate);
    }

    public async Task<long> PostAsync(ExchangeJournalPosting posting, CancellationToken cancellationToken)
    {
        var lines = new List<CreateJournalEntryLineDto>(posting.Lines.Count);
        foreach (var line in posting.Lines)
        {
            var accountId = line.Slot is { } slot
                ? await ResolveSlotAsync(Slots[slot], posting.OutletId, ExchangeErrorCodes.AccountingConfigurationMissing)
                : await ResolveMethodAsync(line.SettlementMethod!, posting.OutletId, posting.BusinessDate);
            lines.Add(new CreateJournalEntryLineDto
            {
                AccountId = accountId,
                Description = line.Description,
                DebitAmount = line.Debit,
                CreditAmount = line.Credit,
                OutletId = posting.OutletId
            });
        }

        if (lines.Sum(line => line.DebitAmount) != lines.Sum(line => line.CreditAmount))
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.SettlementMismatch, "The exchange journal would not balance.");

        // Posting and business date are the session business date as a UTC midnight, like refund reversals (VI-02), so the
        // engine's open-period check evaluates exactly that day.
        var day = DateTime.SpecifyKind(posting.BusinessDate.Date, DateTimeKind.Utc);
        var journal = SaleJournalInputFactory.Create(
            "ExchangePosting", day, day, posting.ReferenceType, posting.ReferenceId, posting.ReferenceNumber, posting.Description,
            posting.OutletId, posting.TerminalId, posting.CashSessionId, lines, posting.TransactionAtUtc);
        try
        {
            var journalId = await sender.Send(new CreateOrGetSystemJournalDraftCommand(posting.BusinessId, posting.ActorUserId, journal, posting.SourceKey), cancellationToken);
            await sender.Send(new PostJournalEntryCommand(posting.BusinessId, posting.ActorUserId, journalId), cancellationToken);
            return journalId;
        }
        catch (InvalidOperationException exception) when (true
            && (exception.Message.Contains("period", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("financial year", StringComparison.OrdinalIgnoreCase)))
        {
            // The engine's guard is the final authority; a pre-checked period that changed still stops the exchange.
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.AccountingPeriodClosed, "The accounting period for this date is not open.");
        }
    }

    private async Task<long> ResolveMethodAsync(string method, long outletId, DateTime businessDate)
    {
        var configuration = await accounting.GetBusinessConfigurationAsync();
        var slot = PaymentPostingPolicy.ResolveSlot(method, businessDate, configuration?.BankClearingActivationDate);
        return await ResolveSlotAsync(slot, outletId, ExchangeErrorCodes.PaymentAccountMissing);
    }

    private async Task<long> ResolveSlotAsync(string slot, long outletId, string errorCode)
    {
        try
        {
            return (await accounting.ResolvePostingAccountAsync(slot, outletId)).AccountId;
        }
        catch (InvalidOperationException)
        {
            throw ExchangeException.Unprocessable(errorCode,
                $"The '{slot}' posting account is not configured for this business, so the exchange cannot be recorded. Ask the accountant to map it.");
        }
    }
}

public sealed class ExchangeAuditWriter(IAuditService audit) : IExchangeAuditWriter
{
    public Task RecordAsync(string action, string summary, long exchangeId, IReadOnlyDictionary<string, object?> metadata, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object?>(metadata) { ["action"] = action };
        return audit.RecordAsync(new AuditEventInput
        {
            ActionType = action.EndsWith("denied", StringComparison.Ordinal) ? AuditActionType.Cancel
                : action.Contains("approval", StringComparison.Ordinal) ? AuditActionType.Approve
                : action.Contains("reversed", StringComparison.Ordinal) ? AuditActionType.Reverse
                : action.Contains("policy", StringComparison.Ordinal) ? AuditActionType.Update
                : AuditActionType.Create,
            Module = AuditModule.Sales,
            Summary = summary,
            PrimaryEntity = ("Exchange", exchangeId.ToString()),
            Metadata = data
        }, cancellationToken);
    }
}

/// <summary>Appends exchange events to the existing transactional outbox (AppendSourceChangeAsync: plain transaction, no atomic audit scope).</summary>
public sealed class ExchangeEventWriter(IIntegrationOutbox outbox) : IExchangeEventWriter
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(long businessId, string messageType, long exchangeId, string discriminator, object payload, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var occurred = new DateTime(occurredAtUtc.Ticks - occurredAtUtc.Ticks % 10, DateTimeKind.Utc);
        var json = JsonSerializer.Serialize(payload, PayloadOptions);
        var identity = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', businessId, messageType, "exchange", exchangeId, discriminator)));
        var correlation = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', businessId, "exchange", exchangeId)));
        await outbox.AppendSourceChangeAsync(new IntegrationEnvelope(
            new Guid(identity.AsSpan(0, 16)), businessId, "RetailPOS", messageType, 1, "exchange", exchangeId.ToString(),
            new Guid(correlation.AsSpan(0, 16)), null, occurred, json, IntegrationEnvelope.Hash(json)), cancellationToken);
    }
}
