using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for GRN (Goods Received Note) business logic
/// </summary>
public class GrnService : IGrnService
{
    private readonly IGrnRepository _grnRepository;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<GrnService> _logger;
    private readonly IStockLedgerService _stockLedger; // UPDATED

    public GrnService(
        IGrnRepository grnRepository,
        IPurchaseOrderRepository purchaseOrderRepository,
        RetailPOSDbContext context,
        ILogger<GrnService> logger,
        IStockLedgerService stockLedger) // UPDATED
    {
        _grnRepository = grnRepository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _context = context;
        _logger = logger;
        _stockLedger = stockLedger; // UPDATED
    }

    public async Task<GrnDto> GetByIdAsync(long id)
    {
        var grn = await _grnRepository.GetByIdAsync(id);
        if (grn == null)
            throw new KeyNotFoundException($"GRN with ID {id} not found");

        return MapToDto(grn);
    }

    public async Task<List<GrnDto>> GetAllAsync(long? poId, string? status)
    {
        var grns = await _grnRepository.GetAllAsync(poId, status);
        return grns.Select(MapToDto).ToList();
    }

    public async Task<GrnListDto> SearchAsync(GrnSearchDto searchDto)
    {
        var (grns, totalCount) = await _grnRepository.SearchAsync(
            searchDto.PoId,
            searchDto.Status,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize);

        return new GrnListDto
        {
            Grns = grns.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<GrnDto> CreateAsync(CreateGrnDto dto, long? userId = null)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("GRN must have at least one item");

        var po = await _purchaseOrderRepository.GetByIdAsync(dto.PoId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {dto.PoId} not found");

        // UPDATED: allow repeat GRNs on partially-received POs
        var poStatus = po.Status.ToLower();
        if (poStatus != "approved" && poStatus != "partial")
            throw new InvalidOperationException(
                $"Can only create a GRN for an approved or partially-received purchase order. " +
                $"Current status: {po.Status}");

        // Validate each PO item exists in this PO
        var poItemMap = po.Items.ToDictionary(i => i.Id);
        foreach (var item in dto.Items)
        {
            if (!poItemMap.ContainsKey(item.PoItemId))
                throw new InvalidOperationException(
                    $"PO item with ID {item.PoItemId} does not belong to purchase order {dto.PoId}");

            if (item.ReceivedQty <= 0)
                throw new InvalidOperationException(
                    $"Received quantity for PO item {item.PoItemId} must be greater than zero");
        }

        // UPDATED: over-receive guard — total received (previous completed GRNs + this GRN)
        //  must not exceed the ordered quantity per PO item.
        var previouslyReceivedByPoItem = await _context.GrnItems
            .Where(gi => gi.Grn.PoId == dto.PoId && gi.Grn.Status == "completed")
            .GroupBy(gi => gi.PoItemId)
            .ToDictionaryAsync(g => g.Key, g => g.Sum(gi => gi.ReceivedQty));

        foreach (var item in dto.Items)
        {
            var poItem = poItemMap[item.PoItemId];
            var previously = previouslyReceivedByPoItem.GetValueOrDefault(item.PoItemId, 0);
            var totalAfter = previously + item.ReceivedQty;
            if (totalAfter > poItem.Quantity)
                throw new InvalidOperationException(
                    $"Over-receiving not allowed for PO item {item.PoItemId} " +
                    $"(ordered: {poItem.Quantity}, already received: {previously}, " +
                    $"this GRN: {item.ReceivedQty}, total would be: {totalAfter})");
        }

        // Determine status: partial if any item received less than ordered in this receipt
        var status = "full";
        foreach (var item in dto.Items)
        {
            var poItem = poItemMap[item.PoItemId];
            if (item.ReceivedQty < poItem.Quantity)
            {
                status = "partial";
                break;
            }
        }

        var grn = new Grn
        {
            PoId = dto.PoId,
            ReceivedDate = DateTime.SpecifyKind(dto.ReceivedDate, DateTimeKind.Utc),
            Status = status,
            Notes = dto.Notes,                              // NEW
            CreatedBy = userId,
            Items = dto.Items.Select(i => new GrnItem
            {
                PoItemId = i.PoItemId,
                ReceivedQty = i.ReceivedQty,
                // UPDATED: use caller-supplied cost or fall back to PO item price
                UnitCost = i.UnitCost ?? poItemMap[i.PoItemId].UnitPrice,  // NEW
                Notes = i.Notes                                              // NEW
            }).ToList()
        };

        var created = await _grnRepository.CreateAsync(grn);
        _logger.LogInformation("GRN {GrnId} created for PO {PoId} by user {UserId}", created.Id, dto.PoId, userId);

        return MapToDto(created);
    }

    /// <summary>
    /// Complete a GRN: update inventory stock and update PO status
    /// </summary>
    public async Task<GrnDto> CompleteAsync(long id)
    {
        var grn = await _grnRepository.GetByIdAsync(id);
        if (grn == null)
            throw new KeyNotFoundException($"GRN with ID {id} not found");

        if (grn.Status == "completed")
            throw new InvalidOperationException("GRN is already completed");

        var po = grn.PurchaseOrder;
        var warehouseId = po.WarehouseId;
        const string warehouseLocationType = "warehouse";

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // UPDATED — Update inventory + write ledger entry for each received item
            foreach (var item in grn.Items)
            {
                var variantId = item.PurchaseOrderItem.VariantId;

                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(inv =>
                        inv.VariantId == variantId &&
                        inv.LocationId == warehouseId &&
                        inv.LocationType.ToLower() == warehouseLocationType);

                if (inventory == null)
                {
                    inventory = new Inventory
                    {
                        VariantId = variantId,
                        LocationId = warehouseId,
                        LocationType = warehouseLocationType,
                        Quantity = item.ReceivedQty
                    };
                    _context.Inventories.Add(inventory);
                }
                else
                {
                    inventory.Quantity += item.ReceivedQty;
                }

                // NEW — ledger entry (queued; saved atomically with SaveChangesAsync below)
                _stockLedger.WriteEntry(
                    variantId: variantId,
                    locationId: warehouseId,
                    locationType: warehouseLocationType,
                    transactionType: RetailPOS.Core.Entities.StockLedgerTransactionType.Grn,
                    qtyIn: item.ReceivedQty,
                    qtyOut: 0,
                    balanceAfter: inventory.Quantity,
                    referenceType: RetailPOS.Core.Entities.StockLedgerReferenceType.Grn,
                    referenceId: grn.Id,
                    remarks: $"GRN completion — PO-{grn.PoId:D6}",
                    createdBy: grn.CreatedBy);
            }

            // Mark GRN as completed
            grn.Status = "completed";
            _context.Grns.Update(grn);

            // Determine new PO status based on total received quantities across all GRNs
            var allGrnsForPo = await _context.Grns
                .Include(g => g.Items)
                .Where(g => g.PoId == po.Id && g.Status == "completed")
                .ToListAsync();

            // Sum received quantities from other completed GRNs
            var receivedQtyByPoItem = allGrnsForPo
                .SelectMany(g => g.Items)
                .Where(i => i.GrnId != grn.Id) // exclude current GRN items (added separately below)
                .GroupBy(i => i.PoItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.ReceivedQty));

            // Include current GRN items
            foreach (var item in grn.Items)
            {
                if (receivedQtyByPoItem.ContainsKey(item.PoItemId))
                    receivedQtyByPoItem[item.PoItemId] += item.ReceivedQty;
                else
                    receivedQtyByPoItem[item.PoItemId] = item.ReceivedQty;
            }

            var allReceived = po.Items.All(poItem =>
                receivedQtyByPoItem.TryGetValue(poItem.Id, out var received) && received >= poItem.Quantity);

            var newPoStatus = allReceived ? "received" : "partial";
            await _purchaseOrderRepository.UpdateStatusAsync(po.Id, newPoStatus);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("GRN {GrnId} completed. PO {PoId} status updated to {Status}", id, po.Id, newPoStatus);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await GetByIdAsync(id);
    }

    public async Task<List<PurchaseOrderForGrnDto>> GetPendingReceiptPOsAsync()
    {
        var pos = await _grnRepository.GetPendingReceiptPOsAsync();
        return pos.Select(po => new PurchaseOrderForGrnDto
        {
            Id = po.Id,
            OrderNumber = $"PO-{po.Id:D6}",
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            WarehouseId = po.WarehouseId,
            WarehouseName = po.Warehouse?.Name ?? string.Empty,
            OrderDate = po.OrderDate,
            TotalAmount = po.TotalAmount,
            Status = po.Status,
            Items = po.Items?.Select(i => new PurchaseOrderItemForGrnDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                ProductName = i.Variant?.Product?.Name ?? string.Empty,
                ProductCode = i.Variant?.Product?.ProductCode,
                VariantName = i.Variant?.Name ?? string.Empty,
                VariantAttributes = i.Variant?.Attributes,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                PrimaryImageThumb = i.Variant?.Product?.Images?.FirstOrDefault(img => img.IsPrimary)?.ThumbPath,
            }).ToList() ?? new List<PurchaseOrderItemForGrnDto>()
        }).ToList();
    }

    public async Task<GrnVarianceDto> GetVarianceAsync(long id)
    {
        var grn = await _grnRepository.GetByIdAsync(id);
        if (grn == null)
            throw new KeyNotFoundException($"GRN with ID {id} not found");

        var po = grn.PurchaseOrder;

        var items = grn.Items.Select(item => new GrnVarianceItemDto
        {
            PoItemId = item.PoItemId,
            VariantId = item.PurchaseOrderItem.VariantId,
            ProductName = item.PurchaseOrderItem.Variant?.Product?.Name ?? string.Empty,
            VariantName = item.PurchaseOrderItem.Variant?.Name ?? string.Empty,
            OrderedQty = item.PurchaseOrderItem.Quantity,
            ReceivedQty = item.ReceivedQty
        }).ToList();

        return new GrnVarianceDto
        {
            GrnId = grn.Id,
            PoId = po.Id,
            PoOrderNumber = $"PO-{po.Id:D6}",
            GrnStatus = grn.Status,
            Items = items
        };
    }

    // NEW: cumulative variance across ALL GRNs for a PO
    public async Task<GrnPoVarianceDto> GetPoVarianceAsync(long poId)
    {
        var po = await _purchaseOrderRepository.GetByIdAsync(poId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {poId} not found");

        var allGrns = await _grnRepository.GetPoGrnsWithItemsAsync(poId);

        // Aggregate received quantities and cost across all completed GRNs per PO item
        var receivedByPoItem = allGrns
            .Where(g => g.Status == "completed")
            .SelectMany(g => g.Items)
            .GroupBy(i => i.PoItemId)
            .ToDictionary(
                g => g.Key,
                g => (Qty: g.Sum(i => i.ReceivedQty), Cost: g.Sum(i => i.ReceivedQty * i.UnitCost)));

        var varianceItems = po.Items.Select(poItem =>
        {
            receivedByPoItem.TryGetValue(poItem.Id, out var received);
            return new GrnPoVarianceItemDto
            {
                PoItemId = poItem.Id,
                VariantId = poItem.VariantId,
                ProductName = poItem.Variant?.Product?.Name ?? string.Empty,
                VariantName = poItem.Variant?.Name ?? string.Empty,
                OrderedQty = poItem.Quantity,
                TotalReceivedQty = received.Qty,
                UnitPrice = poItem.UnitPrice,
                TotalReceivedCost = received.Cost
            };
        }).ToList();

        return new GrnPoVarianceDto
        {
            PoId = po.Id,
            PoOrderNumber = $"PO-{po.Id:D6}",
            PoStatus = po.Status,
            TotalGrns = allGrns.Count,
            CompletedGrns = allGrns.Count(g => g.Status == "completed"),
            Items = varianceItems
        };
    }

    private static GrnDto MapToDto(Grn grn)
    {
        return new GrnDto
        {
            Id = grn.Id,
            PoId = grn.PoId,
            PoOrderNumber = $"PO-{grn.PoId:D6}",
            SupplierId = grn.PurchaseOrder?.SupplierId ?? 0,
            SupplierName = grn.PurchaseOrder?.Supplier?.Name ?? string.Empty,
            WarehouseId = grn.PurchaseOrder?.WarehouseId ?? 0,
            WarehouseName = grn.PurchaseOrder?.Warehouse?.Name ?? string.Empty,
            ReceivedDate = grn.ReceivedDate,
            Status = grn.Status,
            Notes = grn.Notes,                             // NEW
            CreatedBy = grn.CreatedBy ?? grn.PurchaseOrder?.CreatedBy,
            CreatedByName = grn.Creator?.Name ?? grn.PurchaseOrder?.Creator?.Name,
            CreatedAt = grn.CreatedAt,
            Items = grn.Items?.Select(i => new GrnItemDto
            {
                Id = i.Id,
                PoItemId = i.PoItemId,
                VariantId = i.PurchaseOrderItem?.VariantId ?? 0,
                ProductName = i.PurchaseOrderItem?.Variant?.Product?.Name ?? string.Empty,
                ProductCode = i.PurchaseOrderItem?.Variant?.Product?.ProductCode,
                VariantName = i.PurchaseOrderItem?.Variant?.Name ?? string.Empty,
                VariantAttributes = i.PurchaseOrderItem?.Variant?.Attributes,
                OrderedQty = i.PurchaseOrderItem?.Quantity ?? 0,
                ReceivedQty = i.ReceivedQty,
                UnitPrice = i.PurchaseOrderItem?.UnitPrice ?? 0,
                UnitCost = i.UnitCost,                     // NEW
                Notes = i.Notes                            // NEW
            }).ToList() ?? new List<GrnItemDto>()
        };
    }
}
