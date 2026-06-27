using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.DTOs.Warehouse;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Super Admin business setup endpoints — outlets and warehouses scoped by BusinessId.
/// All operations are tenant-isolated via the businessId route parameter.
/// </summary>
[ApiController]
[Route("api/businesses/{businessId:long}")]
[Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]
public class BusinessSetupController : ControllerBase
{
    private readonly IOutletService _outletService;
    private readonly IWarehouseService _warehouseService;
    private readonly IAuditService _auditSvc;
    private readonly ILogger<BusinessSetupController> _logger;

    public BusinessSetupController(
        IOutletService outletService,
        IWarehouseService warehouseService,
        IAuditService auditSvc,
        ILogger<BusinessSetupController> logger)
    {
        _outletService = outletService;
        _warehouseService = warehouseService;
        _auditSvc = auditSvc;
        _logger = logger;
    }

    // ── Outlets ────────────────────────────────────────────────────────────────

    [HttpGet("outlets")]
    public async Task<ActionResult<ApiResponse<IEnumerable<OutletDto>>>> GetOutlets([FromRoute] long businessId)
    {
        var outlets = await _outletService.GetByBusinessIdAsync(businessId);
        return Ok(ApiResponse<IEnumerable<OutletDto>>.SuccessResponse(outlets, "Outlets retrieved successfully"));
    }

    [HttpPost("outlets")]
    public async Task<ActionResult<ApiResponse<OutletDto>>> CreateOutlet(
        [FromRoute] long businessId,
        [FromBody] CreateOutletDto dto)
    {
        try
        {
            var outlet = await _outletService.CreateForBusinessAsync(businessId, dto);

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.BusinessSetup,
                Summary = $"Outlet '{outlet.Name}' created for business {businessId}",
                PrimaryEntity = ("Outlet", outlet.Id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Outlet", outlet.Id.ToString(), AuditOperationType.Insert,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return CreatedAtAction(
                nameof(GetOutlets),
                new { businessId },
                ApiResponse<OutletDto>.SuccessResponse(outlet, "Outlet created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<OutletDto> { Success = false, Message = ex.Message });
        }
    }

    [HttpPut("outlets/{id:long}")]
    public async Task<ActionResult<ApiResponse<OutletDto>>> UpdateOutlet(
        [FromRoute] long businessId,
        [FromRoute] long id,
        [FromBody] UpdateOutletDto dto)
    {
        try
        {
            var outlet = await _outletService.UpdateForBusinessAsync(businessId, id, dto);

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.BusinessSetup,
                Summary = $"Outlet '{outlet.Name}' updated for business {businessId}",
                PrimaryEntity = ("Outlet", outlet.Id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Outlet", outlet.Id.ToString(), AuditOperationType.Update,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return Ok(ApiResponse<OutletDto>.SuccessResponse(outlet, "Outlet updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found"))
                return NotFound(new ApiResponse<OutletDto> { Success = false, Message = ex.Message });
            return BadRequest(new ApiResponse<OutletDto> { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("outlets/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteOutlet(
        [FromRoute] long businessId,
        [FromRoute] long id)
    {
        try
        {
            var deleted = await _outletService.DeleteForBusinessAsync(businessId, id);
            if (!deleted)
                return NotFound(new ApiResponse<object> { Success = false, Message = $"Outlet {id} not found in business {businessId}" });

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Delete,
                Module = AuditModule.BusinessSetup,
                Summary = $"Outlet {id} deleted from business {businessId}",
                PrimaryEntity = ("Outlet", id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Outlet", id.ToString(), AuditOperationType.Delete,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return Ok(ApiResponse<object>.SuccessResponse("Outlet deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

    // ── Warehouses ─────────────────────────────────────────────────────────────

    [HttpGet("warehouses")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WarehouseDto>>>> GetWarehouses([FromRoute] long businessId)
    {
        var warehouses = await _warehouseService.GetByBusinessIdAsync(businessId);
        return Ok(ApiResponse<IEnumerable<WarehouseDto>>.SuccessResponse(warehouses, "Warehouses retrieved successfully"));
    }

    [HttpPost("warehouses")]
    public async Task<ActionResult<ApiResponse<WarehouseDto>>> CreateWarehouse(
        [FromRoute] long businessId,
        [FromBody] CreateWarehouseDto dto)
    {
        try
        {
            var warehouse = await _warehouseService.CreateForBusinessAsync(businessId, dto);

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.BusinessSetup,
                Summary = $"Warehouse '{warehouse.Name}' created for business {businessId}",
                PrimaryEntity = ("Warehouse", warehouse.Id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Warehouse", warehouse.Id.ToString(), AuditOperationType.Insert,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return CreatedAtAction(
                nameof(GetWarehouses),
                new { businessId },
                ApiResponse<WarehouseDto>.SuccessResponse(warehouse, "Warehouse created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<WarehouseDto> { Success = false, Message = ex.Message });
        }
    }

    [HttpPut("warehouses/{id:long}")]
    public async Task<ActionResult<ApiResponse<WarehouseDto>>> UpdateWarehouse(
        [FromRoute] long businessId,
        [FromRoute] long id,
        [FromBody] UpdateWarehouseDto dto)
    {
        try
        {
            var warehouse = await _warehouseService.UpdateForBusinessAsync(businessId, id, dto);

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.BusinessSetup,
                Summary = $"Warehouse '{warehouse.Name}' updated for business {businessId}",
                PrimaryEntity = ("Warehouse", warehouse.Id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Warehouse", warehouse.Id.ToString(), AuditOperationType.Update,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return Ok(ApiResponse<WarehouseDto>.SuccessResponse(warehouse, "Warehouse updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found"))
                return NotFound(new ApiResponse<WarehouseDto> { Success = false, Message = ex.Message });
            return BadRequest(new ApiResponse<WarehouseDto> { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("warehouses/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteWarehouse(
        [FromRoute] long businessId,
        [FromRoute] long id)
    {
        try
        {
            var deleted = await _warehouseService.DeleteForBusinessAsync(businessId, id);
            if (!deleted)
                return NotFound(new ApiResponse<object> { Success = false, Message = $"Warehouse {id} not found in business {businessId}" });

            await _auditSvc.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Delete,
                Module = AuditModule.BusinessSetup,
                Summary = $"Warehouse {id} deleted from business {businessId}",
                PrimaryEntity = ("Warehouse", id.ToString()),
                Entities =
                [
                    new AuditEntityInput("Warehouse", id.ToString(), AuditOperationType.Delete,
                        metadata: $"{{\"businessId\":{businessId}}}")
                ]
            });

            return Ok(ApiResponse<object>.SuccessResponse("Warehouse deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }
}
