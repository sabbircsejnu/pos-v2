using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodePrintHistoryWriteStore(RetailPOSDbContext dbContext)
    : IBarcodePrintHistoryWriteStore
{
    public async Task<BarcodePrintHistoryReadModel> CreateAsync(
        BarcodePrintHistoryWriteModel writeModel,
        long businessId,
        bool isSuperAdmin,
        CancellationToken cancellationToken)
    {
        if (writeModel.TemplateId.HasValue)
        {
            var template = await dbContext.Set<BarcodeTemplate>()
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == writeModel.TemplateId.Value, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Barcode template with ID {writeModel.TemplateId.Value} not found");

            if (!isSuperAdmin && template.BusinessId != businessId)
                throw new UnauthorizedAccessException("You are not authorized to access this barcode template.");
        }

        var history = new BarcodePrintHistory
        {
            BusinessId = businessId,
            OutletId = writeModel.OutletId,
            TemplateId = writeModel.TemplateId,
            PrintedByUserId = writeModel.PrintedByUserId,
            PrintMode = writeModel.PrintMode,
            LabelWidthMm = writeModel.LabelWidthMm,
            LabelHeightMm = writeModel.LabelHeightMm,
            SourceModule = writeModel.SourceModule,
            SourceReferenceType = writeModel.SourceReferenceType,
            SourceReferenceId = writeModel.SourceReferenceId,
            TotalLabels = writeModel.Items.Sum(item => item.QuantityPrinted),
            PrintedAt = DateTime.UtcNow,
            Items = writeModel.Items.Select(item => new BarcodePrintHistoryItem
            {
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantName = item.VariantName,
                VariantSku = item.VariantSku,
                BarcodeValue = item.BarcodeValue,
                VariantAttributes = item.VariantAttributes,
                SellingPrice = item.SellingPrice,
                QuantityPrinted = item.QuantityPrinted
            }).ToList()
        };

        dbContext.Set<BarcodePrintHistory>().Add(history);
        await dbContext.SaveChangesAsync(cancellationToken);

        var loaded = await dbContext.Set<BarcodePrintHistory>()
            .AsNoTracking()
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.Outlet)
            .Include(candidate => candidate.PrintedByUser)
            .Include(candidate => candidate.Items.OrderBy(item => item.Id))
            .SingleAsync(candidate => candidate.Id == history.Id, cancellationToken);

        return new BarcodePrintHistoryReadModel(
            loaded.Id,
            loaded.TemplateId,
            loaded.Template?.Name,
            loaded.OutletId,
            loaded.Outlet?.Name,
            loaded.PrintedByUserId,
            loaded.PrintedByUser?.Name,
            loaded.PrintMode,
            loaded.LabelWidthMm,
            loaded.LabelHeightMm,
            loaded.TotalLabels,
            loaded.SourceModule,
            loaded.SourceReferenceType,
            loaded.SourceReferenceId,
            loaded.PrintedAt,
            loaded.Items.Select(item => new BarcodePrintHistoryItemReadModel(
                item.Id,
                item.VariantId,
                item.ProductName,
                item.VariantName,
                item.VariantSku,
                item.BarcodeValue,
                item.VariantAttributes,
                item.SellingPrice,
                item.QuantityPrinted)).ToArray());
    }
}
