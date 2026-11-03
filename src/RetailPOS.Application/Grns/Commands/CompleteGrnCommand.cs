using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Commands;

public sealed record CompleteGrnCommand(long GrnId, long ActorUserId)
    : ICommand<GrnReadModel>;

public sealed class CompleteGrnCommandHandler(
    IGrnCompletionStore store,
    IGrnCompletionAuthorizer authorizer)
    : IRequestHandler<CompleteGrnCommand, GrnReadModel>
{
    public async Task<GrnReadModel> Handle(
        CompleteGrnCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await store.FindScopeAsync(request.GrnId, cancellationToken)
            ?? throw new KeyNotFoundException($"GRN with ID {request.GrnId} not found");

        var businessId = await authorizer.AuthorizeAsync(
            scope.BusinessId,
            scope.LocationId,
            scope.LocationType,
            cancellationToken);

        if (scope.Status == "completed")
            throw new InvalidOperationException("GRN is already completed");

        return await store.CompleteAsync(
            request.GrnId,
            request.ActorUserId,
            businessId,
            cancellationToken);
    }
}
