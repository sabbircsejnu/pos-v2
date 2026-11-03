using RetailPOS.Application.Mobile;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Mobile;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Inventory.StockCounts.Commands;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/mobile/v1/stock-take/sessions")]
[Authorize]
public sealed class MobileStockTakeController(
    IMobileStockTakeReadStore stockTakes,
    IUserOutletAccessService outletAccess,
    IRoleSwitchContext roleSwitch,
    IMobileSingleBusinessReadinessGuard deploymentGuard,
    IMobileStockCountOperationStore stockCountOperations,
    IMobileStockTakeSubmissionStore submissions,
    ISender sender,
    IMobileStockTakeReviewStore review,
    ILogger<MobileStockTakeController> logger) : ControllerBase
{
    private const int MaximumPageSize = 100;
    private const int MaximumPage = 100_000;
    private const int MaximumSearchLength = 100;
    private const int MaximumRemarksLength = 1000;
    private const int MaximumOperationBatchSize = 100;
    private const int MaximumDeviceSessionIdLength = 128;
    private const int MaximumIdempotencyKeyLength = 100;
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const decimal MaximumReceiptQuantity = 99_999_999_999_999_999_999.99999999m;

    private static readonly string[] SupportedStatuses =
    [
        "Draft",
        "Submitted",
        "Rejected",
        "Approved",
        "AdjustmentGenerated",
        "Completed"
    ];

    private static readonly string[] SupportedSubmissionStatuses =
    [
        RetailPOS.Core.Entities.StockCountSubmissionJobStatus.Accepted,
        RetailPOS.Core.Entities.StockCountSubmissionJobStatus.Processing,
        RetailPOS.Core.Entities.StockCountSubmissionJobStatus.RetryScheduled,
        RetailPOS.Core.Entities.StockCountSubmissionJobStatus.ErpSubmitted,
        RetailPOS.Core.Entities.StockCountSubmissionJobStatus.Failed
    ];

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakePageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakePageDto>>> List(
        [FromQuery] long? outletId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (!IsValidPage(page, pageSize)
            || outletId is <= 0
            || search?.Length > MaximumSearchLength
            || !TryNormalizeStatus(status, out var normalizedStatus))
        {
            return BadRequest(ApiResponse<MobileStockTakePageDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_FILTER"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var outletIds = authorized.Outlets.Select(outlet => outlet.Id).ToArray();
        if (outletId.HasValue && !outletIds.Contains(outletId.Value))
            return Forbid();

        var result = await stockTakes.SearchAsync(
            businessId,
            outletIds,
            outletId,
            normalizedStatus,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            page,
            pageSize,
            ReadOwnerFilter(),
            cancellationToken);

        return Ok(ApiResponse<MobileStockTakePageDto>.SuccessResponse(
            new MobileStockTakePageDto(
                result.Items.Select(ToSessionDto).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount)));
    }

    [HttpGet("{sessionId:long}")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSessionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSessionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeSessionDto>>> GetSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0)
            return BadRequest(ApiResponse<MobileStockTakeSessionDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_ID"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var result = await stockTakes.GetByIdAsync(
            businessId,
            authorized.Outlets.Select(outlet => outlet.Id).ToArray(),
            sessionId,
            ReadOwnerFilter(),
            cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileStockTakeSessionDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeSessionDto>.SuccessResponse(ToSessionDto(result)));
    }

    [HttpGet("{sessionId:long}/lines")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLinePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLinePageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLinePageDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeLinePageDto>>> GetLines(
        long sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || !IsValidPage(page, pageSize))
            return BadRequest(ApiResponse<MobileStockTakeLinePageDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_PAGINATION"));
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var result = await stockTakes.GetLinesAsync(
            businessId,
            authorized.Outlets.Select(outlet => outlet.Id).ToArray(),
            sessionId,
            page,
            pageSize,
            ReadOwnerFilter(),
            cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileStockTakeLinePageDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeLinePageDto>.SuccessResponse(
                new MobileStockTakeLinePageDto(
                    result.Items.Select(line => new MobileStockTakeLineDto(
                        line.Id,
                        line.ProductId,
                        line.VariantId,
                        line.ProductName,
                        line.ProductCode,
                        line.VariantName,
                        line.CountedQuantity,
                        line.Remarks)).ToArray(),
                    result.Page,
                    result.PageSize,
                    result.TotalCount)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSessionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSessionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeSessionDto>>> Create(
        [FromBody] MobileStockTakeCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeCreate(out var businessId))
            return Forbid();
        if (request.OutletId <= 0
            || request.StockCountDate == default
            || request.Remarks?.Length > MaximumRemarksLength)
        {
            return BadRequest(ApiResponse<MobileStockTakeSessionDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_REQUEST"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        if (!authorized.Outlets.Any(outlet => outlet.Id == request.OutletId))
        {
            logger.LogWarning("Mobile Stock Take create denied for an Outlet outside the caller's authorized Outlet set.");
            return Forbid();
        }

        var actorUserId = GetActorUserId();
        if (!actorUserId.HasValue)
            return Unauthorized(ApiResponse<MobileStockTakeSessionDto>.ErrorResponse(
                "USER_IDENTITY_NOT_FOUND"));

        var sessionId = await sender.Send(
            new CreateStockCountCommand(
                businessId,
                request.OutletId,
                "outlet",
                request.StockCountDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                request.Remarks?.Trim(),
                actorUserId.Value),
            cancellationToken);

        var created = await stockTakes.GetByIdAsync(
            businessId,
            authorized.Outlets.Select(outlet => outlet.Id).ToArray(),
            sessionId,
            null,
            cancellationToken);
        if (created is null)
        {
            logger.LogError(
                "Mobile Stock Take {StockTakeId} was created but could not be read in its authorized Outlet scope.",
                sessionId);
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "STOCK_TAKE_READ_AFTER_CREATE_FAILED");
        }

        var response = ApiResponse<MobileStockTakeSessionDto>.SuccessResponse(ToSessionDto(created));
        return CreatedAtAction(nameof(GetSession), new { sessionId }, response);
    }

    [HttpPost("{sessionId:long}/operations:batch")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsBatchDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsBatchDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsBatchDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsBatchDto>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeOperationsBatchDto>>> ApplyOperations(
        long sessionId,
        [FromBody] MobileStockTakeOperationsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeUpload(out var businessId))
            return Forbid();
        if (!IsValidOperationBatch(sessionId, request))
        {
            return BadRequest(ApiResponse<MobileStockTakeOperationsBatchDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_OPERATION_BATCH"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();

        var authorizedOutletIds = authorized.Outlets.Select(outlet => outlet.Id).ToArray();
        var session = await stockTakes.GetByIdAsync(
            businessId,
            authorizedOutletIds,
            sessionId,
            null,
            cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<MobileStockTakeOperationsBatchDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"));

        var actorUserId = GetActorUserId();
        if (!actorUserId.HasValue)
            return Unauthorized(ApiResponse<MobileStockTakeOperationsBatchDto>.ErrorResponse(
                "USER_IDENTITY_NOT_FOUND"));

        var result = await sender.Send(
            new ApplyMobileStockCountOperationsCommand(
                new ApplyMobileStockCountOperationsInput(
                    sessionId,
                    businessId,
                    session.OutletId,
                    actorUserId.Value,
                    request.DeviceSessionId.Trim(),
                    request.ExpectedRevision,
                    HttpContext.TraceIdentifier,
                    request.Operations.Select(operation => new MobileStockCountOperationInput(
                        operation.ClientOperationId,
                        operation.ClientSequence,
                        operation.ProductVariantId,
                        operation.CountedQuantity,
                        operation.RawBarcode,
                        operation.ClientObservedAt?.UtcDateTime)).ToArray())),
            cancellationToken);

        var response = new MobileStockTakeOperationsBatchDto(
            result.CurrentRevision,
            result.ErrorCode,
            result.Receipts.Select(ToOperationReceiptDto).ToArray());
        var body = ApiResponse<MobileStockTakeOperationsBatchDto>.SuccessResponse(response);
        var errorBody = new ApiResponse<MobileStockTakeOperationsBatchDto>
        {
            Success = false,
            Message = result.ErrorCode,
            Data = response,
            Errors = result.ErrorCode is null ? null : [result.ErrorCode]
        };
        return result.ErrorCode switch
        {
            "STOCK_TAKE_NOT_FOUND" => NotFound(ApiResponse<MobileStockTakeOperationsBatchDto>.ErrorResponse(
                result.ErrorCode)),
            "STALE_SESSION_REVISION" or "IDEMPOTENCY_KEY_PAYLOAD_MISMATCH" =>
                Conflict(errorBody),
            "STOCK_TAKE_NOT_DRAFT" or "STOCK_TAKE_SUBMISSION_IN_PROGRESS" =>
                Conflict(errorBody),
            "BATCH_REJECTED" =>
                UnprocessableEntity(errorBody),
            _ => Ok(body)
        };
    }

    [HttpGet("{sessionId:long}/operations")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsPageDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationsPageDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeOperationsPageDto>>> GetOperations(
        long sessionId,
        [FromQuery] long afterRevision = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || afterRevision < 0)
        {
            return BadRequest(ApiResponse<MobileStockTakeOperationsPageDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_OPERATION_CURSOR"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();
        var outletIds = authorized.Outlets.Select(outlet => outlet.Id).ToArray();
        var session = await stockTakes.GetByIdAsync(
            businessId,
            outletIds,
            sessionId,
            ReadOwnerFilter(),
            cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<MobileStockTakeOperationsPageDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"));
        if (afterRevision > session.CountRevision)
        {
            return Conflict(ApiResponse<MobileStockTakeOperationsPageDto>.ErrorResponse(
                "REVISION_AHEAD_OF_SERVER"));
        }

        var result = await stockCountOperations.GetAfterRevisionAsync(
            businessId,
            session.OutletId,
            outletIds,
            sessionId,
            afterRevision,
            cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileStockTakeOperationsPageDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeOperationsPageDto>.SuccessResponse(
                new MobileStockTakeOperationsPageDto(
                    result.CurrentRevision,
                    result.NextRevision,
                    result.HasMore,
                    result.Items.Select(ToOperationReceiptDto).ToArray())));
    }

    [HttpGet("{sessionId:long}/operations/{clientOperationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationReceiptDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeOperationReceiptDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeOperationReceiptDto>>> GetOperationReceipt(
        long sessionId,
        Guid clientOperationId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || clientOperationId == Guid.Empty)
        {
            return BadRequest(ApiResponse<MobileStockTakeOperationReceiptDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_OPERATION_ID"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();
        var outletIds = authorized.Outlets.Select(outlet => outlet.Id).ToArray();
        var session = await stockTakes.GetByIdAsync(
            businessId,
            outletIds,
            sessionId,
            ReadOwnerFilter(),
            cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<MobileStockTakeOperationReceiptDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"));

        var receipt = await stockCountOperations.FindReceiptAsync(
            businessId,
            session.OutletId,
            outletIds,
            sessionId,
            clientOperationId,
            cancellationToken);
        return receipt is null
            ? NotFound(ApiResponse<MobileStockTakeOperationReceiptDto>.ErrorResponse(
                "STOCK_TAKE_OPERATION_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeOperationReceiptDto>.SuccessResponse(
                ToOperationReceiptDto(receipt)));
    }

    [HttpPost("{sessionId:long}/submissions")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeSubmissionDto>>> SubmitStockTake(
        long sessionId,
        [FromBody] MobileStockTakeSubmissionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeSubmit(out var businessId))
            return Forbid();
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader];
        if (idempotencyKey.Count != 1 || !IsValidIdempotencyKey(idempotencyKey[0]))
        {
            return BadRequest(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "INVALID_IDEMPOTENCY_KEY"));
        }
        if (sessionId <= 0 || request is null || request.AcceptedRevision <= 0)
        {
            return BadRequest(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_SUBMISSION_REQUEST"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();
        var session = await stockTakes.GetByIdAsync(
            businessId,
            authorized.Outlets.Select(outlet => outlet.Id).ToArray(),
            sessionId,
            null,
            cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"));

        var actorUserId = GetActorUserId();
        var actorRole = roleSwitch.EffectiveRoleName;
        if (!actorUserId.HasValue || string.IsNullOrWhiteSpace(actorRole))
            return Unauthorized(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "USER_IDENTITY_NOT_FOUND"));

        var result = await sender.Send(
            new AcceptMobileStockTakeSubmissionCommand(
                new AcceptMobileStockTakeSubmissionInput(
                    sessionId,
                    businessId,
                    session.OutletId,
                    actorUserId.Value,
                    actorRole,
                    idempotencyKey[0]!,
                    request.AcceptedRevision,
                    HttpContext.TraceIdentifier)),
            cancellationToken);

        if (result.ErrorCode is not null)
        {
            var errorBody = new ApiResponse<MobileStockTakeSubmissionDto>
            {
                Success = false,
                Message = result.ErrorCode,
                Data = result.Job is null ? null : ToSubmissionDto(result.Job, false),
                Errors = [result.ErrorCode]
            };
            return result.ErrorCode switch
            {
                MobileStockTakeSubmissionErrorCodes.NotFound => NotFound(errorBody),
                MobileStockTakeSubmissionErrorCodes.NoAcceptedCounts => UnprocessableEntity(errorBody),
                _ => Conflict(errorBody)
            };
        }

        var dto = ToSubmissionDto(result.Job!, result.IsReplay);
        return Accepted(dto.StatusUrl, ApiResponse<MobileStockTakeSubmissionDto>.SuccessResponse(dto));
    }

    [HttpGet("{sessionId:long}/submissions/{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeSubmissionDto>>> GetSubmission(
        long sessionId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || jobId == Guid.Empty)
        {
            return BadRequest(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_SUBMISSION_ID"));
        }
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        if (authorized is null)
            return Forbid();
        var outletIds = authorized.Outlets.Select(outlet => outlet.Id).ToArray();
        var session = await stockTakes.GetByIdAsync(
            businessId, outletIds, sessionId, ReadOwnerFilter(), cancellationToken);
        if (session is null)
            return NotFound(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "STOCK_TAKE_NOT_FOUND"));

        var job = await submissions.FindAsync(
            businessId,
            session.OutletId,
            outletIds,
            sessionId,
            jobId,
            cancellationToken);
        return job is null
            ? NotFound(ApiResponse<MobileStockTakeSubmissionDto>.ErrorResponse(
                "STOCK_TAKE_SUBMISSION_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeSubmissionDto>.SuccessResponse(
                ToSubmissionDto(job, false)));
    }

    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeHistoryPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeHistoryPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeHistoryPageDto>>> GetHistory(
        [FromQuery] long? outletId,
        [FromQuery] string? status,
        [FromQuery] string? submissionStatus,
        [FromQuery] string? search,
        [FromQuery] DateTimeOffset? createdFrom,
        [FromQuery] DateTimeOffset? createdTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        var normalizedSubmissionStatus = string.IsNullOrWhiteSpace(submissionStatus)
            ? null
            : SupportedSubmissionStatuses.FirstOrDefault(candidate =>
                string.Equals(candidate, submissionStatus, StringComparison.OrdinalIgnoreCase));
        if (!IsValidPage(page, pageSize)
            || outletId is <= 0
            || search?.Length > MaximumSearchLength
            || !TryNormalizeStatus(status, out var normalizedStatus)
            || (!string.IsNullOrWhiteSpace(submissionStatus) && normalizedSubmissionStatus is null)
            || (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo))
        {
            return BadRequest(ApiResponse<MobileStockTakeHistoryPageDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_FILTER"));
        }

        var outletIds = await ResolveAuthorizedOutletIdsAsync(businessId, cancellationToken);
        if (outletIds is null || (outletId.HasValue && !outletIds.Contains(outletId.Value)))
            return Forbid();

        var result = await review.SearchHistoryAsync(
            businessId,
            outletIds,
            ReadOwnerFilter(),
            new MobileStockTakeHistoryFilter(
                outletId,
                normalizedStatus,
                normalizedSubmissionStatus,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                createdFrom?.UtcDateTime,
                createdTo?.UtcDateTime),
            page,
            pageSize,
            cancellationToken);

        return Ok(ApiResponse<MobileStockTakeHistoryPageDto>.SuccessResponse(
            new MobileStockTakeHistoryPageDto(
                result.Items.Select(item => new MobileStockTakeHistoryItemDto(
                    item.Id,
                    item.Reference,
                    new MobileStockTakeScopeDto("outlet", item.OutletId, item.OutletName),
                    DateOnly.FromDateTime(item.StockCountDate),
                    item.ErpStatus,
                    item.Stage,
                    item.CountRevision,
                    item.TotalItems,
                    item.CreatedAt,
                    item.SubmittedAt,
                    item.ApprovedAt,
                    item.RejectedAt,
                    item.ErpMovementReconciliationStatus,
                    item.LatestSubmission is null
                        ? null
                        : new MobileStockTakeLatestSubmissionDto(
                            item.LatestSubmission.JobId,
                            item.LatestSubmission.Status,
                            item.LatestSubmission.IsTerminal,
                            item.LatestSubmission.AttemptCount,
                            item.LatestSubmission.FailureCode,
                            item.LatestSubmission.CreatedAtUtc,
                            item.LatestSubmission.UpdatedAtUtc,
                            item.LatestSubmission.CompletedAtUtc))).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount)));
    }

    [HttpGet("{sessionId:long}/lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLifecycleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLifecycleDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeLifecycleDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeLifecycleDto>>> GetLifecycle(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0)
            return BadRequest(ApiResponse<MobileStockTakeLifecycleDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_ID"));

        var outletIds = await ResolveAuthorizedOutletIdsAsync(businessId, cancellationToken);
        if (outletIds is null)
            return Forbid();

        var lifecycle = await review.GetLifecycleAsync(
            businessId, outletIds, ReadOwnerFilter(), sessionId, cancellationToken);
        return lifecycle is null
            ? NotFound(ApiResponse<MobileStockTakeLifecycleDto>.ErrorResponse("STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeLifecycleDto>.SuccessResponse(ToLifecycleDto(lifecycle)));
    }

    [HttpGet("{sessionId:long}/submissions")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionPageDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeSubmissionPageDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeSubmissionPageDto>>> GetSubmissions(
        long sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || !IsValidPage(page, pageSize))
            return BadRequest(ApiResponse<MobileStockTakeSubmissionPageDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_PAGINATION"));

        var outletIds = await ResolveAuthorizedOutletIdsAsync(businessId, cancellationToken);
        if (outletIds is null)
            return Forbid();

        var result = await review.GetSubmissionsAsync(
            businessId, outletIds, ReadOwnerFilter(), sessionId, page, pageSize, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileStockTakeSubmissionPageDto>.ErrorResponse("STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeSubmissionPageDto>.SuccessResponse(
                new MobileStockTakeSubmissionPageDto(
                    result.Items.Select(job => ToSubmissionDto(job, false)).ToArray(),
                    result.Page,
                    result.PageSize,
                    result.TotalCount)));
    }

    [HttpGet("{sessionId:long}/results")]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeResultsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeResultsDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<MobileStockTakeResultsDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileStockTakeResultsDto>>> GetResults(
        long sessionId,
        [FromQuery] bool differencesOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryAuthorizeRead(out var businessId))
            return Forbid();
        if (sessionId <= 0 || !IsValidPage(page, pageSize))
            return BadRequest(ApiResponse<MobileStockTakeResultsDto>.ErrorResponse(
                "INVALID_STOCK_TAKE_PAGINATION"));

        var outletIds = await ResolveAuthorizedOutletIdsAsync(businessId, cancellationToken);
        if (outletIds is null)
            return Forbid();

        var result = await review.GetResultsAsync(
            businessId,
            outletIds,
            ReadOwnerFilter(),
            sessionId,
            differencesOnly,
            page,
            pageSize,
            cancellationToken);
        return result is null
            ? NotFound(ApiResponse<MobileStockTakeResultsDto>.ErrorResponse("STOCK_TAKE_NOT_FOUND"))
            : Ok(ApiResponse<MobileStockTakeResultsDto>.SuccessResponse(
                new MobileStockTakeResultsDto(
                    result.Id,
                    result.ErpStatus,
                    result.Availability,
                    result.Reasons,
                    result.ApprovedAt,
                    result.CountedLineCount,
                    result.DifferenceLineCount,
                    result.NetDifference,
                    result.Lines is null
                        ? null
                        : new MobileStockTakeResultLinePageDto(
                            result.Lines.Items.Select(line => new MobileStockTakeResultLineDto(
                                line.Id,
                                line.ProductId,
                                line.VariantId,
                                line.ProductName,
                                line.ProductCode,
                                line.VariantName,
                                line.CountedQuantity,
                                line.BaselineQuantity,
                                line.Difference)).ToArray(),
                            result.Lines.Page,
                            result.Lines.PageSize,
                            result.Lines.TotalCount))));
    }

    private async Task<long[]?> ResolveAuthorizedOutletIdsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return null;

        var authorized = await GetAuthorizedOutletsAsync(businessId);
        return authorized?.Outlets.Select(outlet => outlet.Id).ToArray();
    }

    /// <summary>
    /// ViewAll sees every session in the authorized Outlets; ViewOwn alone sees only sessions the caller
    /// created or submitted. An unresolvable identity matches nothing.
    /// </summary>
    private long? ReadOwnerFilter() =>
        User.HasPermission("StockCount.ViewAll") ? null : GetActorUserId() ?? -1;

    private static MobileStockTakeLifecycleDto ToLifecycleDto(
        RetailPOS.Application.Inventory.StockCounts.MobileStockTakeLifecycleReadModel lifecycle)
    {
        var approvalState = lifecycle.ErpStatus switch
        {
            "Draft" => "NotSubmitted",
            "Submitted" => "PendingReview",
            "Rejected" => "Rejected",
            _ => "Approved"
        };
        var decidedAt = approvalState switch
        {
            "Approved" => lifecycle.ApprovedAt,
            "Rejected" => lifecycle.RejectedAt,
            _ => null
        };
        var evaluation = lifecycle.Reconciliation;
        return new MobileStockTakeLifecycleDto(
            lifecycle.Id,
            lifecycle.Reference,
            new MobileStockTakeScopeDto("outlet", lifecycle.OutletId, lifecycle.OutletName),
            lifecycle.ErpStatus,
            lifecycle.Stage,
            lifecycle.CountRevision,
            lifecycle.CreatedAt,
            lifecycle.SubmittedAt,
            lifecycle.SubmissionJobCount,
            lifecycle.LatestSubmission is null ? null : ToSubmissionDto(lifecycle.LatestSubmission, false),
            new MobileStockTakeReconciliationDto(
                lifecycle.ErpMovementReconciliationStatus,
                evaluation.Status,
                false,
                evaluation.Reasons
                    .Select(reason => new MobileStockTakeReconciliationReasonDto(
                        reason.Code, reason.AffectedLineCount))
                    .ToArray(),
                new MobileStockTakeMovementSummaryDto(
                    evaluation.Movements.SaleCount,
                    evaluation.Movements.ReturnCount,
                    evaluation.Movements.TransferCount,
                    evaluation.Movements.AdjustmentCount,
                    evaluation.Movements.GoodsReceiptCount,
                    evaluation.Movements.OtherCount,
                    evaluation.Movements.PostCountMovementCount),
                evaluation.EvaluatedLineCount,
                evaluation.EvaluatedAtUtc),
            new MobileStockTakeApprovalDto(approvalState, decidedAt, lifecycle.HasRejectionReason),
            new MobileStockTakeInventoryAdjustmentDto(
                lifecycle.AdjustmentJob?.Status ?? lifecycle.AdjustmentState,
                lifecycle.IsInventoryPosted,
                lifecycle.AdjustmentDraftBlockedByErpReconciliation,
                lifecycle.AdjustmentJob is null
                    ? null
                    : new MobileStockTakeAdjustmentJobDto(
                        lifecycle.AdjustmentJob.JobId,
                        lifecycle.AdjustmentJob.Status,
                        RetailPOS.Core.Entities.StockCountAdjustmentJobStatus.IsTerminal(lifecycle.AdjustmentJob.Status),
                        lifecycle.AdjustmentJob.FailureCode,
                        lifecycle.AdjustmentJob.EvidenceStatus,
                        lifecycle.AdjustmentJob.BlockedReasonCodes,
                        lifecycle.AdjustmentJob.CreatedAtUtc,
                        lifecycle.AdjustmentJob.CompletedAtUtc)),
            new MobileStockTakeResultsStatusDto(lifecycle.ResultsAvailability, lifecycle.ResultsReasons));
    }

    private bool TryAuthorizeRead(out long businessId)
    {
        businessId = 0;
        if (!TryAuthorizeMobileContext(out businessId)
            || (!User.HasPermission("StockCount.ViewOwn")
                && !User.HasPermission("StockCount.ViewAll")))
        {
            logger.LogWarning("Mobile Stock Take read denied for the current role, permission or Business context.");
            businessId = 0;
            return false;
        }

        return true;
    }

    private bool TryAuthorizeCreate(out long businessId)
    {
        businessId = 0;
        if (!TryAuthorizeMobileContext(out businessId)
            || !User.HasPermission("StockCount.Create"))
        {
            logger.LogWarning("Mobile Stock Take create denied for the current role, permission or Business context.");
            businessId = 0;
            return false;
        }

        return true;
    }

    private bool TryAuthorizeUpload(out long businessId)
    {
        businessId = 0;
        if (!TryAuthorizeMobileContext(out businessId)
            || !User.HasPermission("StockCount.Upload"))
        {
            logger.LogWarning("Mobile Stock Take operation denied for the current role, permission or Business context.");
            businessId = 0;
            return false;
        }

        return true;
    }

    private bool TryAuthorizeSubmit(out long businessId)
    {
        businessId = 0;
        if (!TryAuthorizeMobileContext(out businessId)
            || !User.HasPermission("StockCount.Submit"))
        {
            logger.LogWarning("Mobile Stock Take submission denied for the current role, permission or Business context.");
            businessId = 0;
            return false;
        }

        return true;
    }

    private static bool IsValidIdempotencyKey(string? key) =>
        key is { Length: >= 8 and <= MaximumIdempotencyKeyLength }
        && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static MobileStockTakeSubmissionDto ToSubmissionDto(
        RetailPOS.Application.Inventory.StockCounts.MobileStockTakeSubmissionReadModel job,
        bool isReplay) =>
        new(
            job.JobId,
            job.StockCountId,
            job.AcceptedCountRevision,
            job.Status,
            RetailPOS.Core.Entities.StockCountSubmissionJobStatus.IsTerminal(job.Status),
            job.AttemptCount,
            job.NextAttemptAtUtc,
            job.FailureCode,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            job.CompletedAtUtc,
            job.SessionStatus,
            job.MovementReconciliationStatus,
            $"/api/mobile/v1/stock-take/sessions/{job.StockCountId}/submissions/{job.JobId}",
            isReplay);

    private bool TryAuthorizeMobileContext(out long businessId)
    {
        businessId = 0;
        if (roleSwitch.IsRoleSwitched
            || roleSwitch.IsSuperAdmin
            || !roleSwitch.EffectiveBusinessId.HasValue
            || !IsSupportedRole(roleSwitch.EffectiveRoleName))
        {
            logger.LogWarning("Mobile Stock Take denied for an unsupported Mobile role or Business context.");
            return false;
        }

        businessId = roleSwitch.EffectiveBusinessId.Value;
        return true;
    }

    private async Task<AuthorizedOutletsDto?> GetAuthorizedOutletsAsync(long businessId)
    {
        try
        {
            return await outletAccess.GetAuthorizedOutletsForBusinessAsync(businessId);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                "Mobile Stock Take denied because Outlet access could not be resolved: {Reason}",
                exception.Message);
            return null;
        }
    }

    private long? GetActorUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(claim, out var userId) && userId > 0 ? userId : null;
    }

    private static bool IsSupportedRole(string? role) =>
        string.Equals(role, "BusinessOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "SalesManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "OutletManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidPage(int page, int pageSize) =>
        page is >= 1 and <= MaximumPage && pageSize is >= 1 and <= MaximumPageSize;

    private static bool TryNormalizeStatus(string? status, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(status))
            return true;

        normalized = SupportedStatuses.FirstOrDefault(
            candidate => string.Equals(candidate, status, StringComparison.OrdinalIgnoreCase));
        return normalized is not null;
    }

    private static MobileStockTakeSessionDto ToSessionDto(
        RetailPOS.Application.Inventory.StockCounts.MobileStockTakeSummaryReadModel session) =>
        new(
            session.Id,
            session.Reference,
            new MobileStockTakeScopeDto("outlet", session.OutletId, session.OutletName),
            DateOnly.FromDateTime(session.StockCountDate),
            session.Status,
            session.Remarks,
            session.TotalItems,
            session.CreatedAt,
            session.SubmittedAt,
            session.CountRevision,
            session.BaselineConsistencyStatus,
            session.BaselineInventoryReadStartedAtUtc,
            session.BaselineInventoryReadCompletedAtUtc,
            session.MovementReconciliationStatus,
            string.Equals(session.Status, "Draft", StringComparison.OrdinalIgnoreCase)
                ? ["resume"]
                : []);

    private static bool IsValidOperationBatch(
        long sessionId,
        MobileStockTakeOperationsBatchRequest? request) =>
        sessionId > 0
        && request is not null
        && request.ExpectedRevision >= 0
        && !string.IsNullOrWhiteSpace(request.DeviceSessionId)
        && request.DeviceSessionId.Trim().Length <= MaximumDeviceSessionIdLength
        && request.Operations is { Count: > 0 and <= MaximumOperationBatchSize }
        && request.Operations.All(operation =>
            operation.ClientOperationId != Guid.Empty
            && operation.CountedQuantity >= -MaximumReceiptQuantity
            && operation.CountedQuantity <= MaximumReceiptQuantity
            && decimal.Round(operation.CountedQuantity, 8) == operation.CountedQuantity
            && (operation.RawBarcode?.Length ?? 0) <= 50)
        && request.Operations.Select(operation => operation.ClientOperationId).Distinct().Count()
            == request.Operations.Count
        && request.Operations.Select(operation => operation.ClientSequence).Distinct().Count()
            == request.Operations.Count;

    private static MobileStockTakeOperationReceiptDto ToOperationReceiptDto(
        RetailPOS.Application.Inventory.StockCounts.MobileStockCountOperationReceiptReadModel receipt) =>
        new(
            receipt.ClientOperationId,
            receipt.DeviceSessionId,
            receipt.ClientSequence,
            receipt.ProductVariantId,
            receipt.OperationType,
            receipt.CountedQuantity,
            receipt.RawBarcode,
            receipt.ClientObservedAtUtc,
            receipt.ServerReceivedAtUtc,
            receipt.DecidedAtUtc,
            receipt.AcceptedRevision,
            receipt.SessionRevisionAtDecision,
            receipt.Outcome,
            receipt.RejectionCode,
            receipt.IsDuplicate);
}
