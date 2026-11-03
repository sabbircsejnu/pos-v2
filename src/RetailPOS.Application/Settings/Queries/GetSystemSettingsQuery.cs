using MediatR;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings;

namespace RetailPOS.Application.Settings.Queries;

public sealed record GetSystemSettingsQuery : IRequest<SystemSettings>;

public sealed class GetSystemSettingsQueryHandler(ISystemSettingsStore store)
    : IRequestHandler<GetSystemSettingsQuery, SystemSettings>
{
    public Task<SystemSettings> Handle(
        GetSystemSettingsQuery request,
        CancellationToken cancellationToken) =>
        store.GetAsync(cancellationToken);
}
