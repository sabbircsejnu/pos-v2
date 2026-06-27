using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.DTOs.Product;

namespace RetailPOS.API.Services;

public interface IBarcodeService
{
    Task<List<ProductVariantSearchDto>> SearchVariantsAsync(
        string query,
        int pageNumber,
        int pageSize,
        long? locationId,
        string? locationType);

    Task<List<BarcodeTemplateDto>> GetTemplatesAsync(bool includeInactive = false);
    Task<BarcodeTemplateDto> GetTemplateByIdAsync(long id);
    Task<BarcodeTemplateDto> CreateTemplateAsync(CreateBarcodeTemplateDto dto, long? userId);
    Task<BarcodeTemplateDto> UpdateTemplateAsync(long id, UpdateBarcodeTemplateDto dto, long? userId);
    Task SetDefaultTemplateAsync(long id, long? userId);
    Task DeleteTemplateAsync(long id);

    Task<BarcodePrintHistoryDto> RecordPrintHistoryAsync(RecordBarcodePrintHistoryDto dto, long? userId);
    Task<BarcodePrintHistoryDto> GetPrintHistoryByIdAsync(long id);
    Task<BarcodePrintHistoryListDto> SearchPrintHistoryAsync(BarcodePrintHistorySearchDto dto);
}
