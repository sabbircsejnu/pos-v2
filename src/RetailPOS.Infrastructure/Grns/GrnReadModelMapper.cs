using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Grns;

internal static class GrnReadModelMapper
{
    public static GrnReadModel Map(Grn grn) =>
        new(
            grn.Id,
            grn.PoId,
            grn.PurchaseOrder?.SupplierId ?? 0,
            grn.PurchaseOrder?.Supplier?.Name ?? string.Empty,
            grn.PurchaseOrder?.WarehouseId,
            grn.PurchaseOrder?.Warehouse?.Name ?? string.Empty,
            grn.ReceivedDate,
            grn.Status,
            grn.Notes,
            grn.SupplierInvoiceNumber,
            grn.SupplierInvoiceDate,
            grn.CreatedBy ?? grn.PurchaseOrder?.CreatedBy,
            grn.Creator?.Name ?? grn.PurchaseOrder?.Creator?.Name,
            grn.CreatedAt,
            grn.Items.Select(item => new GrnItemReadModel(
                item.Id,
                item.PoItemId,
                item.PurchaseOrderItem?.VariantId ?? 0,
                item.PurchaseOrderItem?.Variant?.Product?.Name ?? string.Empty,
                item.PurchaseOrderItem?.Variant?.Product?.ProductCode,
                item.PurchaseOrderItem?.Variant?.Name ?? string.Empty,
                item.PurchaseOrderItem?.Variant?.Attributes,
                item.PurchaseOrderItem?.Quantity ?? 0,
                item.ReceivedQty,
                item.PurchaseOrderItem?.UnitPrice ?? 0,
                item.UnitCost,
                item.Notes)).ToArray(),
            grn.PurchaseOrder?.OutletId,
            grn.PurchaseOrder?.Outlet?.Name);
}
