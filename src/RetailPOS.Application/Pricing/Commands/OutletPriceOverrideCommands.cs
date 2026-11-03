using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Commands;

public sealed record CreateOutletPriceOverrideCommand(OutletPriceOverrideWriteData PriceOverride)
    : ICommand<OutletPriceOverrideReadModel>;

public sealed class CreateOutletPriceOverrideCommandHandler(
    IOutletPriceOverrideCommandStore store,
    IOutletPriceOverrideWriteAuthorizer outletAuthorizer)
    : IRequestHandler<CreateOutletPriceOverrideCommand, OutletPriceOverrideReadModel>
{
    private static readonly HashSet<string> ValidOverrideTypes =
        new(StringComparer.OrdinalIgnoreCase) { "fixed", "margin" };

    public async Task<OutletPriceOverrideReadModel> Handle(
        CreateOutletPriceOverrideCommand request,
        CancellationToken cancellationToken)
    {
        var priceOverride = request.PriceOverride;
        ValidateOverrideType(priceOverride.OverrideType);
        var authorizedOutletId = await outletAuthorizer.AuthorizeWriteAsync(
            priceOverride.OutletId,
            cancellationToken);
        priceOverride = priceOverride with { OutletId = authorizedOutletId };

        if (!await store.OutletExistsAsync(priceOverride.OutletId, cancellationToken))
        {
            throw new KeyNotFoundException($"Outlet with ID {priceOverride.OutletId} not found");
        }

        if (!await store.ProductVariantExistsAsync(
                priceOverride.ProductVariantId,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"ProductVariant with ID {priceOverride.ProductVariantId} not found");
        }

        if (await store.ExistsAsync(
                priceOverride.OutletId,
                priceOverride.ProductVariantId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"An override already exists for outlet {priceOverride.OutletId} and variant {priceOverride.ProductVariantId}. " +
                "Update the existing override instead.");
        }

        return await store.CreateAsync(
            priceOverride with { OverrideType = priceOverride.OverrideType.ToLower() },
            cancellationToken)
            ?? throw new InvalidOperationException("Failed to reload created outlet price override");
    }

    internal static void ValidateOverrideType(string overrideType)
    {
        if (!ValidOverrideTypes.Contains(overrideType))
        {
            throw new InvalidOperationException(
                $"Invalid OverrideType '{overrideType}'. Must be 'fixed' or 'margin'");
        }
    }
}

public sealed record UpdateOutletPriceOverrideCommand(
    long Id,
    string OverrideType,
    decimal OverrideValue,
    bool IsActive) : ICommand<OutletPriceOverrideReadModel>;

public sealed class UpdateOutletPriceOverrideCommandHandler(
    IOutletPriceOverrideCommandStore store,
    IOutletPriceOverrideWriteAuthorizer outletAuthorizer)
    : IRequestHandler<UpdateOutletPriceOverrideCommand, OutletPriceOverrideReadModel>
{
    public async Task<OutletPriceOverrideReadModel> Handle(
        UpdateOutletPriceOverrideCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Outlet price override with ID {request.Id} not found");

        _ = await outletAuthorizer.AuthorizeWriteAsync(existing.OutletId, cancellationToken);
        CreateOutletPriceOverrideCommandHandler.ValidateOverrideType(request.OverrideType);

        return await store.UpdateAsync(
            request.Id,
            new OutletPriceOverrideUpdateData(
                request.OverrideType.ToLower(),
                request.OverrideValue,
                request.IsActive),
            cancellationToken)
            ?? throw new InvalidOperationException("Failed to reload updated outlet price override");
    }
}

public sealed record DeleteOutletPriceOverrideCommand(long Id) : ICommand<bool>;

public sealed class DeleteOutletPriceOverrideCommandHandler(
    IOutletPriceOverrideCommandStore store,
    IOutletPriceOverrideWriteAuthorizer outletAuthorizer)
    : IRequestHandler<DeleteOutletPriceOverrideCommand, bool>
{
    public async Task<bool> Handle(
        DeleteOutletPriceOverrideCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Outlet price override with ID {request.Id} not found");

        _ = await outletAuthorizer.AuthorizeWriteAsync(existing.OutletId, cancellationToken);
        _ = await store.DeleteAsync(request.Id, cancellationToken);
        return true;
    }
}
