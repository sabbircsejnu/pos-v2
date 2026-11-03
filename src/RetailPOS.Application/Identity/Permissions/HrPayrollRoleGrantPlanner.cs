using System.Text.Json;

namespace RetailPOS.Application.Identity.Permissions;

/// <summary>Plans only approved additive changes; persistence, concurrency and audit belong to the upgrade command.</summary>
public static class HrPayrollRoleGrantPlanner
{
    public static string MergeApprovedGrants(string currentJson, IReadOnlyCollection<string> approvedGrants)
    {
        ArgumentNullException.ThrowIfNull(approvedGrants);
        var current = JsonSerializer.Deserialize<string[]>(currentJson)
            ?? throw new JsonException("Role permissions must be a JSON array.");
        if (current.Any(string.IsNullOrWhiteSpace))
            throw new JsonException("Role permissions cannot contain null or blank entries.");
        if (approvedGrants.Any(grant => !HrPayrollPermissionCatalog.All.Contains(grant, StringComparer.Ordinal)))
            throw new ArgumentException("Only approved HR/Payroll catalogue codes may be added.", nameof(approvedGrants));

        return JsonSerializer.Serialize(current.Concat(approvedGrants)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
