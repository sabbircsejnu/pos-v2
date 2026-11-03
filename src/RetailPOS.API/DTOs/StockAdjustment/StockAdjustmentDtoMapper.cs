using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.API.DTOs.StockAdjustment;

internal static class StockAdjustmentDtoMapper
{
    public static StockAdjustmentDto Map(StockAdjustmentReadModel adjustment) => new()
    {
        Id = adjustment.Id,
        AdjustmentNumber = adjustment.AdjustmentNumber,
        Status = adjustment.Status,
        LocationId = adjustment.LocationId,
        LocationType = adjustment.LocationType,
        SourceStockCountId = adjustment.SourceStockCountId,
        LocationName = adjustment.LocationName,
        VariantId = adjustment.VariantId,
        VariantSku = adjustment.VariantSku,
        ProductName = adjustment.ProductName,
        PreviousQuantity = adjustment.PreviousQuantity,
        QuantityChange = adjustment.QuantityChange,
        NewQuantity = adjustment.NewQuantity,
        Reason = adjustment.Reason,
        Notes = adjustment.Notes,
        LineCount = adjustment.LineCount,
        TotalIncrease = adjustment.TotalIncrease,
        TotalDecrease = adjustment.TotalDecrease,
        NetQuantityChange = adjustment.NetQuantityChange,
        Lines = adjustment.Lines.Select(line => new StockAdjustmentLineDto
        {
            Id = line.Id,
            VariantId = line.VariantId,
            ProductName = line.ProductName,
            VariantSku = line.VariantSku,
            ProductCode = line.ProductCode,
            Barcode = line.Barcode,
            PreviousQuantity = line.PreviousQuantity,
            QuantityChange = line.QuantityChange,
            NewQuantity = line.NewQuantity,
            Reason = line.Reason,
            Notes = line.Notes
        }).ToList(),
        AdjustedBy = adjustment.AdjustedBy,
        AdjusterName = adjustment.AdjusterName,
        AdjustmentDate = adjustment.AdjustmentDate,
        CreatedAt = adjustment.CreatedAt,
        UpdatedAt = adjustment.UpdatedAt,
        SubmittedAt = adjustment.SubmittedAt,
        ApprovedBy = adjustment.ApprovedBy,
        ApprovedByName = adjustment.ApprovedByName,
        ApprovedAt = adjustment.ApprovedAt,
        RejectedBy = adjustment.RejectedBy,
        RejectedByName = adjustment.RejectedByName,
        RejectedAt = adjustment.RejectedAt,
        RejectionReason = adjustment.RejectionReason,
        CancelledBy = adjustment.CancelledBy,
        CancelledByName = adjustment.CancelledByName,
        CancelledAt = adjustment.CancelledAt
    };
}
