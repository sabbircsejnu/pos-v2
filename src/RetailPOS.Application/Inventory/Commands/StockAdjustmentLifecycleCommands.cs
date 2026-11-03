using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record ReverseStockAdjustmentCommand(
    long AdjustmentId,
    long ReversedBy,
    string Reason)
    : ICommand<StockAdjustmentReadModel>;

public sealed class ReverseStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentReversalStore reversalStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter)
    : IRequestHandler<ReverseStockAdjustmentCommand, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        ReverseStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidOperationException("Reversal reason is required.");
        }

        var context = await contextProvider.GetAsync(cancellationToken);
        _ = await readStore.GetByIdAsync(
                request.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {request.AdjustmentId} not found");
        var result = await reversalStore.ReverseAsync(
            request.AdjustmentId,
            request.ReversedBy,
            request.Reason,
            context,
            cancellationToken);
        var adjustment = await readStore.GetByIdAsync(
                result.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {result.AdjustmentId} not found");
        await auditWriter.RecordReversedAsync(
            adjustment,
            request.Reason.Trim(),
            request.ReversedBy,
            result.OriginalJournalEntryId,
            result.ReversalJournalEntryId,
            cancellationToken);
        return adjustment;
    }
}

public sealed record UpdateStockAdjustmentDraftLine(
    long VariantId,
    int QuantityChange,
    string? Reason,
    string? Notes);

public sealed record UpdateStockAdjustmentDraftCommand(
    long AdjustmentId,
    long UpdatedBy,
    long LocationId,
    string LocationType,
    IReadOnlyList<UpdateStockAdjustmentDraftLine> Items)
    : ICommand<StockAdjustmentReadModel>;

public sealed class UpdateStockAdjustmentDraftCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentLifecycleStore lifecycleStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter)
    : IRequestHandler<UpdateStockAdjustmentDraftCommand, StockAdjustmentReadModel>
{
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

    public async Task<StockAdjustmentReadModel> Handle(
        UpdateStockAdjustmentDraftCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(cancellationToken);
        var before = await readStore.GetByIdAsync(
                request.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {request.AdjustmentId} not found");
        EnsureDraft(before);
        var input = Normalize(request);
        var id = await lifecycleStore.UpdateDraftAsync(
            request.AdjustmentId,
            input,
            request.UpdatedBy,
            context,
            cancellationToken);
        var after = await readStore.GetByIdAsync(id, context.ReadScope, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");
        await auditWriter.RecordUpdatedAsync(
            before,
            after,
            request.UpdatedBy,
            cancellationToken);
        return after;
    }

    private static StockAdjustmentUpdateInput Normalize(
        UpdateStockAdjustmentDraftCommand request)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one adjustment line is required.");
        }

        if (request.Items.Any(item => item.QuantityChange == 0))
        {
            throw new InvalidOperationException(
                "Quantity change cannot be 0 for any line.");
        }

        var duplicateVariantIds = request.Items
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateVariantIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate variants are not allowed: {string.Join(", ", duplicateVariantIds)}");
        }

        var items = request.Items.Select(item =>
        {
            var reason = item.Reason ?? string.Empty;
            if (!ValidReasons.Contains(reason))
            {
                throw new InvalidOperationException(
                    $"Invalid reason '{reason}'. Valid reasons are: {string.Join(", ", ValidReasons)}");
            }

            return new StockAdjustmentCreateLine(
                item.VariantId,
                item.QuantityChange,
                reason,
                string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes);
        }).ToArray();

        return new StockAdjustmentUpdateInput(
            request.LocationId,
            request.LocationType,
            items);
    }

    private static void EnsureDraft(StockAdjustmentReadModel adjustment)
    {
        if (!string.Equals(
                adjustment.Status,
                RetailPOS.Core.Entities.StockAdjustment.StatusDraft,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Only {RetailPOS.Core.Entities.StockAdjustment.StatusDraft} adjustments can be edited. Current status: {adjustment.Status}.");
        }
    }
}

public sealed record DeleteStockAdjustmentDraftCommand(
    long AdjustmentId,
    long DeletedBy)
    : ICommand<Unit>;

public sealed class DeleteStockAdjustmentDraftCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentLifecycleStore lifecycleStore,
    IStockAdjustmentReadStore readStore)
    : IRequestHandler<DeleteStockAdjustmentDraftCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteStockAdjustmentDraftCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(cancellationToken);
        _ = await readStore.GetByIdAsync(
                request.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {request.AdjustmentId} not found");
        await lifecycleStore.DeleteDraftAsync(
            request.AdjustmentId,
            request.DeletedBy,
            context,
            cancellationToken);
        return Unit.Value;
    }
}

public sealed record CancelStockAdjustmentCommand(
    long AdjustmentId,
    long CancelledBy)
    : ICommand<StockAdjustmentReadModel>;

public sealed class CancelStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentLifecycleStore lifecycleStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter)
    : IRequestHandler<CancelStockAdjustmentCommand, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        CancelStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(cancellationToken);
        _ = await readStore.GetByIdAsync(
                request.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {request.AdjustmentId} not found");
        var previousStatus = await lifecycleStore.CancelAsync(
            request.AdjustmentId,
            request.CancelledBy,
            context,
            cancellationToken);
        var adjustment = await readStore.GetByIdAsync(
                request.AdjustmentId,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {request.AdjustmentId} not found");
        await auditWriter.RecordCancelledAsync(
            adjustment,
            previousStatus,
            request.CancelledBy,
            cancellationToken);
        return adjustment;
    }
}

public sealed record SubmitStockAdjustmentCommand(
    long AdjustmentId,
    long SubmittedBy)
    : ICommand<StockAdjustmentReadModel>;

public sealed class SubmitStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentLifecycleStore lifecycleStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter)
    : IRequestHandler<SubmitStockAdjustmentCommand, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        SubmitStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(cancellationToken);
        var id = await lifecycleStore.SubmitAsync(
            request.AdjustmentId,
            request.SubmittedBy,
            context,
            cancellationToken);
        var adjustment = await readStore.GetByIdAsync(id, context.ReadScope, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");
        await auditWriter.RecordSubmittedAsync(
            adjustment,
            request.SubmittedBy,
            cancellationToken);
        return adjustment;
    }
}

public sealed record RejectStockAdjustmentCommand(
    long AdjustmentId,
    long RejectedBy,
    string Reason)
    : ICommand<StockAdjustmentReadModel>;

public sealed class RejectStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentLifecycleStore lifecycleStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter)
    : IRequestHandler<RejectStockAdjustmentCommand, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        RejectStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidOperationException("Rejection reason is required.");
        }

        var context = await contextProvider.GetAsync(cancellationToken);
        var id = await lifecycleStore.RejectAsync(
            request.AdjustmentId,
            request.RejectedBy,
            request.Reason,
            context,
            cancellationToken);
        var adjustment = await readStore.GetByIdAsync(id, context.ReadScope, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");
        await auditWriter.RecordRejectedAsync(
            adjustment,
            request.RejectedBy,
            cancellationToken);
        return adjustment;
    }
}
