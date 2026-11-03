using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.DTOs.Warehouse;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Application.Outlets;
using RetailPOS.Application.Outlets.Commands;
using RetailPOS.Application.Outlets.Queries;
using RetailPOS.Application.Warehouses;
using RetailPOS.Application.Warehouses.Commands;
using RetailPOS.Application.Warehouses.Queries;
using MediatR;
using RetailPOS.Core.Entities.Audit;

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
    private readonly ILogger<BusinessSetupController> _logger;
    private readonly ISender _sender;

    public BusinessSetupController(
        ILogger<BusinessSetupController> logger,
        ISender sender)
    {
        _logger = logger;
        _sender = sender;
    }

    // ── Outlets ────────────────────────────────────────────────────────────────

    [HttpGet("outlets")]
    public async Task<ActionResult<ApiResponse<IEnumerable<OutletDto>>>> GetOutlets(
        [FromRoute] long businessId,
        CancellationToken cancellationToken)
    {
        var results = await _sender.Send(new GetBusinessOutletsQuery(businessId), cancellationToken);
        var outlets = results.Select(outlet => new OutletDto
        {
            Id = outlet.Id,
            Name = outlet.Name,
            Address = outlet.Address,
            ContactNumber = outlet.ContactNumber,
            ManagerId = outlet.ManagerId,
            ManagerName = outlet.ManagerName,
            UserCount = outlet.UserCount,
            CreatedAt = outlet.CreatedAt,
            UpdatedAt = outlet.UpdatedAt
        });
        return Ok(ApiResponse<IEnumerable<OutletDto>>.SuccessResponse(outlets, "Outlets retrieved successfully"));
    }

    [HttpPost("outlets")]
    public async Task<ActionResult<ApiResponse<OutletDto>>> CreateOutlet(
        [FromRoute] long businessId,
        [FromBody] CreateOutletDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sender.Send(
                new CreateBusinessOutletCommand(
                    businessId,
                    dto.Name,
                    dto.Address,
                    dto.ContactNumber,
                    dto.ManagerId),
                cancellationToken);
            var outlet = MapOutlet(result);

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Create,
                    AuditModule.BusinessSetup,
                    $"Outlet '{outlet.Name}' created for business {businessId}",
                    "Outlet",
                    outlet.Id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Outlet",
                        outlet.Id.ToString(),
                        AuditOperationType.Insert,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

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
        [FromBody] UpdateOutletDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sender.Send(
                new UpdateBusinessOutletCommand(
                    businessId,
                    id,
                    dto.Name,
                    dto.Address,
                    dto.ContactNumber,
                    dto.ManagerId),
                cancellationToken);
            var outlet = MapOutlet(result);

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Update,
                    AuditModule.BusinessSetup,
                    $"Outlet '{outlet.Name}' updated for business {businessId}",
                    "Outlet",
                    outlet.Id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Outlet",
                        outlet.Id.ToString(),
                        AuditOperationType.Update,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

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
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _sender.Send(
                new DeleteBusinessOutletCommand(businessId, id),
                cancellationToken);
            if (!deleted)
                return NotFound(new ApiResponse<object> { Success = false, Message = $"Outlet {id} not found in business {businessId}" });

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Delete,
                    AuditModule.BusinessSetup,
                    $"Outlet {id} deleted from business {businessId}",
                    "Outlet",
                    id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Outlet",
                        id.ToString(),
                        AuditOperationType.Delete,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

            return Ok(ApiResponse<object>.SuccessResponse("Outlet deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

    private static OutletDto MapOutlet(OutletReadModel outlet) => new()
    {
        Id = outlet.Id,
        Name = outlet.Name,
        Address = outlet.Address,
        ContactNumber = outlet.ContactNumber,
        ManagerId = outlet.ManagerId,
        ManagerName = outlet.ManagerName,
        UserCount = outlet.UserCount,
        CreatedAt = outlet.CreatedAt,
        UpdatedAt = outlet.UpdatedAt
    };

    // ── Warehouses ─────────────────────────────────────────────────────────────

    [HttpGet("warehouses")]
    public async Task<ActionResult<ApiResponse<IEnumerable<WarehouseDto>>>> GetWarehouses(
        [FromRoute] long businessId,
        CancellationToken cancellationToken)
    {
        var warehouses = await _sender.Send(new GetBusinessWarehousesQuery(businessId), cancellationToken);
        var dtos = warehouses.Select(warehouse => new WarehouseDto
        {
            Id = warehouse.Id,
            Name = warehouse.Name,
            Address = warehouse.Address,
            Capacity = warehouse.Capacity,
            ManagerId = warehouse.ManagerId,
            ManagerName = warehouse.ManagerName,
            PurchaseOrderCount = warehouse.PurchaseOrderCount,
            CreatedAt = warehouse.CreatedAt,
            UpdatedAt = warehouse.UpdatedAt
        });
        return Ok(ApiResponse<IEnumerable<WarehouseDto>>.SuccessResponse(dtos, "Warehouses retrieved successfully"));
    }

    [HttpPost("warehouses")]
    public async Task<ActionResult<ApiResponse<WarehouseDto>>> CreateWarehouse(
        [FromRoute] long businessId,
        [FromBody] CreateWarehouseDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sender.Send(
                new CreateBusinessWarehouseCommand(
                    businessId,
                    dto.Name,
                    dto.Address,
                    dto.Capacity,
                    dto.ManagerId),
                cancellationToken);
            var warehouse = MapWarehouse(result);
            _logger.LogInformation(
                "Business setup: Warehouse created for business {BusinessId}: ID={Id}, Name={Name}",
                businessId,
                warehouse.Id,
                warehouse.Name);

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Create,
                    AuditModule.BusinessSetup,
                    $"Warehouse '{warehouse.Name}' created for business {businessId}",
                    "Warehouse",
                    warehouse.Id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Warehouse",
                        warehouse.Id.ToString(),
                        AuditOperationType.Insert,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

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
        [FromBody] UpdateWarehouseDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sender.Send(
                new UpdateBusinessWarehouseCommand(
                    businessId,
                    id,
                    dto.Name,
                    dto.Address,
                    dto.Capacity,
                    dto.ManagerId),
                cancellationToken);
            var warehouse = MapWarehouse(result);
            _logger.LogInformation(
                "Business setup: Warehouse updated for business {BusinessId}: ID={Id}",
                businessId,
                warehouse.Id);

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Update,
                    AuditModule.BusinessSetup,
                    $"Warehouse '{warehouse.Name}' updated for business {businessId}",
                    "Warehouse",
                    warehouse.Id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Warehouse",
                        warehouse.Id.ToString(),
                        AuditOperationType.Update,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

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
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _sender.Send(
                new DeleteBusinessWarehouseCommand(businessId, id),
                cancellationToken);
            if (!deleted)
                return NotFound(new ApiResponse<object> { Success = false, Message = $"Warehouse {id} not found in business {businessId}" });

            _logger.LogInformation(
                "Business setup: Warehouse deleted from business {BusinessId}: ID={Id}",
                businessId,
                id);
            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Delete,
                    AuditModule.BusinessSetup,
                    $"Warehouse {id} deleted from business {businessId}",
                    "Warehouse",
                    id.ToString(),
                    [new AuditEventEntityWriteRequest(
                        "Warehouse",
                        id.ToString(),
                        AuditOperationType.Delete,
                        $"{{\"businessId\":{businessId}}}")])),
                cancellationToken);

            return Ok(ApiResponse<object>.SuccessResponse("Warehouse deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

    private static WarehouseDto MapWarehouse(WarehouseReadModel warehouse) => new()
    {
        Id = warehouse.Id,
        Name = warehouse.Name,
        Address = warehouse.Address,
        Capacity = warehouse.Capacity,
        ManagerId = warehouse.ManagerId,
        ManagerName = warehouse.ManagerName,
        PurchaseOrderCount = warehouse.PurchaseOrderCount,
        CreatedAt = warehouse.CreatedAt,
        UpdatedAt = warehouse.UpdatedAt
    };
}
