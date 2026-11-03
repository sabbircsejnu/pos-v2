namespace RetailPOS.Core.Entities;

public class Inventory
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty; // 'outlet' or 'warehouse'
    public int Quantity { get; set; } = 0;
    public int LowStockThreshold { get; set; } = 10;
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// PostgreSQL <c>xmin</c> system column used as an optimistic concurrency token.
    /// EF Core / Npgsql reads this automatically on every SELECT and includes it in the
    /// WHERE clause of every UPDATE, so a concurrent modification throws
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> instead of
    /// silently overwriting the other writer's change.
    /// No DDL migration is required — <c>xmin</c> is present on every PostgreSQL row.
    /// </summary>
    public uint XMin { get; set; }

    // Navigation properties
    public virtual ProductVariant Variant { get; set; } = null!;
}
