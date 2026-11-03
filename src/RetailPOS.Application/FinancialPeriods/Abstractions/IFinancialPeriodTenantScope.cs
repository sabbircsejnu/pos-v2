namespace RetailPOS.Application.FinancialPeriods.Abstractions;

public interface IFinancialPeriodTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
