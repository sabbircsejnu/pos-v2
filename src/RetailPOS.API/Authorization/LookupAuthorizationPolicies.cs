using RetailPOS.Application.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace RetailPOS.API.Authorization;

/// <summary>
/// Named any-of policies for read-only lookups that several screens embed (for example the variant
/// picker). A caller needs only one of the listed permissions, each of which is held by a screen that
/// genuinely uses the lookup, so a screen's own view/create permission is never blocked by an
/// unrelated management permission. Data scope (Business and Outlet/Warehouse) is enforced by the
/// endpoint itself and is not widened here.
/// </summary>
public static class LookupAuthorizationPolicies
{
    public const string ProductVariantSearch = "lookup.product_variants";

    /// <summary>Flat category list used by Category Management and by the Product Create/Edit category picker.</summary>
    public const string CategoryList = "lookup.categories";

    /// <summary>Read-only Business tax settings used by Settings, POS Checkout and the Product Create/Edit tax hint.</summary>
    public const string TaxSettingsRead = "lookup.tax_settings";

    /// <summary>Read-only inventory settings (low-stock threshold and stock flags) used by inventory screens and POS Checkout.</summary>
    public const string InventorySettingsRead = "lookup.inventory_settings";

    /// <summary>Variation definitions (Size, Color, …) used by Variation Management and the Product Edit variant section.</summary>
    public const string VariationList = "lookup.variations";

    /// <summary>
    /// A single product record and its variant/combination reads: editing a product implies reading the record being
    /// edited. Cost stays redacted without products.view_cost; Business and location checks are unchanged.
    /// </summary>
    public const string ProductRecordRead = "lookup.product_record";

    /// <summary>SKU/barcode suggestions for the Product Create/Edit form (no product is persisted).</summary>
    public const string ProductCodeGeneration = "lookup.product_codes";

    /// <summary>Attachment reads, for anyone who works with expenses on the list or the form.</summary>
    public const string ExpenseEntryRead = "lookup.expense_entry_read";

    /// <summary>
    /// What entering an expense needs beyond the expense itself: the payment account lookup and managing attachments.
    /// No Chart of Accounts permission is involved; location scope is enforced by the use case.
    /// </summary>
    public const string ExpenseEntryWrite = "lookup.expense_entry_write";

    /// <summary>The category list: for anyone who works with expenses and for category managers, who may hold nothing else.</summary>
    public const string ExpenseCategoryRead = "lookup.expense_category_read";

    private static readonly IReadOnlyDictionary<string, string[]> Definitions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ExpenseCategoryRead] =
            [
                "accounting.expenses.view",
                "accounting.expenses.create", "accounting.expenses.edit",
                "accounting.expense_categories.manage"
            ],
            [ExpenseEntryRead] =
            [
                "accounting.expenses.view",
                "accounting.expenses.create", "accounting.expenses.edit"
            ],
            [ExpenseEntryWrite] =
            [
                "accounting.expenses.create", "accounting.expenses.edit"
            ],
            [InventorySettingsRead] =
            [
                "settings.view",
                "inventory.view",
                "stock_adjustments.view", "stock_adjustments.create",
                "sales.create"
            ],
            [VariationList] =
            [
                "variations.view",
                "products.edit"
            ],
            [ProductRecordRead] =
            [
                "products.view",
                "products.edit"
            ],
            [ProductCodeGeneration] =
            [
                "products.view",
                "products.create", "products.edit"
            ],
            [CategoryList] =
            [
                "categories.view",
                "products.create", "products.edit"
            ],
            [TaxSettingsRead] =
            [
                "settings.view",
                "sales.create",
                "products.create", "products.edit"
            ],
            [ProductVariantSearch] =
            [
                "products.view",
                "inventory.view",
                "stock_adjustments.create", "stock_adjustments.edit",
                "stock_transfers.view", "stock_transfers.create",
                "purchases.create", "purchases.edit",
                "reports.inventory",
                PermissionCatalog.PackStateView, PermissionCatalog.RepackingView
            ]
        };

    public static IReadOnlyCollection<string> PolicyNames => Definitions.Keys.ToArray();

    public static IReadOnlyList<string> PermissionsFor(string policyName) => Definitions[policyName];

    public static void Register(AuthorizationOptions options)
    {
        foreach (var (name, permissions) in Definitions)
        {
            options.AddPolicy(name, policy => policy.RequireAssertion(
                context => permissions.Any(context.User.HasPermission)));
        }
    }
}
