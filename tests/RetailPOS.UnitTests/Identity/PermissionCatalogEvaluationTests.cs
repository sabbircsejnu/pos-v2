using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.UnitTests.Identity;

public sealed class PermissionCatalogEvaluationTests
{
    [Fact]
    public void Wildcard_GrantsEveryCataloguedPermission()
    {
        foreach (var permission in PermissionCatalog.All)
            Assert.True(PermissionCatalog.IsGranted(["*"], permission), permission);
    }

    [Fact]
    public void ExactMatch_IsCaseInsensitive()
    {
        Assert.True(PermissionCatalog.IsGranted(["SALES.CREATE"], "sales.create"));
        Assert.False(PermissionCatalog.IsGranted(["sales.view"], "sales.create"));
    }

    [Fact]
    public void LegacyAlias_SatisfiesTheAccountingPermission()
    {
        Assert.True(PermissionCatalog.IsGranted(["accounts.view"], "accounting.chart_accounts.view"));
        Assert.False(PermissionCatalog.IsGranted(["accounts.view"], "accounting.chart_accounts.create"));
    }

    [Fact]
    public void EmptyGrants_DenyEverything()
    {
        Assert.False(PermissionCatalog.IsGranted([], "users.view"));
    }

    [Theory]
    [InlineData("**")]
    [InlineData("* ")]
    [InlineData("users.*")]
    public void OnlyTheLiteralWildcard_ActsAsWildcard(string granted)
    {
        Assert.False(PermissionCatalog.IsGranted([granted], "users.view"));
    }
}
