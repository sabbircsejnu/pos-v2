using System.Globalization;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Application.AuditEvents.Queries;
using RetailPOS.Application.AuditEvents.ReadModels;

namespace RetailPOS.API.Controllers;

[ApiController]
[Authorize(Policy = "audit.view")]
[Route("api/audit-events")]
public class AuditEventsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<AuditEventsController> _logger;

    public AuditEventsController(
        ISender sender,
        ILogger<AuditEventsController> logger)
    {
        _sender = sender;
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
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var result = await _sender.Send(
            new GetAuditEventListQuery(new AuditEventListFilter(
                new AuditEventSearchFilter(
                    search,
                    fromDate,
                    toDate,
                    userId,
                    actionType,
                    module,
                    source,
                    outletId,
                    status),
                page,
                pageSize)),
            cancellationToken);

        return Ok(new AuditEventListResponseDto
        {
            Items = result.Items.Select(Map).ToList(),
            Total = result.Total,
            Page = page,
            PageSize = pageSize
        });
    }

    private static AuditEventListItemDto Map(AuditEventListItemReadModel item) => new()
    {
        Id = item.Id,
        CreatedAt = item.CreatedAt,
        UserName = item.UserName,
        ActingAsRole = item.ActingAsRole,
        ActionType = item.ActionType,
        ActionSummary = item.ActionSummary,
        Module = item.Module,
        Source = item.Source,
        PrimaryEntityType = item.PrimaryEntityType,
        PrimaryEntityId = item.PrimaryEntityId,
        AffectedEntitiesCount = item.AffectedEntitiesCount,
        Status = item.Status,
        CorrelationId = item.CorrelationId
    };

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AuditEventDetailsDto>> Details(
        long id,
        CancellationToken cancellationToken)
    {
        var details = await _sender.Send(new GetAuditEventDetailsQuery(id), cancellationToken);
        if (details is null) return NotFound();

        var listItem = details.ListItem;
        var dto = new AuditEventDetailsDto
        {
            Id = listItem.Id,
            CreatedAt = listItem.CreatedAt,
            UserName = listItem.UserName,
            RealUserName = listItem.UserName,
            ActingAsRole = listItem.ActingAsRole,
            OutletName = details.OutletName,
            ActionType = listItem.ActionType,
            ActionSummary = listItem.ActionSummary,
            Module = listItem.Module,
            Source = listItem.Source,
            PrimaryEntityType = listItem.PrimaryEntityType,
            PrimaryEntityId = listItem.PrimaryEntityId,
            AffectedEntitiesCount = listItem.AffectedEntitiesCount,
            Status = listItem.Status,
            CorrelationId = listItem.CorrelationId,
            IpAddress = details.IpAddress,
            RawIp = details.RawIp,
            LocalMachineIp = details.LocalMachineIp,
            IsLocalRequest = details.IsLocalRequest,
            Device = details.DeviceName,
            Browser = details.Browser,
            Os = details.Os,
            RequestMethod = details.RequestMethod,
            RequestPath = details.RequestPath,
            ErrorMessage = details.ErrorMessage,
            ModulesInvolved = new List<string> { listItem.Module },
            AffectedEntities = details.AffectedEntities.Select(entity => new AuditAffectedEntityDto
            {
                Id = entity.Id,
                EntityType = entity.EntityType,
                EntityId = entity.EntityId,
                OperationType = entity.OperationType,
                FieldsChangedCount = entity.FieldsChangedCount,
                IsInternalOperation = entity.IsInternalOperation,
                InternalOperationName = entity.InternalOperationName,
                FieldChanges = entity.FieldChanges.Select(change => new AuditFieldChangeDto
                {
                    FieldName = change.FieldName,
                    DisplayLabel = AuditFieldLabels.GetLabel(change.FieldName),
                    OldValue = ParseJson(change.OldValue),
                    NewValue = ParseJson(change.NewValue),
                    OldDisplayValue = change.OldDisplayValue,
                    NewDisplayValue = change.NewDisplayValue,
                    ReferenceEntityType = change.ReferenceEntityType,
                    IsReferenceField = change.IsReferenceField,
                    Redacted = IsRedacted(change.OldValue) || IsRedacted(change.NewValue),
                }).ToList(),
            }).ToList(),
        };

        return Ok(dto);
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export(
        [FromBody] AuditExportRequestDto request,
        CancellationToken cancellationToken)
    {
        const int hardLimit = 100_000;
        var result = await _sender.Send(
            new GetAuditEventExportQuery(
                new AuditEventSearchFilter(
                    request.Search,
                    request.FromDate,
                    request.ToDate,
                    request.UserId,
                    request.ActionType?.ToArray(),
                    request.Module?.ToArray(),
                    request.Source?.ToArray(),
                    request.OutletId,
                    request.Status),
                hardLimit),
            cancellationToken);
        if (result.Total > hardLimit)
            return StatusCode(413, new { message = $"Too many rows ({result.Total}); cap is {hardLimit}. Narrow date range." });

        var sb = new StringBuilder();
        sb.AppendLine("id,created_at,correlation_id,module,source,action_type,action_summary,primary_entity_type,primary_entity_id,real_user_id,ip_address,request_method,request_path,status,error_message,affected_entities");
        foreach (var r in result.Items)
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

        // Confidential exports are already durably access-audited by the Application read path.
        if (!result.ConfidentialAccessAudited)
        {
            try
            {
                await _sender.Send(
                    new RecordAuditEventCommand(new AuditEventWriteRequest(
                        AuditActionType.Export,
                        AuditModule.Reports,
                        $"Exported {result.Items.Count} audit events ({request.Format}) for {request.FromDate:yyyy-MM-dd} → {request.ToDate:yyyy-MM-dd}")),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to self-audit export");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"audit-events-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
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
