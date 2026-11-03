using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record CreateStockRequisitionCommand(
    StockRequisitionWriteInput Input,
    long RequestedBy)
    : ICommand<StockRequisitionReadModel>;

public sealed class CreateStockRequisitionCommandHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionWriteStore writeStore,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<CreateStockRequisitionCommand, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        CreateStockRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var input = NormalizeAndValidate(request.Input);
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var id = await writeStore.CreateAsync(
            input,
            businessId,
            request.RequestedBy,
            cancellationToken);
        return await readStore.GetByIdAsync(id, businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Created stock requisition {id} could not be read in its Business scope.");
    }

    internal static StockRequisitionWriteInput NormalizeAndValidate(
        StockRequisitionWriteInput input)
    {
        if (input.Lines is null || input.Lines.Count == 0)
            throw new InvalidOperationException("Stock requisition must have at least one line.");

        var requestingType = NormalizeLocationType(input.RequestingLocationType);
        var sourceType = NormalizeLocationType(input.SourceLocationType);
        if (input.RequestingLocationId == input.SourceLocationId
            && requestingType == sourceType)
        {
            throw new InvalidOperationException(
                "Requesting and source locations cannot be the same.");
        }

        if (input.Lines.Any(line => line.RequestedQuantity <= 0))
            throw new InvalidOperationException(
                "All requested quantities must be greater than zero.");

        return input with
        {
            RequestingLocationType = requestingType,
            SourceLocationType = sourceType
        };
    }

    private static string NormalizeLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }
}

public sealed record UpdateStockRequisitionCommand(
    long Id,
    StockRequisitionWriteInput Input,
    long UpdatedBy)
    : ICommand<StockRequisitionReadModel>;

public sealed class UpdateStockRequisitionCommandHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionWriteStore writeStore,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<UpdateStockRequisitionCommand, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        UpdateStockRequisitionCommand request,
        CancellationToken cancellationToken)
    {
        var input = CreateStockRequisitionCommandHandler.NormalizeAndValidate(request.Input);
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        await writeStore.UpdateAsync(
            request.Id,
            input,
            businessId,
            request.UpdatedBy,
            cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Updated stock requisition {request.Id} could not be read in its Business scope.");
    }
}
