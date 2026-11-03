using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodeTemplateDefaultsProvisioner(RetailPOSDbContext dbContext)
    : IBarcodeTemplateDefaultsProvisioner
{
    public async Task EnsureAsync(long businessId, CancellationToken cancellationToken)
    {
        foreach (var template in BuildDefaultTemplates(businessId))
        {
            var name = template.Name.Trim().ToLower();
            var exists = await dbContext.Set<BarcodeTemplate>()
                .AnyAsync(candidate => candidate.BusinessId == businessId
                    && candidate.Name.ToLower() == name, cancellationToken);
            if (exists)
                continue;

            try
            {
                dbContext.Set<BarcodeTemplate>().Add(template);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent request may have inserted the same default template.
            }
        }

        var templates = await dbContext.Set<BarcodeTemplate>()
            .Where(template => template.BusinessId == businessId)
            .ToListAsync(cancellationToken);
        if (templates.Count == 0 || templates.Any(template => template.IsDefault))
            return;

        var firstTemplate = templates.OrderBy(template => template.Id).First();
        firstTemplate.IsDefault = true;
        firstTemplate.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static List<BarcodeTemplate> BuildDefaultTemplates(long businessId) =>
    [
        new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = "Small Barcode Label",
            TemplateType = "small",
            PaperType = "label",
            LabelWidthMm = 40,
            LabelHeightMm = 25,
            IsDefault = false,
            IsActive = true,
            Fields = BuildFields("variant_sku", "barcode")
        },
        new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = "Retail Price Label",
            TemplateType = "retail",
            PaperType = "label",
            LabelWidthMm = 60,
            LabelHeightMm = 40,
            IsDefault = true,
            IsActive = true,
            Fields = BuildFields("company_name", "product_name", "variant_sku", "barcode", "selling_price")
        },
        new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = "Detailed Variant Label",
            TemplateType = "detailed",
            PaperType = "label",
            LabelWidthMm = 60,
            LabelHeightMm = 40,
            IsDefault = false,
            IsActive = true,
            Fields = BuildFields("product_name", "variant_name", "variant_attributes", "variant_sku", "barcode", "selling_price")
        },
        new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = "Warehouse Label",
            TemplateType = "warehouse",
            PaperType = "label",
            LabelWidthMm = 50,
            LabelHeightMm = 25,
            IsDefault = false,
            IsActive = true,
            Fields = BuildFields("product_name", "variant_sku", "barcode", "variant_attributes")
        }
    ];

    private static List<BarcodeTemplateField> BuildFields(params string[] enabled)
    {
        var enabledSet = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        string[] orderedFields =
        [
            "product_name",
            "variant_name",
            "variant_attributes",
            "variant_sku",
            "barcode",
            "selling_price",
            "company_name",
            "company_logo"
        ];

        return orderedFields
            .Select((fieldKey, index) => new BarcodeTemplateField
            {
                FieldKey = fieldKey,
                IsEnabled = enabledSet.Contains(fieldKey),
                SortOrder = index,
                Align = fieldKey == "selling_price" ? "right" : "left"
            })
            .ToList();
    }
}
