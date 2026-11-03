using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.API.Services;

/// <summary>Consumes adjustment-requested messages from a durable quorum queue with a bounded prefetch.</summary>
public sealed class StockCountAdjustmentConsumerHostedService(
    IServiceScopeFactory scopes,
    RabbitMqDeliveryOptions rabbit,
    ILogger<StockCountAdjustmentConsumerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilClosedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException
                or RabbitMQ.Client.Exceptions.RabbitMQClientException)
            {
                logger.LogError(
                    "Stock Count adjustment consumer connection failed ({FailureType}); retrying.",
                    exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ConsumeUntilClosedAsync(CancellationToken stoppingToken)
    {
        rabbit.Validate();
        var factory = new ConnectionFactory
        {
            Uri = new Uri(rabbit.ConnectionUri!),
            AutomaticRecoveryEnabled = false
        };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(rabbit.Exchange!, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(
            StockCountAdjustmentDelivery.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 5
            },
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            StockCountAdjustmentDelivery.QueueName,
            rabbit.Exchange!,
            StockCountAdjustmentMessage.MessageType,
            cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 2, false, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<StockCountAdjustmentDelivery>()
                .HandleAsync(channel, delivery.DeliveryTag, delivery.Body, stoppingToken);
        };
        await channel.BasicConsumeAsync(
            StockCountAdjustmentDelivery.QueueName,
            autoAck: false,
            consumer,
            stoppingToken);
        using var registration = stoppingToken.Register(() => closed.TrySetCanceled(stoppingToken));
        await closed.Task;
    }
}

/// <summary>
/// Re-drives due retries, expired processing leases and Pending jobs whose message did not arrive.
/// PostgreSQL remains authoritative, so this also completes jobs when the broker is not available.
/// </summary>
public sealed class StockCountAdjustmentSweeperHostedService(
    IServiceScopeFactory scopes,
    StockCountAdjustmentOptions options,
    ILogger<StockCountAdjustmentSweeperHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var listScope = scopes.CreateAsyncScope();
                if (!listScope.ServiceProvider.GetRequiredService<IStockCountAsyncAdjustmentSwitch>().Enabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken);
                    continue;
                }

                var ids = await listScope.ServiceProvider
                    .GetRequiredService<IStockCountAdjustmentProcessor>()
                    .FindRecoverableJobIdsAsync(stoppingToken);
                foreach (var id in ids)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IStockCountAdjustmentProcessor>()
                        .ProcessAsync(id, null, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Stock Count adjustment sweep failed ({FailureType}); jobs remain durable.",
                    exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken);
        }
    }
}
