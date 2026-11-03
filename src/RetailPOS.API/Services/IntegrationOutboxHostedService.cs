using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.API.Services;

public sealed class IntegrationOutboxHostedService(
    IServiceScopeFactory scopes, ILogger<IntegrationOutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is IOException or TimeoutException
                or Npgsql.NpgsqlException or RabbitMQ.Client.Exceptions.RabbitMQClientException)
            {
                logger.LogError("Durable outbox dispatch failed ({FailureType}); leased work remains recoverable.",
                    exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
