namespace RetailPOS.API.Authorization;

public static class PermissionCatalog
{
    public static readonly string[] All =
    {
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view", "roles.create", "roles.edit", "roles.delete",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.reject", "stock_adjustments.cancel",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create", "sales.backdate", "sales.void", "sales.refund",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.approve", "purchases.receive",
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
