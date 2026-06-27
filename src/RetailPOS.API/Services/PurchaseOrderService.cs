using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Purchase Order business logic
/// </summary>
public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IProductVariantRepository _productVariantRepository;
    private readonly ITenantAccessService _tenantAccess;
    private readonly IStockLedgerService _stockLedgerService;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        IPurchaseOrderRepository purchaseOrderRepository,
        ISupplierRepository supplierRepository,
        IWarehouseRepository warehouseRepository,
        IProductVariantRepository productVariantRepository,
        ITenantAccessService tenantAccess,
        IStockLedgerService stockLedgerService,
        RetailPOSDbContext context,
        ILogger<PurchaseOrderService> logger)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
        _supplierRepository = supplierRepository;
        _warehouseRepository = warehouseRepository;
        _productVariantRepository = productVariantRepository;
        _tenantAccess = tenantAccess;
        _stockLedgerService = stockLedgerService;
        _context = context;
        _logger = logger;
    }

    public async Task<PurchaseOrderDto> GetByIdAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        return MapToDto(po);
    }

    public async Task<List<PurchaseOrderDto>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var pos = await _purchaseOrderRepository.GetAllAsync(status, supplierId, warehouseId, businessId);
        return pos.Select(MapToDto).ToList();
    }

    public async Task<PurchaseOrderListDto> SearchAsync(PurchaseOrderSearchDto searchDto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var (pos, totalCount) = await _purchaseOrderRepository.SearchAsync(
            searchDto.Status,
            searchDto.SupplierId,
            searchDto.WarehouseId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize,
            searchDto.SortBy,
            searchDto.SortOrder,
            businessId);

        return new PurchaseOrderListDto
        {
            PurchaseOrders = pos.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderDto dto, long? userId = null)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();

        // Validate items
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        // Validate supplier exists
        var supplier = await _supplierRepository.GetByIdAsync(dto.SupplierId);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {dto.SupplierId} not found");

        // Validate warehouse exists
        var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId, businessId);
        if (warehouse == null)
            throw new KeyNotFoundException($"Warehouse with ID {dto.WarehouseId} not found");

        // Validate all product variants exist
        foreach (var item in dto.Items)
        {
            var variant = await _productVariantRepository.GetByIdAsync(item.VariantId);
            if (variant == null)
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");
        }

        // Calculate total amount (with per-item discount and tax)
        var totalAmount = dto.Items.Sum(i =>
            Math.Round(i.Quantity * i.UnitPrice * (1 - i.Discount / 100m) * (1 + i.Tax / 100m), 2));

        var po = new PurchaseOrder
        {
            SupplierId = dto.SupplierId,
            WarehouseId = dto.WarehouseId,
            OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc),
            ExpectedDelivery = dto.ExpectedDelivery.HasValue 
                ? DateTime.SpecifyKind(dto.ExpectedDelivery.Value, DateTimeKind.Utc) 
                : null,
            Status = dto.Status.ToLower(),
            Notes = dto.Notes,
            TotalAmount = totalAmount,
            CreatedBy = userId,
            Items = dto.Items.Select(i => new PurchaseOrderItem
            {
                VariantId = i.VariantId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Discount = i.Discount,
                Tax = i.Tax,
                Unit = i.Unit
            }).ToList()
        };

        var created = await _purchaseOrderRepository.CreateAsync(po);

        // Back-fill the formatted PO number now that we have the Id
        await _purchaseOrderRepository.SetPoNumberAsync(created.Id, GeneratePoNumber(created.Id, created.OrderDate));

        _logger.LogInformation("Purchase order {PoId} created by user {UserId}", created.Id, userId);

        return await GetByIdAsync(created.Id);
    }

    public async Task<PurchaseAndReceiveResultDto> PurchaseAndReceiveAsync(CreatePurchaseAndReceiveDto dto, long? userId = null)
    {
        const string warehouseLocationType = "warehouse";

        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();

        if (string.IsNullOrWhiteSpace(dto.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required for purchase and receive action");

        var normalizedIdempotencyKey = dto.IdempotencyKey.Trim();
        var existingPo = await FindImmediateReceiveByKeyAsync(dto.WarehouseId, normalizedIdempotencyKey, businessId);
        if (existingPo != null)
        {
            return new PurchaseAndReceiveResultDto
            {
                PurchaseOrder = await GetByIdAsync(existingPo.Id),
                GrnId = existingPo.Grns.OrderByDescending(g => g.Id).Select(g => g.Id).FirstOrDefault(),
                IsDuplicateRequest = true
            };
        }

        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        var supplier = await _supplierRepository.GetByIdAsync(dto.SupplierId);
        if (supplier == null)
            throw new KeyNotFoundException($"Supplier with ID {dto.SupplierId} not found");

        var warehouse = await _warehouseRepository.GetByIdAsync(dto.WarehouseId, businessId);
        if (warehouse == null)
            throw new KeyNotFoundException($"Warehouse with ID {dto.WarehouseId} not found");

        foreach (var item in dto.Items)
        {
            var variant = await _productVariantRepository.GetByIdAsync(item.VariantId);
            if (variant == null)
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var totalAmount = dto.Items.Sum(i =>
                Math.Round(i.Quantity * i.UnitPrice * (1 - i.Discount / 100m) * (1 + i.Tax / 100m), 2));

            var po = new PurchaseOrder
            {
                SupplierId = dto.SupplierId,
                WarehouseId = dto.WarehouseId,
                IdempotencyKey = normalizedIdempotencyKey,
                OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc),
                ExpectedDelivery = dto.ExpectedDelivery.HasValue
                    ? DateTime.SpecifyKind(dto.ExpectedDelivery.Value, DateTimeKind.Utc)
                    : null,
                Status = "approved",
                Notes = dto.Notes,
                TotalAmount = totalAmount,
                CreatedBy = userId,
                Items = dto.Items.Select(i => new PurchaseOrderItem
                {
                    VariantId = i.VariantId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Discount = i.Discount,
                    Tax = i.Tax,
                    Unit = i.Unit
                }).ToList()
            };

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();

            po.PoNumber = GeneratePoNumber(po.Id, po.OrderDate);
            await _context.SaveChangesAsync();

            var autoGrn = new Grn
            {
                PoId = po.Id,
                ReceivedDate = DateTime.UtcNow,
                Status = "full",
                Notes = $"System-generated GRN created from Purchase Order {po.PoNumber} by immediate purchase receive action.\nType: AUTO_GENERATED_FROM_PURCHASE_ORDER",
                CreatedBy = userId ?? po.CreatedBy,
                Items = po.Items.Select(i => new GrnItem
                {
                    PoItemId = i.Id,
                    ReceivedQty = i.Quantity,
                    UnitCost = i.UnitPrice,
                    Notes = "AUTO_GENERATED_FROM_PURCHASE_ORDER"
                }).ToList()
            };

            _context.Grns.Add(autoGrn);
            await _context.SaveChangesAsync();

            foreach (var item in autoGrn.Items)
            {
                var poItem = po.Items.First(x => x.Id == item.PoItemId);
                var variantId = poItem.VariantId;

                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(inv =>
                        inv.VariantId == variantId &&
                        inv.LocationId == po.WarehouseId &&
                        inv.LocationType.ToLower() == warehouseLocationType);

                if (inventory == null)
                {
                    inventory = new Inventory
                    {
                        VariantId = variantId,
                        LocationId = po.WarehouseId,
                        LocationType = warehouseLocationType,
                        Quantity = item.ReceivedQty
                    };
                    _context.Inventories.Add(inventory);
                }
                else
                {
                    inventory.Quantity += item.ReceivedQty;
                }

                _stockLedgerService.WriteEntry(
                    variantId: variantId,
                    locationId: po.WarehouseId,
                    locationType: warehouseLocationType,
                    transactionType: StockLedgerTransactionType.Grn,
                    qtyIn: item.ReceivedQty,
                    qtyOut: 0,
                    balanceAfter: inventory.Quantity,
                    referenceType: StockLedgerReferenceType.Grn,
                    referenceId: autoGrn.Id,
                    remarks: $"System auto-GRN from {po.PoNumber}",
                        createdBy: userId ?? po.CreatedBy);
            }

            autoGrn.Status = "completed";
            po.Status = "completed";

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Immediate purchase receive completed for PO {PoId}; GRN {GrnId} created by user {UserId}",
                po.Id, autoGrn.Id, userId);

            return new PurchaseAndReceiveResultDto
            {
                PurchaseOrder = await GetByIdAsync(po.Id),
                GrnId = autoGrn.Id,
                IsDuplicateRequest = false
            };
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync();

            var duplicatePo = await FindImmediateReceiveByKeyAsync(dto.WarehouseId, normalizedIdempotencyKey, businessId);
            if (duplicatePo != null)
            {
                _logger.LogWarning(
                    ex,
                    "Duplicate immediate receive request detected for WarehouseId {WarehouseId} and IdempotencyKey {IdempotencyKey}",
                    dto.WarehouseId,
                    normalizedIdempotencyKey);

                return new PurchaseAndReceiveResultDto
                {
                    PurchaseOrder = await GetByIdAsync(duplicatePo.Id),
                    GrnId = duplicatePo.Grns.OrderByDescending(g => g.Id).Select(g => g.Id).FirstOrDefault(),
                    IsDuplicateRequest = true
                };
            }

            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PurchaseOrderDto> UpdateAsync(long id, UpdatePurchaseOrderDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        // Can only update if draft or sent_back
        var status = po.Status.ToLower();
        if (status != "draft" && status != "sent_back")
            throw new InvalidOperationException($"Cannot update purchase order in '{po.Status}' status. Only draft or sent-back orders can be edited.");

        // Validate items
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Purchase order must have at least one item");

        // Update properties
        po.SupplierId = dto.SupplierId;
        po.WarehouseId = dto.WarehouseId;
        po.OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc);
        po.ExpectedDelivery = dto.ExpectedDelivery.HasValue 
            ? DateTime.SpecifyKind(dto.ExpectedDelivery.Value, DateTimeKind.Utc) 
            : null;
        po.Notes = dto.Notes;
        po.TotalAmount = dto.Items.Sum(i =>
            Math.Round(i.Quantity * i.UnitPrice * (1 - i.Discount / 100m) * (1 + i.Tax / 100m), 2));

        // Update items
        po.Items.Clear();
        foreach (var itemDto in dto.Items)
        {
            po.Items.Add(new PurchaseOrderItem
            {
                PoId = id,
                VariantId = itemDto.VariantId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                Discount = itemDto.Discount,
                Tax = itemDto.Tax,
                Unit = itemDto.Unit
            });
        }

        var updated = await _purchaseOrderRepository.UpdateAsync(po);
        _logger.LogInformation("Purchase order {PoId} updated", id);

        return MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var canDelete = await _purchaseOrderRepository.CanDeleteAsync(id, businessId);
        if (!canDelete)
            throw new InvalidOperationException("Cannot delete purchase order: either it's not in draft/pending status or has associated GRNs");

        var result = await _purchaseOrderRepository.DeleteAsync(id, businessId);
        if (result)
            _logger.LogInformation("Purchase order {PoId} deleted", id);

        return result;
    }

    public async Task<PurchaseOrderDto> SubmitForApprovalAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        var currentStatus = po.Status.ToLower();
        if (currentStatus != "draft" && currentStatus != "sent_back")
            throw new InvalidOperationException($"Can only submit draft or sent-back purchase orders. Current status: {po.Status}");

        // Clear rejection reason on re-submit
        po.Status = "pending";
        po.RejectionReason = null;
        await _purchaseOrderRepository.UpdateAsync(po);
        _logger.LogInformation("Purchase order {PoId} submitted for approval", id);

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseOrderDto> ApproveAsync(long id)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        if (po.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only approve pending purchase orders. Current status: {po.Status}");

        await _purchaseOrderRepository.UpdateStatusAsync(id, "approved", businessId);
        _logger.LogInformation("Purchase order {PoId} approved", id);

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseOrderDto> RejectAsync(long id, string reason)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        if (po.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only reject pending purchase orders. Current status: {po.Status}");

        po.Status = "rejected";
        po.RejectionReason = reason;
        await _purchaseOrderRepository.UpdateAsync(po);
        _logger.LogInformation("Purchase order {PoId} rejected: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseOrderDto> CancelAsync(long id, string reason)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        var cancelableStatuses = new[] { "draft", "pending", "sent_back", "approved" };
        if (!cancelableStatuses.Contains(po.Status.ToLower()))
            throw new InvalidOperationException($"Cannot cancel a purchase order in '{po.Status}' status");

        po.Status = "cancelled";
        po.RejectionReason = reason;
        await _purchaseOrderRepository.UpdateAsync(po);
        _logger.LogInformation("Purchase order {PoId} cancelled: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseOrderDto> SendBackAsync(long id, string reason)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        if (po.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only send back pending purchase orders. Current status: {po.Status}");

        po.Status = "sent_back";
        po.RejectionReason = reason;
        await _purchaseOrderRepository.UpdateAsync(po);
        _logger.LogInformation("Purchase order {PoId} sent back for correction: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    public async Task<List<PurchaseOrderDto>> GetPendingApprovalsAsync()
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var pos = await _purchaseOrderRepository.GetPendingApprovalsAsync(businessId);
        return pos.Select(MapToDto).ToList();
    }

    public async Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        return await _purchaseOrderRepository.GetTotalAmountAsync(status, startDate, endDate, businessId);
    }

    private static PurchaseOrderDto MapToDto(PurchaseOrder po)
    {
        return new PurchaseOrderDto
        {
            Id = po.Id,
            PoNumber = po.PoNumber,
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            WarehouseId = po.WarehouseId,
            WarehouseName = po.Warehouse?.Name ?? string.Empty,
            OrderDate = po.OrderDate,
            ExpectedDelivery = po.ExpectedDelivery,
            TotalAmount = po.TotalAmount,
            Status = po.Status,
            Notes = po.Notes,
            RejectionReason = po.RejectionReason,
            CreatedBy = po.CreatedBy,
            CreatedByName = po.Creator?.Name,
            CreatedAt = po.CreatedAt,
            UpdatedAt = po.UpdatedAt,
            LatestGrnId = po.Grns.OrderByDescending(g => g.Id).Select(g => (long?)g.Id).FirstOrDefault(),
            Items = po.Items?.Select(i => new PurchaseOrderItemDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                ProductName = i.Variant?.Product?.Name ?? string.Empty,
                VariantName = i.Variant?.Name ?? string.Empty,
                ProductCode = i.Variant?.Product?.ProductCode,
                Sku = i.Variant?.Sku,
                VariantAttributes = i.Variant?.Attributes,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Discount = i.Discount,
                Tax = i.Tax,
                Unit = i.Unit
            }).ToList() ?? new List<PurchaseOrderItemDto>()
        };
    }

    /// <summary>
    /// Generates a formatted PO number: PO-YYYYMMDD-{id:D4}
    /// </summary>
    private static string GeneratePoNumber(long id, DateTime orderDate)
        => $"PO-{orderDate:yyyyMMdd}-{id:D4}";

    private async Task<PurchaseOrder?> FindImmediateReceiveByKeyAsync(long warehouseId, string idempotencyKey, long? businessId)
    {
        return await _context.PurchaseOrders
            .AsNoTracking()
            .Include(po => po.Grns)
            .Where(po => po.WarehouseId == warehouseId && po.IdempotencyKey == idempotencyKey)
            .Where(po => businessId == null || po.Warehouse.BusinessId == businessId)
            .OrderByDescending(po => po.Id)
            .FirstOrDefaultAsync();
    }
}
