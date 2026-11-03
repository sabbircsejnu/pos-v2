using MediatR;
using RetailPOS.Application.Suppliers.Abstractions;

namespace RetailPOS.Application.Suppliers.Commands;

public sealed record CreateSupplierCommand(SupplierWriteModel Supplier)
    : IRequest<SupplierReadModel>;

public sealed class CreateSupplierCommandHandler(
    ISupplierWriteStore writeStore,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<CreateSupplierCommand, SupplierReadModel>
{
    public async Task<SupplierReadModel> Handle(
        CreateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        if (await writeStore.NameExistsAsync(
                request.Supplier.Name,
                businessId,
                null,
                cancellationToken))
            throw new InvalidOperationException(
                $"Supplier with name '{request.Supplier.Name}' already exists");

        return await writeStore.CreateAsync(request.Supplier, businessId, cancellationToken);
    }
}

public sealed record UpdateSupplierCommand(long SupplierId, SupplierWriteModel Supplier)
    : IRequest<SupplierReadModel>;

public sealed class UpdateSupplierCommandHandler(
    ISupplierReadStore readStore,
    ISupplierWriteStore writeStore,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<UpdateSupplierCommand, SupplierReadModel>
{
    public async Task<SupplierReadModel> Handle(
        UpdateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        if (await readStore.GetByIdAsync(request.SupplierId, businessId, cancellationToken) is null)
            throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");

        if (await writeStore.NameExistsAsync(
                request.Supplier.Name,
                businessId,
                request.SupplierId,
                cancellationToken))
            throw new InvalidOperationException(
                $"Supplier with name '{request.Supplier.Name}' already exists");

        return await writeStore.UpdateAsync(
                request.SupplierId,
                request.Supplier,
                businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");
    }
}

public sealed record DeleteSupplierCommand(long SupplierId) : IRequest<SupplierReadModel>;

public sealed class DeleteSupplierCommandHandler(
    ISupplierReadStore readStore,
    ISupplierWriteStore writeStore,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<DeleteSupplierCommand, SupplierReadModel>
{
    public async Task<SupplierReadModel> Handle(
        DeleteSupplierCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var supplier = await readStore.GetByIdAsync(request.SupplierId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");

        var purchaseOrderCount = await writeStore.GetPurchaseOrderCountAsync(
            request.SupplierId,
            businessId,
            cancellationToken);
        var billCount = await writeStore.GetBillCountAsync(
            request.SupplierId,
            businessId,
            cancellationToken);
        if (purchaseOrderCount > 0 || billCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete supplier '{supplier.Name}'. It has {purchaseOrderCount} purchase orders and {billCount} bills associated with it.");

        if (!await writeStore.DeleteAsync(request.SupplierId, businessId, cancellationToken))
            throw new Exception("Failed to delete supplier");

        return supplier;
    }
}
