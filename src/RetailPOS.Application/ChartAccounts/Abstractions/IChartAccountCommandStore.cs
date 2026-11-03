using RetailPOS.Application.ChartAccounts.Commands;

namespace RetailPOS.Application.ChartAccounts.Abstractions;

public interface IChartAccountCommandStore
{
    Task<ChartAccountReadModel> CreateAsync(
        long businessId,
        long? actorUserId,
        CreateChartAccountCommand command,
        CancellationToken cancellationToken);

    Task<ChartAccountReadModel> UpdateAsync(
        long businessId,
        long? actorUserId,
        UpdateChartAccountCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeactivateAsync(
        long businessId,
        long? actorUserId,
        long accountId,
        CancellationToken cancellationToken);
}
