using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Controllers;

[ApiController]
[Authorize(Policy = "audit.view")]
[Route("api/audit-events")]
public class AuditEventsController : ControllerBase
{
    private readonly RetailPOSDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<AuditEventsController> _logger;

    public AuditEventsController(RetailPOSDbContext db, IAuditService audit,
        ILogger<AuditEventsController> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AuditEventListResponseDto>> List(
        [FromQuery] string? search,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] long? userId,
        [FromQuery(Name = "actionType")] string[]? actionType,
        [FromQuery(Name = "module")] string[]? module,
        [FromQuery(Name = "source")] string[]? source,
        [FromQuery] long? outletId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var query = BuildQuery(search, fromDate, toDate, userId, actionType, module, source, outletId, status);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEventListItemDto
            {
                Id = e.Id,
                CreatedAt = e.CreatedAt,
                UserName = _db.Users.Where(u => u.Id == e.RealUserId).Select(u => u.Name).FirstOrDefault(),
                ActingAsRole = _db.Roles.Where(r => r.Id == e.ActingRoleId).Select(r => r.Name).FirstOrDefault(),
                ActionType = e.ActionType,
                ActionSummary = e.ActionSummary,
                Module = e.Module,
                Source = e.Source,
                PrimaryEntityType = e.PrimaryEntityType,
                PrimaryEntityId = e.PrimaryEntityId,
                AffectedEntitiesCount = e.Entities.Count,
                Status = e.Status,
                CorrelationId = e.CorrelationId,
            })
            .ToListAsync();

        return Ok(new AuditEventListResponseDto
        {
            Items = items, Total = total, Page = page, PageSize = pageSize
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AuditEventDetailsDto>> Details(long id)
    {
        var ev = await _db.AuditEvents
            .Include(e => e.Entities).ThenInclude(en => en.FieldChanges)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev is null) return NotFound();

        string? userName = null;
        if (ev.RealUserId is not null)
            userName = await _db.Users.Where(u => u.Id == ev.RealUserId).Select(u => u.Name).FirstOrDefaultAsync();

        string? actingRole = null;
        if (ev.ActingRoleId is not null)
            actingRole = await _db.Roles.Where(r => r.Id == ev.ActingRoleId).Select(r => r.Name).FirstOrDefaultAsync();

        string? outletName = null;
        if (ev.OutletId is not null)
            outletName = await _db.Outlets.Where(o => o.Id == ev.OutletId).Select(o => o.Name).FirstOrDefaultAsync();

        var dto = new AuditEventDetailsDto
        {
            Id = ev.Id,
            CreatedAt = ev.CreatedAt,
            UserName = userName,
            RealUserName = userName,
            ActingAsRole = actingRole,
            OutletName = outletName,
            ActionType = ev.ActionType,
            ActionSummary = ev.ActionSummary,
            Module = ev.Module,
            Source = ev.Source,
            PrimaryEntityType = ev.PrimaryEntityType,
            PrimaryEntityId = ev.PrimaryEntityId,
            AffectedEntitiesCount = ev.Entities.Count,
            Status = ev.Status,
            CorrelationId = ev.CorrelationId,
            IpAddress = ev.IpAddress,
            RawIp = ev.RawIp,
            LocalMachineIp = ev.LocalMachineIp,
            IsLocalRequest = ev.IsLocalRequest,
            Device = ev.DeviceName,
            Browser = ev.Browser,
            Os = ev.Os,
            RequestMethod = ev.RequestMethod,
            RequestPath = ev.RequestPath,
            ErrorMessage = ev.ErrorMessage,
            ModulesInvolved = new List<string> { ev.Module },
            AffectedEntities = ev.Entities.Select(en => new AuditAffectedEntityDto
            {
                Id = en.Id,
                EntityType = en.EntityType,
                EntityId = en.EntityId,
                OperationType = en.OperationType,
                FieldsChangedCount = en.FieldsChangedCount,
                IsInternalOperation = en.IsInternalOperation,
                InternalOperationName = en.InternalOperationName,
                FieldChanges = en.FieldChanges.Select(fc => new AuditFieldChangeDto
                {
                    FieldName = fc.FieldName,
                    DisplayLabel = AuditFieldLabels.GetLabel(fc.FieldName),
                    OldValue = ParseJson(fc.OldValue),
                    NewValue = ParseJson(fc.NewValue),
                    OldDisplayValue = fc.OldDisplayValue,
                    NewDisplayValue = fc.NewDisplayValue,
                    ReferenceEntityType = fc.ReferenceEntityType,
                    IsReferenceField = fc.IsReferenceField,
                    Redacted = IsRedacted(fc.OldValue) || IsRedacted(fc.NewValue),
                }).ToList(),
            }).ToList(),
        };

        return Ok(dto);
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] AuditExportRequestDto request)
    {
        var query = BuildQuery(request.Search, request.FromDate, request.ToDate,
            request.UserId,
            request.ActionType?.ToArray(), request.Module?.ToArray(), request.Source?.ToArray(),
            request.OutletId, request.Status);

        const int hardLimit = 100_000;
        var total = await query.CountAsync();
        if (total > hardLimit)
            return StatusCode(413, new { message = $"Too many rows ({total}); cap is {hardLimit}. Narrow date range." });

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                e.Id, e.CreatedAt, e.CorrelationId, e.Module, e.Source, e.ActionType,
                e.ActionSummary, e.PrimaryEntityType, e.PrimaryEntityId, e.RealUserId,
                e.IpAddress, e.RequestMethod, e.RequestPath, e.Status, e.ErrorMessage,
                Entities = e.Entities.Select(en => new
                {
                    en.EntityType, en.EntityId, en.OperationType, en.FieldsChangedCount
                }).ToList()
            })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("id,created_at,correlation_id,module,source,action_type,action_summary,primary_entity_type,primary_entity_id,real_user_id,ip_address,request_method,request_path,status,error_message,affected_entities");
        foreach (var r in rows)
        {
            var entitiesJson = JsonSerializer.Serialize(r.Entities);
            sb.Append(r.Id).Append(',')
              .Append(r.CreatedAt.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.CorrelationId).Append(',')
              .Append(Csv(r.Module)).Append(',')
              .Append(Csv(r.Source)).Append(',')
              .Append(Csv(r.ActionType)).Append(',')
              .Append(Csv(r.ActionSummary)).Append(',')
              .Append(Csv(r.PrimaryEntityType)).Append(',')
              .Append(Csv(r.PrimaryEntityId)).Append(',')
              .Append(r.RealUserId).Append(',')
              .Append(Csv(r.IpAddress)).Append(',')
              .Append(Csv(r.RequestMethod)).Append(',')
              .Append(Csv(r.RequestPath)).Append(',')
              .Append(Csv(r.Status)).Append(',')
              .Append(Csv(r.ErrorMessage)).Append(',')
              .Append(Csv(entitiesJson))
              .AppendLine();
        }

        // Self-audit the export
        try
        {
            await _audit.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Export,
                Module = AuditModule.Reports,
                Summary = $"Exported {rows.Count} audit events ({request.Format}) for {request.FromDate:yyyy-MM-dd} → {request.ToDate:yyyy-MM-dd}",
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to self-audit export");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"audit-events-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private IQueryable<AuditEvent> BuildQuery(string? search, DateTime? fromDate, DateTime? toDate,
        long? userId, string[]? actionType, string[]? module, string[]? source, long? outletId, string? status)
    {
        IQueryable<AuditEvent> q = _db.AuditEvents.AsNoTracking();

        if (fromDate.HasValue) q = q.Where(e => e.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) q = q.Where(e => e.CreatedAt <= toDate.Value);
        if (userId.HasValue) q = q.Where(e => e.RealUserId == userId.Value || e.ActingUserId == userId.Value);
        if (actionType is { Length: > 0 }) q = q.Where(e => actionType.Contains(e.ActionType));
        if (module is { Length: > 0 }) q = q.Where(e => module.Contains(e.Module));
        if (source is { Length: > 0 }) q = q.Where(e => source.Contains(e.Source));
        if (outletId.HasValue) q = q.Where(e => e.OutletId == outletId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(e => e.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            if (Guid.TryParse(s, out var gid))
            {
                q = q.Where(e => e.CorrelationId == gid);
            }
            else
            {
                var like = $"%{s}%";
                q = q.Where(e =>
                    EF.Functions.ILike(e.ActionSummary, like) ||
                    e.PrimaryEntityId == s ||
                    _db.Users.Any(u => u.Id == e.RealUserId && EF.Functions.ILike(u.Name, like)));
            }
        }

        return q;
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<object?>(json); }
        catch { return json; }
    }

    private static bool IsRedacted(string? json) =>
        json is not null && json.Contains(AuditFieldRedaction.RedactedToken, StringComparison.Ordinal);

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        if (!needsQuote) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
