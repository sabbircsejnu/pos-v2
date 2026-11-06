using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Commands;

public sealed record PurchaseAndReceiveCommand(
    long SupplierId,
    PurchaseOrderDestination Destination,
    DateTime OrderDate,
    DateTime? ExpectedDelivery,
    string? Notes,
    IReadOnlyList<PurchaseAndReceiveItemCommand>? Items,
    string? IdempotencyKey,
    long UserId) : ICommand<PurchaseAndReceiveResultReadModel>;

public sealed record PurchaseAndReceiveItemCommand(
    long VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    string? Unit);

public sealed record PurchaseAndReceiveResultReadModel(
    PurchaseOrderReadModel PurchaseOrder,
    long GrnId,
    bool IsDuplicateRequest);

public sealed class PurchaseAndReceiveCommandHandler(
    IPurchaseAndReceiveStore store,
    IPurchaseOrderReadStore readStore,
    IPurchaseOrderTenantScope tenantScope,
    IGrnReceiptLocationAuthorizer locationAuthorizer)
    : IRequestHandler<PurchaseAndReceiveCommand, PurchaseAndReceiveResultReadModel>
{
    public async Task<PurchaseAndReceiveResultReadModel> Handle(
        PurchaseAndReceiveCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new InvalidOperationException("Idempotency key is required for purchase and receive action");

        await locationAuthorizer.AuthorizeAsync(
            request.Destination.LocationType,
            request.Destination.LocationId,
            cancellationToken);

        var result = await store.ExecuteAsync(
            request with { IdempotencyKey = request.IdempotencyKey.Trim() },
            businessId,
            cancellationToken);
        var purchaseOrder = await readStore.FindByIdAsync(
            result.PurchaseOrderId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order with ID {result.PurchaseOrderId} not found");

        return new PurchaseAndReceiveResultReadModel(
            purchaseOrder,
            result.GrnId,
            result.IsDuplicateRequest);
    }
}
