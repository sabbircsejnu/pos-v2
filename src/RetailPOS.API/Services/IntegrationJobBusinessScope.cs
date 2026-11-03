using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;

namespace RetailPOS.API.Services;

/// <summary>
/// Business scope for Business-scoped integration jobs (for example the Stock Count adjustment worker) that run
/// without an HTTP request. HTTP requests keep using the authenticated tenant scope unchanged; the job's
/// initialized Audit v2 identity is used only when no HTTP Business scope exists.
/// </summary>
internal static class IntegrationJobBusinessScope
{
    public static long RequireBusinessId(ITenantAccessService tenantAccess, IAuditContext? audit)
    {
        if (tenantAccess.EffectiveBusinessId.HasValue)
            return tenantAccess.RequireBusinessId();

        if (audit is { IsInitialized: true, Source: AuditSource.Integration, BusinessId: > 0 })
        {
            return audit.BusinessId.Value;
        }

        return tenantAccess.RequireBusinessId();
    }
}
