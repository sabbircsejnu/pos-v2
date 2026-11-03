using MediatR;
using RetailPOS.Application.HR.Employees;

namespace RetailPOS.API.Services;

public sealed class HrEmployeeLocationTransferHostedService(
    IServiceScopeFactory scopes,
    ILogger<HrEmployeeLocationTransferHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int BusinessBatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Employee location transfer activation batch failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IHrEmployeeLocationTransferActivationStore>();
        var sender = services.GetRequiredService<ISender>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var businesses = await store.GetBusinessesWithDueTransfersAsync(
            today, BusinessBatchSize, cancellationToken);
        foreach (var businessId in businesses)
        {
            await sender.Send(
                new ProcessDueHrEmployeeLocationTransfersCommand(businessId, today),
                cancellationToken);
        }
    }
}
