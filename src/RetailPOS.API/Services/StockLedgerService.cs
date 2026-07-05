// =====================================================================
// NEW — Stock Ledger service implementation
// =====================================================================
using RetailPOS.API.DTOs.StockLedger;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

public class StockLedgerService : IStockLedgerService
{
    private readonly IStockLedgerRepository _ledgerRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<StockLedgerService> _logger;

    public StockLedgerService(
        IStockLedgerRepository ledgerRepository,
        RetailPOSDbContext context,
        ILogger<StockLedgerService> logger)
    {
        _ledgerRepository = ledgerRepository;
        _context = context;
        _logger = logger;
    }

    // ── Query ─────────────────────────────────────────────────────────

    public async Task<StockLedgerDto?> GetByIdAsync(long id)
    {
        var entry = await _ledgerRepository.GetByIdAsync(id);
        if (entry == null) return null;
        return await MapToDtoAsync(entry);
    }

    public async Task<List<StockLedgerDto>> GetByVariantAndLocationAsync(
        long variantId, long locationId, string locationType)
    {
        var entries = await _ledgerRepository.GetByVariantAndLocationAsync(
            variantId, locationId, locationType);

        var result = new List<StockLedgerDto>(entries.Count);
        foreach (var e in entries)
            result.Add(await MapToDtoAsync(e));
        return result;
    }

    public async Task<List<StockLedgerDto>> GetByReferenceAsync(
        string referenceType, long referenceId)
    {
        var entries = await _ledgerRepository.GetByReferenceAsync(referenceType, referenceId);

        var result = new List<StockLedgerDto>(entries.Count);
        foreach (var e in entries)
            result.Add(await MapToDtoAsync(e));
        return result;
    }

    public async Task<StockLedgerListDto> SearchAsync(StockLedgerSearchDto searchDto)
    {
        var (entries, totalCount) = await _ledgerRepository.SearchAsync(
            searchDto.VariantId,
            searchDto.LocationId,
            searchDto.LocationType,
            searchDto.TransactionType,
            searchDto.ReferenceType,
            searchDto.ReferenceId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize);

        var dtos = new List<StockLedgerDto>(entries.Count);
        foreach (var e in entries)
            dtos.Add(await MapToDtoAsync(e));

        var totalPages = (int)Math.Ceiling(totalCount / (double)searchDto.PageSize);

        return new StockLedgerListDto
        {
            Entries = dtos,
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize,
            TotalPages = totalPages,
            HasPreviousPage = searchDto.PageNumber > 1,
            HasNextPage = searchDto.PageNumber < totalPages
        };
    }

    // ── Write (synchronous — no SaveChangesAsync) ─────────────────────

    /// <inheritdoc/>
    public void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null)
    {
        var entry = new StockLedger
        {
            VariantId = variantId,
            LocationId = locationId,
            LocationType = locationType.ToLower(),
            TransactionType = transactionType,
            QtyIn = qtyIn,
            QtyOut = qtyOut,
            BalanceAfter = balanceAfter,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Remarks = remarks,
            CreatedBy = createdBy,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        _context.StockLedgers.Add(entry);

        _logger.LogDebug(
            "StockLedger entry queued: variant={VariantId} loc={LocationId}/{LocationType} " +
            "type={TransactionType} in={QtyIn} out={QtyOut} balance={BalanceAfter} ref={RefType}#{RefId}",
            variantId, locationId, locationType, transactionType,
            qtyIn, qtyOut, balanceAfter, referenceType, referenceId);
    }

    // ── Mapping ──────────────────────────────────────────────────────

    private async Task<StockLedgerDto> MapToDtoAsync(StockLedger sl)
    {
        var locationName = await ResolveLocationNameAsync(sl.LocationId, sl.LocationType);

        return new StockLedgerDto
        {
            Id = sl.Id,
            VariantId = sl.VariantId,
            VariantSku = sl.Variant?.Sku ?? string.Empty,
            ProductName = sl.Variant?.Product?.Name ?? string.Empty,
            LocationId = sl.LocationId,
            LocationType = sl.LocationType,
            LocationName = locationName,
            TransactionType = sl.TransactionType,
            QtyIn = sl.QtyIn,
            QtyOut = sl.QtyOut,
            BalanceAfter = sl.BalanceAfter,
            ReferenceType = sl.ReferenceType,
            ReferenceId = sl.ReferenceId,
            Remarks = sl.Remarks,
            CreatedBy = sl.CreatedBy,
            CreatedByName = sl.Creator?.Name,
            CreatedAt = sl.CreatedAt
        };
    }

    private async Task<string> ResolveLocationNameAsync(long locationId, string locationType)
    {
        if (locationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            var outlet = await _context.Outlets.FindAsync(locationId);
            return outlet?.Name ?? $"Outlet {locationId}";
        }

        if (locationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
        {
            var warehouse = await _context.Warehouses.FindAsync(locationId);
            return warehouse?.Name ?? $"Warehouse {locationId}";
        }

        return $"Location {locationId}";
    }
}
