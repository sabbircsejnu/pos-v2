using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Models;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.Uom;

namespace RetailPOS.API.Controllers;

public sealed record SaveUomUnitRequest(string Code, string Name, string Symbol, string Dimension, int QuantityPrecision, bool IsWholeUnit, bool IsActive = true);

public sealed record UpdateVariantUnitConfigRequest(int? ExpectedVersion, VariantUomConfigInput Config);

public sealed record VariantUnitConfigBatchRequest(IReadOnlyList<long> VariantIds);

public sealed record UnitSaleQuoteRequest(long VariantId, long SellingOptionId, decimal EnteredQuantity, long? ExpectedConfigVersionId);

public sealed record OpenSealedPackRequest(long VariantId, long LocationId, string LocationType, string PackCode, decimal BaseQtyPerPack, int Count, string IdempotencyKey);

public sealed record CreateRepackJobRequest(long SourceVariantId, long LocationId, string LocationType, long OutputSellingOptionId, int OutputCount, string IdempotencyKey, string? Notes);

public sealed record ExecuteRepackJobRequest(decimal WastageBaseQuantity = 0m);

/// <summary>
/// Units, variant unit configuration, pack-state and repacking. Handlers resolve the Business from the caller's context and
/// enforce location rights; nothing here trusts a client-supplied Business. Anyone who sells, buys, moves or counts stock
/// may read the configuration of the items they handle; only catalog permissions change it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/uom")]
public sealed class UomController(ISender sender) : ControllerBase
{
    private static readonly string[] ConfigReaders =
    [
        "products.view", "products.edit", "sales.create", "sales.refund", "purchases.view", "purchases.create", "grn.view", "grn.create",
        "stock_transfers.view", "stock_transfers.create", "stock_adjustments.view", "stock_adjustments.create", "inventory.view",
        PermissionCatalog.UomView, PermissionCatalog.PackStateView, PermissionCatalog.RepackingView
    ];

    private bool CanReadConfig() => ConfigReaders.Any(permission => User.HasPermission(permission));

    [HttpGet("units")]
    public async Task<ActionResult<ApiResponse<UomCatalogReadModel>>> GetUnits(
        [FromQuery] bool activeOnly,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (!CanReadConfig())
            return Forbid();
        return Ok(ApiResponse<UomCatalogReadModel>.SuccessResponse(
            await sender.Send(new GetUomCatalogQuery(activeOnly, search), cancellationToken)));
    }

    [HttpPost("units")]
    [Authorize(Policy = PermissionCatalog.UomManage)]
    public async Task<ActionResult<ApiResponse<UomUnitReadModel>>> CreateUnit(
        [FromBody] SaveUomUnitRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<UomUnitReadModel>.SuccessResponse(await sender.Send(
            new CreateUomUnitCommand(new UomUnitInput(request.Code, request.Name, request.Symbol, request.Dimension, request.QuantityPrecision, request.IsWholeUnit)),
            cancellationToken)));

    [HttpPut("units/{id:long}")]
    [Authorize(Policy = PermissionCatalog.UomManage)]
    public async Task<ActionResult<ApiResponse<UomUnitReadModel>>> UpdateUnit(
        long id,
        [FromBody] SaveUomUnitRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<UomUnitReadModel>.SuccessResponse(await sender.Send(
            new UpdateUomUnitCommand(id, new UomUnitInput(request.Code, request.Name, request.Symbol, request.Dimension, request.QuantityPrecision, request.IsWholeUnit), request.IsActive),
            cancellationToken)));

    [HttpGet("variants/{variantId:long}/config")]
    public async Task<ActionResult<ApiResponse<VariantUnitConfigReadModel>>> GetConfig(long variantId, CancellationToken cancellationToken)
    {
        if (!CanReadConfig())
            return Forbid();
        return Ok(ApiResponse<VariantUnitConfigReadModel>.SuccessResponse(
            await sender.Send(new GetVariantUnitConfigQuery(variantId), cancellationToken)));
    }

    [HttpPost("variants/configs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ResolvedVariantUom>>>> GetConfigs(
        [FromBody] VariantUnitConfigBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanReadConfig())
            return Forbid();
        return Ok(ApiResponse<IReadOnlyList<ResolvedVariantUom>>.SuccessResponse(
            await sender.Send(new GetVariantUnitConfigsQuery(request.VariantIds), cancellationToken)));
    }

    [HttpPut("variants/{variantId:long}/config")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<VariantUnitConfigReadModel>>> UpdateConfig(
        long variantId,
        [FromBody] UpdateVariantUnitConfigRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<VariantUnitConfigReadModel>.SuccessResponse(await sender.Send(
            new UpdateVariantUnitConfigCommand(variantId, request.ExpectedVersion, request.Config),
            cancellationToken)));

    [HttpPost("quote")]
    public async Task<ActionResult<ApiResponse<UnitSaleQuoteReadModel>>> Quote(
        [FromBody] UnitSaleQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanReadConfig())
            return Forbid();
        return Ok(ApiResponse<UnitSaleQuoteReadModel>.SuccessResponse(await sender.Send(
            new PreviewUnitSaleQuery(request.VariantId, request.SellingOptionId, request.EnteredQuantity, request.ExpectedConfigVersionId),
            cancellationToken)));
    }

    [HttpGet("pack-state")]
    public async Task<ActionResult<ApiResponse<PackStateReadModel>>> GetPackState(
        [FromQuery] long variantId,
        [FromQuery] long locationId,
        [FromQuery] string locationType,
        CancellationToken cancellationToken)
    {
        if (!User.HasPermission(PermissionCatalog.PackStateView) && !User.HasPermission("sales.create") && !User.HasPermission("inventory.view"))
            return Forbid();
        return Ok(ApiResponse<PackStateReadModel>.SuccessResponse(
            await sender.Send(new GetPackStateQuery(variantId, locationId, locationType), cancellationToken)));
    }

    [HttpPost("pack-state/open")]
    [Authorize(Policy = PermissionCatalog.PackStateManage)]
    public async Task<ActionResult<ApiResponse<PackStateReadModel>>> OpenPack(
        [FromBody] OpenSealedPackRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<PackStateReadModel>.SuccessResponse(await sender.Send(
            new OpenSealedPackCommand(request.VariantId, request.LocationId, request.LocationType, request.PackCode, request.BaseQtyPerPack, request.Count, request.IdempotencyKey),
            cancellationToken)));

    [HttpPut("stock-counts/{stockCountId:long}/lines/{lineId:long}/entries")]
    [Authorize(Policy = "inventory.edit")]
    public async Task<ActionResult<ApiResponse<StockCountEntriesResult>>> SaveCountEntries(
        long stockCountId,
        long lineId,
        [FromBody] IReadOnlyList<StockCountEntryInput> entries,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<StockCountEntriesResult>.SuccessResponse(
            await sender.Send(new SaveStockCountEntriesCommand(stockCountId, lineId, entries), cancellationToken)));

    [HttpGet("repacking")]
    [Authorize(Policy = PermissionCatalog.RepackingView)]
    public async Task<ActionResult<ApiResponse<RepackJobPage>>> GetRepackJobs(
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(ApiResponse<RepackJobPage>.SuccessResponse(
            await sender.Send(new GetRepackJobsQuery(locationId, locationType, status, page, pageSize), cancellationToken)));

    [HttpGet("repacking/{id:long}")]
    [Authorize(Policy = PermissionCatalog.RepackingView)]
    public async Task<ActionResult<ApiResponse<RepackJobReadModel>>> GetRepackJob(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<RepackJobReadModel>.SuccessResponse(await sender.Send(new GetRepackJobQuery(id), cancellationToken)));

    [HttpPost("repacking")]
    [Authorize(Policy = PermissionCatalog.RepackingExecute)]
    public async Task<ActionResult<ApiResponse<RepackJobReadModel>>> CreateRepackJob(
        [FromBody] CreateRepackJobRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<RepackJobReadModel>.SuccessResponse(await sender.Send(
            new CreateRepackJobCommand(request.SourceVariantId, request.LocationId, request.LocationType, request.OutputSellingOptionId, request.OutputCount, request.IdempotencyKey, request.Notes),
            cancellationToken)));

    [HttpPost("repacking/{id:long}/execute")]
    [Authorize(Policy = PermissionCatalog.RepackingExecute)]
    public async Task<ActionResult<ApiResponse<RepackJobReadModel>>> ExecuteRepackJob(
        long id,
        [FromBody] ExecuteRepackJobRequest? request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<RepackJobReadModel>.SuccessResponse(await sender.Send(
            new ExecuteRepackJobCommand(id, request?.WastageBaseQuantity ?? 0m),
            cancellationToken)));

    [HttpPost("repacking/{id:long}/cancel")]
    [Authorize(Policy = PermissionCatalog.RepackingExecute)]
    public async Task<ActionResult<ApiResponse<RepackJobReadModel>>> CancelRepackJob(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<RepackJobReadModel>.SuccessResponse(await sender.Send(new CancelRepackJobCommand(id), cancellationToken)));

    [HttpPost("repacking/{id:long}/reverse")]
    [Authorize(Policy = PermissionCatalog.RepackingExecute)]
    public async Task<ActionResult<ApiResponse<RepackJobReadModel>>> ReverseRepackJob(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<RepackJobReadModel>.SuccessResponse(await sender.Send(new ReverseRepackJobCommand(id), cancellationToken)));
}
