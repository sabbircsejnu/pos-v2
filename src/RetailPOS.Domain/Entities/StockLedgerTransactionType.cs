// =====================================================================
// NEW — String constants for stock ledger transaction / reference types.
// Follows the project convention of using string literals instead of enums.
// =====================================================================
namespace RetailPOS.Core.Entities;

/// <summary>
/// Identifies the business event that triggered a stock movement.
/// </summary>
public static class StockLedgerTransactionType
{
    /// <summary>Goods received via a GRN (inbound from supplier).</summary>
    public const string Grn = "grn";

    /// <summary>Stock sold to a customer.</summary>
    public const string Sale = "sale";

    /// <summary>Stock returned to shelf — covers both void and refund flows.</summary>
    public const string Return = "return";

    /// <summary>Stock exchanged (reserved for future exchange flow).</summary>
    public const string Exchange = "exchange";

    /// <summary>The compensating movements of a reversed product exchange (EX-14).</summary>
    public const string ExchangeReversal = "exchange_reversal";

    /// <summary>Manual stock adjustment (damage, loss, count correction, etc.).</summary>
    public const string Adjustment = "adjustment";

    /// <summary>Stock received at the destination of an inter-location transfer.</summary>
    public const string TransferIn = "transfer_in";

    /// <summary>Stock dispatched from the source of an inter-location transfer.</summary>
    public const string TransferOut = "transfer_out";

    /// <summary>Stock dispatched as part of a return transfer.</summary>
    public const string ReturnTransferOut = "return_transfer_out";

    /// <summary>Stock received as part of a return transfer.</summary>
    public const string ReturnTransferIn = "return_transfer_in";

    /// <summary>Transfer was rejected at destination during receiving.</summary>
    public const string TransferRejection = "transfer_rejection";

    /// <summary>Evidence that units were recorded missing or damaged when a transfer was received; moves no stock.</summary>
    public const string TransferDiscrepancy = "transfer_discrepancy";

    /// <summary>Approved correction of an earlier movement's cost; moves no stock. Its reference is the movement corrected.</summary>
    public const string CostCorrection = "cost_correction";
}

/// <summary>
/// Identifies the source document that produced a ledger row.
/// </summary>
public static class StockLedgerReferenceType
{
    public const string Grn = "grn";
    public const string Sale = "sale";
    public const string StockAdjustment = "stock_adjustment";
    public const string StockTransfer = "stock_transfer";
    public const string StockCostCorrection = "stock_cost_correction";
    /// <summary>A product exchange (both its return-in and replacement-out movements).</summary>
    public const string Exchange = "exchange";
    /// <summary>The compensating movements of a reversed product exchange.</summary>
    public const string ExchangeReversal = "exchange_reversal";
}

/// <summary>Stock ledger values added by UOM v1.</summary>
public static class UomStockLedgerTypes
{
    /// <summary>Packaging physically consumed by a repack job; its value moves into prepared stock.</summary>
    public const string RepackConsumption = "repack_consumption";

    /// <summary>Packaging restored because a repack job was reversed.</summary>
    public const string RepackReversal = "repack_reversal";

    public const string RepackReference = "repack_job";

    /// <summary>Packaging back in stock because prepared packs were opened; it carries their capitalized value.</summary>
    public const string PreparedOpen = "prepared_open";

    /// <summary>Reference type of a prepared-pack opening. The reference id is the opened item's variant.</summary>
    public const string PreparedOpenReference = "prepared_open";
}
