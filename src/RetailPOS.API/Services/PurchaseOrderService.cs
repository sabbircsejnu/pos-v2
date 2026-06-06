using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

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
    private readonly ILogger<PurchaseOrderService> _logger;

    public PurchaseOrderService(
        IPurchaseOrderRepository purchaseOrderRepository,
        ISupplierRepository supplierRepository,
        IWarehouseRepository warehouseRepository,
        IProductVariantRepository productVariantRepository,
        ITenantAccessService tenantAccess,
        ILogger<PurchaseOrderService> logger)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
        _supplierRepository = supplierRepository;
        _warehouseRepository = warehouseRepository;
        _productVariantRepository = productVariantRepository;
        _tenantAccess = tenantAccess;
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

        // Calculate total amount
        var totalAmount = dto.Items.Sum(i => i.Quantity * i.UnitPrice);

        var po = new PurchaseOrder
        {
            SupplierId = dto.SupplierId,
            WarehouseId = dto.WarehouseId,
            OrderDate = DateTime.SpecifyKind(dto.OrderDate, DateTimeKind.Utc),
            ExpectedDelivery = dto.ExpectedDelivery.HasValue 
                ? DateTime.SpecifyKind(dto.ExpectedDelivery.Value, DateTimeKind.Utc) 
                : null,
            Status = dto.Status.ToLower(),
            TotalAmount = totalAmount,
            CreatedBy = userId,
            Items = dto.Items.Select(i => new PurchaseOrderItem
            {
                VariantId = i.VariantId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var created = await _purchaseOrderRepository.CreateAsync(po);
        _logger.LogInformation("Purchase order {PoId} created by user {UserId}", created.Id, userId);

        return MapToDto(created);
    }

    public async Task<PurchaseOrderDto> UpdateAsync(long id, UpdatePurchaseOrderDto dto)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        // Can only update if draft or pending
        if (po.Status.ToLower() != "draft" && po.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Cannot update purchase order in '{po.Status}' status");

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
        po.TotalAmount = dto.Items.Sum(i => i.Quantity * i.UnitPrice);

        // Update items
        po.Items.Clear();
        foreach (var itemDto in dto.Items)
        {
            po.Items.Add(new PurchaseOrderItem
            {
                PoId = id,
                VariantId = itemDto.VariantId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice
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

        if (po.Status.ToLower() != "draft")
            throw new InvalidOperationException($"Can only submit draft purchase orders. Current status: {po.Status}");

        await _purchaseOrderRepository.UpdateStatusAsync(id, "pending", businessId);
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

        await _purchaseOrderRepository.UpdateStatusAsync(id, "rejected", businessId);
        _logger.LogInformation("Purchase order {PoId} rejected: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseOrderDto> CancelAsync(long id, string reason)
    {
        long? businessId = _tenantAccess.IsSuperAdmin ? null : _tenantAccess.RequireBusinessId();
        var po = await _purchaseOrderRepository.GetByIdAsync(id, businessId);
        if (po == null)
            throw new KeyNotFoundException($"Purchase order with ID {id} not found");

        // Can only cancel if not already received
        if (po.Status.ToLower() == "received")
            throw new InvalidOperationException("Cannot cancel a received purchase order");

        await _purchaseOrderRepository.UpdateStatusAsync(id, "cancelled", businessId);
        _logger.LogInformation("Purchase order {PoId} cancelled: {Reason}", id, reason);

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
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            WarehouseId = po.WarehouseId,
            WarehouseName = po.Warehouse?.Name ?? string.Empty,
            OrderDate = po.OrderDate,
            ExpectedDelivery = po.ExpectedDelivery,
            TotalAmount = po.TotalAmount,
            Status = po.Status,
            CreatedBy = po.CreatedBy,
            CreatedByName = po.Creator?.Name,
            CreatedAt = po.CreatedAt,
            UpdatedAt = po.UpdatedAt,
            Items = po.Items?.Select(i => new PurchaseOrderItemDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                ProductName = i.Variant?.Product?.Name ?? string.Empty,
                VariantName = i.Variant?.Name ?? string.Empty,
                VariantAttributes = i.Variant?.Attributes,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList() ?? new List<PurchaseOrderItemDto>()
        };
    }
}
