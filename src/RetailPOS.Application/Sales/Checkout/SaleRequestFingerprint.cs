using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RetailPOS.Application.Sales.Commands;

namespace RetailPOS.Application.Sales.Checkout;

/// <summary>
/// Canonical hash of the client-controlled parts of a checkout request. The requested sale date is excluded
/// because the API stamps it server-side for users without backdating rights.
/// </summary>
public static class SaleRequestFingerprint
{
    public static string Compute(CreateSaleCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = new StringBuilder();
        Append(canonical, "v1");
        Append(canonical, request.OutletId);
        Append(canonical, request.TerminalId);
        Append(canonical, request.CustomerId);
        Append(canonical, request.CashierId);
        Append(canonical, request.OriginalSaleId);
        Append(canonical, request.Discount);
        Append(canonical, request.Tax);
        Append(canonical, request.PaymentMethod?.Trim().ToLowerInvariant());
        Append(canonical, request.CashTendered);
        foreach (var item in request.Items)
        {
            Append(canonical, "item");
            Append(canonical, item.VariantId);
            Append(canonical, item.Quantity);
            Append(canonical, item.UnitPrice);
            Append(canonical, item.DiscountAmount);
        }

        foreach (var payment in request.Payments)
        {
            Append(canonical, "payment");
            Append(canonical, payment.Method?.Trim().ToLowerInvariant());
            Append(canonical, payment.Amount);
            Append(canonical, payment.Tendered);
        }

        // Appended only when supplied, so the fingerprint of a request without a salesperson is unchanged.
        if (request.SalespersonUserId.HasValue)
        {
            Append(canonical, "salesperson");
            Append(canonical, request.SalespersonUserId);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, object? value)
    {
        builder.Append(value switch
        {
            null => "~",
            decimal amount => amount.ToString("0.00##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        });
        builder.Append('|');
    }
}
