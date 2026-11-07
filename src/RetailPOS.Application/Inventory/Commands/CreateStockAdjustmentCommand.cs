using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record CreateStockAdjustmentCommand(
    bool SubmitForApproval,
    long LocationId,
    string LocationType,
    long AdjustedBy,
    string? DefaultReason,
    string? DefaultNotes,
    IReadOnlyList<StockAdjustmentCreateLine> Items,
    bool ApproveImmediately = false,
    string? Action = null)
    : ICommand<StockAdjustmentReadModel>;

public sealed class CreateStockAdjustmentCommandHandler(
    IStockAdjustmentCreateContextProvider contextProvider,
    IStockAdjustmentCreationStore store,
    IStockAdjustmentApprovalStore approvalStore,
    IStockAdjustmentReadStore readStore,
    IStockAdjustmentAuditWriter auditWriter,
    RetailPOS.Application.Uom.IVariantUomReader? uomReader = null,
    RetailPOS.Application.Uom.IUomOperationScope? uomScope = null)
    : IRequestHandler<CreateStockAdjustmentCommand, StockAdjustmentReadModel>
{
    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Submit",
        "SubmitAndApprove"
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

    public async Task<StockAdjustmentReadModel> Handle(
        CreateStockAdjustmentCommand request,
        CancellationToken cancellationToken)
    {
        if (uomScope is not null)
            request = request with
            {
                Items = await RetailPOS.Application.Uom.InventoryUomRules.ResolveAdjustmentLinesAsync(
                    request.Items, uomScope.RequireBusinessId(), uomReader, cancellationToken)
            };
        var input = Normalize(request);
        var context = await contextProvider.GetAsync(cancellationToken);
        var approval = request.ApproveImmediately
            ? await approvalStore.CreateAndApproveAsync(input, context, cancellationToken)
            : null;
        var id = approval?.AdjustmentId
            ?? await store.CreateAsync(input, context, cancellationToken);
        var adjustment = await readStore.GetByIdAsync(
                id,
                context.ReadScope,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {id} not found");
        await auditWriter.RecordCreatedAsync(
            adjustment,
            request.SubmitForApproval && !request.ApproveImmediately,
            cancellationToken);
        if (approval is not null)
        {
            await auditWriter.RecordApprovedAsync(
                adjustment,
                request.AdjustedBy,
                approval.JournalEntryId,
                cancellationToken);
        }

        return adjustment;
    }

    private static StockAdjustmentCreateInput Normalize(CreateStockAdjustmentCommand request)
    {
        var action = string.IsNullOrWhiteSpace(request.Action) ? "Draft" : request.Action.Trim();
        if (!ValidActions.Contains(action))
        {
            throw new InvalidOperationException(
                $"Invalid create action '{request.Action}'. Valid actions are: {string.Join(", ", ValidActions)}");
        }

        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one adjustment line is required.");
        }

        if (request.Items.Any(item => item.QuantityChange == 0))
        {
            throw new InvalidOperationException("Quantity change cannot be 0 for any line.");
        }

        var duplicates = request.Items
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate variants are not allowed: {string.Join(", ", duplicates)}");
        }

        var items = request.Items.Select(item =>
        {
            var reason = string.IsNullOrWhiteSpace(item.Reason)
                ? request.DefaultReason ?? string.Empty
                : item.Reason;
            if (!ValidReasons.Contains(reason))
            {
                throw new InvalidOperationException(
                    $"Invalid reason '{reason}'. Valid reasons are: {string.Join(", ", ValidReasons)}");
            }

            return item with { Reason = reason, Notes = string.IsNullOrWhiteSpace(item.Notes) ? request.DefaultNotes : item.Notes };
        }).ToArray();

        return new StockAdjustmentCreateInput(
            request.SubmitForApproval,
            request.ApproveImmediately,
            request.LocationId,
            request.LocationType,
            request.AdjustedBy,
            items);
    }
}
