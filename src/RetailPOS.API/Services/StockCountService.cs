using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.StockCount;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using System.Collections.Concurrent;
using System.Globalization;

namespace RetailPOS.API.Services;

public class StockCountService : IStockCountService
{
    private const string NumberPrefix = "SC";
    private const string AdjustmentNumberPrefix = "ADJ";
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> AdjustmentGenerationLocks = new();

    private readonly RetailPOSDbContext _context;
    private readonly ITenantAccessService _tenantAccess;
    private readonly IUserOutletAccessService _locationAccess;
    private readonly ILogger<StockCountService> _logger;

    public StockCountService(
        RetailPOSDbContext context,
        ITenantAccessService tenantAccess,
        IUserOutletAccessService locationAccess,
        ILogger<StockCountService> logger)
    {
        _context = context;
        _tenantAccess = tenantAccess;
        _locationAccess = locationAccess;
        _logger = logger;
    }

    public async Task<StockCountListDto> SearchAsync(StockCountSearchDto dto)
    {
        var (resolvedLocationId, resolvedLocationType) = await _locationAccess.ResolveAndAuthorizeLocationAsync(dto.LocationId, dto.LocationType);

        var query = _context.StockCounts
            .AsNoTracking()
            .Include(s => s.Creator)
            .AsQueryable();

        if (!_tenantAccess.IsSuperAdmin)
        {
            var businessId = _tenantAccess.RequireBusinessId();
            query = query.Where(s => s.BusinessId == businessId);
        }
        else if (_tenantAccess.EffectiveBusinessId.HasValue)
        {
            query = query.Where(s => s.BusinessId == _tenantAccess.EffectiveBusinessId.Value);
        }

        if (resolvedLocationId.HasValue)
        {
            query = query.Where(s => s.LocationId == resolvedLocationId.Value && s.LocationType == resolvedLocationType);
        }
        else if (!string.IsNullOrWhiteSpace(resolvedLocationType))
        {
            query = query.Where(s => s.LocationType == resolvedLocationType);
        }

        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            query = query.Where(s => s.Status == dto.Status);
        }

        if (dto.DateFrom.HasValue)
        {
            query = query.Where(s => s.StockCountDate >= dto.DateFrom.Value.Date);
        }

        if (dto.DateTo.HasValue)
        {
            var inclusiveTo = dto.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(s => s.StockCountDate <= inclusiveTo);
        }

        if (!string.IsNullOrWhiteSpace(dto.Search))
        {
            var term = dto.Search.Trim();
            query = query.Where(s => s.StockCountNo.Contains(term) || (s.Remarks != null && s.Remarks.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((Math.Max(dto.PageNumber, 1) - 1) * Math.Clamp(dto.PageSize, 1, 500))
            .Take(Math.Clamp(dto.PageSize, 1, 500))
            .ToListAsync();

        var dtos = new List<StockCountDto>(items.Count);
        foreach (var item in items)
        {
            var movement = await GetMovementSummaryAsync(item.Id, item.LocationId, item.LocationType, item.CreatedAt);
            dtos.Add(new StockCountDto
            {
                Id = item.Id,
                StockCountNo = item.StockCountNo,
                BusinessId = item.BusinessId,
                LocationId = item.LocationId,
                LocationType = item.LocationType,
                LocationName = await GetLocationNameAsync(item.LocationId, item.LocationType),
                StockCountDate = item.StockCountDate,
                Status = item.Status,
                Remarks = item.Remarks,
                TotalItems = item.TotalItems,
                CreatedBy = item.CreatedBy,
                CreatedByName = item.Creator?.Name ?? string.Empty,
                CreatedAt = item.CreatedAt,
                SubmittedBy = item.SubmittedBy,
                SubmittedAt = item.SubmittedAt,
                ApprovedBy = item.ApprovedBy,
                ApprovedAt = item.ApprovedAt,
                RejectedBy = item.RejectedBy,
                RejectedAt = item.RejectedAt,
                RejectionReason = item.RejectionReason,
                HasPostGenerationMovements = movement.HasMovement,
                PostGenerationMovementCount = movement.Count,
                LastPostGenerationMovementAt = movement.LastMovementAt,
            });
        }

        return new StockCountListDto
        {
            StockCounts = dtos,
            TotalCount = totalCount,
            PageNumber = Math.Max(dto.PageNumber, 1),
            PageSize = Math.Clamp(dto.PageSize, 1, 500)
        };
    }

    public async Task<StockCountDto> GetByIdAsync(long id)
    {
        var stockCount = await _context.StockCounts
            .AsNoTracking()
            .Include(s => s.Creator)
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new KeyNotFoundException($"Stock count with ID {id} not found");

        EnsureBusinessAccess(stockCount.BusinessId);
        await _locationAccess.ResolveAndAuthorizeLocationAsync(stockCount.LocationId, stockCount.LocationType);

        var locationName = await GetLocationNameAsync(stockCount.LocationId, stockCount.LocationType);
        var movement = await GetMovementSummaryAsync(stockCount.Id, stockCount.LocationId, stockCount.LocationType, stockCount.CreatedAt);

        return new StockCountDto
        {
            Id = stockCount.Id,
            StockCountNo = stockCount.StockCountNo,
            BusinessId = stockCount.BusinessId,
            LocationId = stockCount.LocationId,
            LocationType = stockCount.LocationType,
            LocationName = locationName,
            StockCountDate = stockCount.StockCountDate,
            Status = stockCount.Status,
            Remarks = stockCount.Remarks,
            TotalItems = stockCount.TotalItems,
            CreatedBy = stockCount.CreatedBy,
            CreatedByName = stockCount.Creator?.Name ?? string.Empty,
            CreatedAt = stockCount.CreatedAt,
            SubmittedBy = stockCount.SubmittedBy,
            SubmittedAt = stockCount.SubmittedAt,
            ApprovedBy = stockCount.ApprovedBy,
            ApprovedAt = stockCount.ApprovedAt,
            RejectedBy = stockCount.RejectedBy,
            RejectedAt = stockCount.RejectedAt,
            RejectionReason = stockCount.RejectionReason,
            HasPostGenerationMovements = movement.HasMovement,
            PostGenerationMovementCount = movement.Count,
            LastPostGenerationMovementAt = movement.LastMovementAt,
            Lines = stockCount.Lines
                .OrderBy(l => l.ProductName)
                .ThenBy(l => l.VariantName)
                .Select(MapLine)
                .ToList()
        };
    }

    public async Task<StockCountDto> CreateAsync(CreateStockCountDto dto, long createdBy)
    {
        var (resolvedLocationId, resolvedLocationType) = await _locationAccess.ResolveAndAuthorizeLocationAsync(dto.LocationId, dto.LocationType);
        if (!resolvedLocationId.HasValue || string.IsNullOrWhiteSpace(resolvedLocationType))
        {
            throw new InvalidOperationException("A valid authorized location is required.");
        }

        var businessId = _tenantAccess.RequireBusinessId();
        var locationId = resolvedLocationId.Value;
        var locationType = resolvedLocationType!;

        var hasActive = await _context.StockCounts
            .AnyAsync(s => s.BusinessId == businessId
                && s.LocationId == locationId
                && s.LocationType == locationType
                && (s.Status == StockCount.StatusDraft || s.Status == StockCount.StatusSubmitted));

        if (hasActive)
        {
            throw new InvalidOperationException("An active stock count already exists for this location.");
        }

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.Product.Status == ProductStatus.Active)
            .Select(v => new
            {
                v.Id,
                v.Name,
                v.ProductId,
                ProductName = v.Product.Name,
                ProductCode = v.Product.ProductCode ?? v.Product.Sku ?? v.Sku
            })
            .ToListAsync();

        var stocks = await _context.Inventories
            .AsNoTracking()
            .Where(i => i.LocationId == locationId && i.LocationType == locationType)
            .GroupBy(i => i.VariantId)
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Quantity);

        var now = DateTime.UtcNow;
        var stockCount = new StockCount
        {
            StockCountNo = await GenerateNumberAsync(),
            BusinessId = businessId,
            LocationId = locationId,
            LocationType = locationType,
            StockCountDate = dto.StockCountDate == default ? now.Date : dto.StockCountDate.Date,
            Status = StockCount.StatusDraft,
            Remarks = dto.Remarks,
            TotalItems = variants.Count,
            CreatedBy = createdBy,
            CreatedAt = now,
            Lines = variants.Select(v => new StockCountLine
            {
                ProductId = v.ProductId,
                VariantId = v.Id,
                ProductName = v.ProductName,
                ProductCode = v.ProductCode ?? string.Empty,
                VariantName = v.Name,
                CurrentStock = stocks.TryGetValue(v.Id, out var qty) ? qty : 0m,
                PhysicalStock = null,
                Difference = null,
                Remarks = null
            }).ToList()
        };

        _context.StockCounts.Add(stockCount);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Stock count {StockCountNo} generated for location {LocationType}:{LocationId}", stockCount.StockCountNo, locationType, locationId);

        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<(byte[] Content, string FileName)> DownloadExcelAsync(long id)
    {
        var stockCount = await GetByIdAsync(id);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Stock Count");

        ws.Cell(1, 1).Value = "Stock Count No";
        ws.Cell(1, 2).Value = stockCount.StockCountNo;
        ws.Cell(2, 1).Value = "Location";
        ws.Cell(2, 2).Value = stockCount.LocationName;
        ws.Cell(3, 1).Value = "Stock Count Date";
        ws.Cell(3, 2).Value = stockCount.StockCountDate;
        ws.Cell(3, 2).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Cell(4, 1).Value = "Generated By";
        ws.Cell(4, 2).Value = stockCount.CreatedByName;
        ws.Cell(5, 1).Value = "Generated Time";
        ws.Cell(5, 2).Value = stockCount.CreatedAt;
        ws.Cell(5, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

        var row = 7;
        ws.Cell(row, 1).Value = "SL";
        ws.Cell(row, 2).Value = "Product Name";
        ws.Cell(row, 3).Value = "Product Code";
        ws.Cell(row, 4).Value = "Variant";
        ws.Cell(row, 5).Value = "Current Stock";
        ws.Cell(row, 6).Value = "Physical Count";
        ws.Cell(row, 7).Value = "Difference";
        ws.Cell(row, 8).Value = "Remarks";
        ws.Row(row).Style.Font.Bold = true;

        row++;
        for (var i = 0; i < stockCount.Lines.Count; i++)
        {
            var line = stockCount.Lines[i];
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = line.ProductName;
            ws.Cell(row, 3).Value = line.ProductCode;
            ws.Cell(row, 4).Value = line.VariantName;
            ws.Cell(row, 5).Value = line.CurrentStock;
            ws.Cell(row, 6).Value = line.PhysicalStock;
            ws.Cell(row, 7).Value = line.Difference;
            ws.Cell(row, 8).Value = line.Remarks;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"StockCount_{stockCount.StockCountNo}.xlsx");
    }

    public async Task<StockCountPrintDto> GetPrintDataAsync(long id)
    {
        var stockCount = await GetByIdAsync(id);
        var businessName = await _context.Businesses
            .AsNoTracking()
            .Where(b => b.Id == stockCount.BusinessId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync() ?? "Business";

        return new StockCountPrintDto
        {
            Id = stockCount.Id,
            StockCountNo = stockCount.StockCountNo,
            CompanyName = businessName,
            LocationName = stockCount.LocationName,
            LocationType = stockCount.LocationType,
            StockCountDate = stockCount.StockCountDate,
            GeneratedBy = stockCount.CreatedByName,
            GeneratedAt = stockCount.CreatedAt,
            HasPostGenerationMovements = stockCount.HasPostGenerationMovements,
            PostGenerationMovementCount = stockCount.PostGenerationMovementCount,
            LastPostGenerationMovementAt = stockCount.LastPostGenerationMovementAt,
            Lines = stockCount.Lines
        };
    }

    public async Task<StockCountDto> UploadAsync(long id, long userId, IFormFile file)
    {
        var stockCount = await GetEditableStockCountAsync(id);
        if (stockCount.Status != StockCount.StatusDraft)
        {
            throw new InvalidOperationException("Excel upload is only allowed while stock count is in Draft status.");
        }

        if (file.Length == 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .xlsx files are supported for stock count upload.");
        }

        // Download export uses product-name ordering; keep the same order for SL-to-line mapping.
        var orderedLines = stockCount.Lines
            .OrderBy(l => l.ProductName)
            .ThenBy(l => l.VariantName)
            .ToList();

        await using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Uploaded workbook does not contain any worksheet.");

        var updates = 0;
        var meaningfulUpdates = 0;

        // Data rows start at row 8 in the generated template.
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 7;
        for (var row = 8; row <= lastRow; row++)
        {
            var slCell = ws.Cell(row, 1).GetValue<string>().Trim();
            var physicalCell = ws.Cell(row, 6).GetValue<string>().Trim();
            var remarksCell = ws.Cell(row, 8).GetValue<string>();

            if (string.IsNullOrWhiteSpace(slCell) && string.IsNullOrWhiteSpace(physicalCell) && string.IsNullOrWhiteSpace(remarksCell))
            {
                continue;
            }

            if (!int.TryParse(slCell, out var sl) || sl < 1 || sl > orderedLines.Count)
            {
                throw new InvalidOperationException($"Invalid serial number at row {row}: '{slCell}'");
            }

            var line = orderedLines[sl - 1];
            var previousPhysical = line.PhysicalStock;
            var previousDifference = line.Difference;
            var previousRemarks = line.Remarks;

            if (string.IsNullOrWhiteSpace(physicalCell))
            {
                line.PhysicalStock = null;
                line.Difference = null;
            }
            else
            {
                if (!decimal.TryParse(physicalCell, NumberStyles.Number, CultureInfo.InvariantCulture, out var physicalStock)
                    && !decimal.TryParse(physicalCell, NumberStyles.Number, CultureInfo.CurrentCulture, out physicalStock))
                {
                    throw new InvalidOperationException($"Invalid physical count value at row {row}: '{physicalCell}'");
                }

                if (physicalStock < 0)
                {
                    throw new InvalidOperationException($"Physical count cannot be negative at row {row}.");
                }

                line.PhysicalStock = physicalStock;
                line.Difference = physicalStock - line.CurrentStock;
            }

            line.Remarks = string.IsNullOrWhiteSpace(remarksCell) ? null : remarksCell.Trim();
            updates++;

            if (line.PhysicalStock != previousPhysical
                || line.Difference != previousDifference
                || !string.Equals(line.Remarks, previousRemarks, StringComparison.Ordinal))
            {
                meaningfulUpdates++;
            }
        }

        if (updates == 0)
        {
            throw new InvalidOperationException("No stock count rows were detected in uploaded Excel.");
        }

        if (meaningfulUpdates == 0)
        {
            throw new InvalidOperationException("Uploaded Excel does not contain any changes to physical count or remarks.");
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Stock count {StockCountNo} uploaded by user {UserId}", stockCount.StockCountNo, userId);

        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<StockCountDto> SubmitAsync(long id, long userId)
    {
        var stockCount = await GetEditableStockCountAsync(id);
        if (stockCount.Status != StockCount.StatusDraft)
        {
            throw new InvalidOperationException("Only Draft stock counts can be submitted.");
        }

        var hasUploadedCounts = stockCount.Lines.Any(l => l.PhysicalStock.HasValue || !string.IsNullOrWhiteSpace(l.Remarks));
        if (!hasUploadedCounts)
        {
            throw new InvalidOperationException("Upload completed Excel before submitting stock count.");
        }

        stockCount.Status = StockCount.StatusSubmitted;
        stockCount.SubmittedBy = userId;
        stockCount.SubmittedAt = DateTime.UtcNow;
        stockCount.RejectedBy = null;
        stockCount.RejectedAt = null;
        stockCount.RejectionReason = null;

        await _context.SaveChangesAsync();
        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<StockCountDto> ApproveAsync(long id, long userId)
    {
        var stockCount = await GetEditableStockCountAsync(id);
        if (stockCount.Status != StockCount.StatusSubmitted)
        {
            throw new InvalidOperationException("Only Submitted stock counts can be approved.");
        }

        var hasMissingPhysicalCounts = stockCount.Lines.Any(l => !l.PhysicalStock.HasValue);
        if (hasMissingPhysicalCounts)
        {
            throw new InvalidOperationException("All lines must have physical counts before approval.");
        }

        stockCount.Status = StockCount.StatusApproved;
        stockCount.ApprovedBy = userId;
        stockCount.ApprovedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<StockCountDto> RejectAsync(long id, long userId, string? reason)
    {
        var stockCount = await GetEditableStockCountAsync(id);
        if (stockCount.Status != StockCount.StatusSubmitted)
        {
            throw new InvalidOperationException("Only Submitted stock counts can be rejected.");
        }

        stockCount.Status = StockCount.StatusRejected;
        stockCount.RejectedBy = userId;
        stockCount.RejectedAt = DateTime.UtcNow;
        stockCount.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await _context.SaveChangesAsync();
        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<StockCountDto> ReopenAsync(long id, long userId)
    {
        var stockCount = await GetEditableStockCountAsync(id);
        if (stockCount.Status != StockCount.StatusRejected)
        {
            throw new InvalidOperationException("Only Rejected stock counts can be reopened.");
        }

        stockCount.Status = StockCount.StatusDraft;
        stockCount.RejectedBy = null;
        stockCount.RejectedAt = null;
        stockCount.RejectionReason = null;

        await _context.SaveChangesAsync();
        return await GetByIdAsync(stockCount.Id);
    }

    public async Task<StockCountDto> GenerateAdjustmentDraftAsync(long id, long userId)
    {
        var generationLock = AdjustmentGenerationLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await generationLock.WaitAsync();
        try
        {
            var stockCount = await GetEditableStockCountAsync(id);
            if (stockCount.Status != StockCount.StatusApproved)
            {
                throw new InvalidOperationException("Stock adjustment draft can only be generated from Approved stock counts.");
            }

            var existingDraft = await _context.StockAdjustments
                .AsNoTracking()
                .AnyAsync(a => a.SourceStockCountId == stockCount.Id);

            if (existingDraft)
            {
                throw new InvalidOperationException("Adjustment draft has already been generated for this stock count.");
            }

            var changedLines = stockCount.Lines
                .Where(l => l.Difference.HasValue && l.Difference.Value != 0)
                .ToList();

            if (changedLines.Count == 0)
            {
                throw new InvalidOperationException("No stock differences found to generate adjustment draft.");
            }

            var now = DateTime.UtcNow;
            var adjustmentLines = new List<StockAdjustmentLine>(changedLines.Count);

            foreach (var line in changedLines)
            {
                var difference = line.Difference!.Value;
                if (difference != decimal.Truncate(difference))
                {
                    throw new InvalidOperationException(
                        $"Difference for variant '{line.VariantName}' has fractional quantity and cannot be converted to stock adjustment draft.");
                }

                var quantityChange = decimal.ToInt32(difference);
                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.VariantId == line.VariantId
                        && i.LocationId == stockCount.LocationId
                        && i.LocationType == stockCount.LocationType);

                var currentQuantity = inventory?.Quantity ?? 0;
                var newQuantity = currentQuantity + quantityChange;
                if (newQuantity < 0)
                {
                    throw new InvalidOperationException(
                        $"Generated adjustment would make inventory negative for variant '{line.VariantName}'.");
                }

                adjustmentLines.Add(new StockAdjustmentLine
                {
                    VariantId = line.VariantId,
                    PreviousQuantity = currentQuantity,
                    QuantityChange = quantityChange,
                    NewQuantity = newQuantity,
                    Reason = "StockCountCorrection",
                    Notes = $"Generated from Stock Count {stockCount.StockCountNo}",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = await GenerateAdjustmentNumberAsync(),
                Status = StockAdjustment.StatusDraft,
                LocationId = stockCount.LocationId,
                LocationType = stockCount.LocationType,
                SourceStockCountId = stockCount.Id,
                AdjustedBy = userId,
                AdjustmentDate = now,
                CreatedAt = now,
                UpdatedAt = now,
                Lines = adjustmentLines
            };

            _context.StockAdjustments.Add(adjustment);

            stockCount.Status = StockCount.StatusAdjustmentGenerated;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Stock adjustment {AdjustmentNumber} drafted from stock count {StockCountNo}",
                adjustment.AdjustmentNumber,
                stockCount.StockCountNo);

            return await GetByIdAsync(stockCount.Id);
        }
        finally
        {
            generationLock.Release();
        }
    }

    private async Task<StockCount> GetEditableStockCountAsync(long id)
    {
        var stockCount = await _context.StockCounts
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new KeyNotFoundException($"Stock count with ID {id} not found");

        EnsureBusinessAccess(stockCount.BusinessId);
        await _locationAccess.ResolveAndAuthorizeLocationAsync(stockCount.LocationId, stockCount.LocationType);
        return stockCount;
    }

    private void EnsureBusinessAccess(long businessId)
    {
        if (_tenantAccess.IsSuperAdmin)
        {
            if (_tenantAccess.EffectiveBusinessId.HasValue && _tenantAccess.EffectiveBusinessId.Value != businessId)
            {
                throw new UnauthorizedAccessException("You are not authorized to access this stock count.");
            }

            return;
        }

        _tenantAccess.EnsureBusinessMatch(businessId, "stock count");
    }

    private async Task<string> GetLocationNameAsync(long locationId, string locationType)
    {
        if (locationType == "warehouse")
        {
            return await _context.Warehouses
                .AsNoTracking()
                .Where(w => w.Id == locationId)
                .Select(w => w.Name)
                .FirstOrDefaultAsync() ?? $"Warehouse #{locationId}";
        }

        return await _context.Outlets
            .AsNoTracking()
            .Where(o => o.Id == locationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync() ?? $"Outlet #{locationId}";
    }

    private async Task<(bool HasMovement, int Count, DateTime? LastMovementAt)> GetMovementSummaryAsync(
        long stockCountId,
        long locationId,
        string locationType,
        DateTime generatedAt)
    {
        var variantIds = await _context.StockCountLines
            .AsNoTracking()
            .Where(l => l.StockCountId == stockCountId)
            .Select(l => l.VariantId)
            .ToListAsync();

        if (variantIds.Count == 0)
        {
            return (false, 0, null);
        }

        var movementQuery = _context.StockLedgers
            .AsNoTracking()
            .Where(l => l.LocationId == locationId
                && l.LocationType == locationType
                && l.CreatedAt > generatedAt
                && variantIds.Contains(l.VariantId));

        var count = await movementQuery.CountAsync();
        if (count == 0)
        {
            return (false, 0, null);
        }

        var lastAt = await movementQuery.MaxAsync(l => (DateTime?)l.CreatedAt);
        return (true, count, lastAt);
    }

    private static StockCountLineDto MapLine(StockCountLine line)
    {
        return new StockCountLineDto
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
        };
    }

    private async Task<string> GenerateNumberAsync()
    {
        var date = DateTime.UtcNow;
        var prefix = $"{NumberPrefix}-{date:yyyyMMdd}";

        var todayCount = await _context.StockCounts
            .AsNoTracking()
            .CountAsync(s => s.StockCountNo.StartsWith(prefix));

        return $"{prefix}-{todayCount + 1:0000}";
    }

    private async Task<string> GenerateAdjustmentNumberAsync()
    {
        var date = DateTime.UtcNow;
        var prefix = $"{AdjustmentNumberPrefix}-{date:yyyyMMdd}";

        var todayCount = await _context.StockAdjustments
            .AsNoTracking()
            .CountAsync(a => a.AdjustmentNumber.StartsWith(prefix));

        return $"{prefix}-{todayCount + 1:0000}";
    }
}
