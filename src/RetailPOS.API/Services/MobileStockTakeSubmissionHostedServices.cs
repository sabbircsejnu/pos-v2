using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.API.Services;

/// <summary>Consumes submission-requested messages from a durable quorum queue with a bounded prefetch.</summary>
public sealed class MobileStockTakeSubmissionConsumerHostedService(
    IServiceScopeFactory scopes,
    RabbitMqDeliveryOptions rabbit,
    ILogger<MobileStockTakeSubmissionConsumerHostedService> logger) : BackgroundService
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
                    "Mobile Stock Take consumer connection failed ({FailureType}); retrying.",
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
            MobileStockTakeSubmissionDelivery.QueueName,
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
            MobileStockTakeSubmissionDelivery.QueueName,
            rabbit.Exchange!,
            MobileStockTakeSubmissionMessage.MessageType,
            cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 4, false, stoppingToken);

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
            await scope.ServiceProvider.GetRequiredService<MobileStockTakeSubmissionDelivery>()
                .HandleAsync(channel, delivery.DeliveryTag, delivery.Body, stoppingToken);
        };
        await channel.BasicConsumeAsync(
            MobileStockTakeSubmissionDelivery.QueueName,
            autoAck: false,
            consumer,
            stoppingToken);
        using var registration = stoppingToken.Register(() => closed.TrySetCanceled(stoppingToken));
        await closed.Task;
    }
}

/// <summary>
/// Re-drives due retries, expired processing leases and Accepted jobs whose message did not arrive.
/// PostgreSQL remains authoritative, so this also recovers work after a worker crash or broker outage.
/// </summary>
public sealed class MobileStockTakeSubmissionSweeperHostedService(
    IServiceScopeFactory scopes,
    MobileStockTakeSubmissionOptions options,
    ILogger<MobileStockTakeSubmissionSweeperHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var listScope = scopes.CreateAsyncScope();
                var ids = await listScope.ServiceProvider
                    .GetRequiredService<IMobileStockTakeSubmissionProcessor>()
                    .FindRecoverableJobIdsAsync(stoppingToken);
                foreach (var id in ids)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMobileStockTakeSubmissionProcessor>()
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
                    "Mobile Stock Take submission sweep failed ({FailureType}); jobs remain durable.",
                    exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.SweepIntervalSeconds), stoppingToken);
        }
    }
}
