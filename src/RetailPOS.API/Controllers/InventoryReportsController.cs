using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using MediatR;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/inventory")]
[Authorize(Policy = "reports.inventory")]
public class InventoryReportsController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public InventoryReportsController(
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _sender = sender;
    }

    /// <summary>Returns current stock levels, optionally filtered by location or low-stock status</summary>
    [HttpGet("stock-levels")]
    public async Task<ActionResult<ApiResponse<List<StockLevelDto>>>> GetStockLevels(
        [FromQuery] long? locationId,
        [FromQuery] string? locationType,
        [FromQuery] bool lowStockOnly = false,
        CancellationToken cancellationToken = default)
    {
        var (resolvedId, resolvedType) =
            await _outletAccess.ResolveAndAuthorizeLocationAsync(locationId, locationType);

        var result = await _sender.Send(
            new GetInventoryStockLevelsQuery(resolvedId, resolvedType, lowStockOnly),
            cancellationToken);
        var includeCost = User.CanViewCost();
        var rows = result.Select(item => new StockLevelDto
        {
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            Sku = item.Sku,
            CategoryName = item.CategoryName,
            LocationId = item.LocationId,
            LocationType = item.LocationType,
            LocationName = item.LocationName,
            Quantity = item.Quantity,
            ReorderLevel = item.ReorderLevel,
            IsLowStock = item.IsLowStock,
            EstimatedValue = includeCost ? item.EstimatedValue : null
        }).ToList();
        return Ok(ApiResponse<List<StockLevelDto>>.SuccessResponse(rows));
    }

    /// <summary>Returns total inventory valuation with per-category breakdown</summary>
    [HttpGet("valuation")]
    public async Task<ActionResult<ApiResponse<InventoryValuationDto>>> GetValuation(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetInventoryReportValuationQuery(), cancellationToken);
        var includeCost = User.CanViewCost();
        var byCategory = result.ByCategory.Select(category => new CategoryValuationDto
        {
            CategoryName = category.CategoryName,
            Value = includeCost ? category.Value : null,
            ItemCount = category.ItemCount
        });
        return Ok(ApiResponse<InventoryValuationDto>.SuccessResponse(new InventoryValuationDto
        {
            TotalValue = includeCost ? result.TotalValue : null,
            TotalItems = result.TotalItems,
            // The read model ranks categories by value; that order would still reveal cost.
            ByCategory = includeCost
                ? byCategory.ToList()
                : byCategory.OrderBy(category => category.CategoryName, StringComparer.OrdinalIgnoreCase).ToList()
        }));
    }

    /// <summary>Returns slow-moving inventory items</summary>
    [HttpGet("slow-moving")]
    public async Task<ActionResult<ApiResponse<List<SlowMovingItemDto>>>> GetSlowMoving(
        [FromQuery] int days = 90,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetSlowMovingInventoryItemsQuery(days), cancellationToken);
        var includeCost = User.CanViewCost();
        var items = result.Select(item => new SlowMovingItemDto
        {
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            Sku = item.Sku,
            CurrentStock = item.CurrentStock,
            DaysSinceLastSale = item.DaysSinceLastSale,
            EstimatedValue = includeCost ? item.EstimatedValue : null
        }).ToList();
        return Ok(ApiResponse<List<SlowMovingItemDto>>.SuccessResponse(items));
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
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Enforce outlet access scoping
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "productName", sortDir ?? "asc", includeCost, "productName");

        var filter = new CurrentStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            ProductId   = productId,
            StockStatus = stockStatus,
            Search      = search,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = page < 1 ? 1 : page,
            PageSize    = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _sender.Send(
            new GetCurrentStockReportQuery(new CurrentStockReportFilter(
                filter.OutletId,
                filter.WarehouseId,
                filter.CategoryId,
                filter.ProductId,
                filter.StockStatus,
                filter.Search,
                filter.SortBy,
                filter.SortDir,
                filter.Page,
                filter.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<CurrentStockReportDto>.SuccessResponse(new CurrentStockReportDto
        {
            Items = result.Items.Select(row => ToCurrentStockDto(row, includeCost)).ToList(),
            TotalCount = result.TotalCount,
            Summary = new CurrentStockSummaryDto
            {
                TotalProducts = result.Summary.TotalProducts,
                TotalQuantity = result.Summary.TotalQuantity,
                TotalStockValue = includeCost ? result.Summary.TotalStockValue : null
            }
        }));
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
        [FromQuery] string format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "productName", sortDir ?? "asc", includeCost, "productName");

        var filter = new CurrentStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            ProductId   = productId,
            StockStatus = stockStatus,
            Search      = search,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = 1,
            PageSize    = 0   // signals no pagination
        };

        var result = await _sender.Send(
            new GetCurrentStockReportQuery(new CurrentStockReportFilter(
                filter.OutletId,
                filter.WarehouseId,
                filter.CategoryId,
                filter.ProductId,
                filter.StockStatus,
                filter.Search,
                filter.SortBy,
                filter.SortDir,
                filter.Page,
                filter.PageSize)),
            cancellationToken);
        var rows = result.Items.Select(row => ToCurrentStockDto(row, includeCost)).ToList();

        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = CurrentStockExporter.ToExcel(rows, dateFormat, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"CurrentStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = CurrentStockExporter.ToCsv(rows, dateFormat, includeCost);
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
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
            return BadRequest(ApiResponse<ProductLedgerReportDto>.ErrorResponse("productId is required."));

        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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
            var result = await _sender.Send(
                new GetProductLedgerReportQuery(ToApplicationFilter(filter)),
                cancellationToken);
            return Ok(ApiResponse<ProductLedgerReportDto>.SuccessResponse(
                ToProductLedgerDto(result, User.CanViewCost())));
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
        [FromQuery] string format = "csv",
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
            return BadRequest("productId is required.");

        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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
            var result = await _sender.Send(
                new GetProductLedgerReportQuery(ToApplicationFilter(filter)),
                cancellationToken);
            var includeCost = User.CanViewCost();
            var rows = result.Items.Select(row => ToProductLedgerRowDto(row, includeCost)).ToList();

            // Build summary from export rows (since export method doesn't return summary)
            var summary = new ProductLedgerSummaryDto
            {
                TotalStockIn          = rows.Sum(r => r.StockIn),
                TotalStockOut         = rows.Sum(r => r.StockOut),
                TotalTransactionValue = includeCost ? rows.Sum(r => r.TransactionValue ?? 0m) : null,
                OpeningStock          = rows.Count > 0 ? rows[0].OpeningQuantity : 0,
                ClosingStock          = rows.Count > 0 ? rows[^1].ClosingQuantity : 0
            };

            var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = ProductLedgerExporter.ToExcel(rows, summary, result.ProductName, dateFormat, includeCost);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"ProductLedger_{productId}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
            }
            else
            {
                var csv = ProductLedgerExporter.ToCsv(rows, summary, result.ProductName, dateFormat, includeCost);
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
        [FromQuery] int       pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetStockMovementReportQuery(ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<StockMovementReportDto>.SuccessResponse(ToStockMovementDto(result)));
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
        [FromQuery] string    format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetStockMovementReportQuery(ToApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(ToStockMovementRowDto).ToList();
        var summary = new StockMovementSummaryDto
        {
            TotalOpeningStock = rows.Sum(r => r.OpeningStock),
            TotalStockIn      = rows.Sum(r => r.StockIn),
            TotalStockOut     = rows.Sum(r => r.StockOut),
            TotalClosingStock = rows.Sum(r => r.ClosingStock),
            NetMovement       = rows.Sum(r => r.NetMovement)
        };

        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockMovementExporter.ToExcel(rows, summary, dateFormat);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockMovement_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = StockMovementExporter.ToCsv(rows, summary, dateFormat);
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
        [FromQuery] int     pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            ValuationSafeSort(sortBy ?? "inventoryValue", sortDir ?? "desc", includeCost);

        var filter = new StockValuationFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = page < 1 ? 1 : page,
            PageSize    = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _sender.Send(
            new GetStockValuationReportQuery(ToApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<StockValuationReportDto>.SuccessResponse(ToStockValuationDto(result, includeCost)));
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
        [FromQuery] string  format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            ValuationSafeSort(sortBy ?? "inventoryValue", sortDir ?? "desc", includeCost);

        var filter = new StockValuationFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = 1,
            PageSize    = 0
        };

        var result = await _sender.Send(
            new GetStockValuationReportQuery(ToApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(row => ToStockValuationRowDto(row, includeCost)).ToList();
        StockValuationSummaryDto summary;
        if (includeCost)
        {
            var totalValue = rows.Sum(r => r.InventoryValue ?? 0m);
            foreach (var r in rows)
                r.PercentOfTotal = totalValue > 0 ? Math.Round((r.InventoryValue ?? 0m) / totalValue * 100, 2) : 0;

            summary = new StockValuationSummaryDto
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
                        InventoryValue = g.Sum(r => r.InventoryValue ?? 0m),
                        PercentOfTotal = totalValue > 0 ? Math.Round(g.Sum(r => r.InventoryValue ?? 0m) / totalValue * 100, 2) : 0
                    })
                    .OrderByDescending(c => c.InventoryValue)
                    .ToList()
            };
        }
        else
        {
            // Quantities by category stay; value, share of value and the value ranking are left out.
            summary = new StockValuationSummaryDto
            {
                TotalProducts       = rows.Select(r => r.VariantId).Distinct().Count(),
                TotalQuantity       = rows.Sum(r => r.Quantity),
                TotalInventoryValue = null,
                ByCategory          = rows
                    .GroupBy(r => r.CategoryName)
                    .Select(g => new StockValuationCategoryDto
                    {
                        CategoryName   = g.Key,
                        Quantity       = g.Sum(r => r.Quantity),
                        InventoryValue = null,
                        PercentOfTotal = null
                    })
                    .OrderBy(c => c.CategoryName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockValuationExporter.ToExcel(rows, summary, dateFormat, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockValuation_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = StockValuationExporter.ToCsv(rows, summary, dateFormat, includeCost);
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
        [FromQuery] int     pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "outletName", sortDir ?? "asc", includeCost, "outletName");

        var filter = new OutletWiseStockFilterDto
        {
            OutletId   = resolvedOutletId,
            CategoryId = categoryId,
            Search     = search,
            SortBy     = effectiveSortBy,
            SortDir    = effectiveSortDir,
            Page       = page < 1 ? 1 : page,
            PageSize   = pageSize is < 1 or > 500 ? 50 : pageSize
        };

        var result = await _sender.Send(
            new GetOutletWiseStockReportQuery(ToOutletWiseStockApplicationFilter(filter)),
            cancellationToken);
        return Ok(ApiResponse<OutletWiseStockReportDto>.SuccessResponse(new OutletWiseStockReportDto
        {
            Items = result.Items.Select(row => ToOutletWiseStockDto(row, includeCost)).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToOutletWiseStockSummaryDto(result.Summary, includeCost)
        }));
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
        [FromQuery] string  format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "outletName", sortDir ?? "asc", includeCost, "outletName");

        var filter = new OutletWiseStockFilterDto
        {
            OutletId   = resolvedOutletId,
            CategoryId = categoryId,
            Search     = search,
            SortBy     = effectiveSortBy,
            SortDir    = effectiveSortDir,
            Page       = 1,
            PageSize   = 0
        };

        var result = await _sender.Send(
            new GetOutletWiseStockReportQuery(ToOutletWiseStockApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(row => ToOutletWiseStockDto(row, includeCost)).ToList();
        var summary = ToOutletWiseStockSummaryDto(result.Summary, includeCost);

        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = OutletWiseStockExporter.ToExcel(rows, summary, dateFormat, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"OutletWiseStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var csv = OutletWiseStockExporter.ToCsv(rows, summary, dateFormat, includeCost);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
                        $"OutletWiseStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private static OutletWiseStockFilter ToOutletWiseStockApplicationFilter(OutletWiseStockFilterDto filter) =>
        new(
            filter.OutletId,
            filter.CategoryId,
            filter.Search,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static OutletWiseStockRowDto ToOutletWiseStockDto(OutletWiseStockRowReadModel row, bool includeCost) =>
        new()
        {
            OutletId = row.OutletId,
            OutletName = row.OutletName,
            VariantId = row.VariantId,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            Quantity = row.Quantity,
            UnitCost = includeCost ? row.UnitCost : null,
            StockValue = includeCost ? row.StockValue : null
        };

    private static OutletWiseStockSummaryDto ToOutletWiseStockSummaryDto(
        OutletWiseStockSummaryReadModel summary,
        bool includeCost) =>
        new()
        {
            TotalOutlets = summary.TotalOutlets,
            TotalSkus = summary.TotalSkus,
            TotalQuantity = summary.TotalQuantity,
            TotalStockValue = includeCost ? summary.TotalStockValue : null,
            ByOutlet = summary.ByOutlet.Select(outlet => new OutletStockSummaryDto
            {
                OutletId = outlet.OutletId,
                OutletName = outlet.OutletName,
                TotalSkus = outlet.TotalSkus,
                TotalQty = outlet.TotalQty,
                StockValue = includeCost ? outlet.StockValue : null
            }).ToList()
        };

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
        [FromQuery] int     pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetLowStockReportQuery(ToLowStockApplicationFilter(filter)),
            cancellationToken);
        return Ok(new LowStockReportDto
        {
            Items = result.Items.Select(ToLowStockDto).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToLowStockSummaryDto(result.Summary)
        });
    }

    [HttpGet("low-stock/export")]
    public async Task<IActionResult> ExportLowStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetLowStockReportQuery(ToLowStockApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(ToLowStockDto).ToList();
        var summary = ToLowStockSummaryDto(result.Summary);

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

    private static LowStockReportFilter ToLowStockApplicationFilter(LowStockFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static LowStockRowDto ToLowStockDto(LowStockRowReadModel row) =>
        new()
        {
            VariantId = row.VariantId,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            LocationId = row.LocationId,
            LocationType = row.LocationType,
            LocationName = row.LocationName,
            CurrentStock = row.CurrentStock,
            ReorderLevel = row.ReorderLevel,
            SuggestedOrderQty = row.SuggestedOrderQty,
            UrgencyLevel = row.UrgencyLevel
        };

    private static LowStockSummaryDto ToLowStockSummaryDto(LowStockSummaryReadModel summary) =>
        new()
        {
            TotalSkus = summary.TotalSkus,
            TotalDeficitQty = summary.TotalDeficitQty,
            CriticalCount = summary.CriticalCount,
            LowCount = summary.LowCount
        };

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
        [FromQuery] int     pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetOutOfStockReportQuery(ToOutOfStockApplicationFilter(filter)),
            cancellationToken);
        var includeCost = User.CanViewCost();
        return Ok(new OutOfStockReportDto
        {
            Items = result.Items.Select(row => ToOutOfStockDto(row, includeCost)).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToOutOfStockSummaryDto(result.Summary, includeCost)
        });
    }

    [HttpGet("out-of-stock/export")]
    public async Task<IActionResult> ExportOutOfStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetOutOfStockReportQuery(ToOutOfStockApplicationFilter(filter)),
            cancellationToken);
        var includeCost = User.CanViewCost();
        var rows = result.Items.Select(row => ToOutOfStockDto(row, includeCost)).ToList();
        var summary = ToOutOfStockSummaryDto(result.Summary, includeCost);

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = OutOfStockExporter.ToExcel(rows, summary, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"OutOfStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = OutOfStockExporter.ToCsv(rows, includeCost);
            return File(bytes, "text/csv", $"OutOfStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private static OutOfStockReportFilter ToOutOfStockApplicationFilter(OutOfStockFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static OutOfStockRowDto ToOutOfStockDto(OutOfStockRowReadModel row, bool includeCost) =>
        new()
        {
            VariantId = row.VariantId,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            LocationId = row.LocationId,
            LocationType = row.LocationType,
            LocationName = row.LocationName,
            ReorderLevel = row.ReorderLevel,
            UnitCost = includeCost ? row.UnitCost : null
        };

    private static OutOfStockSummaryDto ToOutOfStockSummaryDto(OutOfStockSummaryReadModel summary, bool includeCost) =>
        new()
        {
            TotalSkus = summary.TotalSkus,
            TotalLocations = summary.TotalLocations,
            EstimatedCostImpact = includeCost ? summary.EstimatedCostImpact : null
        };

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
        [FromQuery] int     pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "currentStock", sortDir ?? "asc", includeCost, "currentStock");

        var filter = new NegativeStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = page,
            PageSize    = pageSize
        };

        var result = await _sender.Send(
            new GetNegativeStockReportQuery(ToNegativeStockApplicationFilter(filter)),
            cancellationToken);
        return Ok(new NegativeStockReportDto
        {
            Items = result.Items.Select(row => ToNegativeStockDto(row, includeCost)).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToNegativeStockSummaryDto(result.Summary, includeCost)
        });
    }

    [HttpGet("negative-stock/export")]
    public async Task<IActionResult> ExportNegativeStock(
        [FromQuery] long?   outletId,
        [FromQuery] long?   warehouseId,
        [FromQuery] long?   categoryId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] string  format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "currentStock", sortDir ?? "asc", includeCost, "currentStock");

        var filter = new NegativeStockFilterDto
        {
            OutletId    = resolvedOutletId,
            WarehouseId = warehouseId,
            CategoryId  = categoryId,
            Search      = search ?? string.Empty,
            SortBy      = effectiveSortBy,
            SortDir     = effectiveSortDir,
            Page        = 1,
            PageSize    = 0
        };

        var result = await _sender.Send(
            new GetNegativeStockReportQuery(ToNegativeStockApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(row => ToNegativeStockDto(row, includeCost)).ToList();
        var summary = ToNegativeStockSummaryDto(result.Summary, includeCost);

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = NegativeStockExporter.ToExcel(rows, summary, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"NegativeStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = NegativeStockExporter.ToCsv(rows, includeCost);
            return File(bytes, "text/csv", $"NegativeStock_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private static NegativeStockReportFilter ToNegativeStockApplicationFilter(NegativeStockFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static NegativeStockRowDto ToNegativeStockDto(NegativeStockRowReadModel row, bool includeCost) =>
        new()
        {
            VariantId = row.VariantId,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            LocationId = row.LocationId,
            LocationType = row.LocationType,
            LocationName = row.LocationName,
            CurrentStock = row.CurrentStock,
            UnitCost = includeCost ? row.UnitCost : null,
            StockValue = includeCost ? row.StockValue : null
        };

    private static NegativeStockSummaryDto ToNegativeStockSummaryDto(
        NegativeStockSummaryReadModel summary,
        bool includeCost) =>
        new()
        {
            TotalSkus = summary.TotalSkus,
            TotalNegativeQty = summary.TotalNegativeQty,
            TotalNegativeValue = includeCost ? summary.TotalNegativeValue : null
        };

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
        [FromQuery] int      pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetStockAdjustmentReportQuery(ToStockAdjustmentApplicationFilter(filter)),
            cancellationToken);
        return Ok(new StockAdjustmentReportDto
        {
            Items = result.Items.Select(ToStockAdjustmentDto).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToStockAdjustmentSummaryDto(result.Summary)
        });
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
        [FromQuery] string   format = "csv",
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await ResolveInventoryOutletFilterAsync(outletId, warehouseId);

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

        var result = await _sender.Send(
            new GetStockAdjustmentReportQuery(ToStockAdjustmentApplicationFilter(filter)),
            cancellationToken);
        var rows = result.Items.Select(ToStockAdjustmentDto).ToList();
        var summary = ToStockAdjustmentSummaryDto(result.Summary);

        var dateFormat = (await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken)).Company.DateFormat;

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockAdjustmentExporter.ToExcel(rows, summary, dateFormat);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockAdjustments_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = StockAdjustmentExporter.ToCsv(rows, dateFormat);
            return File(bytes, "text/csv", $"StockAdjustments_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private static StockAdjustmentReportFilter ToStockAdjustmentApplicationFilter(
        StockAdjustmentFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.DateFrom,
            filter.DateTo,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static StockAdjustmentRowDto ToStockAdjustmentDto(StockAdjustmentRowReadModel row) =>
        new()
        {
            Id = row.Id,
            AdjustmentDate = row.AdjustmentDate,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            LocationId = row.LocationId,
            LocationType = row.LocationType,
            LocationName = row.LocationName,
            AdjustmentType = row.AdjustmentType,
            QuantityChange = row.QuantityChange,
            Reason = row.Reason,
            AdjustedBy = row.AdjustedBy
        };

    private static StockAdjustmentSummaryDto ToStockAdjustmentSummaryDto(
        StockAdjustmentSummaryReadModel summary) =>
        new()
        {
            TotalAdjustments = summary.TotalAdjustments,
            TotalAdditions = summary.TotalAdditions,
            TotalReductions = summary.TotalReductions,
            NetQuantityChange = summary.NetQuantityChange
        };

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
        [FromQuery] int      pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var visibility = await ResolveTransferVisibilityContextAsync();
        if (!string.IsNullOrWhiteSpace(visibility.ConfigurationError))
            return BadRequest(ApiResponse<StockTransferReportDto>.ErrorResponse(visibility.ConfigurationError));
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "transferDate", sortDir ?? "desc", includeCost, "transferDate");

        var filter = new StockTransferFilterDto
        {
            FromOutletId = fromOutletId,
            ToOutletId   = toOutletId,
            CategoryId   = categoryId,
            Status       = status   ?? string.Empty,
            Search       = search   ?? string.Empty,
            DateFrom     = dateFrom,
            DateTo       = dateTo,
            SortBy       = effectiveSortBy,
            SortDir      = effectiveSortDir,
            Page         = page,
            PageSize     = pageSize,
            CanViewAllLocations = visibility.CanViewAllLocations,
            DefaultLocationType = visibility.DefaultLocationType,
            DefaultLocationId = visibility.DefaultLocationId
        };

        var result = await _sender.Send(
            new GetStockTransferReportQuery(
                ToStockTransferApplicationFilter(filter),
                visibility.CanViewAllLocations,
                visibility.DefaultLocationType,
                visibility.DefaultLocationId),
            cancellationToken);
        return Ok(new StockTransferReportDto
        {
            Items = result.Items.Select(row => ToStockTransferDto(row, includeCost)).ToList(),
            TotalCount = result.TotalCount,
            Summary = ToStockTransferSummaryDto(result.Summary, includeCost)
        });
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
        [FromQuery] string   format = "csv",
        CancellationToken cancellationToken = default)
    {
        var visibility = await ResolveTransferVisibilityContextAsync();
        if (!string.IsNullOrWhiteSpace(visibility.ConfigurationError))
            return BadRequest(ApiResponse<object>.ErrorResponse(visibility.ConfigurationError));
        var includeCost = User.CanViewCost();
        var (effectiveSortBy, effectiveSortDir) =
            CostSafeSort(sortBy ?? "transferDate", sortDir ?? "desc", includeCost, "transferDate");

        var filter = new StockTransferFilterDto
        {
            FromOutletId = fromOutletId,
            ToOutletId   = toOutletId,
            CategoryId   = categoryId,
            Status       = status   ?? string.Empty,
            Search       = search   ?? string.Empty,
            DateFrom     = dateFrom,
            DateTo       = dateTo,
            SortBy       = effectiveSortBy,
            SortDir      = effectiveSortDir,
            Page         = 1,
            PageSize     = 0,
            CanViewAllLocations = visibility.CanViewAllLocations,
            DefaultLocationType = visibility.DefaultLocationType,
            DefaultLocationId = visibility.DefaultLocationId
        };

        var result = await _sender.Send(
            new GetStockTransferReportQuery(
                ToStockTransferApplicationFilter(filter),
                visibility.CanViewAllLocations,
                visibility.DefaultLocationType,
                visibility.DefaultLocationId),
            cancellationToken);
        var rows = result.Items.Select(row => ToStockTransferDto(row, includeCost)).ToList();
        var summary = ToStockTransferSummaryDto(result.Summary, includeCost);
        var settings = await _sender.Send(
            new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken);

        if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = StockTransferReportExporter.ToExcel(rows, summary, settings.Company.DateFormat, includeCost);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"StockTransfers_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
        else
        {
            var bytes = StockTransferReportExporter.ToCsv(rows, settings.Company.DateFormat, includeCost);
            return File(bytes, "text/csv", $"StockTransfers_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }
    }

    private async Task<long?> ResolveInventoryOutletFilterAsync(long? outletId, long? warehouseId)
    {
        if (outletId.HasValue && warehouseId.HasValue)
            throw new InvalidOperationException("Specify either outletId or warehouseId, not both.");

        if (!warehouseId.HasValue)
            return await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        // A warehouse-scoped report needs no outlet context.
        await _outletAccess.ResolveAndAuthorizeLocationAsync(warehouseId, "warehouse");
        return null;
    }

    private static StockTransferReportFilter ToStockTransferApplicationFilter(
        StockTransferFilterDto filter) =>
        new(
            filter.FromOutletId,
            filter.ToOutletId,
            filter.CategoryId,
            filter.Status,
            filter.Search,
            filter.DateFrom,
            filter.DateTo,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static StockTransferRowDto ToStockTransferDto(StockTransferReportRowReadModel row, bool includeCost) =>
        new()
        {
            TransferId = row.TransferId,
            TransferDate = row.TransferDate,
            FromLocationName = row.FromLocationName,
            FromLocationType = row.FromLocationType,
            ToLocationName = row.ToLocationName,
            ToLocationType = row.ToLocationType,
            Status = row.Status,
            ProductCode = row.ProductCode,
            Barcode = row.Barcode,
            Sku = row.Sku,
            ProductName = row.ProductName,
            CategoryName = row.CategoryName,
            Quantity = row.Quantity,
            UnitCost = includeCost ? row.UnitCost : null,
            TransferValue = includeCost ? row.TransferValue : null,
            CreatedBy = row.CreatedBy
        };

    private static StockTransferSummaryDto ToStockTransferSummaryDto(
        StockTransferReportSummaryReadModel summary,
        bool includeCost) =>
        new()
        {
            TotalTransfers = summary.TotalTransfers,
            TotalLines = summary.TotalLines,
            TotalQuantity = summary.TotalQuantity,
            TotalValue = includeCost ? summary.TotalValue : null,
            PendingCount = summary.PendingCount,
            CompletedCount = summary.CompletedCount
        };

    private static CurrentStockRowDto ToCurrentStockDto(CurrentStockRowReadModel row, bool includeCost) => new()
    {
        VariantId = row.VariantId,
        ProductCode = row.ProductCode,
        Barcode = row.Barcode,
        ProductName = row.ProductName,
        Category = row.Category,
        LocationId = row.LocationId,
        LocationType = row.LocationType,
        LocationName = row.LocationName,
        AvailableQuantity = row.AvailableQuantity,
        ReservedQuantity = row.ReservedQuantity,
        ReorderLevel = row.ReorderLevel,
        UnitCost = includeCost ? row.UnitCost : null,
        StockValue = includeCost ? row.StockValue : null,
        LastPurchaseDate = row.LastPurchaseDate,
        LastSaleDate = row.LastSaleDate,
        StockStatus = row.StockStatus
    };

    private static ProductLedgerReportFilter ToApplicationFilter(ProductLedgerFilterDto filter) =>
        new(
            filter.ProductId,
            filter.VariantId,
            filter.OutletId,
            filter.WarehouseId,
            filter.DateFrom,
            filter.DateTo,
            filter.TransactionType,
            filter.Page,
            filter.PageSize);

    private static StockMovementReportFilter ToApplicationFilter(StockMovementFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.DateFrom,
            filter.DateTo,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static StockValuationReportFilter ToApplicationFilter(StockValuationFilterDto filter) =>
        new(
            filter.OutletId,
            filter.WarehouseId,
            filter.CategoryId,
            filter.Search,
            filter.SortBy,
            filter.SortDir,
            filter.Page,
            filter.PageSize);

    private static StockValuationReportDto ToStockValuationDto(StockValuationReportReadModel report, bool includeCost)
    {
        var byCategory = report.Summary.ByCategory
            .Select(category => new StockValuationCategoryDto
            {
                CategoryName = category.CategoryName,
                Quantity = category.Quantity,
                InventoryValue = includeCost ? category.InventoryValue : null,
                PercentOfTotal = includeCost ? category.PercentOfTotal : null
            });
        return new()
        {
            Items = report.Items.Select(row => ToStockValuationRowDto(row, includeCost)).ToList(),
            TotalCount = report.TotalCount,
            Summary = new StockValuationSummaryDto
            {
                TotalProducts = report.Summary.TotalProducts,
                TotalQuantity = report.Summary.TotalQuantity,
                TotalInventoryValue = includeCost ? report.Summary.TotalInventoryValue : null,
                // The read model ranks categories by value; that order would still reveal cost.
                ByCategory = includeCost
                    ? byCategory.ToList()
                    : byCategory.OrderBy(category => category.CategoryName, StringComparer.OrdinalIgnoreCase).ToList()
            }
        };
    }

    private static StockValuationRowDto ToStockValuationRowDto(StockValuationRowReadModel row, bool includeCost) => new()
    {
        VariantId = row.VariantId,
        ProductCode = row.ProductCode,
        Barcode = row.Barcode,
        Sku = row.Sku,
        ProductName = row.ProductName,
        CategoryName = row.CategoryName,
        LocationId = row.LocationId,
        LocationType = row.LocationType,
        LocationName = row.LocationName,
        Quantity = row.Quantity,
        UnitCost = includeCost ? row.UnitCost : null,
        InventoryValue = includeCost ? row.InventoryValue : null,
        PercentOfTotal = includeCost ? row.PercentOfTotal : null
    };

    private static StockMovementReportDto ToStockMovementDto(StockMovementReportReadModel report) => new()
    {
        Items = report.Items.Select(ToStockMovementRowDto).ToList(),
        TotalCount = report.TotalCount,
        Summary = new StockMovementSummaryDto
        {
            TotalOpeningStock = report.Summary.TotalOpeningStock,
            TotalStockIn = report.Summary.TotalStockIn,
            TotalStockOut = report.Summary.TotalStockOut,
            TotalClosingStock = report.Summary.TotalClosingStock,
            NetMovement = report.Summary.NetMovement
        }
    };

    private static StockMovementRowDto ToStockMovementRowDto(StockMovementRowReadModel row) => new()
    {
        VariantId = row.VariantId,
        ProductCode = row.ProductCode,
        Sku = row.Sku,
        ProductName = row.ProductName,
        VariantName = row.VariantName,
        VariantAttributes = row.VariantAttributes,
        CategoryName = row.CategoryName,
        LocationId = row.LocationId,
        LocationType = row.LocationType,
        LocationName = row.LocationName,
        OpeningStock = row.OpeningStock,
        StockIn = row.StockIn,
        StockOut = row.StockOut,
        ClosingStock = row.ClosingStock,
        NetMovement = row.NetMovement
    };

    private static ProductLedgerReportDto ToProductLedgerDto(ProductLedgerReportReadModel report, bool includeCost) => new()
    {
        ProductId = report.ProductId,
        ProductName = report.ProductName,
        ProductSku = report.ProductSku,
        VariantId = report.VariantId,
        VariantName = report.VariantName,
        Items = report.Items.Select(row => ToProductLedgerRowDto(row, includeCost)).ToList(),
        TotalCount = report.TotalCount,
        Summary = new ProductLedgerSummaryDto
        {
            OpeningStock = report.Summary.OpeningStock,
            TotalStockIn = report.Summary.TotalStockIn,
            TotalStockOut = report.Summary.TotalStockOut,
            ClosingStock = report.Summary.ClosingStock,
            TotalTransactionValue = includeCost ? report.Summary.TotalTransactionValue : null
        }
    };

    /// <summary>
    /// Cost and value, and remarks that quote a cost, are left out for a caller without <c>products.view_cost</c>
    /// (the same rule as the stock ledger). Quantities, dates and references stay.
    /// </summary>
    private static ProductLedgerRowDto ToProductLedgerRowDto(ProductLedgerRowReadModel row, bool includeCost) => new()
    {
        LedgerId = row.LedgerId,
        TransactionDate = row.TransactionDate,
        TransactionType = row.TransactionType,
        TransactionTypeLabel = row.TransactionTypeLabel,
        ReferenceNumber = row.ReferenceNumber,
        VariantId = row.VariantId,
        ProductName = row.ProductName,
        Sku = row.Sku,
        LocationId = row.LocationId,
        LocationName = row.LocationName,
        OpeningQuantity = row.OpeningQuantity,
        StockIn = row.StockIn,
        StockOut = row.StockOut,
        ClosingQuantity = row.ClosingQuantity,
        UnitCost = includeCost ? row.UnitCost : null,
        TransactionValue = includeCost ? row.TransactionValue : null,
        PerformedBy = row.PerformedBy,
        Remarks = includeCost ? row.Remarks : CostSafeRemarks(row.Remarks)
    };

    /// <summary>A remark that quotes a cost is replaced for a caller without <c>products.view_cost</c>.</summary>
    private static string? CostSafeRemarks(string? remarks) =>
        remarks?.Contains("cost", StringComparison.OrdinalIgnoreCase) == true
            ? "Cost details require the view-cost permission."
            : remarks;

    private static readonly HashSet<string> CostSortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unitCost", "stockValue", "inventoryValue", "transferValue", "percentOfTotal", "estimatedValue", "value"
    };

    private static readonly HashSet<string> ValuationNeutralSortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "productCode", "sku", "category", "location", "quantity"
    };

    /// <summary>
    /// Without <c>products.view_cost</c> a cost or value sort would still rank the rows by cost,
    /// so it falls back to a neutral key.
    /// </summary>
    private static (string SortBy, string SortDir) CostSafeSort(
        string sortBy, string sortDir, bool includeCost, string neutralSortBy) =>
        includeCost || !CostSortKeys.Contains(sortBy) ? (sortBy, sortDir) : (neutralSortBy, "asc");

    /// <summary>The valuation store ranks by value for any key it does not know, so only neutral keys pass.</summary>
    private static (string SortBy, string SortDir) ValuationSafeSort(string sortBy, string sortDir, bool includeCost) =>
        includeCost || ValuationNeutralSortKeys.Contains(sortBy) ? (sortBy, sortDir) : ("productCode", "asc");

    private async Task<(bool CanViewAllLocations, string? DefaultLocationType, long? DefaultLocationId, string? ConfigurationError)> ResolveTransferVisibilityContextAsync()
    {
        if (User.HasPermission("stock_transfers.view_all_locations"))
            return (true, null, null, null);

        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
            return (false, null, null, "Your default inventory location is not configured.");

        var normalizedType = auth.DefaultLocationType.Trim().ToLowerInvariant();
        if (normalizedType != "outlet" && normalizedType != "warehouse")
            return (false, null, null, "Your default inventory location is not configured.");

        return (false, normalizedType, auth.DefaultLocationId.Value, null);
    }
}
