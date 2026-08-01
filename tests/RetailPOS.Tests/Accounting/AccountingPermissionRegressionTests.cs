using System.Text.RegularExpressions;
using RetailPOS.API.Authorization;

namespace RetailPOS.Tests.Accounting;

public class AccountingPermissionRegressionTests
{
    private static readonly Regex AccountingPermissionRegex = new("accounting\\.[a-z0-9_\\.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public void AccountingPermissions_InControllers_MustExistInCatalog()
    {
        var controllersPath = Path.Combine(RepoRoot, "src", "RetailPOS.API", "Controllers");
        var files = Directory.GetFiles(controllersPath, "*.cs", SearchOption.TopDirectoryOnly);

        var controllerPolicies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var policyRegex = new Regex("Authorize\\(Policy\\s*=\\s*\"([^\"]+)\"\\)", RegexOptions.Compiled);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in policyRegex.Matches(content))
            {
                var policy = match.Groups[1].Value;
                if (policy.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase))
                {
                    controllerPolicies.Add(policy);
                }
            }
        }

        var catalog = CatalogPermissions();
        var missing = controllerPolicies.Where(x => !catalog.Contains(x)).OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0, $"Controller accounting policies not found in PermissionCatalog: {string.Join(", ", missing)}");
    }

    [Fact]
    public void AccountingPermissions_InSqlSeeds_MustCoverCatalog()
    {
        var sqlFiles = new[]
        {
            Path.Combine(RepoRoot, "database", "add-accounting-v2-permissions.sql"),
            Path.Combine(RepoRoot, "database", "seed-v2-roles.sql"),
            Path.Combine(RepoRoot, "database", "seed-data.sql")
        };

        var seedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in sqlFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in AccountingPermissionRegex.Matches(content))
            {
                seedPermissions.Add(match.Value);
            }
        }

        var missingInSeeds = CatalogPermissions().Where(x => !seedPermissions.Contains(x)).OrderBy(x => x).ToList();
        Assert.True(missingInSeeds.Count == 0, $"Accounting permissions missing from SQL seed assets: {string.Join(", ", missingInSeeds)}");
    }

    [Fact]
    public void AccountingPermissions_InFrontendRoutesAndMenu_MustBeCanonicalAndGuarded()
    {
        var routesFile = Path.Combine(RepoRoot, "retailpos-frontend", "src", "app", "app.routes.ts");
        var menuFile = Path.Combine(RepoRoot, "retailpos-frontend", "src", "app", "models", "menu.model.ts");

        var routesContent = File.ReadAllText(routesFile);
        var menuContent = File.ReadAllText(menuFile);
        var catalog = CatalogPermissions();

        var routePermissions = ExtractAccountingPermissions(routesContent);
        var menuPermissions = ExtractAccountingPermissions(menuContent);

        var missingInCatalog = routePermissions
            .Concat(menuPermissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !catalog.Contains(x))
            .OrderBy(x => x)
            .ToList();

        Assert.True(missingInCatalog.Count == 0, $"Frontend accounting permissions not in catalog: {string.Join(", ", missingInCatalog)}");

        var accountingRouteBlocks = Regex.Matches(routesContent, "\\{\\s*path:\\s*'[^']*'[^}]*permissions:\\s*\\[[^\\]]*accounting\\.[^\\]]*\\][^}]*\\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.NotEmpty(accountingRouteBlocks);

        foreach (Match block in accountingRouteBlocks)
        {
            Assert.Contains("canActivate: [routePermissionGuard]", block.Value, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(block.Value, "['\\\"]accounts\\.[a-z0-9_]+['\\\"]", RegexOptions.IgnoreCase));
            Assert.False(Regex.IsMatch(block.Value, "['\\\"]transactions\\.[a-z0-9_]+['\\\"]", RegexOptions.IgnoreCase));
            Assert.False(Regex.IsMatch(block.Value, "['\\\"]expenses\\.[a-z0-9_]+['\\\"]", RegexOptions.IgnoreCase));
        }
    }

    [Fact]
    public void AccountingPermissions_InRoleFormAndAuthAliases_MustStayAligned()
    {
        var roleFormFile = Path.Combine(RepoRoot, "retailpos-frontend", "src", "app", "components", "roles", "role-form.component.ts");
        var authFile = Path.Combine(RepoRoot, "retailpos-frontend", "src", "app", "services", "auth.service.ts");

        var roleFormPermissions = ExtractAccountingPermissions(File.ReadAllText(roleFormFile));
        var catalog = CatalogPermissions();
        var missingInCatalog = roleFormPermissions.Where(x => !catalog.Contains(x)).OrderBy(x => x).ToList();

        Assert.True(missingInCatalog.Count == 0, $"Role form contains unknown accounting permissions: {string.Join(", ", missingInCatalog)}");

        var authContent = File.ReadAllText(authFile);
        var expectedAliasCanonicalPermissions = new[]
        {
            "accounting.chart_accounts.view",
            "accounting.chart_accounts.create",
            "accounting.chart_accounts.edit",
            "accounting.journals.view",
            "accounting.journals.create",
            "accounting.journals.update",
            "accounting.journals.cancel",
            "accounting.journals.post",
            "accounting.journals.reverse",
            "accounting.expenses.view",
            "accounting.expenses.create",
            "accounting.expenses.edit",
            "accounting.expenses.submit",
            "accounting.expenses.approve",
            "accounting.expenses.reject",
            "accounting.expenses.cancel"
        };

        foreach (var permission in expectedAliasCanonicalPermissions)
        {
            Assert.Contains($"'{permission}'", authContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegacyFinancePolicies_AreNotUsedInNewAccountingControllers()
    {
        var controllersPath = Path.Combine(RepoRoot, "src", "RetailPOS.API", "Controllers");
        var accountingControllers = Directory.GetFiles(controllersPath, "Accounting*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in accountingControllers)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("Policy = \"accounts.", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Policy = \"transactions.", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Policy = \"expenses.", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> CatalogPermissions()
    {
        return PermissionCatalog.All
            .Where(x => x.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExtractAccountingPermissions(string content)
    {
        return AccountingPermissionRegex
            .Matches(content)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "RetailPOS.slnx");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not resolve repository root from test base directory.");
    }
}
