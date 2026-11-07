using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockTransfers;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize(Policy = "stock_transfers.view")]
public class StockTransfersController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly IVariantDisplayFormatterService _variantDisplayFormatter;
    private readonly ISender _sender;

    public StockTransfersController(
        IUserOutletAccessService outletAccess,
        IVariantDisplayFormatterService variantDisplayFormatter,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _variantDisplayFormatter = variantDisplayFormatter;
        _sender = sender;
    }

    /// <summary>
    /// Get all stock transfers with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockTransferDto>>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] long? fromLocationId = null,
        [FromQuery] long? toLocationId = null,
        CancellationToken cancellationToken = default)
    {
        var visibility = await ResolveVisibilityContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(visibility.ConfigurationError))
        {
            return BadRequest(ApiResponse<List<StockTransferDto>>.ErrorResponse(visibility.ConfigurationError));
        }

        var transfers = await _sender.Send(
            new GetStockTransfersQuery(
                new StockTransferListFilter(status, fromLocationId, toLocationId),
                visibility.CanViewAllLocations,
                visibility.DefaultLocationType,
                visibility.DefaultLocationId),
            cancellationToken);

        return Ok(ApiResponse<List<StockTransferDto>>.SuccessResponse(
            await MapTransfersAsync(transfers)));
    }

    /// <summary>
    /// Search stock transfers with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockTransferListDto>>> Search(
        [FromBody] StockTransferSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var visibility = await ResolveVisibilityContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(visibility.ConfigurationError))
        {
            return BadRequest(ApiResponse<StockTransferListDto>.ErrorResponse(visibility.ConfigurationError));
        }

        var result = await _sender.Send(
            new SearchStockTransfersQuery(
                new StockTransferSearchFilter(
                    searchDto.SearchTerm,
                    searchDto.Status,
                    searchDto.FromLocationId,
                    searchDto.FromLocationType,
                    searchDto.ToLocationId,
                    searchDto.ToLocationType,
                    searchDto.ProductVariantId,
                    searchDto.TransferType,
                    searchDto.DateFrom,
                    searchDto.DateTo,
                    searchDto.PageNumber,
                    searchDto.PageSize),
                visibility.CanViewAllLocations,
                visibility.DefaultLocationType,
                visibility.DefaultLocationId),
            cancellationToken);

        return Ok(ApiResponse<StockTransferListDto>.SuccessResponse(new StockTransferListDto
        {
            StockTransfers = await MapTransfersAsync(result.StockTransfers),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    /// <summary>
    /// Get stock transfer by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var visibility = await ResolveVisibilityContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(visibility.ConfigurationError))
        {
            return BadRequest(ApiResponse<StockTransferDto>.ErrorResponse(visibility.ConfigurationError));
        }

        var transfer = await _sender.Send(
            new GetStockTransferByIdQuery(id),
            cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var transferDto = await MapTransferAsync(transfer);

        if (!visibility.CanViewAllLocations
            && !IsVisibleForDefaultLocation(transferDto, visibility.DefaultLocationType, visibility.DefaultLocationId))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<StockTransferDto>.ErrorResponse("You are not authorized to view this stock transfer."));
        }

        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transferDto));
    }

    /// <summary>
    /// Create a new stock transfer
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "stock_transfers.create")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Create(
        [FromBody] CreateStockTransferDto dto,
        CancellationToken cancellationToken)
    {
        // Users without the explicit permission are pinned to their default assigned source location.
        if (!User.HasPermission("stock_transfers.transfer_from_any_location"))
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
                throw new UnauthorizedAccessException("Your account is not assigned to a default source location.");

            if (dto.FromLocationId != auth.DefaultLocationId.Value
                || !string.Equals(dto.FromLocationType, auth.DefaultLocationType, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("You are not allowed to create a transfer from the selected source location.");
            }
        }

        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(
            LocationAccessPolicy.StockTransferSource,
            dto.FromLocationId,
            dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        ValidateLocationType(dto.ToLocationType);
        await ValidateDestinationLocationAsync(dto.ToLocationId, dto.ToLocationType);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var created = await _sender.Send(
            new CreateStockTransferCommand(
                new StockTransferCreateInput(
                    dto.TransferType,
                    dto.RelatedRequisitionId,
                    dto.FromLocationId,
                    dto.FromLocationType,
                    dto.ToLocationId,
                    dto.ToLocationType,
                    dto.TransferDate,
                    dto.Notes,
                    dto.Items?.Select(item => new StockTransferCreateItem(
                        item.VariantId,
                        item.Quantity,
                        item.RequestedQuantity,
                        item.TransferQuantity,
                        item.UnitCost,
                        item.Remarks,
                        item.UnitOptionId,
                        item.EnteredQuantity,
                        item.ExpectedConfigVersionId,
                        item.Allocations?.Select(allocation => new RetailPOS.Application.Uom.TransferAllocationInput(
                            allocation.State, allocation.PackCode, allocation.BaseQtyPerPack, allocation.PackCount, allocation.BaseQuantity)).ToArray())).ToArray() ?? []),
                userId),
            cancellationToken);
        var transfer = await MapTransferAsync(created);
        return CreatedAtAction(
            nameof(GetById),
            new { id = transfer.Id },
            ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer created successfully"));
    }

    /// <summary>
    /// Update a stock transfer (only if pending)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "stock_transfers.edit")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Update(
        long id,
        [FromBody] CreateStockTransferDto dto,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(existingDto, "modify");
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        if (!User.HasPermission("stock_transfers.transfer_from_any_location"))
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
                throw new UnauthorizedAccessException("Your account is not assigned to a default source location.");

            if (dto.FromLocationId != auth.DefaultLocationId.Value
                || !string.Equals(dto.FromLocationType, auth.DefaultLocationType, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("You are not allowed to update a transfer with the selected source location.");
            }
        }

        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(
            LocationAccessPolicy.StockTransferSource,
            dto.FromLocationId,
            dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        ValidateLocationType(dto.ToLocationType);
        await ValidateDestinationLocationAsync(dto.ToLocationId, dto.ToLocationType);

        var updated = await _sender.Send(
            new UpdateStockTransferCommand(
                id,
                new StockTransferCreateInput(
                    dto.TransferType,
                    dto.RelatedRequisitionId,
                    dto.FromLocationId,
                    dto.FromLocationType,
                    dto.ToLocationId,
                    dto.ToLocationType,
                    dto.TransferDate,
                    dto.Notes,
                    dto.Items?.Select(item => new StockTransferCreateItem(
                        item.VariantId,
                        item.Quantity,
                        item.RequestedQuantity,
                        item.TransferQuantity,
                        item.UnitCost,
                        item.Remarks,
                        item.UnitOptionId,
                        item.EnteredQuantity,
                        item.ExpectedConfigVersionId,
                        item.Allocations?.Select(allocation => new RetailPOS.Application.Uom.TransferAllocationInput(
                            allocation.State, allocation.PackCode, allocation.BaseQtyPerPack, allocation.PackCount, allocation.BaseQuantity)).ToArray())).ToArray() ?? [])),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(updated),
            "Stock transfer updated successfully"));
    }

    /// <summary>
    /// Submit stock transfer (already pending on creation - provided for workflow completeness)
    /// </summary>
    [HttpPost("{id}/submit")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(existingDto, "submit");
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var submitted = await _sender.Send(
            new SubmitStockTransferCommand(id, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(submitted),
            "Stock transfer submitted"));
    }

    /// <summary>
    /// Atomically submit, approve when required, and dispatch a stock transfer.
    /// </summary>
    [HttpPost("{id}/submit-and-dispatch")]
    [Authorize(Policy = "stock_transfers.dispatch")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> SubmitAndDispatch(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(
            existingDto,
            "submit and dispatch",
            requireDefaultLocation: true);
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var requiresApproval = !existingDto.Status.Equals(RetailPOS.Core.Entities.StockTransfer.StatusApproved, StringComparison.OrdinalIgnoreCase)
            && !existingDto.Status.Equals(RetailPOS.Core.Entities.StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase);

        if (requiresApproval && !User.HasPermission("stock_transfers.approve"))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<StockTransferDto>.ErrorResponse("You do not have permission to approve stock transfers required for submit and dispatch."));
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _sender.Send(
            new SubmitAndDispatchStockTransferCommand(id, userId, requiresApproval),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer submitted and dispatched successfully."));
    }

    /// <summary>
    /// Approve a stock transfer (pending → approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Policy = "stock_transfers.approve")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(existingDto, "approve");
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedApproverId) ? parsedApproverId : null;

        var transfer = await _sender.Send(
            new ApproveStockTransferCommand(id, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer approved"));
    }

    /// <summary>
    /// Reject a stock transfer
    /// </summary>
    [HttpPost("{id}/reject")]
    [Authorize(Policy = "stock_transfers.reject_receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Reject(
        long id,
        [FromBody] TransferStatusUpdateDto dto,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetDestinationActionAuthorizationErrorAsync(existingDto, "reject", "rejected");
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? rejectedBy = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _sender.Send(
            new RejectStockTransferCommand(id, dto.Reason, rejectedBy),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer rejected"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/send")]
    [Authorize(Policy = "stock_transfers.dispatch")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Send(
        long id,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(
            existingDto,
            "dispatch",
            requireDefaultLocation: true);
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _sender.Send(
            new SendStockTransferCommand(id, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer marked as in-transit"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/dispatch")]
    [Authorize(Policy = "stock_transfers.dispatch")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Dispatch(
        long id,
        CancellationToken cancellationToken)
    {
        return await Send(id, cancellationToken);
    }

    /// <summary>
    /// Receive a stock transfer and update inventory (in_transit → received)
    /// </summary>
    [HttpPost("{id}/receive")]
    [Authorize(Policy = "stock_transfers.receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Receive(
        long id,
        [FromBody] ReceiveStockTransferDto? dto,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetDestinationActionAuthorizationErrorAsync(existingDto, "receive", "received");
        if (authorizationError is not null)
        {
            var statusCode = authorizationError.StartsWith("This transfer must be In Transit", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status403Forbidden;

            return StatusCode(statusCode, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var input = dto is null
            ? null
            : new StockTransferReceiveInput(
                dto.Items.Select(item => new StockTransferReceiveItemInput(
                    item.VariantId,
                    item.AcceptedQuantity,
                    item.RejectedQuantity,
                    item.Remarks,
                    item.MissingQuantity,
                    item.DamagedQuantity)).ToArray(),
                dto.Notes);
        var transfer = await _sender.Send(
            new ReceiveStockTransferCommand(id, input, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer received and inventory updated"));
    }

    /// <summary>
    /// Cancel a stock transfer
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "stock_transfers.cancel")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Cancel(
        long id,
        [FromBody] TransferStatusUpdateDto dto,
        CancellationToken cancellationToken)
    {
        var existing = await _sender.Send(new GetStockTransferByIdQuery(id), cancellationToken);
        if (existing is null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var existingDto = await MapTransferAsync(existing);
        var authorizationError = await GetSourceActionAuthorizationErrorAsync(existingDto, "cancel");
        if (authorizationError is not null)
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<StockTransferDto>.ErrorResponse(authorizationError));

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _sender.Send(
            new CancelStockTransferCommand(id, dto.Reason, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Stock transfer cancelled"));
    }

    private async Task<(bool CanViewAllLocations, string? DefaultLocationType, long? DefaultLocationId, string? ConfigurationError)> ResolveVisibilityContextAsync(
        CancellationToken cancellationToken)
    {
        if (User.HasPermission("stock_transfers.view_all_locations"))
        {
            return (true, null, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
        {
            return (false, null, null, "Your default inventory location is not configured.");
        }

        var normalizedType = auth.DefaultLocationType.Trim().ToLowerInvariant();
        if (normalizedType != "outlet" && normalizedType != "warehouse")
        {
            return (false, null, null, "Your default inventory location is not configured.");
        }

        return (false, normalizedType, auth.DefaultLocationId.Value, null);
    }

    private async Task<List<StockTransferDto>> MapTransfersAsync(
        IReadOnlyList<StockTransferReadModel> transfers)
    {
        var results = new List<StockTransferDto>(transfers.Count);
        foreach (var transfer in transfers)
            results.Add(await MapTransferAsync(transfer));

        return results;
    }

    private async Task<StockTransferDto> MapTransferAsync(StockTransferReadModel transfer)
    {
        var items = new List<StockTransferItemDto>(transfer.Items.Count);
        foreach (var item in transfer.Items)
        {
            items.Add(new StockTransferItemDto
            {
                Id = item.Id,
                VariantId = item.VariantId,
                ProductCode = item.ProductCode,
                VariantSku = item.VariantSku,
                ProductName = item.ProductName,
                VariantAttributes = item.VariantAttributes,
                VariantDisplay = await _variantDisplayFormatter.FormatAsync(
                    item.VariantAttributes,
                    HttpContext.RequestAborted),
                PrimaryImageThumb = item.PrimaryImageThumb,
                Quantity = item.Quantity,
                RequestedQuantity = item.RequestedQuantity,
                TransferQuantity = item.TransferQuantity,
                AcceptedQuantity = item.AcceptedQuantity,
                RejectedQuantity = item.RejectedQuantity,
                MissingQuantity = item.MissingQuantity,
                DamagedQuantity = item.DamagedQuantity,
                InTransitQuantity = item.InTransitQuantity,
                ReturnTransitQuantity = item.ReturnTransitQuantity,
                UnitCost = item.UnitCost,
                Remarks = item.Remarks
            });
        }

        return new StockTransferDto
        {
            Id = transfer.Id,
            TransferNo = transfer.TransferNo,
            TransferType = transfer.TransferType,
            RelatedRequisitionId = transfer.RelatedRequisitionId,
            FromLocationId = transfer.FromLocationId,
            FromLocationType = transfer.FromLocationType,
            FromLocationName = transfer.FromLocationName,
            ToLocationId = transfer.ToLocationId,
            ToLocationType = transfer.ToLocationType,
            ToLocationName = transfer.ToLocationName,
            TransferDate = transfer.TransferDate,
            Status = transfer.Status,
            Notes = transfer.Notes,
            ApprovedBy = transfer.ApprovedBy,
            ApproverName = transfer.ApproverName,
            ApprovedAt = transfer.ApprovedAt,
            SubmittedBy = transfer.SubmittedBy,
            SubmittedAt = transfer.SubmittedAt,
            DispatchedBy = transfer.DispatchedBy,
            DispatchedAt = transfer.DispatchedAt,
            ReceivedBy = transfer.ReceivedBy,
            ReceivedAt = transfer.ReceivedAt,
            RejectedBy = transfer.RejectedBy,
            RejectedAt = transfer.RejectedAt,
            CancelledBy = transfer.CancelledBy,
            CancelledAt = transfer.CancelledAt,
            UpdatedBy = transfer.UpdatedBy,
            UpdatedAt = transfer.UpdatedAt,
            CreatedBy = transfer.CreatedBy,
            CreatorName = transfer.CreatorName,
            CreatedAt = transfer.CreatedAt,
            Items = items,
            OriginalTransferId = transfer.OriginalTransferId,
            ReturnTransferId = transfer.ReturnTransferId,
            ReturnTransferNo = transfer.ReturnTransferNo
        };
    }

    private static bool IsVisibleForDefaultLocation(StockTransferDto transfer, string? defaultLocationType, long? defaultLocationId)
    {
        if (!defaultLocationId.HasValue || string.IsNullOrWhiteSpace(defaultLocationType))
        {
            return false;
        }

        return
            (string.Equals(transfer.FromLocationType, defaultLocationType, StringComparison.OrdinalIgnoreCase)
                && transfer.FromLocationId == defaultLocationId.Value)
            ||
            (string.Equals(transfer.ToLocationType, defaultLocationType, StringComparison.OrdinalIgnoreCase)
                && transfer.ToLocationId == defaultLocationId.Value);
    }

    private async Task<string?> GetSourceActionAuthorizationErrorAsync(
        StockTransferDto transfer,
        string action,
        bool requireDefaultLocation = false)
    {
        var sourceAuth = await _outletAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.StockTransferSource);
        var isAuthorizedSource = IsAuthorizedLocation(sourceAuth.Outlets, sourceAuth.Warehouses, transfer.FromLocationType, transfer.FromLocationId);
        if (!isAuthorizedSource)
            return $"Only an authorized user of the source location can {action} this transfer.";

        if (requireDefaultLocation && !User.HasPermission("stock_transfers.transfer_from_any_location"))
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!MatchesDefaultLocation(auth, transfer.FromLocationType, transfer.FromLocationId))
                return $"Only an authorized user of the source location can {action} this transfer.";
        }

        return null;
    }

    private async Task<string?> GetDestinationActionAuthorizationErrorAsync(
        StockTransferDto transfer,
        string action,
        string pastTenseAction)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (!transfer.Status.Equals(RetailPOS.Core.Entities.StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase))
            return $"This transfer must be In Transit before it can be {pastTenseAction}.";

        if (!MatchesDefaultLocation(auth, transfer.ToLocationType, transfer.ToLocationId))
            return $"Only an authorized user of the destination location can {action} this transfer.";

        return null;
    }

    private static bool MatchesDefaultLocation(AuthorizedOutletsDto auth, string locationType, long locationId)
    {
        return auth.DefaultLocationId.HasValue
            && !string.IsNullOrWhiteSpace(auth.DefaultLocationType)
            && auth.DefaultLocationId.Value == locationId
            && string.Equals(auth.DefaultLocationType, locationType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizedLocation(
        IEnumerable<AuthorizedLocationDto> outlets,
        IEnumerable<AuthorizedLocationDto> warehouses,
        string locationType,
        long locationId)
    {
        return locationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? outlets.Any(o => o.Id == locationId)
            : locationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && warehouses.Any(w => w.Id == locationId);
    }

    private static void ValidateLocationType(string? locationType)
    {
        var normalizedType = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType != "outlet" && normalizedType != "warehouse")
            throw new InvalidOperationException("Location type must be either outlet or warehouse.");
    }

    private async Task ValidateDestinationLocationAsync(long locationId, string locationType)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.StockTransferDestination);
        var normalizedType = locationType.Trim().ToLowerInvariant();

        var isAllowed = normalizedType == "outlet"
            ? auth.Outlets.Any(o => o.Id == locationId)
            : auth.Warehouses.Any(w => w.Id == locationId);

        if (!isAllowed)
            throw new UnauthorizedAccessException($"Destination {normalizedType} #{locationId} is not available in your business scope.");
    }
}
