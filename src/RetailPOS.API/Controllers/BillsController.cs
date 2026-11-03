using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Bill;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Bills;
using RetailPOS.Application.Bills.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "accounts.view")]
public class BillsController : ControllerBase
{
    private readonly ILogger<BillsController> _logger;
    private readonly ISender _sender;

    public BillsController(ILogger<BillsController> logger, ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>List bills with optional status filter</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<BillListDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] long? supplierId,
        CancellationToken cancellationToken)
    {
        var searchDto = new BillSearchDto { Status = status, SupplierId = supplierId };
        var result = await _sender.Send(
            new SearchBillsQuery(
                searchDto.Status,
                searchDto.SupplierId,
                searchDto.PageNumber,
                searchDto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<BillListDto>.SuccessResponse(MapList(result, searchDto)));
    }

    /// <summary>Paginated bill search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<BillListDto>>> Search(
        [FromBody] BillSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchBillsQuery(
                searchDto.Status,
                searchDto.SupplierId,
                searchDto.PageNumber,
                searchDto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<BillListDto>.SuccessResponse(MapList(result, searchDto)));
    }

    /// <summary>Get bill by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BillDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var bill = await _sender.Send(new GetBillByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<BillDto>.SuccessResponse(MapBill(bill)));
    }

    /// <summary>Create a new bill</summary>
    [HttpPost]
    [Authorize(Policy = "accounts.create")]
    public async Task<ActionResult<ApiResponse<BillDto>>> Create([FromBody] CreateBillDto dto)
    {
        _logger.LogWarning("Blocked legacy Bill create request. Supplier Payable Documents/AP Journal Engine path is authoritative.");
        return Conflict(ApiResponse<BillDto>.ErrorResponse("Legacy Bill writes are disabled. Use Supplier Payable Documents and the Journal Engine."));
    }

    /// <summary>Update the payment status of a bill</summary>
    [HttpPut("{id}/status")]
    [Authorize(Policy = "accounts.edit")]
    public async Task<ActionResult<ApiResponse<BillDto>>> UpdateStatus(long id, [FromBody] UpdateBillStatusDto dto)
    {
        _logger.LogWarning("Blocked legacy Bill status update request for bill {BillId}. Supplier Payable Documents/AP Journal Engine path is authoritative.", id);
        return Conflict(ApiResponse<BillDto>.ErrorResponse("Legacy Bill writes are disabled. Use Supplier Payable Documents and the Journal Engine."));
    }

    /// <summary>Get summary of unpaid and overdue bills</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<BillSummaryDto>>> GetSummary(
        CancellationToken cancellationToken)
    {
        var summary = await _sender.Send(new GetBillSummaryQuery(), cancellationToken);
        return Ok(ApiResponse<BillSummaryDto>.SuccessResponse(MapSummary(summary)));
    }

    private static BillListDto MapList(BillSearchReadModel result, BillSearchDto request) => new()
    {
        Bills = result.Bills.Select(MapBill).ToList(),
        TotalCount = result.TotalCount,
        PageNumber = request.PageNumber,
        PageSize = request.PageSize
    };

    private static BillDto MapBill(BillReadModel bill) => new()
    {
        Id = bill.Id,
        SupplierId = bill.SupplierId,
        SupplierName = bill.SupplierName,
        PoId = bill.PurchaseOrderId,
        AmountDue = bill.AmountDue,
        DueDate = bill.DueDate,
        Status = bill.Status,
        IsOverdue = bill.IsOverdue
    };

    private static BillSummaryDto MapSummary(BillSummaryReadModel summary) => new()
    {
        TotalUnpaid = summary.TotalUnpaid,
        TotalOverdue = summary.TotalOverdue,
        UnpaidCount = summary.UnpaidCount,
        OverdueCount = summary.OverdueCount
    };
}
