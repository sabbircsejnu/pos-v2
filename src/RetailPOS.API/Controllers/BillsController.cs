using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Bill;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "accounts.view")]
public class BillsController : ControllerBase
{
    private readonly IBillService _billService;
    private readonly ILogger<BillsController> _logger;

    public BillsController(IBillService billService, ILogger<BillsController> logger)
    {
        _billService = billService;
        _logger = logger;
    }

    /// <summary>List bills with optional status filter</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<BillListDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] long? supplierId)
    {
        var searchDto = new BillSearchDto { Status = status, SupplierId = supplierId };
        var result = await _billService.SearchAsync(searchDto);
        return Ok(ApiResponse<BillListDto>.SuccessResponse(result));
    }

    /// <summary>Paginated bill search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<BillListDto>>> Search([FromBody] BillSearchDto searchDto)
    {
        var result = await _billService.SearchAsync(searchDto);
        return Ok(ApiResponse<BillListDto>.SuccessResponse(result));
    }

    /// <summary>Get bill by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BillDto>>> GetById(long id)
    {
        var bill = await _billService.GetByIdAsync(id);
        return Ok(ApiResponse<BillDto>.SuccessResponse(bill));
    }

    /// <summary>Create a new bill</summary>
    [HttpPost]
    [Authorize(Policy = "accounts.create")]
    public async Task<ActionResult<ApiResponse<BillDto>>> Create([FromBody] CreateBillDto dto)
    {
        var bill = await _billService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = bill.Id },
            ApiResponse<BillDto>.SuccessResponse(bill, "Bill created successfully"));
    }

    /// <summary>Update the payment status of a bill</summary>
    [HttpPut("{id}/status")]
    [Authorize(Policy = "accounts.edit")]
    public async Task<ActionResult<ApiResponse<BillDto>>> UpdateStatus(long id, [FromBody] UpdateBillStatusDto dto)
    {
        var bill = await _billService.UpdateStatusAsync(id, dto);
        return Ok(ApiResponse<BillDto>.SuccessResponse(bill, "Bill status updated successfully"));
    }

    /// <summary>Get summary of unpaid and overdue bills</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<BillSummaryDto>>> GetSummary()
    {
        var summary = await _billService.GetSummaryAsync();
        return Ok(ApiResponse<BillSummaryDto>.SuccessResponse(summary));
    }
}
