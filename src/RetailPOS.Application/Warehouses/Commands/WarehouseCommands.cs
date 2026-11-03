using MediatR;
using RetailPOS.Application.Warehouses.Abstractions;

namespace RetailPOS.Application.Warehouses.Commands;

public sealed record CreateWarehouseCommand(
    string Name,
    string Address,
    int? Capacity,
    long? ManagerId) : IRequest<WarehouseReadModel>;

public sealed class CreateWarehouseCommandHandler(
    IWarehouseWriteStore store,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<CreateWarehouseCommand, WarehouseReadModel>
{
    public async Task<WarehouseReadModel> Handle(
        CreateWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        if (await store.ExistsByNameAsync(request.Name, null, businessId, cancellationToken))
            throw new InvalidOperationException($"A warehouse with the name '{request.Name}' already exists");

        if (request.ManagerId.HasValue
            && !await store.ManagerExistsAsync(request.ManagerId.Value, businessId, cancellationToken))
            throw new InvalidOperationException($"Manager with ID {request.ManagerId} not found");

        if (request.Capacity.HasValue && request.Capacity.Value < 0)
            throw new InvalidOperationException("Capacity must be a positive number");

        return await store.CreateAsync(
            request.Name,
            request.Address,
            request.Capacity,
            request.ManagerId,
            businessId,
            cancellationToken);
    }
}

public sealed record UpdateWarehouseCommand(
    long WarehouseId,
    string Name,
    string Address,
    int? Capacity,
    long? ManagerId) : IRequest<WarehouseReadModel>;

public sealed class UpdateWarehouseCommandHandler(
    IWarehouseReadStore readStore,
    IWarehouseWriteStore writeStore,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<UpdateWarehouseCommand, WarehouseReadModel>
{
    public async Task<WarehouseReadModel> Handle(
        UpdateWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var current = await readStore.GetByIdAsync(
            request.WarehouseId,
            businessId,
            cancellationToken);
        if (current is null)
            throw new InvalidOperationException($"Warehouse with ID {request.WarehouseId} not found");

        if (await writeStore.ExistsByNameAsync(
                request.Name,
                request.WarehouseId,
                businessId,
                cancellationToken))
            throw new InvalidOperationException($"A warehouse with the name '{request.Name}' already exists");

        if (request.ManagerId.HasValue
            && !await writeStore.ManagerExistsAsync(request.ManagerId.Value, businessId, cancellationToken))
            throw new InvalidOperationException($"Manager with ID {request.ManagerId} not found");

        if (request.Capacity.HasValue && request.Capacity.Value < 0)
            throw new InvalidOperationException("Capacity must be a positive number");

        var updated = await writeStore.UpdateAsync(
            request.WarehouseId,
            request.Name,
            request.Address,
            request.Capacity,
            request.ManagerId,
            businessId,
            cancellationToken);
        if (updated is null)
            throw new InvalidOperationException($"Warehouse with ID {request.WarehouseId} not found");

        return updated;
    }
}

public sealed record DeleteWarehouseCommand(long WarehouseId) : IRequest<bool>;

public sealed class DeleteWarehouseCommandHandler(
    IWarehouseReadStore readStore,
    IWarehouseWriteStore writeStore,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<DeleteWarehouseCommand, bool>
{
    public async Task<bool> Handle(
        DeleteWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var warehouse = await readStore.GetByIdAsync(
            request.WarehouseId,
            businessId,
            cancellationToken);
        if (warehouse is null)
            return false;

        var purchaseOrderCount = await writeStore.GetPurchaseOrderCountAsync(
            request.WarehouseId,
            businessId,
            cancellationToken);
        if (purchaseOrderCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete warehouse. It has {purchaseOrderCount} purchase order(s)");

        return await writeStore.DeleteAsync(
            request.WarehouseId,
            businessId,
            cancellationToken);
    }
}

public sealed record CreateBusinessWarehouseCommand(
    long BusinessId,
    string Name,
    string Address,
    int? Capacity,
    long? ManagerId) : IRequest<WarehouseReadModel>;

public sealed class CreateBusinessWarehouseCommandHandler(IWarehouseWriteStore store)
    : IRequestHandler<CreateBusinessWarehouseCommand, WarehouseReadModel>
{
    public async Task<WarehouseReadModel> Handle(
        CreateBusinessWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        if (await store.ExistsByNameAsync(
                request.Name,
                null,
                request.BusinessId,
                cancellationToken))
            throw new InvalidOperationException(
                $"A warehouse with the name '{request.Name}' already exists in this business");

        if (request.ManagerId.HasValue
            && !await store.ManagerExistsAsync(
                request.ManagerId.Value,
                request.BusinessId,
                cancellationToken))
            throw new InvalidOperationException(
                $"Manager with ID {request.ManagerId} not found in this business");

        if (request.Capacity.HasValue && request.Capacity.Value < 0)
            throw new InvalidOperationException("Capacity must be a positive number");

        return await store.CreateAsync(
            request.Name,
            request.Address,
            request.Capacity,
            request.ManagerId,
            request.BusinessId,
            cancellationToken);
    }
}

public sealed record UpdateBusinessWarehouseCommand(
    long BusinessId,
    long WarehouseId,
    string Name,
    string Address,
    int? Capacity,
    long? ManagerId) : IRequest<WarehouseReadModel>;

public sealed class UpdateBusinessWarehouseCommandHandler(
    IWarehouseReadStore readStore,
    IWarehouseWriteStore writeStore)
    : IRequestHandler<UpdateBusinessWarehouseCommand, WarehouseReadModel>
{
    public async Task<WarehouseReadModel> Handle(
        UpdateBusinessWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        var current = await readStore.GetByIdAsync(
            request.WarehouseId,
            request.BusinessId,
            cancellationToken);
        if (current is null)
            throw new InvalidOperationException(
                $"Warehouse {request.WarehouseId} not found in business {request.BusinessId}");

        if (await writeStore.ExistsByNameAsync(
                request.Name,
                request.WarehouseId,
                request.BusinessId,
                cancellationToken))
            throw new InvalidOperationException(
                $"A warehouse with the name '{request.Name}' already exists in this business");

        if (request.ManagerId.HasValue
            && !await writeStore.ManagerExistsAsync(
                request.ManagerId.Value,
                request.BusinessId,
                cancellationToken))
            throw new InvalidOperationException(
                $"Manager with ID {request.ManagerId} not found in this business");

        if (request.Capacity.HasValue && request.Capacity.Value < 0)
            throw new InvalidOperationException("Capacity must be a positive number");

        var updated = await writeStore.UpdateAsync(
            request.WarehouseId,
            request.Name,
            request.Address,
            request.Capacity,
            request.ManagerId,
            request.BusinessId,
            cancellationToken);
        return updated
            ?? throw new InvalidOperationException(
                $"Warehouse {request.WarehouseId} not found in business {request.BusinessId}");
    }
}

public sealed record DeleteBusinessWarehouseCommand(long BusinessId, long WarehouseId)
    : IRequest<bool>;

public sealed class DeleteBusinessWarehouseCommandHandler(
    IWarehouseReadStore readStore,
    IWarehouseWriteStore writeStore)
    : IRequestHandler<DeleteBusinessWarehouseCommand, bool>
{
    public async Task<bool> Handle(
        DeleteBusinessWarehouseCommand request,
        CancellationToken cancellationToken)
    {
        var warehouse = await readStore.GetByIdAsync(
            request.WarehouseId,
            request.BusinessId,
            cancellationToken);
        if (warehouse is null)
            return false;

        var purchaseOrderCount = await writeStore.GetPurchaseOrderCountAsync(
            request.WarehouseId,
            request.BusinessId,
            cancellationToken);
        if (purchaseOrderCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete warehouse. It has {purchaseOrderCount} purchase order(s)");

        return await writeStore.DeleteAsync(
            request.WarehouseId,
            request.BusinessId,
            cancellationToken);
    }
}
