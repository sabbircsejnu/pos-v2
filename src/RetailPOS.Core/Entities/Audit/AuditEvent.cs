namespace RetailPOS.Core.Entities.Audit;

public class AuditEvent
{
    public long Id { get; set; }
    public Guid CorrelationId { get; set; }

    public long? BusinessId { get; set; }
    public long? OutletId { get; set; }

    public long? RealUserId { get; set; }
    public long? ActingUserId { get; set; }
    public long? RealRoleId { get; set; }
    public long? ActingRoleId { get; set; }

    public string ActionType { get; set; } = string.Empty;
    public string ActionSummary { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    public string? PrimaryEntityType { get; set; }
    public string? PrimaryEntityId { get; set; }

    public string? RequestMethod { get; set; }
    public string? RequestPath { get; set; }

    /// <summary>Detected (canonical) client IP — best resolved value from headers or connection.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Raw IP from the TCP connection before any header inspection.</summary>
    public string? RawIp { get; set; }

    /// <summary>LAN IP of the server machine (e.g. 192.168.x.x). Populated only for local requests.</summary>
    public string? LocalMachineIp { get; set; }

    /// <summary>True when the request originated from a loopback address (::1 / 127.0.0.1).</summary>
    public bool IsLocalRequest { get; set; }

    public string? UserAgent { get; set; }
    public string? DeviceName { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }

    public string Status { get; set; } = AuditStatus.Success;
    public string? ErrorMessage { get; set; }
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<AuditEventEntity> Entities { get; set; } = new List<AuditEventEntity>();
}
