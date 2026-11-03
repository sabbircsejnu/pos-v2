using RetailPOS.Application.Accounts.Abstractions;

namespace RetailPOS.API.Services;

public sealed class AccountReadScope(ITenantAccessService tenantAccess)
    : IAccountReadScope
{
    public long? BusinessId =>
        tenantAccess.RequireBusinessId();
}
