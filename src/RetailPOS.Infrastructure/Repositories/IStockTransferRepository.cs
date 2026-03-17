using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Interface for Stock Transfer repository operations
/// </summary>
public interface IStockTransferRepository
{
    Task<StockTransfer?> GetByIdAsync(long id);
    Task<IEnumerable<StockTransfer>> GetAllAsync(string? status = null, long? fromLocationId = null, long? toLocationId = null);
    Task<(IEnumerable<StockTransfer>, int)> SearchAsync(
        string? status,
        long? fromLocationId,
        string? fromLocationType,
        long? toLocationId,
        string? toLocationType,
        int pageNumber,
        int pageSize);
    Task<StockTransfer> CreateAsync(StockTransfer transfer);
    Task<StockTransfer> UpdateAsync(StockTransfer transfer);
    Task<bool> UpdateStatusAsync(long id, string status, long? approvedBy = null);
}
