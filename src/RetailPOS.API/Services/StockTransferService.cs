using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Stock Transfer business logic
/// </summary>
public class StockTransferService : IStockTransferService
{
    private readonly IStockTransferRepository _transferRepository;
    private readonly IProductVariantRepository _variantRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<StockTransferService> _logger;

    public StockTransferService(
        IStockTransferRepository transferRepository,
        IProductVariantRepository variantRepository,
        RetailPOSDbContext context,
        ILogger<StockTransferService> logger)
    {
        _transferRepository = transferRepository;
        _variantRepository = variantRepository;
        _context = context;
        _logger = logger;
    }

    public async Task<StockTransferDto> GetByIdAsync(long id)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        return await MapToDtoAsync(transfer);
    }

    public async Task<List<StockTransferDto>> GetAllAsync(string? status = null, long? fromLocationId = null, long? toLocationId = null)
    {
        var transfers = await _transferRepository.GetAllAsync(status, fromLocationId, toLocationId);
        var result = new List<StockTransferDto>();
        foreach (var t in transfers)
            result.Add(await MapToDtoAsync(t));
        return result;
    }

    public async Task<StockTransferListDto> SearchAsync(StockTransferSearchDto searchDto)
    {
        var (transfers, totalCount) = await _transferRepository.SearchAsync(
            searchDto.Status,
            searchDto.FromLocationId,
            searchDto.FromLocationType,
            searchDto.ToLocationId,
            searchDto.ToLocationType,
            searchDto.PageNumber,
            searchDto.PageSize);

        var dtos = new List<StockTransferDto>();
        foreach (var t in transfers)
            dtos.Add(await MapToDtoAsync(t));

        return new StockTransferListDto
        {
            StockTransfers = dtos,
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<StockTransferDto> CreateAsync(CreateStockTransferDto dto, long? userId = null)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Stock transfer must have at least one item");

        if (dto.FromLocationId == dto.ToLocationId && dto.FromLocationType == dto.ToLocationType)
            throw new InvalidOperationException("Source and destination locations cannot be the same");

        // Validate source has enough stock for each item
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException($"Quantity for variant {item.VariantId} must be greater than zero");

            var variant = await _variantRepository.GetByIdAsync(item.VariantId);
            if (variant == null)
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");

            var sourceInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == item.VariantId &&
                i.LocationId == dto.FromLocationId &&
                i.LocationType == dto.FromLocationType);

            if (sourceInventory == null || sourceInventory.Quantity < item.Quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for variant {variant.Sku}. Available: {sourceInventory?.Quantity ?? 0}, Requested: {item.Quantity}");
        }

        var transfer = new StockTransfer
        {
            FromLocationId = dto.FromLocationId,
            FromLocationType = dto.FromLocationType.ToLower(),
            ToLocationId = dto.ToLocationId,
            ToLocationType = dto.ToLocationType.ToLower(),
            TransferDate = DateTime.SpecifyKind(dto.TransferDate, DateTimeKind.Utc),
            Status = "pending",
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Items = dto.Items.Select(i => new StockTransferItem
            {
                VariantId = i.VariantId,
                Quantity = i.Quantity
            }).ToList()
        };

        var created = await _transferRepository.CreateAsync(transfer);
        _logger.LogInformation("Stock transfer {TransferId} created by user {UserId}", created.Id, userId);

        return await MapToDtoAsync(created);
    }

    public async Task<StockTransferDto> UpdateAsync(long id, CreateStockTransferDto dto)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only update pending stock transfers. Current status: {transfer.Status}");

        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Stock transfer must have at least one item");

        if (dto.FromLocationId == dto.ToLocationId && dto.FromLocationType == dto.ToLocationType)
            throw new InvalidOperationException("Source and destination locations cannot be the same");

        // Validate stock availability
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException($"Quantity for variant {item.VariantId} must be greater than zero");

            var variant = await _variantRepository.GetByIdAsync(item.VariantId);
            if (variant == null)
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");

            var sourceInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == item.VariantId &&
                i.LocationId == dto.FromLocationId &&
                i.LocationType == dto.FromLocationType);

            if (sourceInventory == null || sourceInventory.Quantity < item.Quantity)
                throw new InvalidOperationException(
                    $"Insufficient stock for variant {variant.Sku}. Available: {sourceInventory?.Quantity ?? 0}, Requested: {item.Quantity}");
        }

        transfer.FromLocationId = dto.FromLocationId;
        transfer.FromLocationType = dto.FromLocationType.ToLower();
        transfer.ToLocationId = dto.ToLocationId;
        transfer.ToLocationType = dto.ToLocationType.ToLower();
        transfer.TransferDate = DateTime.SpecifyKind(dto.TransferDate, DateTimeKind.Utc);

        // Replace items
        _context.StockTransferItems.RemoveRange(transfer.Items);
        transfer.Items = dto.Items.Select(i => new StockTransferItem
        {
            TransferId = id,
            VariantId = i.VariantId,
            Quantity = i.Quantity
        }).ToList();

        var updated = await _transferRepository.UpdateAsync(transfer);
        _logger.LogInformation("Stock transfer {TransferId} updated", id);

        return await MapToDtoAsync(updated);
    }

    public async Task<StockTransferDto> ApproveAsync(long id, long? approverId = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only approve pending transfers. Current status: {transfer.Status}");

        await _transferRepository.UpdateStatusAsync(id, "approved", approverId);
        _logger.LogInformation("Stock transfer {TransferId} approved by user {UserId}", id, approverId);

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> RejectAsync(long id, string? reason = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() != "pending")
            throw new InvalidOperationException($"Can only reject pending transfers. Current status: {transfer.Status}");

        await _transferRepository.UpdateStatusAsync(id, "rejected");
        _logger.LogInformation("Stock transfer {TransferId} rejected. Reason: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> SendAsync(long id)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() != "approved")
            throw new InvalidOperationException($"Can only send approved transfers. Current status: {transfer.Status}");

        await _transferRepository.UpdateStatusAsync(id, "in_transit");
        _logger.LogInformation("Stock transfer {TransferId} marked as in-transit", id);

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> ReceiveAsync(long id)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() != "in_transit")
            throw new InvalidOperationException($"Can only receive in-transit transfers. Current status: {transfer.Status}");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in transfer.Items)
            {
                // Deduct from source
                var sourceInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == transfer.FromLocationId &&
                    i.LocationType == transfer.FromLocationType);

                if (sourceInventory == null || sourceInventory.Quantity < item.Quantity)
                    throw new InvalidOperationException($"Insufficient stock for variant {item.VariantId}");

                sourceInventory.Quantity -= item.Quantity;

                // Add to destination
                var destInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == transfer.ToLocationId &&
                    i.LocationType == transfer.ToLocationType);

                if (destInventory == null)
                {
                    destInventory = new Inventory
                    {
                        VariantId = item.VariantId,
                        LocationId = transfer.ToLocationId,
                        LocationType = transfer.ToLocationType,
                        Quantity = 0
                    };
                    _context.Inventories.Add(destInventory);
                }
                destInventory.Quantity += item.Quantity;
            }

            // Update transfer status
            transfer.Status = "received";
            _context.StockTransfers.Update(transfer);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Stock transfer {TransferId} received and stock updated", id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> CancelAsync(long id, string? reason = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.ToLower() == "received")
            throw new InvalidOperationException("Cannot cancel a received stock transfer");

        if (transfer.Status.ToLower() == "cancelled")
            throw new InvalidOperationException("Transfer is already cancelled");

        await _transferRepository.UpdateStatusAsync(id, "cancelled");
        _logger.LogInformation("Stock transfer {TransferId} cancelled. Reason: {Reason}", id, reason);

        return await GetByIdAsync(id);
    }

    private async Task<StockTransferDto> MapToDtoAsync(StockTransfer transfer)
    {
        var fromLocationName = await ResolveLocationNameAsync(transfer.FromLocationId, transfer.FromLocationType);
        var toLocationName = await ResolveLocationNameAsync(transfer.ToLocationId, transfer.ToLocationType);

        return new StockTransferDto
        {
            Id = transfer.Id,
            FromLocationId = transfer.FromLocationId,
            FromLocationType = transfer.FromLocationType,
            FromLocationName = fromLocationName,
            ToLocationId = transfer.ToLocationId,
            ToLocationType = transfer.ToLocationType,
            ToLocationName = toLocationName,
            TransferDate = transfer.TransferDate,
            Status = transfer.Status,
            ApprovedBy = transfer.ApprovedBy,
            ApproverName = transfer.Approver?.Name,
            CreatedBy = transfer.CreatedBy,
            CreatorName = transfer.Creator?.Name,
            CreatedAt = transfer.CreatedAt,
            Items = transfer.Items?.Select(i => new StockTransferItemDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                VariantSku = i.Variant?.Sku ?? string.Empty,
                ProductName = i.Variant?.Product?.Name ?? string.Empty,
                Quantity = i.Quantity
            }).ToList() ?? new List<StockTransferItemDto>()
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
