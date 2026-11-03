namespace RetailPOS.Application.AccountingSettlements.ReadModels;

public sealed record SettlementBatchReadModel(
    long Id,
    long BusinessId,
    string SettlementType,
    long ClearingAccountId,
    long BankAccountId,
    DateTime SettlementDate,
    decimal GrossClearingAmount,
    decimal SettlementFeeAmount,
    decimal NetBankAmount,
    string? ExternalReference,
    string IdempotencyKey,
    string Status,
    long? JournalEntryId,
    long? ReversalJournalEntryId);

public sealed record LiquidityTransferReadModel(
    long Id,
    long BusinessId,
    string TransferType,
    long SourceAccountId,
    long DestinationAccountId,
    long? SourceBankAccountId,
    long? DestinationBankAccountId,
    decimal Amount,
    DateTime PostingDate,
    string? Reference,
    string IdempotencyKey,
    string Status,
    long? JournalEntryId,
    long? ReversalJournalEntryId);
