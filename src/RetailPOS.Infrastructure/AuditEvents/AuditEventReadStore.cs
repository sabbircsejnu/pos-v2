using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.ReadModels;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Core.Audit;
using RetailPOS.Infrastructure.Audit;
using System.Security.Cryptography;
using System.Text.Json;

namespace RetailPOS.Infrastructure.AuditEvents;

public sealed class AuditEventReadStore(
    RetailPOSDbContext dbContext,
    IAuditReadAccessScope accessScope,
    IRequiredAuditTransaction requiredAudit)
    : IAuditEventReadStore
{
    public async Task<AuditEventListReadModel> ListAsync(
        AuditEventListFilter filter,
        CancellationToken cancellationToken)
    {
        var access = await accessScope.GetAsync(cancellationToken);
        var query = BuildQuery(filter.Search, access);
        var confidentialQuery = query.Where(ConfidentialExpression);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(eventRow => eventRow.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(eventRow => new
            {
                Confidential = confidentialQuery.Any(candidate => candidate.Id == eventRow.Id),
                Item = new AuditEventListItemReadModel(
                eventRow.Id,
                eventRow.CreatedAt,
                dbContext.Users
                    .Where(user => user.Id == eventRow.RealUserId)
                    .Select(user => user.Name)
                    .FirstOrDefault(),
                dbContext.Roles
                    .Where(role => role.Id == eventRow.ActingRoleId)
                    .Select(role => role.Name)
                    .FirstOrDefault(),
                eventRow.ActionType,
                eventRow.ActionSummary,
                eventRow.Module,
                eventRow.Source,
                eventRow.PrimaryEntityType,
                eventRow.PrimaryEntityId,
                eventRow.Entities.Count,
                eventRow.Status,
                eventRow.CorrelationId)
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => row.Confidential ? row.Item with
        {
            ActionSummary = "Confidential HR/Payroll audit event",
            PrimaryEntityId = ConfidentialAuditPolicy.SafeReference(row.Item.PrimaryEntityId)
        } : row.Item).ToList();
        var confidentialCount = rows.Count(row => row.Confidential);
        if (confidentialCount > 0)
            await RecordAccessAsync(access, "list", null, confidentialCount, cancellationToken,
                HashOutput(items));
        return new AuditEventListReadModel(items, total);
    }

    public async Task<AuditEventExportReadModel> ExportAsync(
        AuditEventSearchFilter filter,
        int hardLimit,
        CancellationToken cancellationToken)
    {
        var access = await accessScope.GetAsync(cancellationToken);
        var query = BuildQuery(filter, access);
        var confidentialQuery = query.Where(ConfidentialExpression);
        var total = await query.CountAsync(cancellationToken);
        if (total > hardLimit)
            return new AuditEventExportReadModel(total, []);

        var rows = await query
            .OrderByDescending(eventRow => eventRow.CreatedAt)
            .Take(hardLimit + 1)
            .Select(eventRow => new
            {
                Confidential = confidentialQuery.Any(candidate => candidate.Id == eventRow.Id),
                Item = new AuditEventExportItemReadModel(
                eventRow.Id,
                eventRow.CreatedAt,
                eventRow.CorrelationId,
                eventRow.Module,
                eventRow.Source,
                eventRow.ActionType,
                eventRow.ActionSummary,
                eventRow.PrimaryEntityType,
                eventRow.PrimaryEntityId,
                eventRow.RealUserId,
                eventRow.IpAddress,
                eventRow.RequestMethod,
                eventRow.RequestPath,
                eventRow.Status,
                eventRow.ErrorMessage,
                eventRow.Entities
                    .Select(entity => new AuditEventExportEntityReadModel(
                        entity.EntityType,
                        entity.EntityId,
                        entity.OperationType,
                        entity.FieldsChangedCount))
                    .ToList())
            })
            .ToListAsync(cancellationToken);
        if (rows.Count > hardLimit)
            return new AuditEventExportReadModel(rows.Count, []);
        var items = rows.Select(row => row.Confidential ? row.Item with
        {
            ActionSummary = "Confidential HR/Payroll audit event",
            PrimaryEntityId = ConfidentialAuditPolicy.SafeReference(row.Item.PrimaryEntityId),
            RequestPath = null,
            ErrorMessage = row.Item.ErrorMessage is null ? null : ConfidentialAuditPolicy.RedactedToken,
            Entities = row.Item.Entities.Select(entity => entity with
            {
                EntityId = ConfidentialAuditPolicy.SafeReference(entity.EntityId) ?? string.Empty
            }).ToArray()
        } : row.Item).ToList();
        var confidentialCount = rows.Count(row => row.Confidential);
        if (confidentialCount > 0)
            await RecordAccessAsync(access, "export", null, confidentialCount, cancellationToken,
                HashOutput(items));
        return new AuditEventExportReadModel(total, items, confidentialCount > 0);
    }

    private IQueryable<AuditEvent> BuildQuery(AuditEventSearchFilter filter, AuditReadAccess access)
    {
        var query = ApplyAccess(dbContext.AuditEvents.AsNoTracking(), access);

        if (filter.FromDate.HasValue)
            query = query.Where(eventRow => eventRow.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(eventRow => eventRow.CreatedAt <= filter.ToDate.Value);
        if (filter.UserId.HasValue)
        {
            var userId = filter.UserId.Value;
            query = query.Where(eventRow =>
                eventRow.RealUserId == userId || eventRow.ActingUserId == userId);
        }
        if (filter.ActionTypes is { Count: > 0 })
            query = query.Where(eventRow => filter.ActionTypes.Contains(eventRow.ActionType));
        if (filter.Modules is { Count: > 0 })
            query = query.Where(eventRow => filter.Modules.Contains(eventRow.Module));
        if (filter.Sources is { Count: > 0 })
            query = query.Where(eventRow => filter.Sources.Contains(eventRow.Source));
        if (filter.OutletId.HasValue)
            query = query.Where(eventRow => eventRow.OutletId == filter.OutletId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(eventRow => eventRow.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            if (Guid.TryParse(search, out var correlationId))
            {
                query = query.Where(eventRow => eventRow.CorrelationId == correlationId);
            }
            else
            {
                var pattern = $"%{search}%";
                query = query.Where(eventRow =>
                    EF.Functions.ILike(eventRow.ActionSummary, pattern)
                    || eventRow.PrimaryEntityId == search
                    || dbContext.Users.Any(user =>
                        user.Id == eventRow.RealUserId
                        && EF.Functions.ILike(user.Name, pattern)));
            }
        }

        return query;
    }

    public async Task<AuditEventDetailsReadModel?> FindDetailsAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var access = await accessScope.GetAsync(cancellationToken);
        var eventRow = await ApplyAccess(dbContext.AuditEvents.AsNoTracking(), access)
            .Include(auditEvent => auditEvent.Entities)
                .ThenInclude(entity => entity.FieldChanges)
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == id, cancellationToken);
        if (eventRow is null)
        {
            if (access.BusinessId is > 0 && await dbContext.AuditEvents
                .Where(ConfidentialExpression).AnyAsync(row => row.Id == id, cancellationToken))
                await RecordAccessAsync(access, "denied_detail", id, 0, cancellationToken);
            return null;
        }

        var confidential = IsConfidentialExpression(eventRow);
        if (confidential)
            ConfidentialAuditSanitizer.Sanitize(eventRow);

        var userName = eventRow.RealUserId.HasValue
            ? await dbContext.Users
                .Where(user => user.Id == eventRow.RealUserId.Value)
                .Select(user => user.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var actingRole = eventRow.ActingRoleId.HasValue
            ? await dbContext.Roles
                .Where(role => role.Id == eventRow.ActingRoleId.Value)
                .Select(role => role.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var outletName = eventRow.OutletId.HasValue
            ? await dbContext.Outlets
                .Where(outlet => outlet.Id == eventRow.OutletId.Value)
                .Select(outlet => outlet.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var listItem = new AuditEventListItemReadModel(
            eventRow.Id,
            eventRow.CreatedAt,
            userName,
            actingRole,
            eventRow.ActionType,
            eventRow.ActionSummary,
            eventRow.Module,
            eventRow.Source,
            eventRow.PrimaryEntityType,
            eventRow.PrimaryEntityId,
            eventRow.Entities.Count,
            eventRow.Status,
            eventRow.CorrelationId);
        var affectedEntities = eventRow.Entities
            .Select(entity => new AuditAffectedEntityReadModel(
                entity.Id,
                entity.EntityType,
                entity.EntityId,
                entity.OperationType,
                entity.FieldsChangedCount,
                entity.IsInternalOperation,
                entity.InternalOperationName,
                entity.FieldChanges
                    .Select(change => new AuditEventFieldChangeReadModel(
                        change.FieldName,
                        change.OldValue,
                        change.NewValue,
                        change.OldDisplayValue,
                        change.NewDisplayValue,
                        change.ReferenceEntityType,
                        change.IsReferenceField))
                    .ToArray()))
            .ToArray();

        var result = new AuditEventDetailsReadModel(
            listItem,
            outletName,
            eventRow.IpAddress,
            eventRow.RawIp,
            eventRow.LocalMachineIp,
            eventRow.IsLocalRequest,
            eventRow.DeviceName,
            eventRow.Browser,
            eventRow.Os,
            eventRow.RequestMethod,
            eventRow.RequestPath,
            eventRow.ErrorMessage,
            affectedEntities);
        if (confidential)
            await RecordAccessAsync(access, "detail", id, 1, cancellationToken, HashOutput(result));
        return result;
    }

    private Task<bool> RecordAccessAsync(AuditReadAccess access, string action, long? id,
        int count, CancellationToken cancellationToken, string? outputHash = null) =>
        requiredAudit.ExecuteAsync(new RequiredAuditOperation(
            access.BusinessId ?? throw new UnauthorizedAccessException("Confidential audit requires Business scope."),
            action, "hr", $"Confidential audit {action}; count {count}",
            "ConfidentialAuditAccess", id?.ToString(),
            [new AuditEventEntityWriteRequest("ConfidentialAuditAccess", id?.ToString() ?? string.Empty,
                action, Fields: [
                    new AuditEventFieldWriteRequest("RecordCount", null, count),
                    new AuditEventFieldWriteRequest("OutputHash", null, outputHash)])]),
            _ => Task.FromResult(true), cancellationToken);

    private static string HashOutput<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static readonly string[] HrTypes = ConfidentialAuditPolicy.HrEntityTypes
        .Select(type => type.ToLowerInvariant()).ToArray();
    private static readonly string[] PayrollTypes = ConfidentialAuditPolicy.PayrollEntityTypes
        .Select(type => type.ToLowerInvariant()).ToArray();

    private static readonly System.Linq.Expressions.Expression<Func<AuditEvent, bool>> HrExpression = row =>
        row.Module.ToLower() == "hr"
        || (row.PrimaryEntityType != null && (row.PrimaryEntityType.ToLower().StartsWith("hr")
            || row.PrimaryEntityType.ToLower().StartsWith("employee")
            || Enumerable.Contains(HrTypes, row.PrimaryEntityType.ToLower())))
        || row.Entities.Any(entity => entity.EntityType.ToLower().StartsWith("hr")
            || entity.EntityType.ToLower().StartsWith("employee")
            || Enumerable.Contains(HrTypes, entity.EntityType.ToLower()));

    private static readonly System.Linq.Expressions.Expression<Func<AuditEvent, bool>> PayrollExpression = row =>
        row.Module.ToLower() == "payroll"
        || (row.PrimaryEntityType != null && (row.PrimaryEntityType.ToLower().StartsWith("payroll")
            || row.PrimaryEntityType.ToLower().StartsWith("salary") || row.PrimaryEntityType.ToLower().StartsWith("payslip")
            || Enumerable.Contains(PayrollTypes, row.PrimaryEntityType.ToLower())))
        || row.Entities.Any(entity => entity.EntityType.ToLower().StartsWith("payroll")
            || entity.EntityType.ToLower().StartsWith("salary") || entity.EntityType.ToLower().StartsWith("payslip")
            || Enumerable.Contains(PayrollTypes, entity.EntityType.ToLower()));

    private static readonly System.Linq.Expressions.Expression<Func<AuditEvent, bool>> ConfidentialExpression =
        Combine(HrExpression, PayrollExpression);

    private static bool IsConfidentialExpression(AuditEvent row) =>
        ConfidentialAuditPolicy.IsHr(row.Module, row.PrimaryEntityType)
        || ConfidentialAuditPolicy.IsPayroll(row.Module, row.PrimaryEntityType)
        || row.Entities.Any(entity => ConfidentialAuditPolicy.IsHrEntity(entity.EntityType)
            || ConfidentialAuditPolicy.IsPayrollEntity(entity.EntityType));

    private static IQueryable<AuditEvent> ApplyAccess(IQueryable<AuditEvent> query, AuditReadAccess access)
    {
        var hrDenied = query.Where(HrExpression);
        var payrollDenied = query.Where(PayrollExpression);
        var confidential = query.Where(ConfidentialExpression);
        return query.Where(row =>
            (!confidential.Any(candidate => candidate.Id == row.Id)
                || (access.BusinessId.HasValue && row.BusinessId == access.BusinessId
                    && (access.AllLocations || (access.OutletId.HasValue && row.OutletId == access.OutletId))))
            && (access.CanReadHr || !hrDenied.Any(candidate => candidate.Id == row.Id))
            && (access.CanReadPayroll || !payrollDenied.Any(candidate => candidate.Id == row.Id)));
    }

    private static System.Linq.Expressions.Expression<Func<AuditEvent, bool>> Combine(
        System.Linq.Expressions.Expression<Func<AuditEvent, bool>> left,
        System.Linq.Expressions.Expression<Func<AuditEvent, bool>> right)
    {
        var rewritten = new ReplaceParameter(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<AuditEvent, bool>>(
            System.Linq.Expressions.Expression.OrElse(left.Body, rewritten), left.Parameters);
    }

    private sealed class ReplaceParameter(
        System.Linq.Expressions.ParameterExpression from,
        System.Linq.Expressions.ParameterExpression to) : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(
            System.Linq.Expressions.ParameterExpression node) => node == from ? to : node;
    }
}
