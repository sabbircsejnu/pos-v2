using RetailPOS.API.DTOs.StockRequisition;
using RetailPOS.API.DTOs.StockTransfer;

namespace RetailPOS.API.Services;

public interface IStockRequisitionService
{
    Task<StockRequisitionDto> GetByIdAsync(long id);
    Task<List<StockRequisitionDto>> GetAllAsync(string? status = null, long? requestingLocationId = null, long? sourceLocationId = null);
    Task<StockRequisitionListDto> SearchAsync(StockRequisitionSearchDto dto);
    Task<StockRequisitionDto> CreateAsync(CreateStockRequisitionDto dto, long requestedBy);
    Task<StockRequisitionDto> UpdateAsync(long id, CreateStockRequisitionDto dto, long updatedBy);
    Task<StockRequisitionDto> SubmitAsync(long id, long submittedBy);
    Task<StockRequisitionDto> ApproveAsync(long id, long approvedBy);
    Task<StockRequisitionDto> RejectAsync(long id, long rejectedBy, string? reason = null);
    Task<StockTransferDto> ConvertToTransferAsync(long id, long createdBy);
}
