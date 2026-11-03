using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Supplier;
using RetailPOS.API.Models;
using RetailPOS.Application.Suppliers;
using RetailPOS.Application.Suppliers.Commands;
using RetailPOS.Application.Suppliers.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "suppliers.view")]
public class SuppliersController : ControllerBase
{
    private readonly ILogger<SuppliersController> _logger;
    private readonly ISender _sender;

    public SuppliersController(
        ILogger<SuppliersController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Get all suppliers
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SupplierDto>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var results = await _sender.Send(new GetSuppliersQuery(), cancellationToken);
        var suppliers = results.Select(MapSupplier).ToList();
        return Ok(ApiResponse<List<SupplierDto>>.SuccessResponse(suppliers));
    }

    /// <summary>
    /// Search suppliers with filters and pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<SupplierListDto>>> Search(
        [FromBody] SupplierSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchSuppliersQuery(
                searchDto.SearchQuery,
                searchDto.MinCreditLimit,
                searchDto.MaxCreditLimit,
                searchDto.PageNumber,
                searchDto.PageSize,
                searchDto.SortBy,
                searchDto.SortOrder),
            cancellationToken);
        var response = new SupplierListDto
        {
            Suppliers = result.Suppliers.Select(MapSupplier).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
        return Ok(ApiResponse<SupplierListDto>.SuccessResponse(response));
    }

    /// <summary>
    /// Get supplier by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var supplier = await _sender.Send(new GetSupplierByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<SupplierDto>.SuccessResponse(MapSupplier(supplier)));
    }

    /// <summary>
    /// Create new supplier
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "suppliers.create")]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> Create(
        [FromBody] CreateSupplierDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateSupplierCommand(new SupplierWriteModel(dto.Name, dto.Contact, dto.Address, dto.CreditLimit)),
            cancellationToken);
        var supplier = MapSupplier(result);
        _logger.LogInformation("Supplier created: {SupplierName} (ID: {SupplierId})", supplier.Name, supplier.Id);
        return CreatedAtAction(
            nameof(GetById),
            new { id = supplier.Id },
            ApiResponse<SupplierDto>.SuccessResponse(supplier, "Supplier created successfully"));
    }

    /// <summary>
    /// Update supplier
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "suppliers.edit")]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> Update(
        long id,
        [FromBody] UpdateSupplierDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdateSupplierCommand(
                id,
                new SupplierWriteModel(dto.Name, dto.Contact, dto.Address, dto.CreditLimit)),
            cancellationToken);
        var supplier = MapSupplier(result);
        _logger.LogInformation("Supplier updated: {SupplierName} (ID: {SupplierId})", supplier.Name, supplier.Id);
        return Ok(ApiResponse<SupplierDto>.SuccessResponse(supplier, "Supplier updated successfully"));
    }

    /// <summary>
    /// Delete supplier
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "suppliers.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        var supplier = await _sender.Send(new DeleteSupplierCommand(id), cancellationToken);
        _logger.LogInformation("Supplier deleted: {SupplierName} (ID: {SupplierId})", supplier.Name, id);
        return Ok(ApiResponse.SuccessResponse("Supplier deleted successfully"));
    }

    /// <summary>
    /// Get supplier performance metrics
    /// </summary>
    [HttpGet("{id}/performance")]
    public async Task<ActionResult<ApiResponse<SupplierPerformanceDto>>> GetPerformance(
        long id,
        CancellationToken cancellationToken)
    {
        var performance = await _sender.Send(new GetSupplierPerformanceQuery(id), cancellationToken);
        return Ok(ApiResponse<SupplierPerformanceDto>.SuccessResponse(MapPerformance(performance)));
    }

    private static SupplierDto MapSupplier(SupplierReadModel supplier) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        Contact = supplier.Contact,
        Address = supplier.Address,
        CreditLimit = supplier.CreditLimit,
        CreatedAt = supplier.CreatedAt,
        UpdatedAt = supplier.UpdatedAt
    };

    private static SupplierPerformanceDto MapPerformance(SupplierPerformanceReadModel performance) => new()
    {
        Id = performance.Id,
        Name = performance.Name,
        TotalPurchaseOrders = performance.TotalPurchaseOrders,
        TotalBills = performance.TotalBills,
        TotalPurchaseAmount = performance.TotalPurchaseAmount,
        OutstandingBalance = performance.OutstandingBalance,
        CreditLimit = performance.CreditLimit,
        CreditUtilizationPercentage = performance.CreditUtilizationPercentage,
        HealthStatus = performance.HealthStatus
    };
}
