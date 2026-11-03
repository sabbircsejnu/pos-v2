namespace RetailPOS.Core.Entities;

/// <summary>
/// Represents the publication/workflow status of a product.
/// </summary>
public enum ProductStatus
{
    /// <summary>
    /// Product is live and available in POS, purchase orders, and all searches.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Product is disabled. Hidden from POS; not selectable in purchase orders.
    /// </summary>
    Inactive = 2,

    /// <summary>
    /// Product is being prepared and has not been published yet.
    /// Not visible in POS or purchase orders.
    /// </summary>
    Draft = 3
}
