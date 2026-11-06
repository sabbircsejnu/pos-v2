using System.Globalization;

namespace RetailPOS.Application.Expenses;

/// <summary>YYYYMMDD-LOCATIONPREFIX-NNNN, dated by the creation business date rather than the expense date.</summary>
public static class ExpenseNumberFormat
{
    public const string BusinessLevelPrefix = "BIZ";

    public static string PrefixFor(long? outletId, string? outletInvoicePrefix, long? warehouseId)
    {
        if (outletId.HasValue)
        {
            return string.IsNullOrWhiteSpace(outletInvoicePrefix)
                ? $"OUT{outletId.Value}"
                : outletInvoicePrefix.Trim().ToUpperInvariant();
        }

        return warehouseId.HasValue ? $"WH{warehouseId.Value}" : BusinessLevelPrefix;
    }

    public static string ScopeKey(DateTime businessDate, string prefix) =>
        $"{businessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}:{prefix}";

    public static string Format(DateTime businessDate, string prefix, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{businessDate:yyyyMMdd}-{prefix}-{sequence:D4}");
}
