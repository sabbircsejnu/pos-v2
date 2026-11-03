using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.ReadModels;

namespace RetailPOS.Application.AuditEvents.Queries;

public sealed record GetAuditEventDetailsQuery(long Id)
    : IQuery<AuditEventDetailsReadModel?>;

public sealed class GetAuditEventDetailsQueryHandler(IAuditEventReadStore readStore)
    : IRequestHandler<GetAuditEventDetailsQuery, AuditEventDetailsReadModel?>
{
    public Task<AuditEventDetailsReadModel?> Handle(
        GetAuditEventDetailsQuery request,
        CancellationToken cancellationToken) =>
        readStore.FindDetailsAsync(request.Id, cancellationToken);
}
