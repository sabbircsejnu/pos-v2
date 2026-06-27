using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/inventory")]
[Authorize(Policy = "reports.inventory")]
public class InventoryReportsController : ControllerBase
{
    private readonly IInventoryReportService _inventoryReportService;
    private readonly IUserOutletAccessService _outletAccess;

    public InventoryReportsController(
        IInventoryReportService inventoryReportService,
        IUserOutletAccessService outletAccess)
    {
        _inventoryReportService = inventoryReportService;
        _outletAccess = outletAccess;
    }

    /// <summary>Returns current stock levels, optionally filtered by location or low-stock status</summary>
    [HttpGet("stock-levels")]
    public async Task<ActionResult<ApiResponse<List<StockLevelDto>>>> GetStockLevels(
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] bool lowStockOnly = false)
    {
        var (resolvedId, resolvedType) =
            await _outletAccess.ResolveAndAuthorizeLocationAsync(locationId, locationType);

        var result = await _inventoryReportService.GetStockLevelsAsync(resolvedId, resolvedType, lowStockOnly);
        return Ok(ApiResponse<List<StockLevelDto>>.SuccessResponse(result));
    }

    /// <summary>Returns total inventory valuation with per-category breakdown</summary>
    [HttpGet("valuation")]
    public async Task<ActionResult<ApiResponse<InventoryValuationDto>>> GetValuation()
    {
        // Inventory valuation is whole-business; restrict to BusinessOwner.
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (!auth.IsBusinessOwner)
            throw new UnauthorizedAccessException("Inventory valuation is restricted to Business Admin.");

        var result = await _inventoryReportService.GetInventoryValuationAsync();
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(result));
    }

    /// <summary>Returns slow-moving inventory items</summary>
    [HttpGet("slow-moving")]
    public async Task<ActionResult<ApiResponse<List<SlowMovingItemDto>>>> GetSlowMoving(
        [FromQuery] int days = 90)
    {
        // Slow-moving aggregates across the whole business; restrict to BusinessOwner.
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (!auth.IsBusinessOwner)
            throw new UnauthorizedAccessException("Slow-moving inventory report is restricted to Business Admin.");

        var result = await _inventoryReportService.GetSlowMovingItemsAsync(days);
        return Ok(ApiResponse<List<SlowMovingItemDto>>.SuccessResponse(result));
    }

    /// <summary>
    /// Current Stock Report — paginated rows plus full-dataset summary totals.
    /// Summary totals are always calculated from the complete filtered dataset,
    /// independent of page/pageSize.
    /// </summary>
    [HttpGet("current-stock")]
    public async Task<ActionResult<ApiResponse<CurrentStockReportDto>>> GetCurrentStock(
        [FromQuery] long? outletId,
        [FromQuery] long? warehouseId,
        [FromQuery] long? categoryId,
        [FromQuery] long? productId,
        [FromQuery] string? stockStatus,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // Enforce outlet access scoping
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new CurrentStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            ProductId   = productId,
            StockStatus = stockStatus,
            Search      = search,
            SortBy      = sortBy ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = page < 1 ? 1 : page,
            PageSize    = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _inventoryReportService.GetCurrentStockReportAsync(filter);
        return Ok(ApiResponse<CurrentStockReportDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Current Stock Report export — returns ALL filtered rows (no pagination).
    /// Format: excel | csv
    /// </summary>
    [HttpGet("current-stock/export")]
    public async Task<IActionResult> ExportCurrentStock(
        [FromQuery] long? outletId,
        [FromQuery] long? warehouseId,
        [FromQuery] long? categoryId,
        [FromQuery] long? productId,
        [FromQuery] string? stockStatus,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new CurrentStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            ProductId   = productId,
            StockStatus = stockStatus,
            Search      = search,
            SortBy      = sortBy ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = 1,
            PageSize    = 0   // signals no pagination
        };

        var rows = await _inventoryReportService.GetCurrentStockExportAsync(filter);

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = CurrentStockExporter.ToExcel(rows);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"CurrentStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = CurrentStockExporter.ToCsv(rows);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                        $"CurrentStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ── Product Ledger Report ─────────────────────────────────────────────

    /// <summary>
    /// Product Ledger Report — paginated movement history for a product.
    /// Summary totals are always computed from the complete filtered dataset.
    /// </summary>
    [HttpGet("product-ledger")]
    public async Task<ActionResult<ApiResponse<ProductLedgerReportDto>>> GetProductLedger(
        [FromQuery] long productId,
        [FromQuery] long? variantId,
        [FromQuery] long? outletId,
        [FromQuery] long? warehouseId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? transactionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (productId <= 0)
            return BadRequest(ApiResponse<ProductLedgerReportDto>.ErrorResponse("productId is required."));

        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new ProductLedgerFilterDto
        {
            ProductId       = productId,
            VariantId       = variantId,
            OutletId        = resolvedOutletId,
            WarehouseId     = warehouseId,
            DateFrom        = dateFrom,
            DateTo          = dateTo,
            TransactionType = transactionType,
            Page            = page < 1 ? 1 : page,
            PageSize        = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        try
        {
            var result = await _inventoryReportService.GetProductLedgerReportAsync(filter);
            return Ok(ApiResponse<ProductLedgerReportDto>.SuccessResponse(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<ProductLedgerReportDto>.ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Product Ledger export — all filtered rows (no pagination). Format: excel | csv.
    /// </summary>
    [HttpGet("product-ledger/export")]
    public async Task<IActionResult> ExportProductLedger(
        [FromQuery] long productId,
        [FromQuery] long? variantId,
        [FromQuery] long? outletId,
        [FromQuery] long? warehouseId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? transactionType,
        [FromQuery] string format = "csv")
    {
        if (productId <= 0)
            return BadRequest("productId is required.");

        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new ProductLedgerFilterDto
        {
            ProductId       = productId,
            VariantId       = variantId,
            OutletId        = resolvedOutletId,
            WarehouseId     = warehouseId,
            DateFrom        = dateFrom,
            DateTo          = dateTo,
            TransactionType = transactionType,
            Page            = 1,
            PageSize        = 0
        };

        try
        {
            var rows = await _inventoryReportService.GetProductLedgerExportAsync(filter);

            // Build summary from export rows (since export method doesn't return summary)
            var summary = new ProductLedgerSummaryDto
            {
                TotalStockIn          = rows.Sum(r => r.StockIn),
                TotalStockOut         = rows.Sum(r => r.StockOut),
                TotalTransactionValue = rows.Sum(r => r.TransactionValue),
                OpeningStock          = rows.Count > 0 ? rows[0].OpeningQuantity : 0,
                ClosingStock          = rows.Count > 0 ? rows[^1].ClosingQuantity : 0
            };

            // Resolve product name for export header
            var product = await _inventoryReportService.GetProductLedgerReportAsync(
                new ProductLedgerFilterDto { ProductId = productId, Page = 1, PageSize = 1 });
            var productName = product.ProductName;

            if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = ProductLedgerExporter.ToExcel(rows, summary, productName);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"ProductLedger_{productId}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
            }
            else
            {
                var csv = ProductLedgerExporter.ToCsv(rows, summary, productName);
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                            $"ProductLedger_{productId}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
            }
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ── Stock Movement Report ─────────────────────────────────────────────

    /// <summary>
    /// Stock Movement Report — aggregated opening/in/out/closing per variant+location
    /// for a selected period. Summary always reflects the full filtered dataset.
    /// </summary>
    [HttpGet("stock-movement")]
    public async Task<ActionResult<ApiResponse<StockMovementReportDto>>> GetStockMovement(
        [FromQuery] long?     outletId,
        [FromQuery] long?     warehouseId,
        [FromQuery] long?     categoryId,
        [FromQuery] string?   search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?   sortBy,
        [FromQuery] string?   sortDir,
        [FromQuery] int       page     = 1,
        [FromQuery] int       pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockMovementFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            DateFrom    = dateFrom,
            DateTo      = dateTo,
            SortBy      = sortBy  ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = page < 1 ? 1 : page,
            PageSize    = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _inventoryReportService.GetStockMovementReportAsync(filter);
        return Ok(ApiResponse<StockMovementReportDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Stock Movement Report export — all filtered rows (no pagination). Format: excel | csv.
    /// </summary>
    [HttpGet("stock-movement/export")]
    public async Task<IActionResult> ExportStockMovement(
        [FromQuery] long?     outletId,
        [FromQuery] long?     warehouseId,
        [FromQuery] long?     categoryId,
        [FromQuery] string?   search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?   sortBy,
        [FromQuery] string?   sortDir,
        [FromQuery] string    format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockMovementFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            DateFrom    = dateFrom,
            DateTo      = dateTo,
            SortBy      = sortBy  ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = 1,
            PageSize    = 0
        };

        var rows = await _inventoryReportService.GetStockMovementExportAsync(filter);
        var summary = new StockMovementSummaryDto
        {
            TotalOpeningStock = rows.Sum(r => r.OpeningStock),
            TotalStockIn      = rows.Sum(r => r.StockIn),
            TotalStockOut     = rows.Sum(r => r.StockOut),
            TotalClosingStock = rows.Sum(r => r.ClosingStock),
            NetMovement       = rows.Sum(r => r.NetMovement)
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockMovementExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockMovement_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = StockMovementExporter.ToCsv(rows, summary);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                        $"StockMovement_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ── Stock Valuation Report ────────────────────────────────────────────

    /// <summary>
    /// Stock Valuation Report — inventory value per variant+location.
    /// Summary totals (total value, by-category breakdown) always reflect the full filtered dataset.
    /// </summary>
    [HttpGet("stock-valuation")]
    public async Task<ActionResult<ApiResponse<StockValuationReportDto>>> GetStockValuation(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockValuationFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            SortBy      = sortBy  ?? "inventoryValue",
            SortDir     = sortDir ?? "desc",
            Page        = page < 1 ? 1 : page,
            PageSize    = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _inventoryReportService.GetStockValuationReportAsync(filter);
        return Ok(ApiResponse<StockValuationReportDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Stock Valuation Report export — all filtered rows (no pagination). Format: excel | csv.
    /// </summary>
    [HttpGet("stock-valuation/export")]
    public async Task<IActionResult> ExportStockValuation(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockValuationFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            SortBy      = sortBy  ?? "inventoryValue",
            SortDir     = sortDir ?? "desc",
            Page        = 1,
            PageSize    = 0
        };

        var rows = await _inventoryReportService.GetStockValuationExportAsync(filter);
        var totalValue = rows.Sum(r => r.InventoryValue);
        foreach (var r in rows)
            r.PercentOfTotal = totalValue > 0 ? Math.Round(r.InventoryValue / totalValue * 100, 2) : 0;

        var summary = new StockValuationSummaryDto
        {
            TotalProducts       = rows.Select(r => r.VariantId).Distinct().Count(),
            TotalQuantity       = rows.Sum(r => r.Quantity),
            TotalInventoryValue = totalValue,
            ByCategory          = rows
                .GroupBy(r => r.CategoryName)
                .Select(g => new StockValuationCategoryDto
                {
                    CategoryName   = g.Key,
                    Quantity       = g.Sum(r => r.Quantity),
                    InventoryValue = g.Sum(r => r.InventoryValue),
                    PercentOfTotal = totalValue > 0 ? Math.Round(g.Sum(r => r.InventoryValue) / totalValue * 100, 2) : 0
                })
                .OrderByDescending(c => c.InventoryValue)
                .ToList()
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockValuationExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockValuation_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = StockValuationExporter.ToCsv(rows, summary);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                        $"StockValuation_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ── Outlet Wise Stock Report ──────────────────────────────────────────

    /// <summary>
    /// Outlet Wise Stock Report — stock per outlet per product variant.
    /// Per-outlet summary and totals always reflect the full filtered dataset.
    /// </summary>
    [HttpGet("outlet-wise-stock")]
    public async Task<ActionResult<ApiResponse<OutletWiseStockReportDto>>> GetOutletWiseStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 50)
    {
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new OutletWiseStockFilterDto
        {
            OutletId   = resolvedOutletId,
            CategoryId = categoryId,
            Search     = search,
            SortBy     = sortBy  ?? "outletName",
            SortDir    = sortDir ?? "asc",
            Page       = page < 1 ? 1 : page,
            PageSize   = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _inventoryReportService.GetOutletWiseStockReportAsync(filter);
        return Ok(ApiResponse<OutletWiseStockReportDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Outlet Wise Stock Report export — all filtered rows (no pagination). Format: excel | csv.
    /// </summary>
    [HttpGet("outlet-wise-stock/export")]
    public async Task<IActionResult> ExportOutletWiseStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv")
    {
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new OutletWiseStockFilterDto
        {
            OutletId   = resolvedOutletId,
            CategoryId = categoryId,
            Search     = search,
            SortBy     = sortBy  ?? "outletName",
            SortDir    = sortDir ?? "asc",
            Page       = 1,
            PageSize   = 0
        };

        var rows = await _inventoryReportService.GetOutletWiseStockExportAsync(filter);

        var byOutlet = rows
            .GroupBy(r => r.OutletId)
            .Select(g => new OutletStockSummaryDto
            {
                OutletId   = g.Key,
                OutletName = g.First().OutletName,
                TotalSkus  = g.Select(r => r.VariantId).Distinct().Count(),
                TotalQty   = g.Sum(r => r.Quantity),
                StockValue = g.Sum(r => r.StockValue)
            })
            .OrderBy(o => o.OutletName)
            .ToList();

        var summary = new OutletWiseStockSummaryDto
        {
            TotalOutlets    = byOutlet.Count,
            TotalSkus       = rows.Select(r => r.VariantId).Distinct().Count(),
            TotalQuantity   = rows.Sum(r => r.Quantity),
            TotalStockValue = rows.Sum(r => r.StockValue),
            ByOutlet        = byOutlet
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = OutletWiseStockExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"OutletWiseStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = OutletWiseStockExporter.ToCsv(rows, summary);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                        $"OutletWiseStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #6: Low Stock Report
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStockReport(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new LowStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "urgencyLevel",
            SortDir     = sortDir ?? "desc",
            Page        = page,
            PageSize    = pageSize
        };

        var result = await _inventoryReportService.GetLowStockReportAsync(filter);
        return Ok(result);
    }

    [HttpGet("low-stock/export")]
    public async Task<IActionResult> ExportLowStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new LowStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "urgencyLevel",
            SortDir     = sortDir ?? "desc",
            Page        = 1,
            PageSize    = 0
        };

        var rows    = await _inventoryReportService.GetLowStockExportAsync(filter);
        var summary = new LowStockSummaryDto
        {
            TotalSkus       = rows.Select(r => r.VariantId).Distinct().Count(),
            TotalDeficitQty = rows.Sum(r => r.SuggestedOrderQty),
            CriticalCount   = rows.Count(r => r.UrgencyLevel == "Critical"),
            LowCount        = rows.Count(r => r.UrgencyLevel == "Low")
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = LowStockExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"LowStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = LowStockExporter.ToCsv(rows);
            return File(bytes, "text/csv", $"LowStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #7: Out Of Stock Report
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("out-of-stock")]
    public async Task<IActionResult> GetOutOfStockReport(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new OutOfStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = page,
            PageSize    = pageSize
        };

        var result = await _inventoryReportService.GetOutOfStockReportAsync(filter);
        return Ok(result);
    }

    [HttpGet("out-of-stock/export")]
    public async Task<IActionResult> ExportOutOfStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new OutOfStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "productName",
            SortDir     = sortDir ?? "asc",
            Page        = 1,
            PageSize    = 0
        };

        var rows    = await _inventoryReportService.GetOutOfStockExportAsync(filter);
        var summary = new OutOfStockSummaryDto
        {
            TotalSkus           = rows.Select(r => r.VariantId).Distinct().Count(),
            TotalLocations      = rows.Select(r => r.LocationId).Distinct().Count(),
            EstimatedCostImpact = rows.Sum(r => r.UnitCost * r.ReorderLevel)
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = OutOfStockExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"OutOfStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = OutOfStockExporter.ToCsv(rows);
            return File(bytes, "text/csv", $"OutOfStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #8: Negative Stock Report
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("negative-stock")]
    public async Task<IActionResult> GetNegativeStockReport(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new NegativeStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "currentStock",
            SortDir     = sortDir ?? "asc",
            Page        = page,
            PageSize    = pageSize
        };

        var result = await _inventoryReportService.GetNegativeStockReportAsync(filter);
        return Ok(result);
    }

    [HttpGet("negative-stock/export")]
    public async Task<IActionResult> ExportNegativeStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new NegativeStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = sortBy  ?? "currentStock",
            SortDir     = sortDir ?? "asc",
            Page        = 1,
            PageSize    = 0
        };

        var rows    = await _inventoryReportService.GetNegativeStockExportAsync(filter);
        var summary = new NegativeStockSummaryDto
        {
            TotalSkus          = rows.Select(r => r.VariantId).Distinct().Count(),
            TotalNegativeQty   = rows.Sum(r => r.CurrentStock),
            TotalNegativeValue = rows.Sum(r => r.StockValue)
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = NegativeStockExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"NegativeStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = NegativeStockExporter.ToCsv(rows);
            return File(bytes, "text/csv", $"NegativeStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #9: Stock Adjustment Report
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("stock-adjustments")]
    public async Task<IActionResult> GetStockAdjustmentReport(
        [FromQuery] long?    outletId,
        [FromQuery] long?    warehouseId,
        [FromQuery] long?    categoryId,
        [FromQuery] string?  search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?  sortBy,
        [FromQuery] string?  sortDir,
        [FromQuery] int      page     = 1,
        [FromQuery] int      pageSize = 50)
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockAdjustmentFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search   ?? string.Empty,
            DateFrom    = dateFrom,
            DateTo      = dateTo,
            SortBy      = sortBy   ?? "adjustmentDate",
            SortDir     = sortDir  ?? "desc",
            Page        = page,
            PageSize    = pageSize
        };

        var result = await _inventoryReportService.GetStockAdjustmentReportAsync(filter);
        return Ok(result);
    }

    [HttpGet("stock-adjustments/export")]
    public async Task<IActionResult> ExportStockAdjustment(
        [FromQuery] long?    outletId,
        [FromQuery] long?    warehouseId,
        [FromQuery] long?    categoryId,
        [FromQuery] string?  search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?  sortBy,
        [FromQuery] string?  sortDir,
        [FromQuery] string   format = "csv")
    {
        await ValidateInventoryLocationFiltersAsync(outletId, warehouseId);
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var filter = new StockAdjustmentFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search   ?? string.Empty,
            DateFrom    = dateFrom,
            DateTo      = dateTo,
            SortBy      = sortBy   ?? "adjustmentDate",
            SortDir     = sortDir  ?? "desc",
            Page        = 1,
            PageSize    = 0
        };

        var rows = await _inventoryReportService.GetStockAdjustmentExportAsync(filter);
        var additions  = rows.Where(r => r.QuantityChange >= 0).Sum(r => r.QuantityChange);
        var reductions = rows.Where(r => r.QuantityChange < 0).Sum(r => r.QuantityChange);
        var summary = new StockAdjustmentSummaryDto
        {
            TotalAdjustments  = rows.Count,
            TotalAdditions    = additions,
            TotalReductions   = Math.Abs(reductions),
            NetQuantityChange = additions + reductions
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockAdjustmentExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockAdjustments_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = StockAdjustmentExporter.ToCsv(rows);
            return File(bytes, "text/csv", $"StockAdjustments_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #10: Stock Transfer Report
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("stock-transfers-report")]
    public async Task<IActionResult> GetStockTransferReport(
        [FromQuery] long?    fromOutletId,
        [FromQuery] long?    toOutletId,
        [FromQuery] long?    categoryId,
        [FromQuery] string?  status,
        [FromQuery] string?  search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?  sortBy,
        [FromQuery] string?  sortDir,
        [FromQuery] int      page     = 1,
        [FromQuery] int      pageSize = 50)
    {
        // For outlet-scoped users, restrict fromOutletId to their assigned outlet
        var resolvedFromOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(fromOutletId);
        if (toOutletId.HasValue)
            await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(toOutletId);

        var filter = new StockTransferFilterDto
        {
            FromOutletId = resolvedFromOutletId,
            ToOutletId   = toOutletId,
            CategoryId   = categoryId,
            Status       = status   ?? string.Empty,
            Search       = search   ?? string.Empty,
            DateFrom     = dateFrom,
            DateTo       = dateTo,
            SortBy       = sortBy   ?? "transferDate",
            SortDir      = sortDir  ?? "desc",
            Page         = page,
            PageSize     = pageSize
        };

        var result = await _inventoryReportService.GetStockTransferReportAsync(filter);
        return Ok(result);
    }

    [HttpGet("stock-transfers-report/export")]
    public async Task<IActionResult> ExportStockTransferReport(
        [FromQuery] long?    fromOutletId,
        [FromQuery] long?    toOutletId,
        [FromQuery] long?    categoryId,
        [FromQuery] string?  status,
        [FromQuery] string?  search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string?  sortBy,
        [FromQuery] string?  sortDir,
        [FromQuery] string   format = "csv")
    {
        var resolvedFromOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(fromOutletId);
        if (toOutletId.HasValue)
            await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(toOutletId);

        var filter = new StockTransferFilterDto
        {
            FromOutletId = resolvedFromOutletId,
            ToOutletId   = toOutletId,
            CategoryId   = categoryId,
            Status       = status   ?? string.Empty,
            Search       = search   ?? string.Empty,
            DateFrom     = dateFrom,
            DateTo       = dateTo,
            SortBy       = sortBy   ?? "transferDate",
            SortDir      = sortDir  ?? "desc",
            Page         = 1,
            PageSize     = 0
        };

        var rows = await _inventoryReportService.GetStockTransferReportExportAsync(filter);
        var distinctIds = rows.Select(r => r.TransferId).Distinct().ToList();
        var summary = new StockTransferSummaryDto
        {
            TotalTransfers = distinctIds.Count,
            TotalLines     = rows.Count,
            TotalQuantity  = rows.Sum(r => r.Quantity),
            TotalValue     = rows.Sum(r => r.TransferValue),
            PendingCount   = rows.Count(r => r.Status == "pending"),
            CompletedCount = rows.Count(r => r.Status == "completed")
        };

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockTransferReportExporter.ToExcel(rows, summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockTransfers_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = StockTransferReportExporter.ToCsv(rows);
            return File(bytes, "text/csv", $"StockTransfers_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private async Task ValidateInventoryLocationFiltersAsync(long? outletId, long? warehouseId)
    {
        if (outletId.HasValue && warehouseId.HasValue)
            throw new InvalidOperationException("Specify either outletId or warehouseId, not both.");

        if (warehouseId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(warehouseId, "warehouse");
    }
}
