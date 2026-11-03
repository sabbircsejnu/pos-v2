namespace RetailPOS.Core.Entities.HR.Organisation;

public sealed class OperationalLocationProjection
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string SourceSystem { get; set; } = "";
    public string SourceEntityType { get; set; } = "";
    public string SourceEntityId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? ContactNumber { get; set; }
    public string? ManagerExternalUserId { get; set; }
    public DateTime SourceUpdatedAtUtc { get; set; }
    public long SourceVersion { get; set; }
    public string SourcePayloadHash { get; set; } = "";
    public DateTime LastSyncedAtUtc { get; set; }
    public bool IsSourceActive { get; set; } = true;
    public bool IsSourceDeleted { get; set; }
    public string SyncStatus { get; set; } = "Current";
}
