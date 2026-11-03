using MediatR;
using RetailPOS.Application.Outlets.Abstractions;

namespace RetailPOS.Application.Outlets.Commands;

public sealed record CreateOutletCommand(
    string Name,
    string Address,
    string? ContactNumber,
    long? ManagerId,
    OutletPosSetupInput? PosSetup = null) : IRequest<OutletReadModel>;

public sealed class CreateOutletCommandHandler(
    IOutletWriteStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<CreateOutletCommand, OutletReadModel>
{
    public async Task<OutletReadModel> Handle(
        CreateOutletCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.CreateAsync(
            request.Name,
            request.Address,
            request.ContactNumber,
            request.ManagerId,
            businessId,
            request.PosSetup ?? OutletPosSetupInput.None,
            cancellationToken);
    }
}

public sealed record UpdateOutletCommand(
    long OutletId,
    string Name,
    string Address,
    string? ContactNumber,
    long? ManagerId,
    OutletPosSetupInput? PosSetup = null) : IRequest<OutletReadModel>;

public sealed class UpdateOutletCommandHandler(
    IOutletWriteStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<UpdateOutletCommand, OutletReadModel>
{
    public async Task<OutletReadModel> Handle(
        UpdateOutletCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.UpdateAsync(
            request.OutletId,
            request.Name,
            request.Address,
            request.ContactNumber,
            request.ManagerId,
            businessId,
            request.PosSetup ?? OutletPosSetupInput.None,
            cancellationToken);
    }
}

public sealed record DeleteOutletTerminalCommand(long OutletId, long TerminalId) : IRequest;

public sealed class DeleteOutletTerminalCommandHandler(
    IOutletWriteStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<DeleteOutletTerminalCommand>
{
    public async Task Handle(
        DeleteOutletTerminalCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        await store.DeleteTerminalAsync(request.OutletId, request.TerminalId, businessId, cancellationToken);
    }
}

public sealed record DeleteOutletCommand(long OutletId) : IRequest<bool>;

public sealed class DeleteOutletCommandHandler(
    IOutletWriteStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<DeleteOutletCommand, bool>
{
    public async Task<bool> Handle(
        DeleteOutletCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.DeleteAsync(request.OutletId, businessId, cancellationToken);
    }
}
