using System.Text.Json;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Integration;

namespace RetailPOS.Infrastructure.Integration;

public sealed class OperationalLocationProjectionConsumer(
    IOperationalLocationProjectionWriteStore projections) : IIntegrationConsumer
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "hr.operational-location-projection";

    public void ValidateTrustedEnvelope(IntegrationEnvelope envelope)
    {
        envelope.Validate();
        if (envelope.Producer != "RetailPOS" || envelope.SchemaVersion != 1
            || envelope.AggregateType is not ("Outlet" or "Warehouse")
            || envelope.MessageType is not ("operational-location.outlet.created"
                or "operational-location.outlet.updated" or "operational-location.outlet.deleted"
                or "operational-location.warehouse.created"
                or "operational-location.warehouse.updated"
                or "operational-location.warehouse.deleted"))
            throw new IntegrationPoisonException("Unsupported operational location source event.");

        var source = Parse(envelope);
        if (source.BusinessId != envelope.BusinessId
            || source.SourceEntityType != envelope.AggregateType
            || source.SourceEntityId != envelope.AggregateId
            || source.SourceVersion != envelope.SourceVersion
            || source.IsDeleted != envelope.MessageType.EndsWith("deleted", StringComparison.Ordinal))
            throw new IntegrationPoisonException("Operational location event identity does not match its envelope.");
    }

    public async Task<string?> ApplyAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        var result = await projections.UpsertFromSourceAsync(Parse(envelope), cancellationToken);
        return $"{envelope.AggregateType}:{envelope.AggregateId}:{result}";
    }

    private static OperationalLocationSourceEvent Parse(IntegrationEnvelope envelope) =>
        JsonSerializer.Deserialize<OperationalLocationSourceEvent>(envelope.PayloadJson, PayloadJsonOptions)
        ?? throw new IntegrationPoisonException("Operational location event payload is empty.");
}
