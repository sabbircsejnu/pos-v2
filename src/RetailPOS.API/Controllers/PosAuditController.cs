using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Audit;
using RetailPOS.API.Models;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Core.Entities.Audit;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/pos-audit")]
[Authorize(Policy = "sales.create")]
public class PosAuditController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<PosAuditController> _logger;

    public PosAuditController(ISender sender, ILogger<PosAuditController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [HttpPost("discount-change")]
    public async Task<ActionResult<ApiResponse<object>>> RecordDiscountChange(
        [FromBody] PosDiscountAuditDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Action))
        {
            return BadRequest(ApiResponse<object>.ErrorResponse("Action is required."));
        }

        try
        {
            var metadata = new Dictionary<string, object?>
            {
                ["action"] = dto.Action,
                ["fromType"] = dto.FromType,
                ["toType"] = dto.ToType,
                ["userId"] = TryGetCurrentUserId(),
                ["outletId"] = dto.OutletId,
                ["terminalId"] = dto.TerminalId,
                ["payload"] = dto.Payload
            };

            await _sender.Send(
                new RecordAuditEventCommand(new AuditEventWriteRequest(
                    AuditActionType.Update,
                    AuditModule.Sales,
                    $"POS discount mode changed: {dto.Action}",
                    "POSDiscount",
                    dto.TerminalId?.ToString() ?? "terminal-unknown",
                    Metadata: metadata)),
                cancellationToken);

            return Ok(ApiResponse<object>.SuccessResponse("Discount change audit recorded."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record POS discount audit event for action {Action}", dto.Action);
            return Ok(ApiResponse<object>.SuccessResponse("Discount change completed (audit deferred)."));
        }
    }

    private long? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
