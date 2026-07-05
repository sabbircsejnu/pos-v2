using Microsoft.EntityFrameworkCore;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Audit;

/// <summary>
/// Resolves human-readable display names for audit reference fields at write time.
/// Batches all lookups per entity type to avoid N+1 queries.
/// </summary>
public sealed class AuditReferenceSnapshotResolver
{
    // Maps field name (case-insensitive) → reference entity type label
    private static readonly Dictionary<string, string> FieldToEntityType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CreatedBy"]          = "User",
            ["UpdatedBy"]          = "User",
            ["DeletedBy"]          = "User",
            ["UserId"]             = "User",
            ["ApprovedByUserId"]   = "User",
            ["CreatedByUserId"]    = "User",
            ["SupplierId"]         = "Supplier",
            ["CustomerId"]         = "Customer",
            ["WarehouseId"]        = "Warehouse",
            ["OutletId"]           = "Outlet",
            ["ProductId"]          = "Product",
            ["VariantId"]          = "ProductVariant",
            ["ProductVariantId"]   = "ProductVariant",
            ["CategoryId"]         = "Category",
            ["RoleId"]             = "Role",
            ["PurchaseOrderId"]    = "PurchaseOrder",
            ["SaleId"]             = "Sale",
            ["PaymentId"]          = "Payment",
        };

    /// <summary>
    /// Determines whether a field name is a known reference field.
    /// </summary>
    public static bool IsReferenceField(string fieldName) =>
        FieldToEntityType.ContainsKey(fieldName);

    /// <summary>
    /// Returns the entity type label for a reference field, or null if unknown.
    /// </summary>
    public static string? GetEntityType(string fieldName) =>
        FieldToEntityType.TryGetValue(fieldName, out var t) ? t : null;

    /// <summary>
    /// Result record — one per field change that is a reference field.
    /// </summary>
    public sealed record ResolvedSnapshot(
        string? OldDisplayValue,
        string? NewDisplayValue,
        string ReferenceEntityType
    );

    // -------------------------------------------------------------------------
    // Per-type batch result (resolved once, keyed by raw ID string → display)
    // -------------------------------------------------------------------------
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    /// <summary>
    /// Pre-loads display names for all reference fields from a set of pending
    /// field changes in a single pass. Call once before building AuditEventFieldChange rows.
    /// </summary>
    public async Task PreloadAsync(
        RetailPOSDbContext db,
        IEnumerable<(string FieldName, object? OldValue, object? NewValue)> fields,
        CancellationToken ct = default)
    {
        // Collect IDs to resolve, grouped by entity type
        var toResolve = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);

        foreach (var (fieldName, old, @new) in fields)
        {
            if (!FieldToEntityType.TryGetValue(fieldName, out var entityType))
                continue;

            if (!toResolve.TryGetValue(entityType, out var ids))
                toResolve[entityType] = ids = new HashSet<long>();

            if (TryParseId(old, out var oldId)) ids.Add(oldId);
            if (TryParseId(@new, out var newId)) ids.Add(newId);
        }

        // Batch query each entity type exactly once
        foreach (var (entityType, ids) in toResolve)
        {
            var resolved = await ResolveEntityTypeAsync(db, entityType, ids, ct);
            _cache[entityType] = resolved;
        }
    }

    /// <summary>
    /// Resolves a single field change snapshot using pre-loaded cache.
    /// Returns null if the field is not a reference field.
    /// </summary>
    public ResolvedSnapshot? Resolve(string fieldName, object? oldValue, object? newValue)
    {
        if (!FieldToEntityType.TryGetValue(fieldName, out var entityType))
            return null;

        _cache.TryGetValue(entityType, out var map);

        return new ResolvedSnapshot(
            OldDisplayValue: FormatDisplay(entityType, oldValue, map),
            NewDisplayValue: FormatDisplay(entityType, newValue, map),
            ReferenceEntityType: entityType
        );
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string? FormatDisplay(string entityType, object? rawValue, Dictionary<string, string>? map)
    {
        if (rawValue is null) return null;

        var rawStr = rawValue switch
        {
            string s => s,
            _ => rawValue.ToString()
        };

        if (rawStr is null) return null;

        // Strip JSON quotes if value came in as serialised JSON string "\"42\""
        if (rawStr.StartsWith('"') && rawStr.EndsWith('"') && rawStr.Length > 2)
            rawStr = rawStr[1..^1];

        if (!long.TryParse(rawStr, out var id))
            return null; // not an integer ID — skip

        if (map is not null && map.TryGetValue(rawStr, out var displayName))
            return displayName;

        // Record exists in map keys but name was empty — fallback
        return $"Deleted {entityType} (#{id})";
    }

    private static bool TryParseId(object? value, out long id)
    {
        id = 0;
        if (value is null) return false;

        var str = value switch
        {
            long l  => l.ToString(),
            int i   => i.ToString(),
            string s => s,
            _        => value.ToString()
        };

        if (str is null) return false;
        // Strip JSON quotes
        if (str.StartsWith('"') && str.EndsWith('"') && str.Length > 2)
            str = str[1..^1];

        return long.TryParse(str, out id);
    }

    private static async Task<Dictionary<string, string>> ResolveEntityTypeAsync(
        RetailPOSDbContext db, string entityType, HashSet<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new();

        return entityType switch
        {
            "User" => await db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id.ToString(), u => $"{u.Name} (#{u.Id})", ct),

            "Supplier" => await db.Suppliers.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id.ToString(), s => $"{s.Name} (#{s.Id})", ct),

            "Customer" => await db.Customers.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id.ToString(), c => $"{c.Name ?? "Customer"} (#{c.Id})", ct),

            "Warehouse" => await db.Warehouses.AsNoTracking()
                .Where(w => ids.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id.ToString(), w => $"{w.Name} (#{w.Id})", ct),

            "Outlet" => await db.Outlets.AsNoTracking()
                .Where(o => ids.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id.ToString(), o => $"{o.Name} (#{o.Id})", ct),

            "Product" => await db.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id.ToString(), p => $"{p.Name} (#{p.Id})", ct),

            "ProductVariant" => await db.ProductVariants.AsNoTracking()
                .Where(v => ids.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id.ToString(), v => $"{v.Name} (#{v.Id})", ct),

            "Category" => await db.Categories.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id.ToString(), c => $"{c.Name} (#{c.Id})", ct),

            "Role" => await db.Roles.AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id.ToString(), r => $"{r.Name} (#{r.Id})", ct),

            "PurchaseOrder" => await db.PurchaseOrders.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id.ToString(), p => $"PO #{p.Id}", ct),

            "Sale" => await db.Sales.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id.ToString(), s => $"{s.SaleNumber} (#{s.Id})", ct),

            // Payment → Transaction table
            "Payment" => await db.Transactions.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id.ToString(), t => $"Transaction #{t.Id}", ct),

            _ => new()
        };
    }
}
