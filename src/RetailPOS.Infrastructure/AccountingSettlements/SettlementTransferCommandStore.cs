using Microsoft.EntityFrameworkCore;
using System.Data;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingSettlements;

public sealed class SettlementTransferCommandStore(
    RetailPOSDbContext dbContext,
    ISettlementPostingAccountResolver accountResolver,
    ISettlementJournalPoster journalPoster,
    ISettlementJournalReverser journalReverser) : ISettlementTransferCommandStore
{
    public async Task<SettlementBatchReadModel> PostSettlementAsync(
        long businessId,
        long actorUserId,
        SettlementPostingInput input,
        CancellationToken cancellationToken)
    {
        var key = NormalizeKey(input.IdempotencyKey);
        var existing = await dbContext.SettlementBatches
            .SingleOrDefaultAsync(
                batch => batch.BusinessId == businessId && batch.IdempotencyKey == key,
                cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        ValidateSettlementAmounts(input);
        var clearingSlot = input.SettlementType.Contains("mobile", StringComparison.OrdinalIgnoreCase)
            || input.SettlementType.Contains("mfs", StringComparison.OrdinalIgnoreCase)
            ? "mobile_mfs_clearing"
            : "card_clearing";
        var configuredClearingAccountId = await accountResolver.ResolveAsync(
            businessId,
            clearingSlot,
            cancellationToken);
        if (input.ClearingAccountId != configuredClearingAccountId)
        {
            throw new InvalidOperationException(
                "Settlement clearing account does not match the configured channel mapping.");
        }

        var clearingAccount = await dbContext.ChartAccounts.SingleAsync(
            account => account.Id == input.ClearingAccountId
                && account.BusinessId == businessId
                && account.IsActive
                && account.AccountType == ChartAccountTypes.Asset,
            cancellationToken);
        var bank = await dbContext.BankAccounts.SingleOrDefaultAsync(
            account => account.BusinessId == businessId
                && account.Id == input.BankAccountId
                && account.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Bank account not found in this business.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var eligibleClearingAmount = await dbContext.JournalEntryLines
                .Where(line => line.JournalEntry.BusinessId == businessId
                    && line.JournalEntry.Status == JournalEntryStatuses.Posted
                    && line.AccountId == clearingAccount.Id)
                .SumAsync(line => line.DebitAmount - line.CreditAmount, cancellationToken);
            if (input.GrossClearingAmount > eligibleClearingAmount)
            {
                throw new InvalidOperationException(
                    $"Settlement exceeds eligible unsettled clearing amount of {eligibleClearingAmount:0.00}.");
            }

            if (input.SettlementFeeAmount > input.GrossClearingAmount)
            {
                throw new InvalidOperationException("Settlement fee cannot exceed gross clearing amount.");
            }

            var net = input.GrossClearingAmount - input.SettlementFeeAmount;
            var feeAccountId = input.SettlementFeeAmount > 0
                ? await accountResolver.ResolveAsync(
                    businessId,
                    "settlement_fee_expense",
                    cancellationToken)
                : (long?)null;
            var now = DateTime.UtcNow;
            var batch = new SettlementBatch
            {
                BusinessId = businessId,
                SettlementType = input.SettlementType.Trim().ToLowerInvariant(),
                ClearingAccountId = clearingAccount.Id,
                BankAccountId = bank.Id,
                SettlementDate = input.SettlementDate.Date,
                GrossClearingAmount = input.GrossClearingAmount,
                SettlementFeeAmount = input.SettlementFeeAmount,
                NetBankAmount = net,
                ExternalReference = NormalizeOptional(input.ExternalReference),
                IdempotencyKey = key,
                Status = SettlementBatchStatuses.Draft,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId
            };
            dbContext.SettlementBatches.Add(batch);
            await dbContext.SaveChangesAsync(cancellationToken);

            var lines = new List<SettlementJournalLineInput>
            {
                new(bank.ChartAccountId, "Settlement bank receipt", net, 0),
                new(clearingAccount.Id, "Clearing settlement", 0, input.GrossClearingAmount)
            };
            if (feeAccountId.HasValue)
            {
                lines.Insert(
                    1,
                    new SettlementJournalLineInput(
                        feeAccountId.Value,
                        "Settlement fee",
                        input.SettlementFeeAmount,
                        0));
            }

            var journalId = await journalPoster.CreateOrGetAndPostAsync(
                businessId,
                actorUserId,
                new SettlementJournalInput(
                    "BankSettlement",
                    input.SettlementDate.Date,
                    input.SettlementDate.Date,
                    input.SettlementDate.Date,
                    "SettlementBatch",
                    batch.Id.ToString(),
                    batch.ExternalReference,
                    $"{batch.SettlementType} settlement",
                    $"settlement:{businessId}:{key}",
                    lines),
                cancellationToken);

            batch.JournalEntryId = journalId;
            batch.Status = SettlementBatchStatuses.Posted;
            batch.UpdatedAt = DateTime.UtcNow;
            batch.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(batch);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<LiquidityTransferReadModel> PostTransferAsync(
        long businessId,
        long actorUserId,
        LiquidityTransferPostingInput input,
        CancellationToken cancellationToken)
    {
        var key = NormalizeKey(input.IdempotencyKey);
        var existing = await dbContext.LiquidityTransfers
            .SingleOrDefaultAsync(
                transfer => transfer.BusinessId == businessId
                    && transfer.IdempotencyKey == key,
                cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        if (input.Amount <= 0)
        {
            throw new InvalidOperationException("Transfer amount must be greater than zero.");
        }

        var cashAccountId = await accountResolver.ResolveAsync(
            businessId,
            "cash",
            cancellationToken);
        var source = await ResolveTransferAccountAsync(
            businessId,
            input.TransferType,
            input.SourceBankAccountId,
            cashAccountId,
            source: true,
            cancellationToken);
        var destination = await ResolveTransferAccountAsync(
            businessId,
            input.TransferType,
            input.DestinationBankAccountId,
            cashAccountId,
            source: false,
            cancellationToken);
        if (source.AccountId == destination.AccountId)
        {
            throw new InvalidOperationException(
                "Transfer source and destination accounts must differ.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var transfer = new LiquidityTransfer
            {
                BusinessId = businessId,
                TransferType = input.TransferType.Trim().ToLowerInvariant(),
                SourceAccountId = source.AccountId,
                DestinationAccountId = destination.AccountId,
                SourceBankAccountId = source.BankId,
                DestinationBankAccountId = destination.BankId,
                Amount = input.Amount,
                PostingDate = input.PostingDate.Date,
                Reference = NormalizeOptional(input.Reference),
                IdempotencyKey = key,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId
            };
            dbContext.LiquidityTransfers.Add(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);

            var journalId = await journalPoster.CreateOrGetAndPostAsync(
                businessId,
                actorUserId,
                new SettlementJournalInput(
                    "LiquidityTransfer",
                    input.PostingDate.Date,
                    input.PostingDate.Date,
                    input.PostingDate.Date,
                    "LiquidityTransfer",
                    transfer.Id.ToString(),
                    transfer.Reference,
                    transfer.TransferType,
                    $"transfer:{businessId}:{key}",
                    [
                        new SettlementJournalLineInput(
                            destination.AccountId,
                            "Transfer destination",
                            input.Amount,
                            0),
                        new SettlementJournalLineInput(
                            source.AccountId,
                            "Transfer source",
                            0,
                            input.Amount)
                    ]),
                cancellationToken);
            transfer.JournalEntryId = journalId;
            transfer.Status = LiquidityTransferStatuses.Posted;
            transfer.UpdatedAt = DateTime.UtcNow;
            transfer.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(transfer);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<SettlementBatchReadModel> ReverseSettlementAsync(
        long businessId,
        long actorUserId,
        long settlementId,
        SettlementReversalInput input,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.SettlementBatches.SingleOrDefaultAsync(
            settlement => settlement.BusinessId == businessId && settlement.Id == settlementId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Settlement batch not found.");
        if (batch.Status == SettlementBatchStatuses.Reversed)
        {
            return Map(batch);
        }

        if (batch.Status != SettlementBatchStatuses.Posted || !batch.JournalEntryId.HasValue)
        {
            throw new InvalidOperationException("Only a posted settlement can be reversed.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var reason = input.Reason.Trim();
            var postingDate = input.PostingDate.Date;
            var reversalJournalId = await journalReverser.ReverseAsync(
                businessId,
                actorUserId,
                batch.JournalEntryId.Value,
                new SettlementJournalReversalInput(reason, postingDate, postingDate),
                cancellationToken);

            var now = DateTime.UtcNow;
            batch.ReversalJournalEntryId = reversalJournalId;
            batch.ReversedAt = now;
            batch.ReversedBy = actorUserId;
            batch.ReversalReason = reason;
            batch.Status = SettlementBatchStatuses.Reversed;
            batch.UpdatedAt = now;
            batch.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(batch);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<LiquidityTransferReadModel> ReverseTransferAsync(
        long businessId,
        long actorUserId,
        long transferId,
        LiquidityTransferReversalInput input,
        CancellationToken cancellationToken)
    {
        var transfer = await dbContext.LiquidityTransfers.SingleOrDefaultAsync(
            value => value.BusinessId == businessId && value.Id == transferId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Liquidity transfer not found.");
        if (transfer.Status == LiquidityTransferStatuses.Reversed)
        {
            return Map(transfer);
        }

        if (transfer.Status != LiquidityTransferStatuses.Posted
            || !transfer.JournalEntryId.HasValue)
        {
            throw new InvalidOperationException("Only a posted transfer can be reversed.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var reason = input.Reason.Trim();
            var postingDate = input.PostingDate.Date;
            var reversalJournalId = await journalReverser.ReverseAsync(
                businessId,
                actorUserId,
                transfer.JournalEntryId.Value,
                new SettlementJournalReversalInput(reason, postingDate, postingDate),
                cancellationToken);

            var now = DateTime.UtcNow;
            transfer.ReversalJournalEntryId = reversalJournalId;
            transfer.ReversedAt = now;
            transfer.ReversedBy = actorUserId;
            transfer.ReversalReason = reason;
            transfer.Status = LiquidityTransferStatuses.Reversed;
            transfer.UpdatedAt = now;
            transfer.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(transfer);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void ValidateSettlementAmounts(SettlementPostingInput input)
    {
        if (input.GrossClearingAmount <= 0)
        {
            throw new InvalidOperationException("Gross settlement amount must be greater than zero.");
        }

        if (input.SettlementFeeAmount < 0)
        {
            throw new InvalidOperationException("Settlement fee cannot be negative.");
        }
    }

    private static string NormalizeKey(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("Idempotency key is required.")
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<(long AccountId, long? BankId)> ResolveTransferAccountAsync(
        long businessId,
        string type,
        long? bankId,
        long cashAccountId,
        bool source,
        CancellationToken cancellationToken)
    {
        var needsBank = source
            ? type != LiquidityTransferTypes.CashToBank
            : type != LiquidityTransferTypes.BankToCash;
        if (!needsBank)
        {
            return (cashAccountId, null);
        }

        if (!bankId.HasValue)
        {
            throw new InvalidOperationException(
                "BankAccount is required for this transfer direction.");
        }

        var bank = await dbContext.BankAccounts.SingleOrDefaultAsync(
            account => account.BusinessId == businessId
                && account.Id == bankId.Value
                && account.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Bank account not found in this business.");
        return (bank.ChartAccountId, bank.Id);
    }

    private static SettlementBatchReadModel Map(SettlementBatch batch) =>
        new(
            batch.Id,
            batch.BusinessId,
            batch.SettlementType,
            batch.ClearingAccountId,
            batch.BankAccountId,
            batch.SettlementDate,
            batch.GrossClearingAmount,
            batch.SettlementFeeAmount,
            batch.NetBankAmount,
            batch.ExternalReference,
            batch.IdempotencyKey,
            batch.Status,
            batch.JournalEntryId,
            batch.ReversalJournalEntryId);

    private static LiquidityTransferReadModel Map(LiquidityTransfer transfer) =>
        new(
            transfer.Id,
            transfer.BusinessId,
            transfer.TransferType,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.SourceBankAccountId,
            transfer.DestinationBankAccountId,
            transfer.Amount,
            transfer.PostingDate,
            transfer.Reference,
            transfer.IdempotencyKey,
            transfer.Status,
            transfer.JournalEntryId,
            transfer.ReversalJournalEntryId);
}
