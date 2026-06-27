using RetailPOS.API.DTOs.StockCount;
using Microsoft.AspNetCore.Http;

namespace RetailPOS.API.Services;

public interface IStockCountService
{
    Task<StockCountListDto> SearchAsync(StockCountSearchDto dto);
    Task<StockCountDto> GetByIdAsync(long id);
    Task<StockCountDto> CreateAsync(CreateStockCountDto dto, long createdBy);
    Task<(byte[] Content, string FileName)> DownloadExcelAsync(long id);
    Task<StockCountPrintDto> GetPrintDataAsync(long id);

    Task<StockCountDto> UploadAsync(long id, long userId, IFormFile file);
    Task<StockCountDto> SubmitAsync(long id, long userId);
    Task<StockCountDto> ApproveAsync(long id, long userId);
    Task<StockCountDto> RejectAsync(long id, long userId, string? reason);
    Task<StockCountDto> ReopenAsync(long id, long userId);
    Task<StockCountDto> GenerateAdjustmentDraftAsync(long id, long userId);
}
