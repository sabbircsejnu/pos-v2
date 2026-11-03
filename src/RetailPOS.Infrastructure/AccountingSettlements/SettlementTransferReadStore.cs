using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingSettlements;

public sealed class SettlementTransferReadStore(RetailPOSDbContext dbContext)
    : ISettlementTransferReadStore
{
    public async Task<IReadOnlyList<SettlementBatchReadModel>> ListSettlementsAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.SettlementBatches
            .AsNoTracking()
            .Where(batch => batch.BusinessId == businessId)
            .OrderByDescending(batch => batch.Id)
            .Select(batch => new SettlementBatchReadModel(
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
                batch.ReversalJournalEntryId))
            .ToListAsync(cancellationToken);

    public Task<SettlementBatchReadModel?> FindSettlementAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.SettlementBatches
            .AsNoTracking()
            .Where(batch => batch.BusinessId == businessId && batch.Id == id)
            .Select(batch => new SettlementBatchReadModel(
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
                batch.ReversalJournalEntryId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LiquidityTransferReadModel>> ListTransfersAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.LiquidityTransfers
            .AsNoTracking()
            .Where(transfer => transfer.BusinessId == businessId)
            .OrderByDescending(transfer => transfer.Id)
            .Select(transfer => new LiquidityTransferReadModel(
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
                transfer.ReversalJournalEntryId))
            .ToListAsync(cancellationToken);

    public Task<LiquidityTransferReadModel?> FindTransferAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken) =>
        dbContext.LiquidityTransfers
            .AsNoTracking()
            .Where(transfer => transfer.BusinessId == businessId && transfer.Id == id)
            .Select(transfer => new LiquidityTransferReadModel(
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
                transfer.ReversalJournalEntryId))
            .SingleOrDefaultAsync(cancellationToken);
}
