// =====================================================================
// NEW — Stock Ledger entity
// Every change to inventory balance is recorded here for full traceability.
// =====================================================================
namespace RetailPOS.Core.Entities;

public class StockLedger
{
    public long Id { get; set; }

    /// <summary>The product variant whose balance changed.</summary>
    public long VariantId { get; set; }

    /// <summary>Physical location where the stock sits (outlet or warehouse ID).</summary>
    public long LocationId { get; set; }

    /// <summary>"outlet" or "warehouse" — mirrors the Inventory.LocationType pattern.</summary>
    public string LocationType { get; set; } = string.Empty;

    /// <summary>
    /// What caused the movement.
    /// See <see cref="StockLedgerTransactionType"/> for valid values.
    /// </summary>
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>Units added (0 when this row is a pure deduction).</summary>
    public int QtyIn { get; set; }

    /// <summary>Units removed (0 when this row is a pure addition).</summary>
    public int QtyOut { get; set; }

    /// <summary>Inventory balance for this variant+location immediately after this movement.</summary>
    public int BalanceAfter { get; set; }

    /// <summary>Unit cost used when this movement was recorded.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Monetary value of the movement using the recorded unit cost.</summary>
    public decimal MovementValue { get; set; }

    /// <summary>True when the movement carries a reliable cost basis that can be replayed for valuation.</summary>
    public bool IsCostValuationReliable { get; set; }

    /// <summary>Business date associated with this movement.</summary>
    public DateTime BusinessDate { get; set; } = DateTime.UtcNow.Date;

    /// <summary>Deterministic sequence number for ordering movements within a variant/location.</summary>
    public long MovementSequence { get; set; }

    /// <summary>Links this movement to the original movement it reverses.</summary>
    public long? ReversalOfMovementId { get; set; }

    /// <summary>
    /// Source document type.
    /// See <see cref="StockLedgerReferenceType"/> for valid values.
    /// </summary>
    public string ReferenceType { get; set; } = string.Empty;

    /// <summary>Primary key of the source document (Grn.Id, Sale.Id, etc.).</summary>
    public long ReferenceId { get; set; }

    /// <summary>Optional human-readable note (e.g. adjustment reason, PO number).</summary>
    public string? Remarks { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual User? Creator { get; set; }
}
