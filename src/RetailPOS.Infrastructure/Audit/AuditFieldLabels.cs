namespace RetailPOS.Infrastructure.Audit;

/// <summary>
/// Maps raw EF property names to human-readable display labels for the audit log UI.
/// </summary>
public static class AuditFieldLabels
{
    private static readonly Dictionary<string, string> _labels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Common reference fields
            ["SupplierId"]         = "Supplier",
            ["CustomerId"]         = "Customer",
            ["WarehouseId"]        = "Warehouse",
            ["OutletId"]           = "Outlet",
            ["ProductId"]          = "Product",
            ["VariantId"]          = "Variant",
            ["ProductVariantId"]   = "Variant",
            ["CategoryId"]         = "Category",
            ["RoleId"]             = "Role",
            ["UserId"]             = "User",
            ["PurchaseOrderId"]    = "Purchase Order",
            ["SaleId"]             = "Sale",
            ["PaymentId"]          = "Payment",
            ["ApprovedByUserId"]   = "Approved By",
            ["CreatedByUserId"]    = "Created By",

            // Audit / user fields
            ["CreatedBy"]          = "Created By",
            ["UpdatedBy"]          = "Updated By",
            ["DeletedBy"]          = "Deleted By",
            ["CreatedAt"]          = "Created At",
            ["UpdatedAt"]          = "Updated At",

            // Common scalar fields
            ["TotalAmount"]        = "Total Amount",
            ["UnitPrice"]          = "Unit Price",
            ["CostPrice"]          = "Cost Price",
            ["SalePrice"]          = "Sale Price",
            ["SellingPrice"]       = "Selling Price",
            ["PurchasePrice"]      = "Purchase Price",
            ["DiscountAmount"]     = "Discount Amount",
            ["DiscountPercent"]    = "Discount %",
            ["TaxAmount"]          = "Tax Amount",
            ["TaxRate"]            = "Tax Rate",
            ["StockQuantity"]      = "Stock Quantity",
            ["ReorderLevel"]       = "Reorder Level",
            ["MinStockLevel"]      = "Min Stock Level",
            ["MaxStockLevel"]      = "Max Stock Level",
            ["OpeningBalance"]     = "Opening Balance",
            ["ClosingBalance"]     = "Closing Balance",
            ["CreditLimit"]        = "Credit Limit",
            ["IsActive"]           = "Active",
            ["IsDefault"]          = "Default",
            ["IsFeatured"]         = "Featured",
            ["SaleNumber"]         = "Sale Number",
            ["OrderDate"]          = "Order Date",
            ["ExpectedDelivery"]   = "Expected Delivery",
            ["PaymentStatus"]      = "Payment Status",
            ["PaymentMethod"]      = "Payment Method",
            ["PasswordHash"]       = "Password",
            ["PhoneNumber"]        = "Phone Number",
            ["ContactNumber"]      = "Contact Number",
            ["EmailAddress"]       = "Email",
            ["StreetAddress"]      = "Street Address",
        };

    /// <summary>
    /// Returns a human-readable label for a field name, or null if no mapping is defined
    /// (callers should fall back to a prettified version of the raw name).
    /// </summary>
    public static string? GetLabel(string fieldName) =>
        _labels.TryGetValue(fieldName, out var label) ? label : null;
}
