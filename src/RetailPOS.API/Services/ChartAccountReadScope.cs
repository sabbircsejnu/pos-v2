using RetailPOS.Application.ChartAccounts.Abstractions;

namespace RetailPOS.API.Services;

public sealed class ChartAccountReadScope(ITenantAccessService tenantAccess)
    : IChartAccountReadScope
{
    public long RequireBusinessId() => tenantAccess.RequireBusinessId();
}
