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
}
