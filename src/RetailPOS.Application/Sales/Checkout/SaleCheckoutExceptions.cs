namespace RetailPOS.Application.Sales.Checkout;

public static class SaleCheckoutErrorCodes
{
    public const string PriceChanged = "SALE_PRICE_CHANGED";
    public const string TaxMismatch = "SALE_TAX_MISMATCH";
    public const string IdempotencyPayloadMismatch = "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string DiscountConflict = "SALE_DISCOUNT_CONFLICT";
    public const string InvalidDiscount = "SALE_INVALID_DISCOUNT";
    public const string InvalidQuantity = "SALE_INVALID_QUANTITY";
    public const string InvalidPayment = "SALE_INVALID_PAYMENT";
    public const string PaymentMismatch = "SALE_PAYMENT_MISMATCH";
    public const string VariantNotSellable = "SALE_VARIANT_NOT_SELLABLE";
    public const string InvalidExchangeLink = "SALE_INVALID_EXCHANGE_LINK";
    public const string DiscountNotPermitted = "SALE_DISCOUNT_NOT_PERMITTED";
    public const string DiscountLimitExceeded = "SALE_DISCOUNT_LIMIT_EXCEEDED";
    public const string TerminalRequired = "SALE_TERMINAL_REQUIRED";
    public const string NoActiveTerminal = "SALE_NO_ACTIVE_TERMINAL";
    public const string OutletInvoicePrefixMissing = "SALE_OUTLET_INVOICE_PREFIX_MISSING";
    public const string SalespersonNotEligible = "SALE_SALESPERSON_NOT_ELIGIBLE";
    /// <summary>A sale with any returned quantity (refund or exchange) can no longer be voided (Exchange D-14).</summary>
    public const string SaleHasReturns = "SALE_HAS_RETURNS";
    /// <summary>New exchange links through ordinary checkout are no longer accepted (Exchange D-01).</summary>
    public const string ExchangeLinkDeprecated = "SALE_EXCHANGE_LINK_DEPRECATED";
}

/// <summary>A checkout request the caller is not allowed to make; maps to HTTP 403 with a stable code.</summary>
public sealed class SaleCheckoutForbiddenException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>A rejected checkout request that maps to HTTP 400 and carries a stable machine-readable code.</summary>
public sealed class SaleCheckoutValidationException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>A checkout request that conflicts with server state; maps to HTTP 409.</summary>
public sealed class SaleCheckoutConflictException(string code, string message, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public sealed record SalePriceChange(long VariantId, decimal SubmittedUnitPrice, decimal CurrentUnitPrice);
