using System.Text.Json;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.UnitTests.HR;

public sealed class HrPayrollPermissionTests
{
    [Fact]
    public void Catalogue_IsUniqueAndEveryNewPermissionHasACapability()
    {
        Assert.Equal(PermissionCatalog.All.Length, PermissionCatalog.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(HrPayrollPermissionCatalog.All, permission =>
        {
            Assert.Contains(permission, PermissionCatalog.All);
            Assert.NotNull(HrPayrollPermissionCatalog.RequiredCapability(permission));
        });
        Assert.Equal(HrPayrollCapabilityKeys.Payroll,
            HrPayrollPermissionCatalog.RequiredCapability("payroll.payment.view"));
        Assert.Equal(HrPayrollCapabilityKeys.Payroll,
            HrPayrollPermissionCatalog.RequiredCapability("payroll.salary.view"));
        Assert.Equal(HrPayrollCapabilityKeys.Hr,
            HrPayrollPermissionCatalog.RequiredCapability("hr.employee.view"));
        Assert.Equal(new[] { "hr.employee.view" }, PermissionCatalog.ExpandClaims("hr.employee.view"));
        Assert.Equal(HrPayrollCapabilityKeys.DeviceIntegration,
            HrPayrollPermissionCatalog.RequiredCapability("hr.device_user_mapping.manage"));
        Assert.Null(HrPayrollPermissionCatalog.RequiredCapability("sales.create"));
    }

    [Fact]
    public void GrantPlan_IsAdditiveDuplicateSafeAndPreservesCustomAndWildcardGrants()
    {
        var current = """["custom.grant","sales.view","*","sales.view"]""";
        var once = HrPayrollRoleGrantPlanner.MergeApprovedGrants(current, ["hr.employee.view", "hr.employee.view"]);
        var twice = HrPayrollRoleGrantPlanner.MergeApprovedGrants(once, ["hr.employee.view"]);
        Assert.Equal(once, twice);
        Assert.Equal(new[] { "custom.grant", "sales.view", "*", "hr.employee.view" },
            JsonSerializer.Deserialize<string[]>(once));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    public void MalformedExistingGrants_AreNotSilentlyReplaced(string json) =>
        Assert.Throws<JsonException>(() => HrPayrollRoleGrantPlanner.MergeApprovedGrants(json, ["hr.employee.view"]));

    [Theory]
    [InlineData("*")]
    [InlineData("sales.create")]
    [InlineData("payroll.unknown")]
    public void UpgradeCannotAddUnreviewedOrWildcardGrant(string grant) =>
        Assert.Throws<ArgumentException>(() => HrPayrollRoleGrantPlanner.MergeApprovedGrants("[]", [grant]));
}
