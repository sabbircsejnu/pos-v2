using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.API.Documents.Pdf;

internal static class PurchaseDocumentDtoMapper
{
    public static string LocationLabel(string? locationType) =>
        string.Equals(locationType, "outlet", StringComparison.OrdinalIgnoreCase) ? "Outlet" : "Warehouse";

    public static PurchaseOrderDto MapPurchaseOrder(PurchaseOrderReadModel order) => new()
    {
        Id = order.Id,
        PoNumber = order.PoNumber,
        SupplierId = order.SupplierId,
        SupplierName = order.SupplierName,
        WarehouseId = order.WarehouseId,
        WarehouseName = order.WarehouseName,
        OutletId = order.OutletId,
        OutletName = order.OutletName,
        LocationType = order.LocationType,
        LocationId = order.LocationId,
        LocationName = order.LocationName,
        OrderDate = order.OrderDate,
        ExpectedDelivery = order.ExpectedDelivery,
        TotalAmount = order.TotalAmount,
        Status = order.Status,
        Notes = order.Notes,
        RejectionReason = order.RejectionReason,
        CreatedBy = order.CreatedBy,
        CreatedByName = order.CreatedByName,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        LatestGrnId = order.LatestGrnId,
        Items = order.Items.Select(item => new PurchaseOrderItemDto
        {
            Id = item.Id,
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            ProductCode = item.ProductCode,
            Sku = item.Sku,
            VariantAttributes = item.VariantAttributes,
            Quantity = item.Quantity,
            Unit = item.Unit,
            UnitPrice = item.UnitPrice,
            Discount = item.Discount,
            Tax = item.Tax
        }).ToList()
    };

    public static GrnDto MapGrn(GrnReadModel grn) => new()
    {
        Id = grn.Id,
        PoId = grn.PurchaseOrderId,
        PoOrderNumber = $"PO-{grn.PurchaseOrderId:D6}",
        SupplierId = grn.SupplierId,
        SupplierName = grn.SupplierName,
        WarehouseId = grn.WarehouseId,
        WarehouseName = grn.WarehouseName,
        OutletId = grn.OutletId,
        OutletName = grn.OutletName,
        LocationType = grn.LocationType,
        LocationId = grn.LocationId,
        LocationName = grn.LocationName,
        ReceivedDate = grn.ReceivedDate,
        Status = grn.Status,
        Notes = grn.Notes,
        SupplierInvoiceNumber = grn.SupplierInvoiceNumber,
        SupplierInvoiceDate = grn.SupplierInvoiceDate,
        CreatedBy = grn.CreatedBy,
        CreatedByName = grn.CreatedByName,
        CreatedAt = grn.CreatedAt,
        Items = grn.Items.Select(item => new GrnItemDto
        {
            Id = item.Id,
            PoItemId = item.PurchaseOrderItemId,
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            ProductCode = item.ProductCode,
            VariantName = item.VariantName,
            VariantAttributes = item.VariantAttributes,
            OrderedQty = item.OrderedQuantity,
            ReceivedQty = item.ReceivedQuantity,
            UnitPrice = item.UnitPrice,
            UnitCost = item.UnitCost,
            Notes = item.Notes
        }).ToList()
    };
}
