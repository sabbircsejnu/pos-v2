using MediatR;
using RetailPOS.Application.ChartAccounts.Abstractions;

namespace RetailPOS.Application.ChartAccounts.Commands;

public sealed record CreateChartAccountCommand(
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalanceType,
    long? ParentAccountId,
    bool IsControlAccount,
    bool AllowManualPosting,
    bool IsActive,
    int DisplayOrder,
    long? ActorUserId) : IRequest<ChartAccountReadModel>;

public sealed class CreateChartAccountCommandHandler(
    IChartAccountCommandStore commandStore,
    IChartAccountReadScope scope)
    : IRequestHandler<CreateChartAccountCommand, ChartAccountReadModel>
{
    public Task<ChartAccountReadModel> Handle(
        CreateChartAccountCommand request,
        CancellationToken cancellationToken) =>
        commandStore.CreateAsync(
            scope.RequireBusinessId(),
            request.ActorUserId,
            request,
            cancellationToken);
}

public sealed record UpdateChartAccountCommand(
    long Id,
    string AccountName,
    long? ParentAccountId,
    bool IsControlAccount,
    bool AllowManualPosting,
    bool IsActive,
    int DisplayOrder,
    long? ActorUserId) : IRequest<ChartAccountReadModel>;

public sealed class UpdateChartAccountCommandHandler(
    IChartAccountCommandStore commandStore,
    IChartAccountReadScope scope)
    : IRequestHandler<UpdateChartAccountCommand, ChartAccountReadModel>
{
    public Task<ChartAccountReadModel> Handle(
        UpdateChartAccountCommand request,
        CancellationToken cancellationToken) =>
        commandStore.UpdateAsync(
            scope.RequireBusinessId(),
            request.ActorUserId,
            request,
            cancellationToken);
}

public sealed record DeactivateChartAccountCommand(long Id, long? ActorUserId) : IRequest<bool>;

public sealed class DeactivateChartAccountCommandHandler(
    IChartAccountCommandStore commandStore,
    IChartAccountReadScope scope)
    : IRequestHandler<DeactivateChartAccountCommand, bool>
{
    public Task<bool> Handle(
        DeactivateChartAccountCommand request,
        CancellationToken cancellationToken) =>
        commandStore.DeactivateAsync(
            scope.RequireBusinessId(),
            request.ActorUserId,
            request.Id,
            cancellationToken);
}
