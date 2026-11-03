namespace RetailPOS.Application.ChartAccounts.Abstractions;

public interface IChartAccountReadStore
{
    Task<IReadOnlyList<ChartAccountReadModel>> GetAllAsync(
        long businessId,
        string? query,
        string? accountType,
        bool? isActive,
        CancellationToken cancellationToken);

    Task<ChartAccountReadModel?> GetByIdAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken);
}

public interface IChartAccountReadScope
{
    long RequireBusinessId();
}
