using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Models;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Read-only Owner Dashboard API. One permission decides each dashboard (SRS §2.1) and holders see every location of the
/// current Business. Location parameters are reporting filters that the handlers validate against that Business.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/owner-dashboard")]
public sealed class OwnerDashboardController(ISender sender) : ControllerBase
{
    private const string SalesView = PermissionCatalog.DashboardSalesFinanceView;
    private const string InventoryView = PermissionCatalog.DashboardInventoryIntelligenceView;

    [HttpGet("meta")]
    public async Task<ActionResult<ApiResponse<OwnerDashboardMetaReadModel>>> GetMeta(CancellationToken cancellationToken)
    {
        // Either dashboard opens this; holding neither permission is a 403.
        if (!User.HasPermission(SalesView) && !User.HasPermission(InventoryView))
            return Forbid();

        return Ok(ApiResponse<OwnerDashboardMetaReadModel>.SuccessResponse(
            await sender.Send(new GetOwnerDashboardMetaQuery(), cancellationToken)));
    }

    [HttpGet("sales/summary")]
    [Authorize(Policy = SalesView)]
    public async Task<ActionResult<ApiResponse<SalesFinanceSummaryReadModel>>> GetSalesSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<SalesFinanceSummaryReadModel>.SuccessResponse(
            await sender.Send(new GetSalesFinanceSummaryQuery(from, to, outletId), cancellationToken)));

    [HttpGet("sales/showrooms")]
    [Authorize(Policy = SalesView)]
    public async Task<ActionResult<ApiResponse<ShowroomPerformanceReadModel>>> GetShowrooms(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<ShowroomPerformanceReadModel>.SuccessResponse(
            await sender.Send(new GetShowroomPerformanceQuery(from, to, outletId), cancellationToken)));

    [HttpGet("sales/cash-flow")]
    [Authorize(Policy = SalesView)]
    public async Task<ActionResult<ApiResponse<CashFlowReadModel>>> GetCashFlow(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<CashFlowReadModel>.SuccessResponse(
            await sender.Send(new GetCashFlowQuery(from, to, outletId), cancellationToken)));

    [HttpGet("sales/trend")]
    [Authorize(Policy = SalesView)]
    public async Task<ActionResult<ApiResponse<SalesTrendReadModel>>> GetTrend(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<SalesTrendReadModel>.SuccessResponse(
            await sender.Send(new GetSalesTrendQuery(from, to, outletId), cancellationToken)));

    [HttpGet("inventory/summary")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<InventorySummaryReadModel>>> GetInventorySummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<InventorySummaryReadModel>.SuccessResponse(
            await sender.Send(
                new GetInventorySummaryQuery(new InventoryFilter(from, to, locationId, locationType, categoryId, null)),
                cancellationToken)));

    [HttpGet("inventory/locations")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<StockValueByLocationReadModel>>> GetStockByLocation(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<StockValueByLocationReadModel>.SuccessResponse(
            await sender.Send(
                new GetStockValueByLocationQuery(new InventoryFilter(from, to, locationId, locationType, categoryId, null)),
                cancellationToken)));

    /// <summary>Dispatched transfer lines not yet received or rejected. Optional direction: incoming or outgoing (needs a location).</summary>
    [HttpGet("inventory/in-transit")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<InTransitStockReadModel>>> GetInTransitStock(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] string? direction,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15) =>
        Ok(ApiResponse<InTransitStockReadModel>.SuccessResponse(
            await sender.Send(
                new GetInTransitStockQuery(
                    new InventoryFilter(from, to, locationId, locationType, categoryId, search),
                    direction,
                    page,
                    pageSize),
                cancellationToken)));

    [HttpGet("inventory/best-sellers")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<PagedResult<BestSellerRow>>>> GetBestSellers(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15) =>
        Ok(ApiResponse<PagedResult<BestSellerRow>>.SuccessResponse(
            await sender.Send(
                new GetBestSellersQuery(
                    new InventoryFilter(from, to, locationId, locationType, categoryId, search),
                    sortBy,
                    sortDirection,
                    page,
                    pageSize),
                cancellationToken)));

    [HttpGet("inventory/slow-movers")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<SlowMoversReadModel>>> GetSlowMovers(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] string? classification,
        [FromQuery] decimal? minStock,
        [FromQuery] decimal? maxStock,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15) =>
        Ok(ApiResponse<SlowMoversReadModel>.SuccessResponse(
            await sender.Send(
                new GetSlowMoversQuery(
                    new InventoryFilter(from, to, locationId, locationType, categoryId, search),
                    classification,
                    minStock,
                    maxStock,
                    sortBy,
                    sortDirection,
                    page,
                    pageSize),
                cancellationToken)));

    [HttpGet("inventory/transfer-opportunities")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<TransferOpportunitiesReadModel>>> GetTransferOpportunities(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] long? sourceLocationId,
        [FromQuery] long? destinationLocationId,
        [FromQuery] string? priority,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10) =>
        Ok(ApiResponse<TransferOpportunitiesReadModel>.SuccessResponse(
            await sender.Send(
                new GetTransferOpportunitiesQuery(
                    new InventoryFilter(from, to, locationId, locationType, categoryId, search),
                    sourceLocationId,
                    destinationLocationId,
                    priority,
                    page,
                    pageSize),
                cancellationToken)));

    [HttpGet("inventory/transfer-opportunities/{key}")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<TransferOpportunity>>> GetTransferOpportunity(
        string key,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<TransferOpportunity>.SuccessResponse(
            await sender.Send(new GetTransferOpportunityDetailQuery(key, from, to), cancellationToken)));

    [HttpGet("inventory/products/{productId:long}/insights")]
    [Authorize(Policy = InventoryView)]
    public async Task<ActionResult<ApiResponse<ProductInsightsReadModel>>> GetProductInsights(
        long productId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? variantId,
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<ProductInsightsReadModel>.SuccessResponse(
            await sender.Send(
                new GetProductInsightsQuery(
                    productId,
                    variantId,
                    new InventoryFilter(from, to, locationId, locationType, null, null)),
                cancellationToken)));
}
