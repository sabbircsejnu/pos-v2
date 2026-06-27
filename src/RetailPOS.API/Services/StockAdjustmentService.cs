using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.StockAdjustment;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

public class StockAdjustmentService : IStockAdjustmentService
{
    private static readonly HashSet<string> ValidCreateActions = new(StringComparer.OrdinalIgnoreCase)
    {
        StockAdjustmentCreateActions.Draft,
        StockAdjustmentCreateActions.Submit,
        StockAdjustmentCreateActions.SubmitAndApprove
    };

    private static readonly HashSet<string> ValidReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Found",
        "Damaged",
        "Expired",
        "Lost",
        "Stolen",
        "OpeningBalanceCorrection",
        "StockCountCorrection",
        "SystemCorrection",
        "Other"
    };

    private const string AdjustmentNumberPrefix = "ADJ";

    private readonly IStockAdjustmentRepository _adjustmentRepository;
    private readonly IProductVariantRepository _variantRepository;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<StockAdjustmentService> _logger;
    private readonly IStockLedgerService _stockLedger;
    private readonly IPosCacheService _posCache;
    private readonly ISettingsService _settingsService;
    private readonly IAuditService _auditService;

    public StockAdjustmentService(
        IStockAdjustmentRepository adjustmentRepository,
        IProductVariantRepository variantRepository,
        RetailPOSDbContext context,
        ILogger<StockAdjustmentService> logger,
        IStockLedgerService stockLedger,
        IPosCacheService posCache,
        ISettingsService settingsService,
        IAuditService auditService)
    {
        _adjustmentRepository = adjustmentRepository;
        _variantRepository = variantRepository;
        _context = context;
        _logger = logger;
        _stockLedger = stockLedger;
        _posCache = posCache;
        _settingsService = settingsService;
        _auditService = auditService;
    }

    public async Task<StockAdjustmentDto> GetByIdAsync(long id)
    {
        var adjustment = await _adjustmentRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");

        return await MapToDtoAsync(adjustment);
    }

    public async Task<List<StockAdjustmentDto>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null, string? status = null)
    {
        var adjustments = await _adjustmentRepository.GetAllAsync(locationId, locationType, variantId, status);
        var result = new List<StockAdjustmentDto>();
        foreach (var adjustment in adjustments)
        {
            result.Add(await MapToDtoAsync(adjustment));
        }

        return result;
    }

    public async Task<StockAdjustmentListDto> SearchAsync(StockAdjustmentSearchDto searchDto)
    {
        var (adjustments, totalCount) = await _adjustmentRepository.SearchAsync(
            searchDto.LocationId,
            searchDto.LocationType,
            searchDto.VariantId,
            searchDto.Status,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize);

        var dtos = new List<StockAdjustmentDto>();
        foreach (var adjustment in adjustments)
        {
            dtos.Add(await MapToDtoAsync(adjustment));
        }

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
        foreach (var adjustment in adjustments)
        {
            result.Add(await MapToDtoAsync(adjustment));
        }

        return result;
    }

    public async Task<StockAdjustmentDto> CreateAsync(CreateStockAdjustmentDto dto, long adjustedBy)
    {
        return await CreateBatchAsync(new CreateStockAdjustmentBatchDto
        {
            Action = string.IsNullOrWhiteSpace(dto.Action) ? StockAdjustmentCreateActions.Draft : dto.Action,
            LocationId = dto.LocationId,
            LocationType = dto.LocationType,
            Items =
            [
                new CreateStockAdjustmentLineDto
                {
                    VariantId = dto.VariantId,
                    QuantityChange = dto.QuantityChange,
                    Reason = dto.Reason,
                    Notes = dto.Notes,
                }
            ]
        }, adjustedBy);
    }

    public async Task<StockAdjustmentDto> CreateBatchAsync(CreateStockAdjustmentBatchDto dto, long adjustedBy)
    {
        var action = NormalizeCreateAction(dto.Action);
        var normalizedItems = await NormalizeItemsAsync(dto.Reason, dto.Notes, dto.Items);
        var locationType = dto.LocationType.ToLowerInvariant();
        var now = DateTime.UtcNow;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var lines = new List<StockAdjustmentLine>();
            foreach (var item in normalizedItems)
            {
                var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                    i.VariantId == item.VariantId &&
                    i.LocationId == dto.LocationId &&
                    i.LocationType == locationType);

                var currentQty = inventory?.Quantity ?? 0;
                var projectedQty = currentQty + item.QuantityChange;
                await ValidateProjectedQuantityAsync(currentQty, item.QuantityChange, item.VariantId);

                lines.Add(new StockAdjustmentLine
                {
                    VariantId = item.VariantId,
                    PreviousQuantity = currentQty,
                    QuantityChange = item.QuantityChange,
                    NewQuantity = projectedQty,
                    Reason = item.Reason!,
                    Notes = item.Notes,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var initialStatus = action == StockAdjustmentCreateActions.Draft
                ? StockAdjustment.StatusDraft
                : StockAdjustment.StatusPendingApproval;

            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = await GenerateAdjustmentNumberAsync(),
                Status = initialStatus,
                LocationId = dto.LocationId,
                LocationType = locationType,
                AdjustedBy = adjustedBy,
                AdjustmentDate = now,
                CreatedAt = now,
                UpdatedAt = now,
                SubmittedAt = action == StockAdjustmentCreateActions.Draft ? null : now,
                Lines = lines
            };

            _context.StockAdjustments.Add(adjustment);
            await _context.SaveChangesAsync();

            if (action == StockAdjustmentCreateActions.SubmitAndApprove)
            {
                await ApplyApprovalAsync(adjustment, adjustedBy, now);
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Stock adjustment {AdjustmentNumber} created at location {LocationId} ({LocationType}) with {LineCount} lines and action {Action}",
                adjustment.AdjustmentNumber,
                dto.LocationId,
                locationType,
                lines.Count,
                action);

            var created = await GetByIdAsync(adjustment.Id);
            await TryRecordAuditAsync(
                BuildLifecycleAudit(
                    AuditActionType.Create,
                    AuditOperationType.Insert,
                    created,
                    $"Stock adjustment {created.AdjustmentNumber} created as {created.Status}",
                    created.AdjustedBy,
                    []),
                "create stock adjustment");

            if (action == StockAdjustmentCreateActions.Submit)
            {
                await TryRecordAuditAsync(
                    BuildLifecycleAudit(
                        AuditActionType.Submit,
                        AuditOperationType.Update,
                        created,
                        $"Stock adjustment {created.AdjustmentNumber} submitted for approval",
                        created.AdjustedBy,
                        [
                            ("Status", StockAdjustment.StatusDraft, StockAdjustment.StatusPendingApproval),
                            ("SubmittedAt", null, created.SubmittedAt)
                        ]),
                    "submit stock adjustment");
            }

            if (action == StockAdjustmentCreateActions.SubmitAndApprove)
            {
                await TryRecordAuditAsync(
                    BuildApproveAudit(created, adjustedBy),
                    "approve stock adjustment");
            }

            return created;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<StockAdjustmentDto> UpdateAsync(long id, UpdateStockAdjustmentDto dto, long updatedBy)
    {
        var before = await GetByIdAsync(id);
        var adjustment = await GetTrackedAdjustmentAsync(id, includeLines: true);
        EnsureCurrentStatus(adjustment, StockAdjustment.StatusDraft, "edit");

        var normalizedItems = await NormalizeItemsAsync(null, null, dto.Items);

        var locationType = dto.LocationType.ToLowerInvariant();
        var replacementLines = new List<StockAdjustmentLine>();
        foreach (var item in normalizedItems)
        {
            var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == item.VariantId &&
                i.LocationId == dto.LocationId &&
                i.LocationType == locationType);

            var currentQty = inventory?.Quantity ?? 0;
            var projectedQty = currentQty + item.QuantityChange;
            await ValidateProjectedQuantityAsync(currentQty, item.QuantityChange, item.VariantId);

            replacementLines.Add(new StockAdjustmentLine
            {
                VariantId = item.VariantId,
                PreviousQuantity = currentQty,
                QuantityChange = item.QuantityChange,
                NewQuantity = projectedQty,
                Reason = item.Reason!,
                Notes = item.Notes,
                CreatedAt = adjustment.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            });
        }

        var now = DateTime.UtcNow;
        adjustment.LocationId = dto.LocationId;
        adjustment.LocationType = locationType;
        adjustment.UpdatedAt = now;
        adjustment.AdjustmentDate = now;

        _context.StockAdjustmentLines.RemoveRange(adjustment.Lines);
        adjustment.Lines = replacementLines;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} updated by user {UserId}",
            adjustment.AdjustmentNumber,
            updatedBy);

        var updated = await GetByIdAsync(id);
        await TryRecordAuditAsync(
            BuildLifecycleAudit(
                AuditActionType.Update,
                AuditOperationType.Update,
                updated,
                $"Stock adjustment {updated.AdjustmentNumber} edited",
                updatedBy,
                BuildUpdateFields(before, updated)),
            "update stock adjustment");

        return updated;
    }

    public async Task DeleteAsync(long id, long deletedBy)
    {
        var adjustment = await GetTrackedAdjustmentAsync(id, includeLines: true);
        EnsureCurrentStatus(adjustment, StockAdjustment.StatusDraft, "delete");

        _context.StockAdjustments.Remove(adjustment);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} deleted by user {UserId}",
            adjustment.AdjustmentNumber,
            deletedBy);
    }

    public async Task<StockAdjustmentDto> SubmitAsync(long id, long submittedBy)
    {
        var adjustment = await GetTrackedAdjustmentAsync(id);
        EnsureCurrentStatus(adjustment, StockAdjustment.StatusDraft, "submit");

        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusPendingApproval;
        adjustment.SubmittedAt = now;
        adjustment.UpdatedAt = now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} submitted by user {UserId}",
            adjustment.AdjustmentNumber,
            submittedBy);

        var submitted = await GetByIdAsync(id);
        await TryRecordAuditAsync(
            BuildLifecycleAudit(
                AuditActionType.Submit,
                AuditOperationType.Update,
                submitted,
                $"Stock adjustment {submitted.AdjustmentNumber} submitted for approval",
                submittedBy,
                [
                    ("Status", StockAdjustment.StatusDraft, StockAdjustment.StatusPendingApproval),
                    ("SubmittedAt", null, submitted.SubmittedAt)
                ]),
            "submit stock adjustment");

        return submitted;
    }

    public async Task<StockAdjustmentDto> ApproveAsync(long id, long approvedBy)
    {
        var adjustment = await GetTrackedAdjustmentAsync(id, includeLines: true);
        EnsureCurrentStatus(adjustment, StockAdjustment.StatusPendingApproval, "approve");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            await ApplyApprovalAsync(adjustment, approvedBy, now);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            if (adjustment.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var variantId in adjustment.Lines.Select(line => line.VariantId).Distinct())
                {
                    await _posCache.InvalidateStockAsync(variantId, adjustment.LocationId);
                }
            }

            _logger.LogInformation(
                "Stock adjustment {AdjustmentNumber} approved by user {UserId}",
                adjustment.AdjustmentNumber,
                approvedBy);

            var approved = await GetByIdAsync(id);
            await TryRecordAuditAsync(
                BuildApproveAudit(approved, approvedBy),
                "approve stock adjustment");

            return approved;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<StockAdjustmentDto> RejectAsync(long id, long rejectedBy, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Rejection reason is required.");

        var adjustment = await GetTrackedAdjustmentAsync(id);
        EnsureCurrentStatus(adjustment, StockAdjustment.StatusPendingApproval, "reject");

        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusRejected;
        adjustment.RejectedBy = rejectedBy;
        adjustment.RejectedAt = now;
        adjustment.RejectionReason = reason.Trim();
        adjustment.UpdatedAt = now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} rejected by user {UserId}",
            adjustment.AdjustmentNumber,
            rejectedBy);

        var rejected = await GetByIdAsync(id);
        await TryRecordAuditAsync(
            BuildLifecycleAudit(
                AuditActionType.Reject,
                AuditOperationType.Update,
                rejected,
                $"Stock adjustment {rejected.AdjustmentNumber} rejected",
                rejectedBy,
                [
                    ("Status", StockAdjustment.StatusPendingApproval, StockAdjustment.StatusRejected),
                    ("RejectedAt", null, rejected.RejectedAt),
                    ("RejectionReason", null, rejected.RejectionReason)
                ]),
            "reject stock adjustment");

        return rejected;
    }

    public async Task<StockAdjustmentDto> CancelAsync(long id, long cancelledBy)
    {
        var adjustment = await GetTrackedAdjustmentAsync(id);
        if (adjustment.Status != StockAdjustment.StatusDraft && adjustment.Status != StockAdjustment.StatusPendingApproval)
        {
            throw new InvalidOperationException(
                $"Only {StockAdjustment.StatusDraft} or {StockAdjustment.StatusPendingApproval} adjustments can be cancelled.");
        }

        var oldStatus = adjustment.Status;
        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusCancelled;
        adjustment.CancelledBy = cancelledBy;
        adjustment.CancelledAt = now;
        adjustment.UpdatedAt = now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} cancelled by user {UserId}",
            adjustment.AdjustmentNumber,
            cancelledBy);

        var cancelled = await GetByIdAsync(id);
        await TryRecordAuditAsync(
            BuildLifecycleAudit(
                AuditActionType.Cancel,
                AuditOperationType.Update,
                cancelled,
                $"Stock adjustment {cancelled.AdjustmentNumber} cancelled",
                cancelledBy,
                [
                    ("Status", oldStatus, StockAdjustment.StatusCancelled),
                    ("CancelledAt", null, cancelled.CancelledAt)
                ]),
            "cancel stock adjustment");

        return cancelled;
    }

    private async Task<StockAdjustmentDto> MapToDtoAsync(StockAdjustment adjustment)
    {
        var locationName = await ResolveLocationNameAsync(adjustment.LocationId, adjustment.LocationType);
        var lines = adjustment.Lines
            .OrderBy(line => line.Id)
            .Select(line => new StockAdjustmentLineDto
            {
                Id = line.Id,
                VariantId = line.VariantId,
                ProductName = line.Variant?.Product?.Name ?? string.Empty,
                VariantSku = line.Variant?.Sku ?? string.Empty,
                ProductCode = line.Variant?.Product?.ProductCode ?? string.Empty,
                Barcode = line.Variant?.Barcode ?? line.Variant?.Product?.Barcode ?? string.Empty,
                PreviousQuantity = line.PreviousQuantity,
                QuantityChange = line.QuantityChange,
                NewQuantity = line.NewQuantity,
                Reason = line.Reason,
                Notes = line.Notes
            })
            .ToList();

        var firstLine = lines.FirstOrDefault();
        var totalIncrease = lines.Where(line => line.QuantityChange > 0).Sum(line => line.QuantityChange);
        var totalDecrease = lines.Where(line => line.QuantityChange < 0).Sum(line => Math.Abs(line.QuantityChange));

        return new StockAdjustmentDto
        {
            Id = adjustment.Id,
            AdjustmentNumber = adjustment.AdjustmentNumber,
            Status = adjustment.Status,
            LocationId = adjustment.LocationId,
            LocationType = adjustment.LocationType,
            LocationName = locationName,
            VariantId = firstLine?.VariantId ?? 0,
            VariantSku = firstLine?.VariantSku ?? string.Empty,
            ProductName = firstLine?.ProductName ?? string.Empty,
            PreviousQuantity = firstLine?.PreviousQuantity ?? 0,
            QuantityChange = firstLine?.QuantityChange ?? 0,
            NewQuantity = firstLine?.NewQuantity ?? 0,
            Reason = firstLine?.Reason ?? string.Empty,
            Notes = firstLine?.Notes,
            LineCount = lines.Count,
            TotalIncrease = totalIncrease,
            TotalDecrease = totalDecrease,
            NetQuantityChange = totalIncrease - totalDecrease,
            Lines = lines,
            AdjustedBy = adjustment.AdjustedBy,
            AdjusterName = adjustment.Adjuster?.Name ?? string.Empty,
            AdjustmentDate = adjustment.AdjustmentDate,
            CreatedAt = adjustment.CreatedAt,
            UpdatedAt = adjustment.UpdatedAt,
            SubmittedAt = adjustment.SubmittedAt,
            ApprovedBy = adjustment.ApprovedBy,
            ApprovedByName = adjustment.Approver?.Name,
            ApprovedAt = adjustment.ApprovedAt,
            RejectedBy = adjustment.RejectedBy,
            RejectedByName = adjustment.Rejector?.Name,
            RejectedAt = adjustment.RejectedAt,
            RejectionReason = adjustment.RejectionReason,
            CancelledBy = adjustment.CancelledBy,
            CancelledByName = adjustment.Canceller?.Name,
            CancelledAt = adjustment.CancelledAt
        };
    }

    private AuditEventInput BuildApproveAudit(StockAdjustmentDto adjustment, long approvedBy)
    {
        return BuildLifecycleAudit(
            AuditActionType.Approve,
            AuditOperationType.Update,
            adjustment,
            $"Stock adjustment {adjustment.AdjustmentNumber} approved and inventory updated",
            approvedBy,
            [
                ("Status", StockAdjustment.StatusPendingApproval, StockAdjustment.StatusApproved),
                ("ApprovedAt", null, adjustment.ApprovedAt)
            ],
            adjustment.Lines.Select(line => new AuditEntityInput(
                "Inventory",
                $"{adjustment.LocationType}:{adjustment.LocationId}:variant:{line.VariantId}",
                AuditOperationType.Update,
                [
                    ("Quantity", line.PreviousQuantity, line.NewQuantity)
                ],
                metadata: $"{{\"locationId\":{adjustment.LocationId},\"locationType\":\"{adjustment.LocationType}\",\"variantId\":{line.VariantId},\"adjustmentLineId\":{line.Id}}}")
            ).ToList());
    }

    private AuditEventInput BuildLifecycleAudit(
        string actionType,
        string operationType,
        StockAdjustmentDto adjustment,
        string summary,
        long actorUserId,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields,
        IReadOnlyList<AuditEntityInput>? additionalEntities = null)
    {
        var entities = new List<AuditEntityInput>
        {
            new(
                "StockAdjustment",
                adjustment.Id.ToString(),
                operationType,
                fields,
                metadata: $"{{\"adjustmentNumber\":\"{adjustment.AdjustmentNumber}\",\"status\":\"{adjustment.Status}\",\"locationId\":{adjustment.LocationId},\"locationType\":\"{adjustment.LocationType}\",\"locationName\":\"{EscapeJson(adjustment.LocationName)}\",\"lineCount\":{adjustment.LineCount},\"netQuantityChange\":{adjustment.NetQuantityChange},\"actorUserId\":{actorUserId}}}")
        };

        foreach (var line in adjustment.Lines)
        {
            entities.Add(new AuditEntityInput(
                "StockAdjustmentLine",
                line.Id.ToString(),
                operationType,
                [
                    ("VariantId", null, line.VariantId),
                    ("PreviousQuantity", null, line.PreviousQuantity),
                    ("AdjustmentQuantity", null, line.QuantityChange),
                    ("NewQuantity", null, line.NewQuantity),
                    ("Reason", null, line.Reason),
                    ("Notes", null, line.Notes)
                ],
                metadata: $"{{\"variantSku\":\"{EscapeJson(line.VariantSku)}\",\"productName\":\"{EscapeJson(line.ProductName)}\",\"productCode\":\"{EscapeJson(line.ProductCode)}\"}}"));
        }

        if (additionalEntities is { Count: > 0 })
        {
            entities.AddRange(additionalEntities);
        }

        return new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Inventory,
            Summary = summary,
            PrimaryEntity = ("StockAdjustment", adjustment.Id.ToString()),
            Entities = entities,
            Metadata = new Dictionary<string, object?>
            {
                ["adjustmentNumber"] = adjustment.AdjustmentNumber,
                ["status"] = adjustment.Status,
                ["userId"] = adjustment.AdjustedBy,
                ["userName"] = adjustment.AdjusterName,
                ["userRole"] = ResolveRoleNameForAudit(adjustment, actorUserId),
                ["locationId"] = adjustment.LocationId,
                ["locationType"] = adjustment.LocationType,
                ["locationName"] = adjustment.LocationName,
                ["lineCount"] = adjustment.LineCount,
                ["totalIncrease"] = adjustment.TotalIncrease,
                ["totalDecrease"] = adjustment.TotalDecrease,
                ["netQuantityChange"] = adjustment.NetQuantityChange,
                ["lines"] = adjustment.Lines.Select(line => new Dictionary<string, object?>
                {
                    ["variantId"] = line.VariantId,
                    ["variantSku"] = line.VariantSku,
                    ["productName"] = line.ProductName,
                    ["productCode"] = line.ProductCode,
                    ["previousQuantity"] = line.PreviousQuantity,
                    ["adjustmentQuantity"] = line.QuantityChange,
                    ["newQuantity"] = line.NewQuantity,
                    ["reason"] = line.Reason,
                    ["notes"] = line.Notes,
                }).ToList(),
                ["timestamp"] = adjustment.UpdatedAt == default ? adjustment.AdjustmentDate : adjustment.UpdatedAt,
                ["approvedBy"] = adjustment.ApprovedBy,
                ["approvedByName"] = adjustment.ApprovedByName,
                ["approvedAt"] = adjustment.ApprovedAt,
                ["rejectedBy"] = adjustment.RejectedBy,
                ["rejectedByName"] = adjustment.RejectedByName,
                ["rejectedAt"] = adjustment.RejectedAt,
                ["rejectionReason"] = adjustment.RejectionReason,
                ["cancelledBy"] = adjustment.CancelledBy,
                ["cancelledByName"] = adjustment.CancelledByName,
                ["cancelledAt"] = adjustment.CancelledAt,
            }
        };
    }

    private static List<(string FieldName, object? OldValue, object? NewValue)> BuildUpdateFields(StockAdjustmentDto before, StockAdjustmentDto after)
    {
        return
        [
            ("LocationId", before.LocationId, after.LocationId),
            ("LocationType", before.LocationType, after.LocationType),
            ("LineCount", before.LineCount, after.LineCount),
            ("NetQuantityChange", before.NetQuantityChange, after.NetQuantityChange),
            ("UpdatedAt", before.UpdatedAt, after.UpdatedAt)
        ];
    }

    private async Task TryRecordAuditAsync(AuditEventInput input, string operation)
    {
        try
        {
            await _auditService.RecordAsync(input);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to audit {Operation}", operation);
        }
    }

    private static string? ResolveRoleNameForAudit(StockAdjustmentDto adjustment, long actorUserId)
    {
        if (adjustment.ApprovedBy == actorUserId && !string.IsNullOrWhiteSpace(adjustment.ApprovedByName))
            return adjustment.ApprovedByName;

        if (adjustment.RejectedBy == actorUserId && !string.IsNullOrWhiteSpace(adjustment.RejectedByName))
            return adjustment.RejectedByName;

        if (adjustment.CancelledBy == actorUserId && !string.IsNullOrWhiteSpace(adjustment.CancelledByName))
            return adjustment.CancelledByName;

        return adjustment.AdjusterName;
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ToJsonString(string? value)
    {
        return value == null ? "null" : $"\"{EscapeJson(value)}\"";
    }

    private async Task<StockAdjustment> GetTrackedAdjustmentAsync(long id, bool includeLines = false)
    {
        IQueryable<StockAdjustment> query = _context.StockAdjustments;
        if (includeLines)
        {
            query = query.Include(sa => sa.Lines);
        }

        var adjustment = await query.FirstOrDefaultAsync(sa => sa.Id == id)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");

        return adjustment;
    }

    private static void ValidateReason(string reason)
    {
        if (!ValidReasons.Contains(reason))
            throw new InvalidOperationException(
                $"Invalid reason '{reason}'. Valid reasons are: {string.Join(", ", ValidReasons)}");
    }

    private async Task<List<CreateStockAdjustmentLineDto>> NormalizeItemsAsync(
        string? defaultReason,
        string? defaultNotes,
        List<CreateStockAdjustmentLineDto> items)
    {
        if (items == null || items.Count == 0)
            throw new InvalidOperationException("At least one adjustment line is required.");

        if (items.Any(item => item.QuantityChange == 0))
            throw new InvalidOperationException("Quantity change cannot be 0 for any line.");

        var duplicateVariantIds = items
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateVariantIds.Count > 0)
            throw new InvalidOperationException($"Duplicate variants are not allowed: {string.Join(", ", duplicateVariantIds)}");

        var normalized = items.Select(item => new CreateStockAdjustmentLineDto
        {
            VariantId = item.VariantId,
            QuantityChange = item.QuantityChange,
            Reason = string.IsNullOrWhiteSpace(item.Reason) ? (defaultReason ?? string.Empty) : item.Reason,
            Notes = string.IsNullOrWhiteSpace(item.Notes) ? defaultNotes : item.Notes,
        }).ToList();

        foreach (var item in normalized)
        {
            ValidateReason(item.Reason!);
            await EnsureVariantExistsAsync(item.VariantId);
        }

        return normalized;
    }

    private static string NormalizeCreateAction(string? action)
    {
        var normalized = string.IsNullOrWhiteSpace(action) ? StockAdjustmentCreateActions.Draft : action.Trim();
        if (!ValidCreateActions.Contains(normalized))
        {
            throw new InvalidOperationException(
                $"Invalid create action '{action}'. Valid actions are: {string.Join(", ", ValidCreateActions)}");
        }

        return normalized;
    }

    private async Task ApplyApprovalAsync(StockAdjustment adjustment, long approvedBy, DateTime now)
    {
        foreach (var line in adjustment.Lines)
        {
            var inventory = await _context.Inventories.FirstOrDefaultAsync(i =>
                i.VariantId == line.VariantId &&
                i.LocationId == adjustment.LocationId &&
                i.LocationType == adjustment.LocationType);

            var currentQty = inventory?.Quantity ?? 0;
            var newQty = currentQty + line.QuantityChange;
            await ValidateProjectedQuantityAsync(currentQty, line.QuantityChange, line.VariantId);

            if (inventory == null)
            {
                inventory = new Inventory
                {
                    VariantId = line.VariantId,
                    LocationId = adjustment.LocationId,
                    LocationType = adjustment.LocationType,
                    Quantity = newQty
                };
                _context.Inventories.Add(inventory);
            }
            else
            {
                inventory.Quantity = newQty;
            }

            line.PreviousQuantity = currentQty;
            line.NewQuantity = newQty;
            line.UpdatedAt = now;

            _stockLedger.WriteEntry(
                variantId: line.VariantId,
                locationId: adjustment.LocationId,
                locationType: adjustment.LocationType,
                transactionType: StockLedgerTransactionType.Adjustment,
                qtyIn: line.QuantityChange > 0 ? line.QuantityChange : 0,
                qtyOut: line.QuantityChange < 0 ? Math.Abs(line.QuantityChange) : 0,
                balanceAfter: newQty,
                referenceType: StockLedgerReferenceType.StockAdjustment,
                referenceId: adjustment.Id,
                remarks: line.Reason,
                createdBy: approvedBy);
        }

        adjustment.Status = StockAdjustment.StatusApproved;
        adjustment.ApprovedBy = approvedBy;
        adjustment.ApprovedAt = now;
        adjustment.UpdatedAt = now;
    }

    private async Task EnsureVariantExistsAsync(long variantId)
    {
        var variant = await _variantRepository.GetByIdAsync(variantId);
        if (variant == null)
            throw new KeyNotFoundException($"Product variant with ID {variantId} not found");
    }

    private async Task ValidateProjectedQuantityAsync(int currentQty, int quantityChange, long variantId)
    {
        var projectedQty = currentQty + quantityChange;
        var inventorySettings = await _settingsService.GetInventorySettingsAsync();
        if (!inventorySettings.AllowNegativeStock && projectedQty < 0)
        {
            throw new InvalidOperationException(
                $"Adjustment would result in negative stock for variant {variantId}. Current: {currentQty}, Change: {quantityChange}, Result: {projectedQty}");
        }
    }

    private static void EnsureCurrentStatus(StockAdjustment adjustment, string expectedStatus, string action)
    {
        if (!string.Equals(adjustment.Status, expectedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Only {expectedStatus} adjustments can be {action}d. Current status: {adjustment.Status}.");
        }
    }

    private async Task<string> GenerateAdjustmentNumberAsync()
    {
        var maxId = await _context.StockAdjustments
            .AsNoTracking()
            .Select(adjustment => (long?)adjustment.Id)
            .MaxAsync() ?? 0;

        return $"{AdjustmentNumberPrefix}-{DateTime.UtcNow:yyyy}-{(maxId + 1):D5}";
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
