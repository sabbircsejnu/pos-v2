namespace RetailPOS.Core.Entities;

public class PurchaseOrder
{
    public long Id { get; set; }
    /// <summary>Human-readable reference, e.g. PO-20260607-0001. Auto-generated on creation.</summary>
    public string PoNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    /// <summary>Destination warehouse. Exactly one of <see cref="WarehouseId"/> and <see cref="OutletId"/> is set.</summary>
    public long? WarehouseId { get; set; }
    /// <summary>Destination outlet. Exactly one of <see cref="WarehouseId"/> and <see cref="OutletId"/> is set.</summary>
    public long? OutletId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    /// <summary>Client-supplied idempotency key (UUID) for immediate purchase-receive de-duplication.</summary>
    public string? IdempotencyKey { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>draft | pending | sent_back | approved | partially_received | fully_received | completed | cancelled | rejected</summary>
    public string Status { get; set; } = "draft";
    /// <summary>Optional notes for this purchase order.</summary>
    public string? Notes { get; set; }
    /// <summary>Reason provided when the PO is rejected or sent back for correction.</summary>
    public string? RejectionReason { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual Warehouse? Warehouse { get; set; }
    public virtual Outlet? Outlet { get; set; }
    public virtual User? Creator { get; set; }
    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public virtual ICollection<Grn> Grns { get; set; } = new List<Grn>();
    public virtual ICollection<Bill> Bills { get; set; } = new List<Bill>();

    // Derived destination helpers (not mapped; usable only in memory, not inside EF queries).
    public string LocationType => OutletId.HasValue
        ? PurchaseLocationTypes.Outlet
        : PurchaseLocationTypes.Warehouse;

    public long LocationId => OutletId ?? WarehouseId ?? 0;

    public string LocationName => OutletId.HasValue
        ? Outlet?.Name ?? string.Empty
        : Warehouse?.Name ?? string.Empty;

    /// <summary>Business of the destination location (requires the matching navigation to be loaded).</summary>
    public long? LocationBusinessId => OutletId.HasValue
        ? Outlet?.BusinessId
        : Warehouse?.BusinessId;

    public void SetDestination(string locationType, long locationId)
    {
        if (locationId <= 0)
            throw new InvalidOperationException("A destination location is required.");

        switch (PurchaseLocationTypes.Normalize(locationType))
        {
            case PurchaseLocationTypes.Outlet:
                OutletId = locationId;
                WarehouseId = null;
                Warehouse = null;
                if (Outlet is not null && Outlet.Id != locationId)
                    Outlet = null;
                break;
            default:
                WarehouseId = locationId;
                OutletId = null;
                Outlet = null;
                if (Warehouse is not null && Warehouse.Id != locationId)
                    Warehouse = null;
                break;
        }
    }
}

public static class PurchaseLocationTypes
{
    public const string Outlet = "outlet";
    public const string Warehouse = "warehouse";

    public static string Normalize(string? locationType)
    {
        var normalized = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Outlet => Outlet,
            Warehouse => Warehouse,
            _ => throw new InvalidOperationException(
                $"Invalid location type '{locationType}'. Expected 'outlet' or 'warehouse'.")
        };
    }
}
