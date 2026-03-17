using RetailPOS.API.DTOs.StockTransfer;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for Stock Transfer service operations
/// </summary>
public interface IStockTransferService
{
    Task<StockTransferDto> GetByIdAsync(long id);
    Task<List<StockTransferDto>> GetAllAsync(string? status = null, long? fromLocationId = null, long? toLocationId = null);
    Task<StockTransferListDto> SearchAsync(StockTransferSearchDto searchDto);
    Task<StockTransferDto> CreateAsync(CreateStockTransferDto dto, long? userId = null);
    Task<StockTransferDto> UpdateAsync(long id, CreateStockTransferDto dto);
    Task<StockTransferDto> ApproveAsync(long id, long? approverId = null);
    Task<StockTransferDto> RejectAsync(long id, string? reason = null);
    Task<StockTransferDto> SendAsync(long id);
    Task<StockTransferDto> ReceiveAsync(long id);
    Task<StockTransferDto> CancelAsync(long id, string? reason = null);
}
