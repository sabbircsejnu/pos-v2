using System.Text.Json.Serialization;

namespace RetailPOS.Application.Integration;

public sealed record OperationalLocationSourceEvent(
    [property: JsonPropertyName("businessId")] long BusinessId,
    [property: JsonPropertyName("sourceSystem")] string SourceSystem,
    [property: JsonPropertyName("sourceEntityType")] string SourceEntityType,
    [property: JsonPropertyName("sourceEntityId")] string SourceEntityId,
    [property: JsonPropertyName("sourceVersion")] long SourceVersion,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("contactNumber")] string? ContactNumber,
    [property: JsonPropertyName("managerExternalUserId")] string? ManagerExternalUserId,
    [property: JsonPropertyName("sourceUpdatedAtUtc")] DateTime SourceUpdatedAtUtc);

public interface IOperationalLocationSourceEventWriter
{
    Task AppendAsync(
        string changeType,
        OperationalLocationSourceEvent source,
        CancellationToken cancellationToken);
}
