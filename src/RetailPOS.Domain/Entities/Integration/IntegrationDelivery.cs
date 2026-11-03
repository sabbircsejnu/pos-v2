namespace RetailPOS.Core.Entities.Integration;

public sealed class IntegrationOutboxMessage
{
    public Guid Id { get; set; }
    public long BusinessId { get; set; }
    public string Producer { get; set; } = "";
    public string MessageType { get; set; } = "";
    public int SchemaVersion { get; set; }
    public long SourceVersion { get; set; }
    public string AggregateType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string PayloadJson { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string EnvelopeHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}

public sealed class IntegrationInboxMessage
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ConsumerName { get; set; } = "";
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string PayloadHash { get; set; } = "";
    public string EnvelopeHash { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
    public string? EffectReference { get; set; }
}
