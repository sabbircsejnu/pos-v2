using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.StockAdjustment;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Stock Adjustment business logic
/// </summary>
public class StockAdjustmentService : IStockAdjustmentService
{
    private static readonly HashSet<string> ValidReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Damage", "Loss", "Found", "Expired", "Count", "Return", "Other"
    };

    private readonly IStockAdjustmentRepository _adjustmentRepository;
    private readonly IProductVariantRepository _variantRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<StockAdjustmentService> _logger;
    private readonly IStockLedgerService _stockLedger; // UPDATED
    private readonly IPosCacheService    _posCache;    // UPDATED

    public StockAdjustmentService(
        IStockAdjustmentRepository adjustmentRepository,
        IProductVariantRepository variantRepository,
        RetailPOSDbContext context,
        ILogger<StockAdjustmentService> logger,
        IStockLedgerService stockLedger,  // UPDATED
        IPosCacheService    posCache)     // UPDATED
    {
        _adjustmentRepository = adjustmentRepository;
        _variantRepository = variantRepository;
        _context = context;
        _logger = logger;
        _stockLedger = stockLedger; // UPDATED
        _posCache    = posCache;    // UPDATED
    }

    public async Task<StockAdjustmentDto> GetByIdAsync(long id)
    {
        var adjustment = await _adjustmentRepository.GetByIdAsync(id);
        if (adjustment == null)
            throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");

        return await MapToDtoAsync(adjustment);
    }

    public async Task<List<StockAdjustmentDto>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null)
    {
        var adjustments = await _adjustmentRepository.GetAllAsync(locationId, locationType, variantId);
        var result = new List<StockAdjustmentDto>();
        foreach (var a in adjustments)
            result.Add(await MapToDtoAsync(a));
        return result;
    }

    public async Task<StockAdjustmentListDto> SearchAsync(StockAdjustmentSearchDto searchDto)
    {
        var (adjustments, totalCount) = await _adjustmentRepository.SearchAsync(
            searchDto.LocationId,
            searchDto.LocationType,
            searchDto.VariantId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize);

        var dtos = new List<StockAdjustmentDto>();
        foreach (var a in adjustments)
            dtos.Add(await MapToDtoAsync(a));

        return new StockAdjustmentListDto
        {
            StockAdjustments = dtos,
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<List<StockAdjustmentDto>> GetHistoryAsync(long variantId, long locationId)
    {
        var adjustments = await _adjustmentRepository.GetHistoryAsync(variantId, locationId);
        var result = new List<StockAdjustmentDto>();
        foreach (var a in adjustments)
            result.Add(await MapToDtoAsync(a));
        return result;
    }

    public async Task<StockAdjustmentDto> CreateAsync(CreateStockAdjustmentDto dto, long adjustedBy)
    {
        // Validate reason
        if (!ValidReasons.Contains(dto.Reason))
            throw new InvalidOperationException(
                $"Invalid reason '{dto.Reason}'. Valid reasons are: {string.Join(", ", ValidReasons)}");

        // Validate variant exists
        var variant = await _variantRepository.GetByIdAsync(dto.VariantId);
        if (variant == null)
            throw new KeyNotFoundException($"Product variant with ID {dto.VariantId} not found");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Read inventory inside the transaction so currentQty reflects the latest committed value.
            var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == dto.VariantId &&
                i.LocationId == dto.LocationId &&
                i.LocationType == dto.LocationType);

            var currentQty = inventory?.Quantity ?? 0;
            var newQty = currentQty + dto.QuantityChange;

            // Validate negative adjustments don't go below 0
            if (newQty < 0)
                throw new InvalidOperationException(
                    $"Adjustment would result in negative stock. Current: {currentQty}, Change: {dto.QuantityChange}, Result: {newQty}");

            // Update inventory
            if (inventory == null)
            {
                inventory = new Inventory
                {
                    VariantId = dto.VariantId,
                    LocationId = dto.LocationId,
                    LocationType = dto.LocationType.ToLower(),
                    Quantity = dto.QuantityChange
                };
                _context.Inventories.Add(inventory);
            }
            else
            {
                inventory.Quantity = newQty;
            }

            // Create adjustment record
            var adjustment = new StockAdjustment
            {
                LocationId = dto.LocationId,
                LocationType = dto.LocationType.ToLower(),
                VariantId = dto.VariantId,
                QuantityChange = dto.QuantityChange,
                Reason = dto.Reason,
                AdjustedBy = adjustedBy,
                AdjustmentDate = DateTime.UtcNow
            };

            _context.StockAdjustments.Add(adjustment);

            // First save: generate adjustment.Id (needed as ledger referenceId)
            await _context.SaveChangesAsync();

            // Write ledger entry referencing the persisted adjustment
            _stockLedger.WriteEntry(
                variantId: dto.VariantId,
                locationId: dto.LocationId,
                locationType: dto.LocationType.ToLower(),
                transactionType: StockLedgerTransactionType.Adjustment,
                qtyIn:  dto.QuantityChange > 0 ? dto.QuantityChange : 0,
                qtyOut: dto.QuantityChange < 0 ? Math.Abs(dto.QuantityChange) : 0,
                balanceAfter: newQty,
                referenceType: StockLedgerReferenceType.StockAdjustment,
                referenceId: adjustment.Id,
                remarks: dto.Reason,
                createdBy: adjustedBy);

            await _context.SaveChangesAsync(); // persists ledger entry
            await transaction.CommitAsync();

            // UPDATED — invalidate stock hint cache when adjustment is at an outlet
            if (dto.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
                await _posCache.InvalidateStockAsync(dto.VariantId, dto.LocationId);

            _logger.LogInformation(
                "Stock adjustment created for variant {VariantId} at location {LocationId} ({LocationType}): change={Change}",
                dto.VariantId, dto.LocationId, dto.LocationType, dto.QuantityChange);

            return await GetByIdAsync(adjustment.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<StockAdjustmentDto> MapToDtoAsync(StockAdjustment adjustment)
    {
        var locationName = await ResolveLocationNameAsync(adjustment.LocationId, adjustment.LocationType);

        return new StockAdjustmentDto
        {
            Id = adjustment.Id,
            LocationId = adjustment.LocationId,
            LocationType = adjustment.LocationType,
            LocationName = locationName,
            VariantId = adjustment.VariantId,
            VariantSku = adjustment.Variant?.Sku ?? string.Empty,
            ProductName = adjustment.Variant?.Product?.Name ?? string.Empty,
            QuantityChange = adjustment.QuantityChange,
            Reason = adjustment.Reason,
            AdjustedBy = adjustment.AdjustedBy,
            AdjusterName = adjustment.Adjuster?.Name ?? string.Empty,
            AdjustmentDate = adjustment.AdjustmentDate
        };
    }

    private async Task<string> ResolveLocationNameAsync(long locationId, string locationType)
    {
        if (locationType.ToLower() == "outlet")
        {
            var outlet = await _context.Outlets.FindAsync(locationId);
            return outlet?.Name ?? $"Outlet {locationId}";
        }
        else if (locationType.ToLower() == "warehouse")
        {
            var warehouse = await _context.Warehouses.FindAsync(locationId);
            return warehouse?.Name ?? $"Warehouse {locationId}";
        }
        return $"Location {locationId}";
    }
}
