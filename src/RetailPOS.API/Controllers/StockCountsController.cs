using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.StockCount;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Inventory.StockCounts.Commands;
using RetailPOS.Application.Inventory.StockCounts.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-counts")]
[Authorize]
public class StockCountsController : ControllerBase
{
    private const string StockCountPhase3FeatureKey = "stockcount.phase3";

    public sealed class UploadStockCountRequest
    {
        public IFormFile File { get; set; } = default!;
    }

    public sealed class RejectStockCountRequest
    {
        public string? Reason { get; set; }
    }

    private readonly IFeatureEntitlementService _featureEntitlement;
    private readonly IConfiguration _configuration;
    private readonly ISender _sender;
    private readonly IStockCountSearchReadScopeProvider _scopeProvider;
    private readonly IStockCountAsyncAdjustmentSwitch _asyncAdjustment;
    private readonly IRoleSwitchContext _roleSwitch;

    public StockCountsController(
        IFeatureEntitlementService featureEntitlement,
        IConfiguration configuration,
        ISender sender,
        IStockCountSearchReadScopeProvider scopeProvider,
        IStockCountAsyncAdjustmentSwitch asyncAdjustment,
        IRoleSwitchContext roleSwitch)
    {
        _featureEntitlement = featureEntitlement;
        _configuration = configuration;
        _sender = sender;
        _scopeProvider = scopeProvider;
        _asyncAdjustment = asyncAdjustment;
        _roleSwitch = roleSwitch;
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockCountListDto>>> Search(
        [FromBody] StockCountSearchDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanView())
        {
            return ForbidResponse<StockCountListDto>();
        }

        var result = await _sender.Send(
            new SearchStockCountsQuery(
                dto.Search,
                dto.LocationId,
                dto.LocationType,
                dto.DateFrom,
                dto.DateTo,
                dto.Status,
                dto.PageNumber,
                dto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<StockCountListDto>.SuccessResponse(new StockCountListDto
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            StockCounts = result.StockCounts.Select(item => new StockCountDto
            {
                Id = item.Id,
                StockCountNo = item.StockCountNo,
                BusinessId = item.BusinessId,
                LocationId = item.LocationId,
                LocationType = item.LocationType,
                LocationName = item.LocationName,
                StockCountDate = item.StockCountDate,
                Status = item.Status,
                Remarks = item.Remarks,
                TotalItems = item.TotalItems,
                CreatedBy = item.CreatedBy,
                CreatedByName = item.CreatedByName,
                CreatedAt = item.CreatedAt,
                SubmittedBy = item.SubmittedBy,
                SubmittedAt = item.SubmittedAt,
                ApprovedBy = item.ApprovedBy,
                ApprovedAt = item.ApprovedAt,
                RejectedBy = item.RejectedBy,
                RejectedAt = item.RejectedAt,
                RejectionReason = item.RejectionReason,
                HasPostGenerationMovements = item.HasPostGenerationMovements,
                PostGenerationMovementCount = item.PostGenerationMovementCount,
                LastPostGenerationMovementAt = item.LastPostGenerationMovementAt
            }).ToList()
        }));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        if (!CanView())
        {
            return ForbidResponse<StockCountDto>();
        }

        var result = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(result)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Create(
        [FromBody] CreateStockCountDto dto,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Create"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var scope = await _scopeProvider.GetCreateScopeAsync(
            dto.LocationId,
            dto.LocationType,
            cancellationToken);
        var stockCountId = await _sender.Send(
            new CreateStockCountCommand(
                scope.BusinessId
                    ?? throw new InvalidOperationException("A valid Business is required to create a stock count."),
                scope.LocationId
                    ?? throw new InvalidOperationException("A valid authorized location is required."),
                scope.LocationType
                    ?? throw new InvalidOperationException("A valid authorized location is required."),
                dto.StockCountDate,
                dto.Remarks,
                userId),
            cancellationToken);
        var created = await _sender.Send(new GetStockCountByIdQuery(stockCountId), cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            ApiResponse<StockCountDto>.SuccessResponse(MapDetail(created)));
    }

    private static StockCountDto MapDetail(StockCountDetailReadModel result) => new()
    {
        Id = result.Id,
        StockCountNo = result.StockCountNo,
        BusinessId = result.BusinessId,
        LocationId = result.LocationId,
        LocationType = result.LocationType,
        LocationName = result.LocationName,
        StockCountDate = result.StockCountDate,
        Status = result.Status,
        Remarks = result.Remarks,
        TotalItems = result.TotalItems,
        CreatedBy = result.CreatedBy,
        CreatedByName = result.CreatedByName,
        CreatedAt = result.CreatedAt,
        SubmittedBy = result.SubmittedBy,
        SubmittedAt = result.SubmittedAt,
        ApprovedBy = result.ApprovedBy,
        ApprovedAt = result.ApprovedAt,
        RejectedBy = result.RejectedBy,
        RejectedAt = result.RejectedAt,
        RejectionReason = result.RejectionReason,
        HasPostGenerationMovements = result.HasPostGenerationMovements,
        PostGenerationMovementCount = result.PostGenerationMovementCount,
        LastPostGenerationMovementAt = result.LastPostGenerationMovementAt,
        Lines = result.Lines.Select(line => new StockCountLineDto
        {
            Id = line.Id,
            ProductId = line.ProductId,
            VariantId = line.VariantId,
            ProductName = line.ProductName,
            ProductCode = line.ProductCode,
            VariantName = line.VariantName,
            CurrentStock = line.CurrentStock,
            PhysicalStock = line.PhysicalStock,
            Difference = line.Difference,
            Remarks = line.Remarks
        }).ToList(),
        AdjustmentState = result.AdjustmentJob?.Status ?? result.Status switch
        {
            RetailPOS.Core.Entities.StockCount.StatusAdjustmentGenerated => "DraftGenerated",
            RetailPOS.Core.Entities.StockCount.StatusCompleted => "Completed",
            _ => "None"
        },
        AdjustmentJob = result.AdjustmentJob is null
            ? null
            : new StockCountAdjustmentJobDto
            {
                JobId = result.AdjustmentJob.JobId,
                Status = result.AdjustmentJob.Status,
                IsTerminal = RetailPOS.Core.Entities.StockCountAdjustmentJobStatus.IsTerminal(result.AdjustmentJob.Status),
                AttemptCount = result.AdjustmentJob.AttemptCount,
                NextAttemptAtUtc = result.AdjustmentJob.NextAttemptAtUtc,
                FailureCode = result.AdjustmentJob.FailureCode,
                EvidenceStatus = result.AdjustmentJob.EvidenceStatus,
                BlockedReasonCodes = result.AdjustmentJob.BlockedReasonCodes.ToList(),
                StockAdjustmentId = result.AdjustmentJob.StockAdjustmentId,
                CreatedAtUtc = result.AdjustmentJob.CreatedAtUtc,
                UpdatedAtUtc = result.AdjustmentJob.UpdatedAtUtc,
                CompletedAtUtc = result.AdjustmentJob.CompletedAtUtc
            }
    };

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Download"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.ErrorResponse("Permission denied"));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        var settings = await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken);
        var file = StockCountExcelExporter.Export(stockCount, settings.Company.DateFormat);
        return File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
    }

    [HttpGet("{id:long}/print")]
    public async Task<ActionResult<ApiResponse<StockCountPrintDto>>> Print(
        long id,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Print"))
        {
            return ForbidResponse<StockCountPrintDto>();
        }

        var result = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        var dto = new StockCountPrintDto
        {
            Id = result.Id,
            StockCountNo = result.StockCountNo,
            CompanyName = result.BusinessName,
            LocationName = result.LocationName,
            LocationType = result.LocationType,
            StockCountDate = result.StockCountDate,
            GeneratedBy = result.CreatedByName,
            GeneratedAt = result.CreatedAt,
            HasPostGenerationMovements = result.HasPostGenerationMovements,
            PostGenerationMovementCount = result.PostGenerationMovementCount,
            LastPostGenerationMovementAt = result.LastPostGenerationMovementAt,
            Lines = result.Lines.Select(line => new StockCountLineDto
            {
                Id = line.Id,
                ProductId = line.ProductId,
                VariantId = line.VariantId,
                ProductName = line.ProductName,
                ProductCode = line.ProductCode,
                VariantName = line.VariantName,
                CurrentStock = line.CurrentStock,
                PhysicalStock = line.PhysicalStock,
                Difference = line.Difference,
                Remarks = line.Remarks
            }).ToList()
        };
        return Ok(ApiResponse<StockCountPrintDto>.SuccessResponse(dto));
    }

    [HttpPost("{id:long}/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Upload(
        long id,
        [FromForm] UploadStockCountRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Upload"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(ApiResponse<StockCountDto>.ErrorResponse("An Excel file is required for upload."));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        var lineUpdates = StockCountExcelUploadParser.Parse(stockCount, request.File);
        await _sender.Send(
            new UploadStockCountCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId,
                lineUpdates),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    [HttpPost("{id:long}/submit")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Submit"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        await _sender.Send(
            new SubmitStockCountCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        var phase3Allowed = await IsStockCountPhase3EnabledAsync(cancellationToken);
        if (!phase3Allowed)
        {
            return ForbidResponse<StockCountDto>("Stock Count Phase 3 actions are currently disabled.");
        }

        if (!User.HasPermission("StockCount.Approve"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        if (_asyncAdjustment.Enabled)
        {
            return await ApproveWithAsyncAdjustmentAsync(id, userId, cancellationToken);
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        await _sender.Send(
            new ApproveStockCountCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    /// <summary>
    /// Approval that returns immediately: the decision, the durable adjustment job and its outbox message are
    /// committed together; Inventory is changed later by the background worker (AdjustmentPending, not Adjusted).
    /// </summary>
    private async Task<ActionResult<ApiResponse<StockCountDto>>> ApproveWithAsyncAdjustmentAsync(
        long id,
        long userId,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission(StockCountAdjustmentApproverPolicy.PostPermission))
        {
            return ForbidResponse<StockCountDto>(
                "Approving a stock count also posts its stock adjustment and requires the stock_adjustments.approve permission.");
        }

        if (_roleSwitch.IsSuperAdmin || _roleSwitch.IsRoleSwitched)
        {
            return ForbidResponse<StockCountDto>(
                "Automatic stock adjustment posting requires a Business-scoped approver without role switching.");
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        try
        {
            await _sender.Send(
                new ApproveStockCountWithAdjustmentCommand(
                    new ApproveStockCountWithAdjustmentInput(
                        stockCount.Id,
                        stockCount.BusinessId,
                        stockCount.LocationId,
                        stockCount.LocationType,
                        userId,
                        _roleSwitch.EffectiveRoleName ?? string.Empty,
                        HttpContext.TraceIdentifier)),
                cancellationToken);
        }
        catch (StockCountApprovalPolicyException exception)
        {
            return ForbidResponse<StockCountDto>(exception.Message);
        }

        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(
            MapDetail(updated),
            "Stock count approved. The stock adjustment is pending and inventory has not changed yet."));
    }

    [HttpPost("{id:long}/reject")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Reject(
        long id,
        [FromBody] RejectStockCountRequest? request,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Reject"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        await _sender.Send(
            new RejectStockCountCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId,
                request?.Reason),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    [HttpPost("{id:long}/reopen")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Reopen(
        long id,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission("StockCount.Reopen"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        await _sender.Send(
            new ReopenStockCountCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    [HttpPost("{id:long}/generate-adjustment-draft")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> GenerateAdjustmentDraft(
        long id,
        CancellationToken cancellationToken)
    {
        var phase3Allowed = await IsStockCountPhase3EnabledAsync(cancellationToken);
        if (!phase3Allowed)
        {
            return ForbidResponse<StockCountDto>("Stock Count Phase 3 actions are currently disabled.");
        }

        if (!User.HasPermission("StockCount.Approve"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (_asyncAdjustment.Enabled)
        {
            return ForbidResponse<StockCountDto>(
                "Adjustment drafts are generated automatically after approval while asynchronous stock count adjustment is enabled.");
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var stockCount = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        await _sender.Send(
            new GenerateStockCountAdjustmentDraftCommand(
                stockCount.Id,
                stockCount.BusinessId,
                stockCount.LocationId,
                stockCount.LocationType,
                userId),
            cancellationToken);
        var updated = await _sender.Send(new GetStockCountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(MapDetail(updated)));
    }

    private bool CanView()
    {
        return User.HasPermission("StockCount.ViewOwn") || User.HasPermission("StockCount.ViewAll");
    }

    private ActionResult<ApiResponse<T>> ForbidResponse<T>(string? message = null)
    {
        return StatusCode(StatusCodes.Status403Forbidden,
            ApiResponse<T>.ErrorResponse(message ?? "You do not have permission to perform this action"));
    }

    private async Task<bool> IsStockCountPhase3EnabledAsync(CancellationToken cancellationToken)
    {
        var globalFlag = _configuration.GetValue<bool>("FeatureFlags:StockCountPhase3Enabled");
        if (!globalFlag)
        {
            return false;
        }

        return await _featureEntitlement.IsFeatureEnabledAsync(
            StockCountPhase3FeatureKey,
            cancellationToken: cancellationToken);
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out userId);
    }
}
