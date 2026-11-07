using ClosedXML.Excel;

namespace RetailPOS.API.Services;

/// <summary>
/// Leaves the cost and value columns out of an inventory report export for a caller without
/// <c>products.view_cost</c> (U-13). With <c>includeCost</c> true nothing changes, so authorized
/// users keep the exact output they had before.
/// </summary>
internal static class ReportExportCostColumns
{
    /// <summary>The values without the given zero-based cost columns, or all of them when cost is included.</summary>
    public static IEnumerable<T> Without<T>(IEnumerable<T> values, bool includeCost, params int[] costColumns) =>
        includeCost ? values : values.Where((_, index) => Array.IndexOf(costColumns, index) < 0);

    /// <summary>Deletes the given one-based columns, highest first so the others keep their place.</summary>
    public static void DeleteColumns(IXLWorksheet sheet, bool includeCost, params int[] columns)
    {
        if (includeCost)
            return;
        foreach (var column in columns.OrderByDescending(column => column))
            sheet.Column(column).Delete();
    }
}
