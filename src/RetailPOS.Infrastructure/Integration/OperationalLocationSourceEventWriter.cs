using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetailPOS.Application.Integration;

namespace RetailPOS.Infrastructure.Integration;

public sealed class OperationalLocationSourceEventWriter(
    IIntegrationOutbox outbox) : IOperationalLocationSourceEventWriter
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(
        string changeType,
        OperationalLocationSourceEvent source,
        CancellationToken cancellationToken)
    {
        if (source.BusinessId <= 0)
            return;
        if (source.SourceEntityType is not ("Outlet" or "Warehouse"))
            throw new InvalidDataException("Only source-owned Outlet and Warehouse events are supported.");
        if (source.SourceVersion <= 0 || !long.TryParse(source.SourceEntityId,
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out var sourceId) || sourceId <= 0)
            throw new InvalidDataException("Operational location event has invalid source identity/version.");
        ArgumentException.ThrowIfNullOrWhiteSpace(changeType);

        var operation = (source.SourceEntityType, changeType) switch
        {
            ("Outlet", "OutletCreated") or ("Warehouse", "WarehouseCreated") => "created",
            ("Outlet", "OutletUpdated") or ("Warehouse", "WarehouseUpdated") => "updated",
            ("Outlet", "OutletDeleted") or ("Warehouse", "WarehouseDeleted") => "deleted",
            _ => throw new InvalidDataException("Unsupported operational location change type.")
        };
        var sourceUpdatedAtUtc = source.SourceUpdatedAtUtc.Kind switch
        {
            DateTimeKind.Utc => source.SourceUpdatedAtUtc,
            DateTimeKind.Local => source.SourceUpdatedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(source.SourceUpdatedAtUtc, DateTimeKind.Utc)
        };
        source = source with
        {
            SourceUpdatedAtUtc = new DateTime(sourceUpdatedAtUtc.Ticks
                - sourceUpdatedAtUtc.Ticks % 10, DateTimeKind.Utc)
        };
        var payload = JsonSerializer.Serialize(source, PayloadJsonOptions);
        var payloadHash = IntegrationEnvelope.Hash(payload);
        var envelope = new IntegrationEnvelope(
            CreateMessageId(source),
            source.BusinessId,
            "RetailPOS",
            $"operational-location.{source.SourceEntityType.ToLowerInvariant()}.{operation}",
            1,
            source.SourceEntityType,
            source.SourceEntityId,
            CreateCorrelationId(source),
            null,
            source.SourceUpdatedAtUtc,
            payload,
            payloadHash,
            source.SourceVersion);
        await outbox.AppendSourceChangeAsync(envelope, cancellationToken);
    }

    private static Guid CreateMessageId(OperationalLocationSourceEvent source)
    {
        var identity = string.Join('|', source.BusinessId, source.SourceSystem, source.SourceEntityType,
            source.SourceEntityId, source.SourceVersion);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static Guid CreateCorrelationId(OperationalLocationSourceEvent source)
    {
        var identity = string.Join('|', source.BusinessId, source.SourceSystem, source.SourceEntityType,
            source.SourceEntityId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(16, 16));
    }
}
