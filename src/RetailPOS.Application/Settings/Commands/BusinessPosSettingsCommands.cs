using MediatR;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Settings.Commands;

public sealed record GetBusinessPosSettingsQuery(long BusinessId) : IRequest<BusinessPosSettingsSnapshot>;

public sealed class GetBusinessPosSettingsQueryHandler(IBusinessPosSettingsStore store)
    : IRequestHandler<GetBusinessPosSettingsQuery, BusinessPosSettingsSnapshot>
{
    public Task<BusinessPosSettingsSnapshot> Handle(
        GetBusinessPosSettingsQuery request,
        CancellationToken cancellationToken) =>
        store.GetAsync(request.BusinessId, cancellationToken);
}

public sealed record UpdateBusinessPosSettingsCommand(
    long BusinessId,
    BusinessPosSettingsUpdate Update,
    long? UserId) : IRequest<BusinessPosSettingsSnapshot>;

public interface IBusinessPosSettingsAuditWriter
{
    Task RecordUpdatedAsync(
        BusinessPosSettingsSnapshot before,
        BusinessPosSettingsSnapshot after,
        long? userId,
        CancellationToken cancellationToken);
}

public sealed class UpdateBusinessPosSettingsCommandHandler(
    IBusinessPosSettingsStore store,
    IBusinessPosSettingsAuditWriter auditWriter)
    : IRequestHandler<UpdateBusinessPosSettingsCommand, BusinessPosSettingsSnapshot>
{
    public async Task<BusinessPosSettingsSnapshot> Handle(
        UpdateBusinessPosSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var (before, after) = await store.UpdateAsync(
            request.BusinessId,
            request.Update,
            request.UserId,
            cancellationToken);
        await auditWriter.RecordUpdatedAsync(before, after, request.UserId, cancellationToken);
        return after;
    }
}
