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
    private readonly IStockLedgerService _stockLedger; // UPDATED
    private readonly IPosCacheService    _posCache;    // UPDATED

    public StockTransferService(
        IStockTransferRepository transferRepository,
        IProductVariantRepository variantRepository,
        RetailPOSDbContext context,
        ILogger<StockTransferService> logger,
        IStockLedgerService stockLedger,  // UPDATED
        IPosCacheService    posCache)     // UPDATED
    {
        _transferRepository = transferRepository;
        _variantRepository = variantRepository;
        _context = context;
        _logger = logger;
        _stockLedger = stockLedger; // UPDATED
        _posCache    = posCache;    // UPDATED
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
        ValidateCreateOrUpdateRequest(dto);

        var now = DateTime.UtcNow;
        var normalizedFromType = NormalizeLocationType(dto.FromLocationType);
        var normalizedToType = NormalizeLocationType(dto.ToLocationType);

        await ValidateSourceStockAsync(dto.Items, dto.FromLocationId, normalizedFromType);

        var transfer = new StockTransfer
        {
            TransferNo = await GenerateTransferNumberAsync(),
            TransferType = NormalizeTransferType(dto.TransferType),
            RelatedRequisitionId = dto.RelatedRequisitionId,
            FromLocationId = dto.FromLocationId,
            FromLocationType = normalizedFromType,
            ToLocationId = dto.ToLocationId,
            ToLocationType = normalizedToType,
            TransferDate = DateTime.SpecifyKind(dto.TransferDate, DateTimeKind.Utc),
            Status = StockTransfer.StatusDraft,
            Notes = dto.Notes,
            CreatedBy = userId,
            CreatedAt = now,
            Items = dto.Items.Select(i => new StockTransferItem
            {
                VariantId = i.VariantId,
                Quantity = ResolveRequestedQuantity(i),
                RequestedQuantity = ResolveRequestedQuantity(i),
                TransferQuantity = ResolveTransferQuantity(i),
                AcceptedQuantity = 0,
                RejectedQuantity = 0,
                UnitCost = i.UnitCost,
                Remarks = i.Remarks
            }).ToList()
        };

        var created = await _transferRepository.CreateAsync(transfer);
        _logger.LogInformation("Stock transfer {TransferNo} ({TransferId}) created by user {UserId}", created.TransferNo, created.Id, userId);

        return await MapToDtoAsync(created);
    }

    public async Task<StockTransferDto> UpdateAsync(long id, CreateStockTransferDto dto)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusDraft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot edit transfer in status '{transfer.Status}'. Only draft transfers are editable.");

        ValidateCreateOrUpdateRequest(dto);
        var normalizedFromType = NormalizeLocationType(dto.FromLocationType);
        var normalizedToType = NormalizeLocationType(dto.ToLocationType);
        await ValidateSourceStockAsync(dto.Items, dto.FromLocationId, normalizedFromType);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            transfer.FromLocationId = dto.FromLocationId;
            transfer.FromLocationType = normalizedFromType;
            transfer.ToLocationId = dto.ToLocationId;
            transfer.ToLocationType = normalizedToType;
            transfer.TransferDate = DateTime.SpecifyKind(dto.TransferDate, DateTimeKind.Utc);
            transfer.TransferType = NormalizeTransferType(dto.TransferType);
            transfer.RelatedRequisitionId = dto.RelatedRequisitionId;
            transfer.Notes = dto.Notes;
            transfer.UpdatedAt = DateTime.UtcNow;

            // Replace items atomically
            _context.StockTransferItems.RemoveRange(transfer.Items);
            transfer.Items = dto.Items.Select(i => new StockTransferItem
            {
                TransferId = id,
                VariantId = i.VariantId,
                Quantity = ResolveRequestedQuantity(i),
                RequestedQuantity = ResolveRequestedQuantity(i),
                TransferQuantity = ResolveTransferQuantity(i),
                AcceptedQuantity = 0,
                RejectedQuantity = 0,
                UnitCost = i.UnitCost,
                Remarks = i.Remarks
            }).ToList();

            var updated = await _transferRepository.UpdateAsync(transfer);

            await transaction.CommitAsync();
            _logger.LogInformation("Stock transfer {TransferId} updated", id);

            return await MapToDtoAsync(updated);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<StockTransferDto> SubmitAsync(long id, long? submittedBy = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusDraft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only submit draft transfers. Current status: {transfer.Status}");

        transfer.Status = StockTransfer.StatusSubmitted;
        transfer.SubmittedBy = submittedBy;
        transfer.SubmittedAt = DateTime.UtcNow;
        transfer.UpdatedBy = submittedBy;
        transfer.UpdatedAt = DateTime.UtcNow;
        await _transferRepository.UpdateAsync(transfer);

        _logger.LogInformation("Stock transfer {TransferNo} ({TransferId}) submitted by user {UserId}", transfer.TransferNo, id, submittedBy);
        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> ApproveAsync(long id, long? approverId = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusSubmitted, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only approve submitted transfers. Current status: {transfer.Status}");

        transfer.Status = StockTransfer.StatusApproved;
        transfer.ApprovedBy = approverId;
        transfer.ApprovedAt = DateTime.UtcNow;
        transfer.UpdatedBy = approverId;
        transfer.UpdatedAt = DateTime.UtcNow;
        await _transferRepository.UpdateAsync(transfer);

        _logger.LogInformation("Stock transfer {TransferId} approved by user {UserId}", id, approverId);

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> RejectAsync(long id, string? reason = null, long? rejectedBy = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        var status = transfer.Status.ToLowerInvariant();
        if (status != StockTransfer.StatusSubmitted && status != StockTransfer.StatusInTransit && status != StockTransfer.StatusApproved)
            throw new InvalidOperationException($"Cannot reject transfer in status '{transfer.Status}'.");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            transfer.Status = StockTransfer.StatusRejected;
            transfer.RejectedBy = rejectedBy;
            transfer.RejectedAt = DateTime.UtcNow;
            transfer.UpdatedBy = rejectedBy;
            transfer.UpdatedAt = DateTime.UtcNow;

            if (status == StockTransfer.StatusInTransit)
            {
                foreach (var item in transfer.Items)
                {
                    var rejectedQty = ResolveTransferQuantity(item);
                    item.AcceptedQuantity = 0;
                    item.RejectedQuantity = rejectedQty;

                    _stockLedger.WriteEntry(
                        variantId: item.VariantId,
                        locationId: transfer.ToLocationId,
                        locationType: transfer.ToLocationType,
                        transactionType: StockLedgerTransactionType.TransferRejection,
                        qtyIn: 0,
                        qtyOut: 0,
                        balanceAfter: await GetCurrentInventoryBalanceAsync(item.VariantId, transfer.ToLocationId, transfer.ToLocationType),
                        referenceType: StockLedgerReferenceType.StockTransfer,
                        referenceId: transfer.Id,
                        remarks: $"Transfer rejected. Reason: {reason}",
                        createdBy: rejectedBy ?? transfer.CreatedBy);
                }

                await CreateReturnTransferForRejectedAsync(transfer, rejectedBy ?? transfer.CreatedBy);
            }

            await _transferRepository.UpdateAsync(transfer);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        _logger.LogInformation("Stock transfer {TransferId} rejected by user {UserId}. Reason: {Reason}", id, rejectedBy, reason);

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> SendAsync(long id, long? dispatchedBy = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusApproved, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only send approved transfers. Current status: {transfer.Status}");

        // Deduct source inventory at dispatch time and write transfer_out ledger entries.
        // This ensures in-transit stock is no longer counted as available at the source.
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var normalizedFromType = NormalizeLocationType(transfer.FromLocationType);

            foreach (var item in transfer.Items)
            {
                var sourceInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == transfer.FromLocationId &&
                    i.LocationType.ToLower() == normalizedFromType);

                var transferQty = ResolveTransferQuantity(item);

                if (sourceInventory == null || sourceInventory.Quantity < transferQty)
                    throw new InvalidOperationException(
                        $"Insufficient stock for variant {item.VariantId} at source. " +
                        $"Available: {sourceInventory?.Quantity ?? 0}, Required: {transferQty}");

                sourceInventory.Quantity -= transferQty;

                var transactionType = transfer.TransferType.Equals(StockTransfer.TransferTypeReturn, StringComparison.OrdinalIgnoreCase)
                    ? StockLedgerTransactionType.ReturnTransferOut
                    : StockLedgerTransactionType.TransferOut;

                _stockLedger.WriteEntry(
                    variantId: item.VariantId,
                    locationId: transfer.FromLocationId,
                    locationType: normalizedFromType,
                    transactionType: transactionType,
                    qtyIn: 0,
                    qtyOut: transferQty,
                    balanceAfter: sourceInventory.Quantity,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transfer.Id,
                    remarks: $"Dispatched to {transfer.ToLocationType} {transfer.ToLocationId}",
                    createdBy: transfer.CreatedBy);
            }

            transfer.Status = StockTransfer.StatusInTransit;
            transfer.DispatchedBy = dispatchedBy ?? transfer.UpdatedBy ?? transfer.CreatedBy;
            transfer.DispatchedAt = DateTime.UtcNow;
            transfer.UpdatedBy = transfer.DispatchedBy;
            transfer.UpdatedAt = DateTime.UtcNow;
            _context.StockTransfers.Update(transfer);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // UPDATED — if source was an outlet, its stock hint is now stale
            if (transfer.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
                foreach (var item in transfer.Items)
                    await _posCache.InvalidateStockAsync(item.VariantId, transfer.FromLocationId);

            _logger.LogInformation("Stock transfer {TransferId} marked as in-transit; source stock deducted", id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> ReceiveAsync(long id, ReceiveStockTransferDto? dto = null, long? receivedBy = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only receive in-transit transfers. Current status: {transfer.Status}");

        // Source stock was already deducted at SendAsync (in_transit transition).
        // Here we only add stock to the destination and write transfer_in ledger entries.
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var normalizedToType = NormalizeLocationType(transfer.ToLocationType);
            var requestedLines = dto?.Items.ToDictionary(x => x.VariantId, x => x);
            var totalAccepted = 0;
            var totalRejected = 0;

            foreach (var item in transfer.Items)
            {
                var destInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == transfer.ToLocationId &&
                    i.LocationType.ToLower() == normalizedToType);

                if (destInventory == null)
                {
                    destInventory = new Inventory
                    {
                        VariantId = item.VariantId,
                        LocationId = transfer.ToLocationId,
                        LocationType = normalizedToType,
                        Quantity = 0
                    };
                    _context.Inventories.Add(destInventory);
                }

                var transferQty = ResolveTransferQuantity(item);
                var acceptedQty = transferQty;
                var rejectedQty = 0;
                string? lineRemarks = item.Remarks;

                if (requestedLines != null && requestedLines.TryGetValue(item.VariantId, out var receiveLine))
                {
                    acceptedQty = receiveLine.AcceptedQuantity;
                    rejectedQty = receiveLine.RejectedQuantity;
                    lineRemarks = receiveLine.Remarks;

                    if (acceptedQty < 0 || rejectedQty < 0)
                        throw new InvalidOperationException("Accepted and rejected quantities cannot be negative.");

                    if (acceptedQty + rejectedQty > transferQty)
                        throw new InvalidOperationException(
                            $"Accepted + rejected quantities exceed transfer quantity for variant {item.VariantId}. " +
                            $"Transfer: {transferQty}, Accepted: {acceptedQty}, Rejected: {rejectedQty}");
                }

                item.AcceptedQuantity = acceptedQty;
                item.RejectedQuantity = rejectedQty;
                item.Remarks = lineRemarks;
                destInventory.Quantity += acceptedQty;
                totalAccepted += acceptedQty;
                totalRejected += rejectedQty;

                var transactionType = transfer.TransferType.Equals(StockTransfer.TransferTypeReturn, StringComparison.OrdinalIgnoreCase)
                    ? StockLedgerTransactionType.ReturnTransferIn
                    : StockLedgerTransactionType.TransferIn;

                _stockLedger.WriteEntry(
                    variantId: item.VariantId,
                    locationId: transfer.ToLocationId,
                    locationType: normalizedToType,
                    transactionType: transactionType,
                    qtyIn: acceptedQty,
                    qtyOut: 0,
                    balanceAfter: destInventory.Quantity,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transfer.Id,
                    remarks: $"Received from {transfer.FromLocationType} {transfer.FromLocationId}",
                    createdBy: receivedBy ?? transfer.CreatedBy);
            }

            transfer.Status = ResolveReceiveStatus(totalAccepted, totalRejected);
            transfer.ReceivedBy = receivedBy ?? transfer.UpdatedBy ?? transfer.CreatedBy;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.Notes = string.IsNullOrWhiteSpace(dto?.Notes) ? transfer.Notes : dto!.Notes;
            transfer.UpdatedBy = transfer.ReceivedBy;
            transfer.UpdatedAt = DateTime.UtcNow;
            _context.StockTransfers.Update(transfer);

            if (totalRejected > 0)
                await CreateReturnTransferForRejectedAsync(transfer, transfer.ReceivedBy);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // UPDATED — if destination is an outlet, invalidate its stock hint cache
            if (transfer.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
                foreach (var item in transfer.Items)
                    await _posCache.InvalidateStockAsync(item.VariantId, transfer.ToLocationId);

            _logger.LogInformation("Stock transfer {TransferId} received; destination stock updated", id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> CancelAsync(long id, string? reason = null, long? cancelledBy = null)
    {
        var transfer = await _transferRepository.GetByIdAsync(id);
        if (transfer == null)
            throw new KeyNotFoundException($"Stock transfer with ID {id} not found");

        if (transfer.Status.Equals(StockTransfer.StatusCancelled, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Transfer is already cancelled");

        if (transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase)
            || transfer.Status.Equals(StockTransfer.StatusReceived, StringComparison.OrdinalIgnoreCase)
            || transfer.Status.Equals(StockTransfer.StatusPartiallyReceived, StringComparison.OrdinalIgnoreCase)
            || transfer.Status.Equals(StockTransfer.StatusRejected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot cancel transfer in status '{transfer.Status}'. Cancellation is allowed only before dispatch.");

        transfer.Status = StockTransfer.StatusCancelled;
        transfer.CancelledBy = cancelledBy;
        transfer.CancelledAt = DateTime.UtcNow;
        transfer.UpdatedBy = cancelledBy;
        transfer.UpdatedAt = DateTime.UtcNow;
        await _transferRepository.UpdateAsync(transfer);

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
            TransferNo = transfer.TransferNo,
            TransferType = transfer.TransferType,
            RelatedRequisitionId = transfer.RelatedRequisitionId,
            FromLocationId = transfer.FromLocationId,
            FromLocationType = transfer.FromLocationType,
            FromLocationName = fromLocationName,
            ToLocationId = transfer.ToLocationId,
            ToLocationType = transfer.ToLocationType,
            ToLocationName = toLocationName,
            TransferDate = transfer.TransferDate,
            Status = transfer.Status,
            Notes = transfer.Notes,
            ApprovedBy = transfer.ApprovedBy,
            ApproverName = transfer.Approver?.Name,
            ApprovedAt = transfer.ApprovedAt,
            SubmittedBy = transfer.SubmittedBy,
            SubmittedAt = transfer.SubmittedAt,
            DispatchedBy = transfer.DispatchedBy,
            DispatchedAt = transfer.DispatchedAt,
            ReceivedBy = transfer.ReceivedBy,
            ReceivedAt = transfer.ReceivedAt,
            RejectedBy = transfer.RejectedBy,
            RejectedAt = transfer.RejectedAt,
            CancelledBy = transfer.CancelledBy,
            CancelledAt = transfer.CancelledAt,
            UpdatedBy = transfer.UpdatedBy,
            UpdatedAt = transfer.UpdatedAt,
            CreatedBy = transfer.CreatedBy,
            CreatorName = transfer.Creator?.Name,
            CreatedAt = transfer.CreatedAt,
            Items = transfer.Items?.Select(i => new StockTransferItemDto
            {
                Id = i.Id,
                VariantId = i.VariantId,
                VariantSku = i.Variant?.Sku ?? string.Empty,
                ProductName = i.Variant?.Product?.Name ?? string.Empty,
                Quantity = ResolveTransferQuantity(i),
                RequestedQuantity = i.RequestedQuantity,
                TransferQuantity = ResolveTransferQuantity(i),
                AcceptedQuantity = i.AcceptedQuantity,
                RejectedQuantity = i.RejectedQuantity,
                UnitCost = i.UnitCost,
                Remarks = i.Remarks
            }).ToList() ?? new List<StockTransferItemDto>()
        };
    }

    private static string NormalizeTransferType(string transferType)
    {
        var normalized = string.IsNullOrWhiteSpace(transferType)
            ? StockTransfer.TransferTypeDirect
            : transferType.Trim().ToLowerInvariant();

        if (normalized != StockTransfer.TransferTypeDirect
            && normalized != StockTransfer.TransferTypeRequisition
            && normalized != StockTransfer.TransferTypeReturn)
        {
            throw new InvalidOperationException($"Invalid transfer type '{transferType}'.");
        }

        return normalized;
    }

    private static string NormalizeLocationType(string locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }

    private static void ValidateCreateOrUpdateRequest(CreateStockTransferDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            throw new InvalidOperationException("Stock transfer must have at least one item.");

        var fromType = NormalizeLocationType(dto.FromLocationType);
        var toType = NormalizeLocationType(dto.ToLocationType);
        if (dto.FromLocationId == dto.ToLocationId && fromType == toType)
            throw new InvalidOperationException("Source and destination locations cannot be the same.");

        if (dto.Items.Any(i => ResolveTransferQuantity(i) <= 0))
            throw new InvalidOperationException("All transfer quantities must be greater than zero.");
    }

    private async Task ValidateSourceStockAsync(IEnumerable<CreateStockTransferItemDto> items, long sourceLocationId, string sourceLocationType)
    {
        var normalizedSourceType = NormalizeLocationType(sourceLocationType);

        foreach (var item in items)
        {
            var variant = await _variantRepository.GetByIdAsync(item.VariantId)
                ?? throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");

            var sourceInventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == item.VariantId &&
                i.LocationId == sourceLocationId &&
                i.LocationType.ToLower() == normalizedSourceType);

            var transferQty = ResolveTransferQuantity(item);
            if (sourceInventory == null || sourceInventory.Quantity < transferQty)
                throw new InvalidOperationException(
                    $"Insufficient stock for variant {variant.Sku}. Available: {sourceInventory?.Quantity ?? 0}, Requested: {transferQty}");
        }
    }

    private static int ResolveRequestedQuantity(CreateStockTransferItemDto item)
    {
        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;

        if (item.Quantity > 0)
            return item.Quantity;

        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;

        return item.Quantity;
    }

    private static int ResolveTransferQuantity(CreateStockTransferItemDto item)
    {
        if (item.TransferQuantity.HasValue && item.TransferQuantity.Value > 0)
            return item.TransferQuantity.Value;

        if (item.Quantity > 0)
            return item.Quantity;

        if (item.RequestedQuantity.HasValue && item.RequestedQuantity.Value > 0)
            return item.RequestedQuantity.Value;

        return item.Quantity;
    }

    private static string ResolveReceiveStatus(int totalAccepted, int totalRejected)
    {
        if (totalAccepted > 0 && totalRejected > 0)
            return StockTransfer.StatusPartiallyReceived;

        if (totalAccepted == 0 && totalRejected > 0)
            return StockTransfer.StatusRejected;

        return StockTransfer.StatusReceived;
    }

    private static int ResolveTransferQuantity(StockTransferItem item)
        => item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;

    private async Task<int> GetCurrentInventoryBalanceAsync(long variantId, long locationId, string locationType)
    {
        var normalizedType = NormalizeLocationType(locationType);

        var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
            i.VariantId == variantId &&
            i.LocationId == locationId &&
            i.LocationType.ToLower() == normalizedType);
        return inventory?.Quantity ?? 0;
    }

    private async Task<string> GenerateTransferNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var maxId = await _context.StockTransfers.MaxAsync(x => (long?)x.Id) ?? 0;
        return $"TRF-{year}-{(maxId + 1):D5}";
    }

    private async Task CreateReturnTransferForRejectedAsync(StockTransfer rejectedTransfer, long? createdBy)
    {
        var returnItems = rejectedTransfer.Items
            .Select(i => new { i.VariantId, Qty = i.RejectedQuantity > 0 ? i.RejectedQuantity : ResolveTransferQuantity(i) })
            .Where(x => x.Qty > 0)
            .ToList();

        if (returnItems.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var returnTransfer = new StockTransfer
        {
            TransferNo = await GenerateTransferNumberAsync(),
            TransferType = StockTransfer.TransferTypeReturn,
            FromLocationId = rejectedTransfer.ToLocationId,
            FromLocationType = rejectedTransfer.ToLocationType,
            ToLocationId = rejectedTransfer.FromLocationId,
            ToLocationType = rejectedTransfer.FromLocationType,
            TransferDate = now,
            Status = StockTransfer.StatusDraft,
            CreatedBy = createdBy,
            CreatedAt = now,
            Notes = $"Auto-created return transfer for rejected transfer #{rejectedTransfer.TransferNo ?? rejectedTransfer.Id.ToString()}.",
            Items = returnItems.Select(x => new StockTransferItem
            {
                VariantId = x.VariantId,
                Quantity = x.Qty,
                RequestedQuantity = x.Qty,
                TransferQuantity = x.Qty,
                AcceptedQuantity = 0,
                RejectedQuantity = 0,
                UnitCost = 0m
            }).ToList()
        };

        _context.StockTransfers.Add(returnTransfer);
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
