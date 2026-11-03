using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.ReadModels;

namespace RetailPOS.Application.AuditEvents.Queries;

public sealed record GetAuditEventListQuery(AuditEventListFilter Filter)
    : IQuery<AuditEventListReadModel>;

public sealed class GetAuditEventListQueryHandler(IAuditEventReadStore readStore)
    : IRequestHandler<GetAuditEventListQuery, AuditEventListReadModel>
{
    public Task<AuditEventListReadModel> Handle(
        GetAuditEventListQuery request,
        CancellationToken cancellationToken) =>
        readStore.ListAsync(request.Filter, cancellationToken);
}
