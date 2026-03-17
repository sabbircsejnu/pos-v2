using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/purchases")]
[Authorize]
public class PurchaseReportsController : ControllerBase
{
    private readonly RetailPOSDbContext _context;

    public PurchaseReportsController(RetailPOSDbContext context)
    {
        _context = context;
    }

    /// <summary>Returns aggregated purchase order summary</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PurchaseSummaryDto>>> GetSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var end   = endDate?.ToUniversalTime()   ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        var orders = await _context.PurchaseOrders
            .AsNoTracking()
            .Where(po => po.OrderDate >= start && po.OrderDate <= end)
            .ToListAsync();

        var result = new PurchaseSummaryDto
        {
            TotalOrders       = orders.Count,
            TotalAmount       = orders.Sum(po => po.TotalAmount),
            PendingApprovals  = orders.Count(po => po.Status == "pending"),
            ReceivedOrders    = orders.Count(po => po.Status == "received")
        };

        return Ok(ApiResponse<PurchaseSummaryDto>.SuccessResponse(result));
    }

    /// <summary>Returns purchase totals grouped by supplier</summary>
    [HttpGet("by-supplier")]
    public async Task<ActionResult<ApiResponse<List<PurchaseBySupplierDto>>>> GetBySupplier(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var end   = endDate?.ToUniversalTime()   ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        var grouped = await _context.PurchaseOrders
            .AsNoTracking()
            .Where(po => po.OrderDate >= start && po.OrderDate <= end)
            .GroupBy(po => po.SupplierId)
            .Select(g => new
            {
                SupplierId  = g.Key,
                OrderCount  = g.Count(),
                TotalAmount = g.Sum(po => po.TotalAmount)
            })
            .ToListAsync();

        var supplierIds = grouped.Select(x => x.SupplierId).ToList();
        var supplierNames = await _context.Suppliers
            .AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var result = grouped
            .Select(g => new PurchaseBySupplierDto
            {
                SupplierId   = g.SupplierId,
                SupplierName = supplierNames.TryGetValue(g.SupplierId, out var n) ? n : string.Empty,
                OrderCount   = g.OrderCount,
                TotalAmount  = g.TotalAmount
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        return Ok(ApiResponse<List<PurchaseBySupplierDto>>.SuccessResponse(result));
    }
}
