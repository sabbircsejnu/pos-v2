using MediatR;
using RetailPOS.Application.Outlets.Abstractions;

namespace RetailPOS.Application.Outlets.Commands;

public sealed record CreateBusinessOutletCommand(
    long BusinessId,
    string Name,
    string Address,
    string? ContactNumber,
    long? ManagerId) : IRequest<OutletReadModel>;

public sealed class CreateBusinessOutletCommandHandler(IBusinessSetupOutletWriteStore store)
    : IRequestHandler<CreateBusinessOutletCommand, OutletReadModel>
{
    public Task<OutletReadModel> Handle(
        CreateBusinessOutletCommand request,
        CancellationToken cancellationToken) =>
        store.CreateForBusinessAsync(
            request.BusinessId,
            request.Name,
            request.Address,
            request.ContactNumber,
            request.ManagerId,
            cancellationToken);
}

public sealed record UpdateBusinessOutletCommand(
    long BusinessId,
    long OutletId,
    string Name,
    string Address,
    string? ContactNumber,
    long? ManagerId) : IRequest<OutletReadModel>;

public sealed class UpdateBusinessOutletCommandHandler(IBusinessSetupOutletWriteStore store)
    : IRequestHandler<UpdateBusinessOutletCommand, OutletReadModel>
{
    public Task<OutletReadModel> Handle(
        UpdateBusinessOutletCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateForBusinessAsync(
            request.BusinessId,
            request.OutletId,
            request.Name,
            request.Address,
            request.ContactNumber,
            request.ManagerId,
            cancellationToken);
}

public sealed record DeleteBusinessOutletCommand(long BusinessId, long OutletId) : IRequest<bool>;

public sealed class DeleteBusinessOutletCommandHandler(IBusinessSetupOutletWriteStore store)
    : IRequestHandler<DeleteBusinessOutletCommand, bool>
{
    public Task<bool> Handle(
        DeleteBusinessOutletCommand request,
        CancellationToken cancellationToken) =>
        store.DeleteForBusinessAsync(request.BusinessId, request.OutletId, cancellationToken);
}
