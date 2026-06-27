using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Supplier;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "suppliers.view")]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;
    private readonly ILogger<SuppliersController> _logger;

    public SuppliersController(
        ISupplierService supplierService,
        ILogger<SuppliersController> logger)
    {
        _supplierService = supplierService;
        _logger = logger;
    }

    /// <summary>
    /// Get all suppliers
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SupplierDto>>>> GetAll()
    {
        var suppliers = await _supplierService.GetAllSuppliersAsync();
        return Ok(ApiResponse<List<SupplierDto>>.SuccessResponse(suppliers));
    }

    /// <summary>
    /// Search suppliers with filters and pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<SupplierListDto>>> Search([FromBody] SupplierSearchDto searchDto)
    {
        var result = await _supplierService.SearchSuppliersAsync(searchDto);
        return Ok(ApiResponse<SupplierListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get supplier by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> GetById(long id)
    {
        var supplier = await _supplierService.GetSupplierByIdAsync(id);
        return Ok(ApiResponse<SupplierDto>.SuccessResponse(supplier));
    }

    /// <summary>
    /// Create new supplier
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "suppliers.create")]
    public async Task<ActionResult<ApiResponse<SupplierDto>>> Create([FromBody] CreateSupplierDto dto)
    {
        var supplier = await _supplierService.CreateSupplierAsync(dto);
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
    public async Task<ActionResult<ApiResponse<SupplierDto>>> Update(long id, [FromBody] UpdateSupplierDto dto)
    {
        var supplier = await _supplierService.UpdateSupplierAsync(id, dto);
        return Ok(ApiResponse<SupplierDto>.SuccessResponse(supplier, "Supplier updated successfully"));
    }

    /// <summary>
    /// Delete supplier
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "suppliers.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _supplierService.DeleteSupplierAsync(id);
        return Ok(ApiResponse.SuccessResponse("Supplier deleted successfully"));
    }

    /// <summary>
    /// Get supplier performance metrics
    /// </summary>
    [HttpGet("{id}/performance")]
    public async Task<ActionResult<ApiResponse<SupplierPerformanceDto>>> GetPerformance(long id)
    {
        var performance = await _supplierService.GetSupplierPerformanceAsync(id);
        return Ok(ApiResponse<SupplierPerformanceDto>.SuccessResponse(performance));
    }
}
