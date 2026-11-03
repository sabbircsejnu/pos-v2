using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Pos.Abstractions;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// Appends Cash Session events to the existing durable integration outbox (LLD §26). The message identity is a
/// deterministic hash of (type, aggregate, discriminator), so a retried transition collapses into the same row.
/// </summary>
public sealed class CashSessionEventWriter(IIntegrationOutbox outbox) : ICashSessionEventWriter
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(CashSessionEvent cashSessionEvent, CancellationToken cancellationToken)
    {
        var occurred = new DateTime(
            RetailPOS.Core.Entities.UtcTime.Normalize(cashSessionEvent.OccurredAtUtc).Ticks
            - cashSessionEvent.OccurredAtUtc.Ticks % 10,
            DateTimeKind.Utc);
        var payload = JsonSerializer.Serialize(cashSessionEvent.Payload, PayloadOptions);
        var identity = string.Join('|', cashSessionEvent.BusinessId, cashSessionEvent.MessageType,
            cashSessionEvent.AggregateType, cashSessionEvent.AggregateId, cashSessionEvent.Discriminator);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var correlation = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', cashSessionEvent.BusinessId, cashSessionEvent.AggregateType, cashSessionEvent.AggregateId)));

        var envelope = new IntegrationEnvelope(
            new Guid(hash.AsSpan(0, 16)),
            cashSessionEvent.BusinessId,
            "RetailPOS",
            cashSessionEvent.MessageType,
            1,
            cashSessionEvent.AggregateType,
            cashSessionEvent.AggregateId,
            new Guid(correlation.AsSpan(0, 16)),
            null,
            occurred,
            payload,
            IntegrationEnvelope.Hash(payload));
        await outbox.AppendSourceChangeAsync(envelope, cancellationToken);
    }
}
