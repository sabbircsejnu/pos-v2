using RetailPOS.API.DTOs.Bill;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Bill business logic
/// </summary>
public class BillService : IBillService
{
    private readonly IBillRepository _billRepository;
    private readonly ILogger<BillService> _logger;

    public BillService(IBillRepository billRepository, ILogger<BillService> logger)
    {
        _billRepository = billRepository;
        _logger = logger;
    }

    /// <summary>Searches bills with optional status and supplier filters</summary>
    public async Task<BillListDto> SearchAsync(BillSearchDto searchDto)
    {
        var (bills, totalCount) = await _billRepository.SearchAsync(
            searchDto.Status,
            searchDto.SupplierId,
            searchDto.PageNumber,
            searchDto.PageSize);

        return new BillListDto
        {
            Bills = bills.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    /// <summary>Gets a bill by ID</summary>
    public async Task<BillDto> GetByIdAsync(long id)
    {
        var bill = await _billRepository.GetByIdAsync(id);
        if (bill == null)
            throw new KeyNotFoundException($"Bill with ID {id} not found");

        return MapToDto(bill);
    }

    /// <summary>Creates a new bill</summary>
    public async Task<BillDto> CreateAsync(CreateBillDto dto)
    {
        var bill = new Bill
        {
            SupplierId = dto.SupplierId,
            PoId = dto.PoId,
            AmountDue = dto.AmountDue,
            DueDate = dto.DueDate,
            Status = "unpaid"
        };

        var created = await _billRepository.CreateAsync(bill);
        _logger.LogInformation("Bill {BillId} created for supplier {SupplierId}", created.Id, dto.SupplierId);
        return MapToDto(created);
    }

    /// <summary>Updates the payment status of a bill</summary>
    public async Task<BillDto> UpdateStatusAsync(long id, UpdateBillStatusDto dto)
    {
        var bill = await _billRepository.GetByIdAsync(id);
        if (bill == null)
            throw new KeyNotFoundException($"Bill with ID {id} not found");

        var validStatuses = new[] { "unpaid", "partial", "paid" };
        if (!validStatuses.Contains(dto.Status.ToLower()))
            throw new InvalidOperationException($"Invalid status '{dto.Status}'. Must be one of: {string.Join(", ", validStatuses)}");

        bill.Status = dto.Status.ToLower();
        var updated = await _billRepository.UpdateAsync(bill);
        _logger.LogInformation("Bill {BillId} status updated to {Status}", id, dto.Status);
        return MapToDto(updated);
    }

    /// <summary>Returns aggregate summary of unpaid and overdue bills</summary>
    public async Task<BillSummaryDto> GetSummaryAsync()
    {
        var (totalUnpaid, totalOverdue, unpaidCount, overdueCount) = await _billRepository.GetSummaryAsync();

        return new BillSummaryDto
        {
            TotalUnpaid = totalUnpaid,
            TotalOverdue = totalOverdue,
            UnpaidCount = unpaidCount,
            OverdueCount = overdueCount
        };
    }

    private static BillDto MapToDto(Bill b) => new BillDto
    {
        Id = b.Id,
        SupplierId = b.SupplierId,
        SupplierName = b.Supplier?.Name ?? string.Empty,
        PoId = b.PoId,
        AmountDue = b.AmountDue,
        DueDate = b.DueDate,
        Status = b.Status,
        IsOverdue = b.Status != "paid" && b.DueDate < DateTime.UtcNow
    };
}
