using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.AuditEvents.Commands;

public sealed record RecordAuditEventCommand(AuditEventWriteRequest Request) : ICommand;

public sealed class RecordAuditEventCommandHandler(IAuditEventCommandStore commandStore)
    : IRequestHandler<RecordAuditEventCommand>
{
    public Task Handle(
        RecordAuditEventCommand request,
        CancellationToken cancellationToken) =>
        commandStore.RecordAsync(request.Request, cancellationToken);
}

public sealed record RecordAuditEventFailureCommand(
    string ActionType,
    string Module,
    string Summary,
    string? EntityType,
    string? EntityId,
    string ErrorMessage) : ICommand;

public sealed class RecordAuditEventFailureCommandHandler(
    IAuditEventCommandStore commandStore)
    : IRequestHandler<RecordAuditEventFailureCommand>
{
    public Task Handle(
        RecordAuditEventFailureCommand request,
        CancellationToken cancellationToken) =>
        commandStore.RecordFailureAsync(
            request.ActionType,
            request.Module,
            request.Summary,
            request.EntityType,
            request.EntityId,
            request.ErrorMessage,
            cancellationToken);
}

public sealed record FlushAuditScopeCommand : ICommand;

public sealed class FlushAuditScopeCommandHandler(
    IAuditEventCommandStore commandStore)
    : IRequestHandler<FlushAuditScopeCommand>
{
    public Task Handle(
        FlushAuditScopeCommand request,
        CancellationToken cancellationToken) =>
        commandStore.FlushScopeAsync(cancellationToken);
}
