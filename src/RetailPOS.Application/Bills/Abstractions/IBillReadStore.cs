namespace RetailPOS.Application.Bills.Abstractions;

public interface IBillReadStore
{
    Task<BillSearchReadModel> SearchAsync(
        string? status,
        long? supplierId,
        int pageNumber,
        int pageSize,
        long businessId,
        CancellationToken cancellationToken);

    Task<BillReadModel?> GetByIdAsync(
        long billId,
        long businessId,
        CancellationToken cancellationToken);

    Task<BillSummaryReadModel> GetSummaryAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public interface IBillReadScopeProvider
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
