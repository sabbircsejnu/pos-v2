using System.Text.Json;
using MediatR;
using RetailPOS.Application.Sales;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Commands;

public sealed record HoldSaleItem(
    long VariantId,
    string ProductName,
    string VariantSku,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string? AppliedRuleName);

public sealed record HoldSaleCommand(
    long OutletId,
    long CashierId,
    long? CustomerId,
    IReadOnlyList<HoldSaleItem> Items,
    decimal DiscountPercent,
    string PaymentMethod,
    string? Note) : IRequest<HeldSaleReadModel>;

public sealed class HoldSaleCommandHandler(
    IHeldSaleStore store,
    IHeldSaleOutletAuthorizer outletAuthorizer) : IRequestHandler<HoldSaleCommand, HeldSaleReadModel>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HeldSaleReadModel> Handle(
        HoldSaleCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Items.Count == 0)
            throw new InvalidOperationException("Cannot hold a sale with no items");

        var outletId = await outletAuthorizer.AuthorizeWriteAsync(request.OutletId, cancellationToken);
        if (request.CustomerId.HasValue &&
            !await store.CustomerBelongsToOutletBusinessAsync(request.CustomerId.Value, outletId, cancellationToken))
            throw new KeyNotFoundException($"Customer with ID {request.CustomerId.Value} not found");

        var heldSale = await store.CreateAsync(
            new HeldSale
            {
                OutletId = outletId,
                CashierId = request.CashierId,
                CustomerId = request.CustomerId,
                ItemsJson = JsonSerializer.Serialize(request.Items, JsonOptions),
                DiscountPercent = request.DiscountPercent,
                PaymentMethod = request.PaymentMethod.ToLower(),
                Note = request.Note
            },
            cancellationToken);

        return HeldSaleMapper.ToReadModel(heldSale);
    }
}

public sealed record DeleteHeldSaleCommand(long Id) : IRequest;

public sealed class DeleteHeldSaleCommandHandler(
    IHeldSaleStore store,
    IHeldSaleOutletAuthorizer outletAuthorizer) : IRequestHandler<DeleteHeldSaleCommand>
{
    public async Task Handle(DeleteHeldSaleCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outletId = await store.GetOutletIdAsync(request.Id, cancellationToken);
        if (outletId is null)
            return;

        await outletAuthorizer.AuthorizeWriteAsync(outletId.Value, cancellationToken);
        await store.DeleteAsync(request.Id, cancellationToken);
    }
}
