using System.Text.RegularExpressions;
using RetailPOS.API.Authorization;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.BusinessAdministration;
using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.Tests.Security;

/// <summary>
/// Source-level guards that keep the permission registry, the Role Edit screen, the Angular menu/route
/// requirements, the frontend alias mirror and the API policies aligned.
/// </summary>
public class RbacPermissionConsistencyTests
{
    private static readonly Regex QuotedCode = new("['\"]([A-Za-z_]+(?:\\.[A-Za-z_]+)+)['\"]", RegexOptions.Compiled);
    private static readonly string RepoRoot = ResolveRepoRoot();

    // Legacy finance codes are kept only as backend aliases of the canonical accounting.* permissions, and
    // accounting.dashboard.view has no implemented screen or endpoint yet. Neither is offered in Role Edit.
    private static readonly HashSet<string> IntentionallyHiddenFromRoleEdit = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.view", "accounts.create", "accounts.edit", "accounts.delete",
        "transactions.view", "transactions.create", "transactions.edit", "transactions.delete",
        "expenses.view", "expenses.create", "expenses.edit", "expenses.delete",
        "accounting.dashboard.view"
    };

    [Fact]
    public void RoleEdit_OffersEveryRegisteredPermission_ExceptTheDocumentedHiddenOnes()
    {
        var offered = CodesIn("retailpos-frontend", "src", "app", "components", "roles", "role-form.component.ts")
            .Concat(CodesIn("retailpos-frontend", "src", "app", "models", "hr-payroll-permissions.ts"))
            .ToHashSet(StringComparer.Ordinal);

        var unassignable = PermissionCatalog.All
            .Where(permission => !offered.Contains(permission) && !IntentionallyHiddenFromRoleEdit.Contains(permission))
            .OrderBy(permission => permission)
            .ToList();

        Assert.True(unassignable.Count == 0,
            $"Registered permissions that administrators cannot assign in Role Edit: {string.Join(", ", unassignable)}");
    }

    [Fact]
    public void RoleEdit_OffersOnlyRegisteredPermissions()
    {
        var catalog = PermissionCatalog.All.ToHashSet(StringComparer.Ordinal);
        var unknown = CodesIn("retailpos-frontend", "src", "app", "components", "roles", "role-form.component.ts")
            .Concat(CodesIn("retailpos-frontend", "src", "app", "models", "hr-payroll-permissions.ts"))
            .Where(code => !catalog.Contains(code))
            .Distinct()
            .ToList();

        Assert.True(unknown.Count == 0, $"Role Edit offers unregistered permissions: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void MenuAndRoutes_RequireOnlyRegisteredPermissions()
    {
        var catalog = PermissionCatalog.All.ToHashSet(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var declaration = new Regex("\\bpermissions?\\s*:\\s*(\\[[^\\]]*\\]|'[^']+')", RegexOptions.Compiled);
        foreach (var file in new[]
        {
            Path.Combine("retailpos-frontend", "src", "app", "app.routes.ts"),
            Path.Combine("retailpos-frontend", "src", "app", "models", "menu.model.ts")
        })
        {
            foreach (Match match in declaration.Matches(File.ReadAllText(Path.Combine(RepoRoot, file))))
            {
                foreach (Match code in QuotedCode.Matches(match.Groups[1].Value))
                {
                    declared.Add(code.Groups[1].Value);
                }
            }
        }

        Assert.NotEmpty(declared);
        var unknown = declared.Where(code => !catalog.Contains(code)).OrderBy(code => code).ToList();
        Assert.True(unknown.Count == 0, $"Menu/route permissions missing from the registry: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void FrontendAliasMirror_NeverGrantsMoreThanTheBackendAliases()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "retailpos-frontend", "src", "app", "services", "auth.service.ts"));
        var block = source[source.IndexOf("permissionAliases", StringComparison.Ordinal)..source.IndexOf("API_URL", StringComparison.Ordinal)];
        var entry = new Regex("'([^']+)':\\s*\\[([^\\]]*)\\]", RegexOptions.Compiled);
        var excess = new List<string>();

        foreach (Match match in entry.Matches(block))
        {
            var required = match.Groups[1].Value;
            foreach (Match alias in Regex.Matches(match.Groups[2].Value, "'([^']+)'"))
            {
                if (!PermissionCatalog.IsGranted([alias.Groups[1].Value], required))
                {
                    excess.Add($"{alias.Groups[1].Value} => {required}");
                }
            }
        }

        Assert.True(excess.Count == 0,
            $"Frontend aliases the backend does not honour (menu would show, API would answer 403): {string.Join(", ", excess)}");
    }

    [Fact]
    public void ControllerPolicies_AreAllRegistered()
    {
        var registered = PermissionCatalog.All
            .Concat(LookupAuthorizationPolicies.PolicyNames)
            .ToHashSet(StringComparer.Ordinal);
        var literal = new Regex("Policy\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        var lookup = new Regex("Policy\\s*=\\s*LookupAuthorizationPolicies\\.(\\w+)", RegexOptions.Compiled);
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(
                     Path.Combine(RepoRoot, "src", "RetailPOS.API", "Controllers"), "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            foreach (Match match in literal.Matches(content))
            {
                if (!registered.Contains(match.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
                }
            }

            foreach (Match match in lookup.Matches(content))
            {
                var field = typeof(LookupAuthorizationPolicies).GetField(match.Groups[1].Value);
                Assert.NotNull(field);
            }
        }

        Assert.True(missing.Count == 0, $"Controllers reference unregistered policies (HTTP 500 at runtime): {string.Join("; ", missing)}");
    }

    [Fact]
    public void LookupPolicies_OnlyReferenceRegisteredPermissions()
    {
        var catalog = PermissionCatalog.All.ToHashSet(StringComparer.Ordinal);
        foreach (var policy in LookupAuthorizationPolicies.PolicyNames)
        {
            Assert.DoesNotContain(policy, catalog);
            var unknown = LookupAuthorizationPolicies.PermissionsFor(policy).Where(permission => !catalog.Contains(permission)).ToList();
            Assert.True(unknown.Count == 0, $"{policy} references unregistered permissions: {string.Join(", ", unknown)}");
        }
    }

    [Fact]
    public void DefaultRoleTemplates_OnlyGrantRegisteredPermissionsOrTheWildcard()
    {
        var catalog = PermissionCatalog.All.ToHashSet(StringComparer.Ordinal);
        var templates = new Dictionary<string, IReadOnlyList<string>>
        {
            [nameof(BusinessRolePermissions.BusinessOwner)] = BusinessRolePermissions.BusinessOwner,
            [nameof(BusinessRolePermissions.OutletManager)] = BusinessRolePermissions.OutletManager,
            [nameof(BusinessRolePermissions.SalesPerson)] = BusinessRolePermissions.SalesPerson,
            [nameof(BusinessRolePermissions.AccountsAdmin)] = BusinessRolePermissions.AccountsAdmin,
            [nameof(BusinessRolePermissions.WarehouseManager)] = BusinessRolePermissions.WarehouseManager,
            [AccountingManagerPermissions.RoleName] = AccountingManagerPermissions.All
        };

        foreach (var (name, permissions) in templates)
        {
            var unknown = permissions.Where(permission => permission != PermissionCatalog.Wildcard && !catalog.Contains(permission)).ToList();
            Assert.True(unknown.Count == 0, $"{name} template grants unregistered permissions: {string.Join(", ", unknown)}");
        }

        Assert.All(PermissionCatalog.All, permission => Assert.True(PermissionCatalog.IsGranted(BusinessRolePermissions.BusinessOwner, permission), permission));
    }

    [Fact]
    public void PermissionCatalog_HasNoDuplicateCodes()
    {
        var duplicates = PermissionCatalog.All.GroupBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ExpenseCategoryEndpoints_AreGovernedByTheRoleEditExpensePermissions()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "src", "RetailPOS.API", "Controllers", "AccountingExpensesController.cs"));

        // Reading categories is part of entering an expense, so create/edit roles can read them too; D3 gives the
        // business-wide writes and their account lookup a permission of their own.
        Assert.Matches("HttpGet\\(\"categories\"\\)\\]\\s*\\[Authorize\\(Policy = LookupAuthorizationPolicies.ExpenseCategoryRead\\)\\]", controller);
        Assert.Matches("HttpPost\\(\"categories\"\\)\\]\\s*\\[Authorize\\(Policy = PermissionCatalog.ExpenseCategoriesManage\\)\\]", controller);
        Assert.Matches("HttpPut\\(\"categories/\\{id:long\\}\"\\)\\]\\s*\\[Authorize\\(Policy = PermissionCatalog.ExpenseCategoriesManage\\)\\]", controller);
        Assert.Matches("HttpGet\\(\"expense-accounts\"\\)\\]\\s*\\[Authorize\\(Policy = PermissionCatalog.ExpenseCategoriesManage\\)\\]", controller);
    }

    [Fact]
    public void ExpenseCategoryManagement_IsNotImpliedByAnyAliasOrSeededNonOwnerRole()
    {
        const string manage = "accounting.expense_categories.manage";

        Assert.Equal([manage], RetailPOS.Application.Identity.Permissions.PermissionCatalog.ExpandClaims(manage));
        Assert.False(RetailPOS.Application.Identity.Permissions.PermissionCatalog.IsGranted(["accounting.expenses.edit", "expenses.edit"], manage));
        Assert.True(RetailPOS.Application.Identity.Permissions.PermissionCatalog.IsGranted(["*"], manage));
        Assert.DoesNotContain(manage, RetailPOS.Application.BusinessAdministration.BusinessRolePermissions.OutletManager);
        Assert.DoesNotContain(manage, RetailPOS.Application.BusinessAdministration.BusinessRolePermissions.WarehouseManager);
        Assert.DoesNotContain(manage, RetailPOS.Application.BusinessAdministration.BusinessRolePermissions.AccountsAdmin);
        Assert.DoesNotContain(manage, RetailPOS.Application.Authorization.AccountingManagerPermissions.All);
    }

    private static IEnumerable<string> CodesIn(params string[] relativePath) =>
        QuotedCode.Matches(File.ReadAllText(Path.Combine([RepoRoot, .. relativePath]))).Select(match => match.Groups[1].Value);

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RetailPOS.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not resolve repository root from test base directory.");
    }
}
