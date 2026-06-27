using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.StockRequisition;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

public class StockRequisitionService : IStockRequisitionService
{
    private readonly IStockRequisitionRepository _requisitionRepository;
    private readonly IStockTransferRepository _transferRepository;
    private readonly IProductVariantRepository _variantRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<StockRequisitionService> _logger;

    public StockRequisitionService(
        IStockRequisitionRepository requisitionRepository,
        IStockTransferRepository transferRepository,
        IProductVariantRepository variantRepository,
        RetailPOSDbContext context,
        ILogger<StockRequisitionService> logger)
    {
        _requisitionRepository = requisitionRepository;
        _transferRepository = transferRepository;
        _variantRepository = variantRepository;
        _context = context;
        _logger = logger;
    }

    public async Task<StockRequisitionDto> GetByIdAsync(long id)
    {
        var requisition = await _requisitionRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        return await MapToDtoAsync(requisition);
    }

    public async Task<List<StockRequisitionDto>> GetAllAsync(string? status = null, long? requestingLocationId = null, long? sourceLocationId = null)
    {
        var requisitions = await _requisitionRepository.GetAllAsync(status, requestingLocationId, sourceLocationId);
        var result = new List<StockRequisitionDto>();
        foreach (var requisition in requisitions)
            result.Add(await MapToDtoAsync(requisition));

        return result;
    }

    public async Task<StockRequisitionListDto> SearchAsync(StockRequisitionSearchDto dto)
    {
        var (requisitions, totalCount) = await _requisitionRepository.SearchAsync(
            dto.Status,
            dto.RequestingLocationId,
            dto.RequestingLocationType,
            dto.SourceLocationId,
            dto.SourceLocationType,
            dto.PageNumber,
            dto.PageSize);

        var rows = new List<StockRequisitionDto>();
        foreach (var requisition in requisitions)
            rows.Add(await MapToDtoAsync(requisition));

        return new StockRequisitionListDto
        {
            StockRequisitions = rows,
            TotalCount = totalCount,
            PageNumber = dto.PageNumber,
            PageSize = dto.PageSize
        };
    }

    public async Task<StockRequisitionDto> CreateAsync(CreateStockRequisitionDto dto, long requestedBy)
    {
        ValidateCreateOrUpdate(dto);

        var now = DateTime.UtcNow;
        var requisition = new StockRequisition
        {
            RequisitionNo = await GenerateRequisitionNumberAsync(),
            RequestingLocationId = dto.RequestingLocationId,
            RequestingLocationType = NormalizeLocationType(dto.RequestingLocationType),
            SourceLocationId = dto.SourceLocationId,
            SourceLocationType = NormalizeLocationType(dto.SourceLocationType),
            RequestedBy = requestedBy,
            RequestDate = now,
            Status = StockRequisition.StatusDraft,
            Notes = dto.Notes,
            CreatedAt = now,
            Lines = dto.Lines.Select(l => new StockRequisitionLine
            {
                VariantId = l.VariantId,
                RequestedQuantity = l.RequestedQuantity,
                FulfilledQuantity = 0,
                Remarks = l.Remarks
            }).ToList()
        };

        var created = await _requisitionRepository.CreateAsync(requisition);
        _logger.LogInformation("Stock requisition {RequisitionNo} ({RequisitionId}) created by user {UserId}", created.RequisitionNo, created.Id, requestedBy);
        return await MapToDtoAsync(created);
    }

    public async Task<StockRequisitionDto> UpdateAsync(long id, CreateStockRequisitionDto dto, long updatedBy)
    {
        ValidateCreateOrUpdate(dto);

        var requisition = await _context.StockRequisitions
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        if (!requisition.Status.Equals(StockRequisition.StatusDraft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot edit requisition in status '{requisition.Status}'. Only draft requisitions are editable.");

        requisition.RequestingLocationId = dto.RequestingLocationId;
        requisition.RequestingLocationType = NormalizeLocationType(dto.RequestingLocationType);
        requisition.SourceLocationId = dto.SourceLocationId;
        requisition.SourceLocationType = NormalizeLocationType(dto.SourceLocationType);
        requisition.Notes = dto.Notes;
        requisition.UpdatedBy = updatedBy;
        requisition.UpdatedAt = DateTime.UtcNow;

        _context.StockRequisitionLines.RemoveRange(requisition.Lines);
        requisition.Lines = dto.Lines.Select(l => new StockRequisitionLine
        {
            RequisitionId = requisition.Id,
            VariantId = l.VariantId,
            RequestedQuantity = l.RequestedQuantity,
            FulfilledQuantity = 0,
            Remarks = l.Remarks
        }).ToList();

        await _requisitionRepository.UpdateAsync(requisition);
        _logger.LogInformation("Stock requisition {RequisitionNo} ({RequisitionId}) updated by user {UserId}", requisition.RequisitionNo, requisition.Id, updatedBy);

        return await GetByIdAsync(id);
    }

    public async Task<StockRequisitionDto> SubmitAsync(long id, long submittedBy)
    {
        var requisition = await _requisitionRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        if (!requisition.Status.Equals(StockRequisition.StatusDraft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only submit draft requisitions. Current status: {requisition.Status}");

        requisition.Status = StockRequisition.StatusSubmitted;
        requisition.SubmittedBy = submittedBy;
        requisition.SubmittedAt = DateTime.UtcNow;
        requisition.UpdatedBy = submittedBy;
        requisition.UpdatedAt = DateTime.UtcNow;

        await _requisitionRepository.UpdateAsync(requisition);
        return await GetByIdAsync(id);
    }

    public async Task<StockRequisitionDto> ApproveAsync(long id, long approvedBy)
    {
        var requisition = await _requisitionRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        if (!requisition.Status.Equals(StockRequisition.StatusSubmitted, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only approve submitted requisitions. Current status: {requisition.Status}");

        requisition.Status = StockRequisition.StatusApproved;
        requisition.ApprovedBy = approvedBy;
        requisition.ApprovedAt = DateTime.UtcNow;
        requisition.UpdatedBy = approvedBy;
        requisition.UpdatedAt = DateTime.UtcNow;

        await _requisitionRepository.UpdateAsync(requisition);
        return await GetByIdAsync(id);
    }

    public async Task<StockRequisitionDto> RejectAsync(long id, long rejectedBy, string? reason = null)
    {
        var requisition = await _requisitionRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        if (!requisition.Status.Equals(StockRequisition.StatusSubmitted, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Can only reject submitted requisitions. Current status: {requisition.Status}");

        requisition.Status = StockRequisition.StatusRejected;
        requisition.RejectedBy = rejectedBy;
        requisition.RejectedAt = DateTime.UtcNow;
        requisition.RejectionReason = reason;
        requisition.UpdatedBy = rejectedBy;
        requisition.UpdatedAt = DateTime.UtcNow;

        await _requisitionRepository.UpdateAsync(requisition);
        return await GetByIdAsync(id);
    }

    public async Task<StockTransferDto> ConvertToTransferAsync(long id, long createdBy)
    {
        var requisition = await _context.StockRequisitions
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

        if (!requisition.Status.Equals(StockRequisition.StatusApproved, StringComparison.OrdinalIgnoreCase)
            && !requisition.Status.Equals(StockRequisition.StatusPartiallyFulfilled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Requisition in status '{requisition.Status}' cannot be converted to transfer.");
        }

        var pendingLines = requisition.Lines
            .Select(line => new
            {
                Line = line,
                Pending = Math.Max(0, line.RequestedQuantity - line.FulfilledQuantity)
            })
            .Where(x => x.Pending > 0)
            .ToList();

        if (pendingLines.Count == 0)
            throw new InvalidOperationException("Requisition is already fully fulfilled.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            var transfer = new StockTransfer
            {
                TransferNo = await GenerateTransferNumberAsync(),
                TransferType = StockTransfer.TransferTypeRequisition,
                RelatedRequisitionId = requisition.Id,
                FromLocationId = requisition.SourceLocationId,
                FromLocationType = requisition.SourceLocationType,
                ToLocationId = requisition.RequestingLocationId,
                ToLocationType = requisition.RequestingLocationType,
                TransferDate = now,
                Status = StockTransfer.StatusDraft,
                CreatedBy = createdBy,
                CreatedAt = now,
                Notes = $"Created from requisition {requisition.RequisitionNo}",
                Items = pendingLines.Select(x => new StockTransferItem
                {
                    VariantId = x.Line.VariantId,
                    Quantity = x.Pending,
                    RequestedQuantity = x.Line.RequestedQuantity,
                    TransferQuantity = x.Pending,
                    AcceptedQuantity = 0,
                    RejectedQuantity = 0,
                    UnitCost = 0m,
                    Remarks = x.Line.Remarks
                }).ToList()
            };

            foreach (var pending in pendingLines)
                pending.Line.FulfilledQuantity += pending.Pending;

            requisition.Status = requisition.Lines.All(l => l.FulfilledQuantity >= l.RequestedQuantity)
                ? StockRequisition.StatusFullyFulfilled
                : StockRequisition.StatusPartiallyFulfilled;
            requisition.UpdatedBy = createdBy;
            requisition.UpdatedAt = now;

            _context.StockTransfers.Add(transfer);
            _context.StockRequisitions.Update(requisition);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var createdTransfer = await _transferRepository.GetByIdAsync(transfer.Id)
                ?? throw new InvalidOperationException("Transfer was created but could not be loaded.");

            _logger.LogInformation(
                "Stock requisition {RequisitionNo} ({RequisitionId}) converted to transfer {TransferNo} ({TransferId}) by user {UserId}",
                requisition.RequisitionNo,
                requisition.Id,
                createdTransfer.TransferNo,
                createdTransfer.Id,
                createdBy);

            return await MapTransferToDtoAsync(createdTransfer);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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

    private void ValidateCreateOrUpdate(CreateStockRequisitionDto dto)
    {
        if (dto.Lines == null || dto.Lines.Count == 0)
            throw new InvalidOperationException("Stock requisition must have at least one line.");

        var requestingType = NormalizeLocationType(dto.RequestingLocationType);
        var sourceType = NormalizeLocationType(dto.SourceLocationType);

        if (dto.RequestingLocationId == dto.SourceLocationId && requestingType == sourceType)
            throw new InvalidOperationException("Requesting and source locations cannot be the same.");

        if (dto.Lines.Any(line => line.RequestedQuantity <= 0))
            throw new InvalidOperationException("All requested quantities must be greater than zero.");
    }

    private async Task<string> GenerateRequisitionNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var maxId = await _context.StockRequisitions.MaxAsync(x => (long?)x.Id) ?? 0;
        return $"REQ-{year}-{(maxId + 1):D5}";
    }

    private async Task<string> GenerateTransferNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var maxId = await _context.StockTransfers.MaxAsync(x => (long?)x.Id) ?? 0;
        return $"TRF-{year}-{(maxId + 1):D5}";
    }

    private async Task<StockRequisitionDto> MapToDtoAsync(StockRequisition requisition)
    {
        var requestingLocationName = await ResolveLocationNameAsync(requisition.RequestingLocationId, requisition.RequestingLocationType);
        var sourceLocationName = await ResolveLocationNameAsync(requisition.SourceLocationId, requisition.SourceLocationType);

        return new StockRequisitionDto
        {
            Id = requisition.Id,
            RequisitionNo = requisition.RequisitionNo,
            RequestingLocationId = requisition.RequestingLocationId,
            RequestingLocationType = requisition.RequestingLocationType,
            RequestingLocationName = requestingLocationName,
            SourceLocationId = requisition.SourceLocationId,
            SourceLocationType = requisition.SourceLocationType,
            SourceLocationName = sourceLocationName,
            RequestedBy = requisition.RequestedBy,
            RequesterName = requisition.Requester?.Name,
            RequestDate = requisition.RequestDate,
            Status = requisition.Status,
            Notes = requisition.Notes,
            CreatedAt = requisition.CreatedAt,
            UpdatedAt = requisition.UpdatedAt,
            UpdatedBy = requisition.UpdatedBy,
            SubmittedAt = requisition.SubmittedAt,
            SubmittedBy = requisition.SubmittedBy,
            ApprovedAt = requisition.ApprovedAt,
            ApprovedBy = requisition.ApprovedBy,
            RejectedAt = requisition.RejectedAt,
            RejectedBy = requisition.RejectedBy,
            RejectionReason = requisition.RejectionReason,
            ClosedAt = requisition.ClosedAt,
            ClosedBy = requisition.ClosedBy,
            Lines = requisition.Lines.Select(line => new StockRequisitionLineDto
            {
                Id = line.Id,
                VariantId = line.VariantId,
                VariantSku = line.Variant?.Sku ?? string.Empty,
                ProductName = line.Variant?.Product?.Name ?? string.Empty,
                RequestedQuantity = line.RequestedQuantity,
                FulfilledQuantity = line.FulfilledQuantity,
                Remarks = line.Remarks
            }).ToList()
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

    private async Task<StockTransferDto> MapTransferToDtoAsync(StockTransfer transfer)
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
            Items = transfer.Items.Select(item => new StockTransferItemDto
            {
                Id = item.Id,
                VariantId = item.VariantId,
                VariantSku = item.Variant?.Sku ?? string.Empty,
                ProductName = item.Variant?.Product?.Name ?? string.Empty,
                Quantity = item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity,
                RequestedQuantity = item.RequestedQuantity,
                TransferQuantity = item.TransferQuantity,
                AcceptedQuantity = item.AcceptedQuantity,
                RejectedQuantity = item.RejectedQuantity,
                UnitCost = item.UnitCost,
                Remarks = item.Remarks
            }).ToList()
        };
    }
}
