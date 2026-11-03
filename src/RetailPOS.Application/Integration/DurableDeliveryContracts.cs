using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RetailPOS.Application.Integration;

public sealed record IntegrationEnvelope(
    Guid MessageId, long BusinessId, string Producer, string MessageType, int SchemaVersion,
    string AggregateType, string AggregateId, Guid CorrelationId, Guid? CausationId,
    DateTime OccurredAtUtc, string PayloadJson, string PayloadHash, long SourceVersion = 1)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string EnvelopeHash => Hash(JsonSerializer.Serialize(this with
    {
        // PostgreSQL stores microsecond precision; freeze the hash at the same precision.
        OccurredAtUtc = new DateTime(OccurredAtUtc.Ticks - OccurredAtUtc.Ticks % 10, DateTimeKind.Utc)
    }));
    public static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public void Validate()
    {
        if (MessageId == Guid.Empty || BusinessId <= 0 || CorrelationId == Guid.Empty
            || SchemaVersion <= 0 || SourceVersion <= 0 || OccurredAtUtc.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(Producer) || string.IsNullOrWhiteSpace(MessageType)
            || string.IsNullOrWhiteSpace(AggregateType) || string.IsNullOrWhiteSpace(AggregateId)
            || string.IsNullOrWhiteSpace(PayloadJson) || string.IsNullOrWhiteSpace(PayloadHash))
            throw new InvalidDataException("Integration envelope lacks required identity/scope/schema.");
        if (Producer.Length > 100 || MessageType.Length > 200 || AggregateType.Length > 100 || AggregateId.Length > 150
            || PayloadJson.Length > 1_000_000 || Hash(PayloadJson) != PayloadHash)
            throw new InvalidDataException("Integration envelope size or hash is invalid.");
        using var payload = JsonDocument.Parse(PayloadJson);
        if (payload.RootElement.ValueKind != JsonValueKind.Object
            || !payload.RootElement.TryGetProperty("businessId", out var business)
            || business.ValueKind != JsonValueKind.Number
            || !business.TryGetInt64(out var payloadBusiness) || payloadBusiness != BusinessId)
            throw new InvalidDataException("Payload Business must match the trusted envelope.");
    }
}

public interface IIntegrationOutbox
{
    Task AppendAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken);
    Task AppendSourceChangeAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken);
    Task ReplayAsync(long businessId, Guid messageId, string reason, CancellationToken cancellationToken);
}

public interface IConfirmedIntegrationPublisher
{
    // Returns only after routing success and a positive publisher confirm.
    Task PublishAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken);
}

public interface IIntegrationReplayAuthorization
{
    Task DemandAsync(long businessId, CancellationToken cancellationToken);
}

public interface IIntegrationConsumer
{
    string Name { get; }
    void ValidateTrustedEnvelope(IntegrationEnvelope envelope);
    Task<string?> ApplyAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class IntegrationConflictException(string message) : InvalidOperationException(message);
public sealed class IntegrationPoisonException(string message) : Exception(message);

public sealed record DurableDeliveryOptions(int BatchSize = 20, int MaxAttempts = 10,
    int LeaseSeconds = 60, int BaseRetrySeconds = 5, int MaxRetrySeconds = 3600)
{
    public void Validate()
    {
        if (BatchSize is < 1 or > 200 || MaxAttempts is < 1 or > 100 || LeaseSeconds < 10
            || BaseRetrySeconds < 1 || MaxRetrySeconds < BaseRetrySeconds)
            throw new InvalidOperationException("Durable delivery bounds are invalid.");
    }
}
