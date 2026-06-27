using RetailPOS.API.DTOs.Product;

namespace RetailPOS.API.DTOs.Barcode;

public class BarcodeTemplateFieldDto
{
    public long Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public decimal? X { get; set; }
    public decimal? Y { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? FontSize { get; set; }
    public string? FontWeight { get; set; }
    public string? Align { get; set; }
}

public class BarcodeTemplateDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string PaperType { get; set; } = string.Empty;
    public decimal LabelWidthMm { get; set; }
    public decimal LabelHeightMm { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<BarcodeTemplateFieldDto> Fields { get; set; } = new();
}

public class UpsertBarcodeTemplateFieldDto
{
    public string FieldKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public decimal? X { get; set; }
    public decimal? Y { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? FontSize { get; set; }
    public string? FontWeight { get; set; }
    public string? Align { get; set; }
}

public class CreateBarcodeTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = "retail";
    public string PaperType { get; set; } = "label";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public List<UpsertBarcodeTemplateFieldDto> Fields { get; set; } = new();
}

public class UpdateBarcodeTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string TemplateType { get; set; } = "retail";
    public string PaperType { get; set; } = "label";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public List<UpsertBarcodeTemplateFieldDto> Fields { get; set; } = new();
}

public class BarcodePrintHistoryItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string? BarcodeValue { get; set; }
    public string? VariantAttributes { get; set; }
    public decimal? SellingPrice { get; set; }
    public int QuantityPrinted { get; set; }
}

public class BarcodePrintHistoryDto
{
    public long Id { get; set; }
    public long? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public long? PrintedByUserId { get; set; }
    public string? PrintedByUserName { get; set; }
    public string PrintMode { get; set; } = string.Empty;
    public decimal LabelWidthMm { get; set; }
    public decimal LabelHeightMm { get; set; }
    public int TotalLabels { get; set; }
    public string? SourceModule { get; set; }
    public string? SourceReferenceType { get; set; }
    public long? SourceReferenceId { get; set; }
    public DateTime PrintedAt { get; set; }
    public List<BarcodePrintHistoryItemDto> Items { get; set; } = new();
}

public class BarcodePrintHistorySearchDto
{
    public long? TemplateId { get; set; }
    public long? PrintedByUserId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class BarcodePrintHistoryListDto
{
    public List<BarcodePrintHistoryDto> Rows { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class CreateBarcodePrintHistoryItemDto
{
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string? BarcodeValue { get; set; }
    public string? VariantAttributes { get; set; }
    public decimal? SellingPrice { get; set; }
    public int QuantityPrinted { get; set; }
}

public class RecordBarcodePrintHistoryDto
{
    public long? OutletId { get; set; }
    public long? TemplateId { get; set; }
    public string? CompanyName { get; set; }
    public string PrintMode { get; set; } = "pdf";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    public string? SourceModule { get; set; }
    public string? SourceReferenceType { get; set; }
    public long? SourceReferenceId { get; set; }
    public List<CreateBarcodePrintHistoryItemDto> Items { get; set; } = new();
}

public class BarcodeVariantSearchResultDto
{
    public List<ProductVariantSearchDto> Variants { get; set; } = new();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
