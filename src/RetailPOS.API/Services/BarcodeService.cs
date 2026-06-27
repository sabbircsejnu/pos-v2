using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.DTOs.Product;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace RetailPOS.API.Services;

public class BarcodeService : IBarcodeService
{
    private static readonly HashSet<string> AllowedPrintModes =
        new(StringComparer.OrdinalIgnoreCase) { "pdf", "browser", "thermal" };

    private readonly ITenantAccessService _tenantAccess;
    private readonly IProductService _productService;
    private readonly IBarcodeTemplateRepository _templateRepository;
    private readonly IBarcodePrintHistoryRepository _historyRepository;

    public BarcodeService(
        ITenantAccessService tenantAccess,
        IProductService productService,
        IBarcodeTemplateRepository templateRepository,
        IBarcodePrintHistoryRepository historyRepository)
    {
        _tenantAccess = tenantAccess;
        _productService = productService;
        _templateRepository = templateRepository;
        _historyRepository = historyRepository;
    }

    public async Task<List<ProductVariantSearchDto>> SearchVariantsAsync(
        string query,
        int pageNumber,
        int pageSize,
        long? locationId,
        string? locationType)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var variants = await _productService.SearchVariantsAsync(
            query,
            pageNumber,
            pageSize,
            locationId,
            locationType);

        return variants.ToList();
    }

    public async Task<List<BarcodeTemplateDto>> GetTemplatesAsync(bool includeInactive = false)
    {
        var businessId = _tenantAccess.RequireBusinessId();
        var rows = await _templateRepository.GetByBusinessAsync(businessId, includeInactive);
        if (rows.Count == 0)
        {
            await EnsureDefaultTemplatesAsync(businessId);
            rows = await _templateRepository.GetByBusinessAsync(businessId, includeInactive);
        }

        return rows.Select(MapTemplate).ToList();
    }

    public async Task<BarcodeTemplateDto> GetTemplateByIdAsync(long id)
    {
        var row = await _templateRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Barcode template with ID {id} not found");

        _tenantAccess.EnsureBusinessMatch(row.BusinessId, "barcode template");
        return MapTemplate(row);
    }

    public async Task<BarcodeTemplateDto> CreateTemplateAsync(CreateBarcodeTemplateDto dto, long? userId)
    {
        ValidateTemplate(dto.Name, dto.LabelWidthMm, dto.LabelHeightMm);

        var businessId = _tenantAccess.RequireBusinessId();
        if (await _templateRepository.NameExistsAsync(businessId, dto.Name))
        {
            throw new InvalidOperationException($"Barcode template '{dto.Name}' already exists");
        }

        if (dto.IsDefault)
        {
            await _templateRepository.ClearDefaultAsync(businessId);
        }

        var template = new BarcodeTemplate
        {
            BusinessId = businessId,
            Name = dto.Name.Trim(),
            TemplateType = dto.TemplateType.Trim().ToLowerInvariant(),
            PaperType = dto.PaperType.Trim().ToLowerInvariant(),
            LabelWidthMm = dto.LabelWidthMm,
            LabelHeightMm = dto.LabelHeightMm,
            IsDefault = dto.IsDefault,
            IsActive = dto.IsActive,
            CreatedBy = userId,
            UpdatedBy = userId,
            Fields = dto.Fields.Select(MapField).ToList()
        };

        var created = await _templateRepository.CreateAsync(template);
        var loaded = await _templateRepository.GetByIdAsync(created.Id) ?? created;
        return MapTemplate(loaded);
    }

    public async Task<BarcodeTemplateDto> UpdateTemplateAsync(long id, UpdateBarcodeTemplateDto dto, long? userId)
    {
        ValidateTemplate(dto.Name, dto.LabelWidthMm, dto.LabelHeightMm);

        var row = await _templateRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Barcode template with ID {id} not found");

        _tenantAccess.EnsureBusinessMatch(row.BusinessId, "barcode template");

        if (await _templateRepository.NameExistsAsync(row.BusinessId, dto.Name, id))
        {
            throw new InvalidOperationException($"Barcode template '{dto.Name}' already exists");
        }

        if (dto.IsDefault)
        {
            await _templateRepository.ClearDefaultAsync(row.BusinessId);
        }

        row.Name = dto.Name.Trim();
        row.TemplateType = dto.TemplateType.Trim().ToLowerInvariant();
        row.PaperType = dto.PaperType.Trim().ToLowerInvariant();
        row.LabelWidthMm = dto.LabelWidthMm;
        row.LabelHeightMm = dto.LabelHeightMm;
        row.IsDefault = dto.IsDefault;
        row.IsActive = dto.IsActive;
        row.UpdatedBy = userId;
        row.UpdatedAt = DateTime.UtcNow;

        row.Fields.Clear();
        foreach (var field in dto.Fields)
        {
            row.Fields.Add(MapField(field));
        }

        var updated = await _templateRepository.UpdateAsync(row);
        return MapTemplate(updated);
    }

    public async Task SetDefaultTemplateAsync(long id, long? userId)
    {
        var row = await _templateRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Barcode template with ID {id} not found");

        _tenantAccess.EnsureBusinessMatch(row.BusinessId, "barcode template");

        await _templateRepository.ClearDefaultAsync(row.BusinessId);
        row.IsDefault = true;
        row.UpdatedBy = userId;
        row.UpdatedAt = DateTime.UtcNow;
        await _templateRepository.UpdateAsync(row);
    }

    public async Task DeleteTemplateAsync(long id)
    {
        var row = await _templateRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Barcode template with ID {id} not found");

        _tenantAccess.EnsureBusinessMatch(row.BusinessId, "barcode template");

        var deleted = await _templateRepository.DeleteAsync(id);
        if (!deleted)
        {
            throw new InvalidOperationException("Failed to delete barcode template");
        }
    }

    public async Task<BarcodePrintHistoryDto> RecordPrintHistoryAsync(RecordBarcodePrintHistoryDto dto, long? userId)
    {
        if (!AllowedPrintModes.Contains(dto.PrintMode))
        {
            throw new InvalidOperationException("Invalid print mode. Allowed values: pdf, browser, thermal");
        }

        if (dto.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one print history item is required");
        }

        var businessId = _tenantAccess.RequireBusinessId();

        if (dto.TemplateId.HasValue)
        {
            var template = await _templateRepository.GetByIdAsync(dto.TemplateId.Value)
                ?? throw new KeyNotFoundException($"Barcode template with ID {dto.TemplateId.Value} not found");

            _tenantAccess.EnsureBusinessMatch(template.BusinessId, "barcode template");
        }

        var history = new BarcodePrintHistory
        {
            BusinessId = businessId,
            OutletId = dto.OutletId,
            TemplateId = dto.TemplateId,
            PrintedByUserId = userId,
            PrintMode = dto.PrintMode.Trim().ToLowerInvariant(),
            LabelWidthMm = dto.LabelWidthMm,
            LabelHeightMm = dto.LabelHeightMm,
            SourceModule = dto.SourceModule?.Trim(),
            SourceReferenceType = dto.SourceReferenceType?.Trim(),
            SourceReferenceId = dto.SourceReferenceId,
            TotalLabels = dto.Items.Sum(i => i.QuantityPrinted),
            PrintedAt = DateTime.UtcNow,
            Items = dto.Items.Select(i => new BarcodePrintHistoryItem
            {
                VariantId = i.VariantId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                VariantSku = i.VariantSku,
                BarcodeValue = i.BarcodeValue,
                VariantAttributes = i.VariantAttributes,
                SellingPrice = i.SellingPrice,
                QuantityPrinted = i.QuantityPrinted
            }).ToList()
        };

        var created = await _historyRepository.CreateAsync(history);
        var loaded = await _historyRepository.GetByIdAsync(created.Id) ?? created;
        return MapHistory(loaded);
    }

    public async Task<BarcodePrintHistoryDto> GetPrintHistoryByIdAsync(long id)
    {
        var row = await _historyRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Barcode print history with ID {id} not found");

        _tenantAccess.EnsureBusinessMatch(row.BusinessId, "barcode print history");
        return MapHistory(row);
    }

    public async Task<BarcodePrintHistoryListDto> SearchPrintHistoryAsync(BarcodePrintHistorySearchDto dto)
    {
        var businessId = _tenantAccess.RequireBusinessId();

        var pageNumber = Math.Max(1, dto.PageNumber);
        var pageSize = Math.Clamp(dto.PageSize, 1, 100);

        var (rows, totalCount) = await _historyRepository.SearchAsync(
            businessId,
            dto.TemplateId,
            dto.PrintedByUserId,
            dto.StartDate,
            dto.EndDate,
            pageNumber,
            pageSize);

        return new BarcodePrintHistoryListDto
        {
            Rows = rows.Select(MapHistory).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private static void ValidateTemplate(string name, decimal widthMm, decimal heightMm)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Template name is required");
        }

        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new InvalidOperationException("Label width and height must be greater than zero");
        }
    }

    private static BarcodeTemplateField MapField(UpsertBarcodeTemplateFieldDto dto)
    {
        return new BarcodeTemplateField
        {
            FieldKey = dto.FieldKey.Trim().ToLowerInvariant(),
            IsEnabled = dto.IsEnabled,
            SortOrder = dto.SortOrder,
            X = dto.X,
            Y = dto.Y,
            Width = dto.Width,
            Height = dto.Height,
            FontSize = dto.FontSize,
            FontWeight = dto.FontWeight?.Trim(),
            Align = dto.Align?.Trim().ToLowerInvariant()
        };
    }

    private static BarcodeTemplateDto MapTemplate(BarcodeTemplate row)
    {
        return new BarcodeTemplateDto
        {
            Id = row.Id,
            Name = row.Name,
            TemplateType = row.TemplateType,
            PaperType = row.PaperType,
            LabelWidthMm = row.LabelWidthMm,
            LabelHeightMm = row.LabelHeightMm,
            IsDefault = row.IsDefault,
            IsActive = row.IsActive,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            Fields = row.Fields
                .OrderBy(f => f.SortOrder)
                .Select(f => new BarcodeTemplateFieldDto
                {
                    Id = f.Id,
                    FieldKey = f.FieldKey,
                    IsEnabled = f.IsEnabled,
                    SortOrder = f.SortOrder,
                    X = f.X,
                    Y = f.Y,
                    Width = f.Width,
                    Height = f.Height,
                    FontSize = f.FontSize,
                    FontWeight = f.FontWeight,
                    Align = f.Align
                })
                .ToList()
        };
    }

    private static BarcodePrintHistoryDto MapHistory(BarcodePrintHistory row)
    {
        return new BarcodePrintHistoryDto
        {
            Id = row.Id,
            TemplateId = row.TemplateId,
            TemplateName = row.Template?.Name,
            OutletId = row.OutletId,
            OutletName = row.Outlet?.Name,
            PrintedByUserId = row.PrintedByUserId,
            PrintedByUserName = row.PrintedByUser?.Name,
            PrintMode = row.PrintMode,
            LabelWidthMm = row.LabelWidthMm,
            LabelHeightMm = row.LabelHeightMm,
            TotalLabels = row.TotalLabels,
            SourceModule = row.SourceModule,
            SourceReferenceType = row.SourceReferenceType,
            SourceReferenceId = row.SourceReferenceId,
            PrintedAt = row.PrintedAt,
            Items = row.Items.Select(i => new BarcodePrintHistoryItemDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                VariantSku = i.VariantSku,
                BarcodeValue = i.BarcodeValue,
                VariantAttributes = i.VariantAttributes,
                SellingPrice = i.SellingPrice,
                QuantityPrinted = i.QuantityPrinted
            }).ToList()
        };
    }

    private async Task EnsureDefaultTemplatesAsync(long businessId)
    {
        var defaults = BuildDefaultTemplates(businessId);
        foreach (var template in defaults)
        {
            if (await _templateRepository.NameExistsAsync(businessId, template.Name))
            {
                continue;
            }

            try
            {
                await _templateRepository.CreateAsync(template);
            }
            catch (DbUpdateException)
            {
                // Another concurrent request likely created it first.
            }
        }

        var templates = await _templateRepository.GetByBusinessAsync(businessId, includeInactive: true);
        if (templates.Count == 0 || templates.Any(t => t.IsDefault))
        {
            return;
        }

        var firstTemplate = templates.OrderBy(t => t.Id).First();
        firstTemplate.IsDefault = true;
        firstTemplate.UpdatedAt = DateTime.UtcNow;
        await _templateRepository.UpdateAsync(firstTemplate);
    }

    private static List<BarcodeTemplate> BuildDefaultTemplates(long businessId)
    {
        return
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
    }

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
