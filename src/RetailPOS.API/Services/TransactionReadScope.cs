using RetailPOS.Application.Transactions.Abstractions;

namespace RetailPOS.API.Services;

public sealed class TransactionReadScope(ITenantAccessService tenantAccess)
    : ITransactionReadScope
{
    public long? BusinessId =>
        tenantAccess.RequireBusinessId();
}
