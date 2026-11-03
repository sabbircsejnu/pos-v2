using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.ReadModels;

namespace RetailPOS.Application.AuditEvents.Queries;

public sealed record GetAuditEventExportQuery(
    AuditEventSearchFilter Filter,
    int HardLimit)
    : IQuery<AuditEventExportReadModel>;

public sealed class GetAuditEventExportQueryHandler(IAuditEventReadStore readStore)
    : IRequestHandler<GetAuditEventExportQuery, AuditEventExportReadModel>
{
    public Task<AuditEventExportReadModel> Handle(
        GetAuditEventExportQuery request,
        CancellationToken cancellationToken) =>
        readStore.ExportAsync(
            request.Filter,
            request.HardLimit,
            cancellationToken);
}
