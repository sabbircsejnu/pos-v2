using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleCashSessionValidator
{
    /// <summary>Locks the terminal's open session <c>FOR SHARE</c> in the sale transaction and validates status and cutoff.</summary>
    Task<CashSession> ValidateActiveForSaleAsync(long outletId, long terminalId, long? actorUserId, CancellationToken cancellationToken);

    /// <summary>The last eligibility check, run immediately before the sale transaction commits (D4, no bypass).</summary>
    Task ValidateFinalGateAsync(CashSession session, CancellationToken cancellationToken);
}

/// <summary>An invoice number issued from the D13 sequence, with its structured parts.</summary>
public sealed record AllocatedInvoiceNumber(
    string InvoiceNumber,
    string Prefix,
    long Sequence,
    DateTime BusinessDate,
    DateTime IssuedAtUtc)
{
    /// <summary>{OutletPrefix}{BusinessDate:yyMMdd}{DailySequence} - no separators and no padding (D13).</summary>
    public static string Render(string prefix, DateTime businessDate, long sequence) =>
        $"{prefix}{businessDate:yyMMdd}{sequence}";
}

public interface ISaleInvoiceNumberAllocator
{
    /// <summary>
    /// Takes the next sequence for (Business, Outlet, BusinessDate). It must run inside the sale's database transaction
    /// so a rolled-back sale never consumes a number and two concurrent sales never receive the same one.
    /// </summary>
    Task<AllocatedInvoiceNumber> AllocateAsync(
        long businessId,
        long outletId,
        DateTime businessDate,
        CancellationToken cancellationToken);
}

public interface ISaleCreationAuditWriter
{
    Task RecordCreatedAsync(Sale sale, long? journalEntryId, CancellationToken cancellationToken);

    Task RecordManualDiscountAsync(
        Sale sale,
        RetailPOS.Application.Sales.Checkout.ManualDiscountEvidence evidence,
        CancellationToken cancellationToken);
}

/// <summary>Supplies the tax and discount policy the server applies to every checkout; the client never decides them.</summary>
public interface ISaleCheckoutSettingsProvider
{
    Task<RetailPOS.Application.Sales.Checkout.SaleTaxPolicy> GetTaxPolicyAsync(long businessId, CancellationToken cancellationToken);

    Task<RetailPOS.Application.Sales.Checkout.SaleDiscountPolicy> GetDiscountPolicyAsync(long businessId, CancellationToken cancellationToken);
}

public interface ISaleStockLedgerWriter
{
    void AppendSaleMovement(
        long variantId,
        long outletId,
        decimal quantity,
        decimal balanceAfter,
        long saleId,
        long cashierId,
        DateTime saleDate,
        decimal unitCost);
}

public interface ISaleStockCacheInvalidator
{
    Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken);
}

public interface ISaleCompletedPublisher
{
    Task PublishAsync(SaleCompletedNotice sale, CancellationToken cancellationToken);
}

public interface ISaleCreationLogger
{
    void DuplicateRequest(string idempotencyKey, long saleId);
    void SaleCreated(string saleNumber, long saleId, long outletId);
    void WalkInCustomerCreated();
    void WalkInCustomerRepaired();
}

public sealed record SaleCompletedNotice(
    long SaleId,
    string SaleNumber,
    long OutletId,
    long CashierId,
    long? CustomerId,
    decimal TotalAmount,
    DateTime CompletedAt,
    int LoyaltyPointsAwarded);
