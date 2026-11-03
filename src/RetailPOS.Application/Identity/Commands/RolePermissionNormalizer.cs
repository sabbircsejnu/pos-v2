namespace RetailPOS.Application.Identity.Commands;

internal static class RolePermissionNormalizer
{
    private static readonly IReadOnlyDictionary<string, string[]> LegacyPermissionAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["inventory.adjust"] =
            [
                "stock_adjustments.view",
                "stock_adjustments.create",
                "stock_adjustments.edit",
                "stock_adjustments.delete",
                "stock_adjustments.approve",
                "stock_adjustments.reject",
                "stock_adjustments.cancel"
            ],
            ["inventory.transfer"] =
            [
                "stock_transfers.view",
                "stock_transfers.view_all_locations",
                "stock_transfers.create",
                "stock_transfers.edit",
                "stock_transfers.delete",
                "stock_transfers.approve",
                "stock_transfers.cancel",
                "stock_transfers.dispatch",
                "stock_transfers.receive",
                "stock_transfers.reject_receive",
                "stock_transfers.return_create",
                "stock_transfers.transfer_from_any_location",
                "stock_requisitions.view",
                "stock_requisitions.create",
                "stock_requisitions.edit",
                "stock_requisitions.approve",
                "stock_requisitions.reject",
                "stock_requisitions.convert_to_transfer"
            ]
        };

    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? permissions)
    {
        if (permissions is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            var key = permission.Trim();
            if (key == "*")
            {
                return ["*"];
            }

            var values = LegacyPermissionAliases.TryGetValue(key, out var aliases)
                ? aliases
                : [key];
            foreach (var value in values)
            {
                if (seen.Add(value))
                {
                    normalized.Add(value);
                }
            }
        }

        return normalized;
    }
}
