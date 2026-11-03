using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface ICashBookReadStore
{
    Task<CashBookReadModel> GetAsync(
        CashBookFilter filter,
        long businessId,
        IReadOnlyList<CashBookAccountResolution> resolutions,
        CancellationToken cancellationToken);
}

public interface ICashBookPostingAccountResolver
{
    Task<CashBookAccountResolution> ResolveAsync(
        string slot,
        long? outletId,
        CancellationToken cancellationToken);
}
