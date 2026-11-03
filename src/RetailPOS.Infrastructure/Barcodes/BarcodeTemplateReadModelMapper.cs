using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Barcodes;

internal static class BarcodeTemplateReadModelMapper
{
    public static BarcodeTemplateReadModel ToReadModel(BarcodeTemplate template) =>
        new(
            template.Id,
            template.Name,
            template.TemplateType,
            template.PaperType,
            template.LabelWidthMm,
            template.LabelHeightMm,
            template.IsDefault,
            template.IsActive,
            template.CreatedAt,
            template.UpdatedAt,
            template.Fields
                .OrderBy(field => field.SortOrder)
                .Select(field => new BarcodeTemplateFieldReadModel(
                    field.Id,
                    field.FieldKey,
                    field.IsEnabled,
                    field.SortOrder,
                    field.X,
                    field.Y,
                    field.Width,
                    field.Height,
                    field.FontSize,
                    field.FontWeight,
                    field.Align))
                .ToArray());
}
