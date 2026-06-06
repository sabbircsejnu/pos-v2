namespace RetailPOS.API.Authorization;

public static class PermissionCatalog
{
    public static readonly string[] All =
    {
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view", "roles.create", "roles.edit", "roles.delete",
        "products.view", "products.create", "products.edit", "products.delete",
        "inventory.view", "inventory.adjust", "inventory.transfer",
        "sales.view", "sales.create", "sales.void", "sales.refund",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.approve",
        "grn.view", "grn.create", "grn.receive",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "reports.sales", "reports.inventory", "reports.financial", "reports.export",
        "settings.view", "settings.edit",
        "audit.view",
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete",
        "accounts.view", "accounts.create", "accounts.edit", "accounts.delete",
        "transactions.view", "transactions.create", "transactions.edit", "transactions.delete"
    };
}
