namespace RetailPOS.API.DTOs.Pos;

// NEW ─────────────────────────────────────────────────────────────────────────
// Lightweight DTOs designed for the fast POS screen.
// These are intentionally smaller than the full ProductDto / InventoryDto to
// minimise serialisation cost and HTTP payload on every barcode scan.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Combined product + price + stock result returned by GET /api/pos/lookup.
/// One call replaces three separate product / pricing / inventory round-trips.
/// </summary>
public class PosProductLookupDto
{
    public long    VariantId   { get; set; }
    public long    ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public string  VariantName { get; set; } = string.Empty;
    public string? Barcode     { get; set; }
    public string  Sku         { get; set; } = string.Empty;
    public string? ImageUrl    { get; set; }

    // ── Pricing ───────────────────────────────────────────────────────────
    /// <summary>Product.BasePrice + Variant.PriceAdjustment (before rules/overrides).</summary>
    public decimal BasePrice      { get; set; }
    /// <summary>Final per-unit price after outlet override and best campaign rule.</summary>
    public decimal EffectivePrice { get; set; }
    public decimal TaxRate        { get; set; }
    /// <summary>standard | zero_rated | exempt | none.</summary>
    public string  TaxClassification { get; set; } = "standard";
    public bool    PricesIncludeTax  { get; set; }
    /// <summary>Name of the winning discount rule, if any.</summary>
    public string? AppliedRuleName { get; set; }

    // ── Stock (display hint only) ─────────────────────────────────────────
    /// <summary>
    /// Current quantity at the outlet.
    /// ⚠ DISPLAY HINT ONLY. This value may be up to 30 seconds stale when served
    ///   from Redis. Authoritative stock deduction happens inside the Application sale command
    ///   using a DB-level xmin concurrency token — not from this field.
    /// </summary>
    public decimal  StockQty       { get; set; }
    public bool InStock        { get; set; }
    /// <summary>True when StockQty was read from Redis rather than a live DB query.</summary>
    public bool StockFromCache { get; set; }
}

/// <summary>
/// Lightweight stock availability hint returned by GET /api/pos/stock-hint.
/// Used to colour cart rows (green / amber / red) before checkout is pressed.
///
/// ⚠ NEVER use this as the final gate for allowing a sale.
///   Definitive stock validation always runs inside the Application sale command
///   within a DB transaction with an xmin optimistic concurrency check.
/// </summary>
public class PosStockHintDto
{
    public long VariantId    { get; set; }
    public long OutletId     { get; set; }
    public decimal  AvailableQty { get; set; }
    /// <summary>True when AvailableQty >= requested quantity.</summary>
    public bool Sufficient   { get; set; }
    /// <summary>True when the value was read from the Redis cache (≤30 s stale).</summary>
    public bool FromCache    { get; set; }
}

/// <summary>
/// One sellable variant returned by GET /api/pos/catalogue. ListPrice is a display hint only;
/// the cart always re-prices through POST /api/pos/cart-prices. No cost data.
/// </summary>
public class PosCatalogueItemDto
{
    public long    VariantId         { get; set; }
    public long    ProductId         { get; set; }
    public string  ProductName       { get; set; } = string.Empty;
    public string  VariantName       { get; set; } = string.Empty;
    public string? ProductCode       { get; set; }
    public string  Sku               { get; set; } = string.Empty;
    public string? Barcode           { get; set; }
    public string? Attributes        { get; set; }
    public long    CategoryId        { get; set; }
    public string? CategoryName      { get; set; }
    public decimal ListPrice         { get; set; }
    public string? PrimaryImageThumb { get; set; }
    /// <summary>Quantity at the requested (authorized) outlet; null when no outlet was requested.</summary>
    public decimal?    StockQty          { get; set; }
}

/// <summary>Active category returned by GET /api/pos/categories for POS filtering.</summary>
public class PosCategoryDto
{
    public long   Id               { get; set; }
    public string Name             { get; set; } = string.Empty;
    public long?  ParentCategoryId { get; set; }
    public int    DisplayOrder     { get; set; }
}

/// <summary>Customer returned by GET /api/pos/customers/search; deliberately excludes email, loyalty and purchase history.</summary>
public class PosCustomerLookupDto
{
    public long    Id            { get; set; }
    public string  Name          { get; set; } = string.Empty;
    public string? Phone         { get; set; }
}

/// <summary>Body of POST /api/pos/customers. No Business ID, email or loyalty fields: the server owns them.</summary>
public class CreatePosCustomerDto
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Customer name is required")]
    [System.ComponentModel.DataAnnotations.MaxLength(255, ErrorMessage = "Customer name must be 255 characters or fewer")]
    public string Name { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Phone number is required")]
    [System.ComponentModel.DataAnnotations.MaxLength(20, ErrorMessage = "Phone number must be 20 characters or fewer")]
    public string Phone { get; set; } = string.Empty;
}

public class PosTerminalDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
