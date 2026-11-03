using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollSalaryRevisionStore(RetailPOSDbContext db, TimeProvider? clock = null)
    : IPayrollSalaryRevisionStore, IApprovalKernelWorkflow, IApprovalTargetSnapshotReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string TargetType = PayrollSalaryRevisionApprovalConstants.TargetType;

    public async Task<PayrollSalaryRevisionRequestResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollSalaryRevisionRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await LockEmployeeAsync(businessId, request.EmployeeId, cancellationToken);
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == request.EmployeeId
                && period.StartDate <= request.EffectiveFrom
                && (period.EndDate == null || period.EndDate >= request.EffectiveFrom), cancellationToken))
            throw new ArgumentException("The employee is not active and employed on the requested effective date.");

        var key = request.IdempotencyKey.Trim();
        var payloadHash = Hash(Normalize(request));
        var priorRequest = await db.PayrollSalaryRevisionRequests.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.EmployeeId == request.EmployeeId && row.IdempotencyKey == key, cancellationToken);
        if (priorRequest is not null)
        {
            if (!FixedHashEquals(priorRequest.IdempotencyPayloadHash, payloadHash))
                throw new PayrollSalaryConflictException(
                    "The idempotency key was already used with a different salary revision payload.");
            return (await MapAsync(priorRequest, cancellationToken)) with { IsReplay = true };
        }

        var source = await db.PayrollEmployeeSalaryVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == request.EmployeeId
                && row.EffectiveFrom < request.EffectiveFrom)
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PayrollSalaryConflictException(
                "No approved salary version is effective before the requested revision date.");
        if (source.Id != request.SourceSalaryVersionId)
            throw new PayrollSalaryConflictException(
                "The source salary version is stale for the requested effective date.");
        if (await db.PayrollEmployeeSalaryVersions.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == request.EmployeeId
                && row.EffectiveFrom >= request.EffectiveFrom, cancellationToken))
            throw new PayrollSalaryConflictException(
                "A revision must follow the latest approved salary version to preserve successor history.");
        if (await db.PayrollSalaryRevisionRequests.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == request.EmployeeId
                && row.Status == "PendingApproval", cancellationToken))
            throw new PayrollSalaryConflictException(
                "A salary revision is already pending approval for this employee.");

        var currentComponents = ReadComponents(source.ComponentSnapshotJson);
        var requestedIds = request.Components.Select(line => line.SalaryComponentId).Order().ToArray();
        var currentIds = currentComponents.Select(line => line.SalaryComponentId).Order().ToArray();
        if (!requestedIds.SequenceEqual(currentIds))
            throw new ArgumentException(
                "A revision must retain the source salary's components and cannot alter its structure.");
        var input = request.Components.ToDictionary(line => line.SalaryComponentId, line => line.Value);
        var proposed = currentComponents.Select(component =>
        {
            var supplied = input[component.SalaryComponentId];
            var calculated = component.ValueType is "Formula" or "Calculated";
            if (calculated && supplied.HasValue)
                throw new ArgumentException(
                    $"Calculated component {component.Code} cannot have a manually proposed value.");
            var value = calculated ? component.Value : supplied;
            if (!calculated && !value.HasValue)
                throw new ArgumentException($"Component {component.Code} requires a proposed value.");
            ValidateValue(value, component.Code);
            return component with { Value = value };
        }).ToArray();

        var locationId = await ResolveEmployeeLocationAsync(
            businessId, request.EmployeeId, request.EffectiveFrom, cancellationToken);
        var reason = request.Reason.Trim();
        var requestHash = Hash(new
        {
            businessId,
            request.EmployeeId,
            SourceSalaryVersionId = source.Id,
            request.EffectiveFrom,
            locationId,
            ProposedComponents = proposed,
            Reason = reason
        });
        var row = new PayrollSalaryRevisionRequest
        {
            BusinessId = businessId,
            EmployeeId = request.EmployeeId,
            SourceSalaryVersionId = source.Id,
            EffectiveFrom = request.EffectiveFrom,
            OperationalLocationProjectionId = locationId,
            StructureSnapshotJson = source.StructureSnapshotJson,
            CurrentComponentSnapshotJson = source.ComponentSnapshotJson,
            ProposedComponentSnapshotJson = JsonSerializer.Serialize(proposed, JsonOptions),
            RequestHash = requestHash,
            IdempotencyPayloadHash = payloadHash,
            IdempotencyKey = key,
            Reason = reason,
            Status = "Draft",
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc(),
            Version = 1
        };
        db.PayrollSalaryRevisionRequests.Add(row);
        await SaveAsync(cancellationToken);
        return await MapAsync(row, cancellationToken);
    }

    public async Task<PayrollSalaryRevisionRequestResponse> RequestAsync(
        long businessId, long requestId, CancellationToken cancellationToken)
    {
        var row = await db.PayrollSalaryRevisionRequests.AsNoTracking()
            .Include(request => request.ApprovalInstance)
            .ThenInclude(instance => instance!.Steps)
            .SingleOrDefaultAsync(request => request.BusinessId == businessId && request.Id == requestId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Salary revision request was not found in this Business.");
        return await MapAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollSalaryRevisionRequestResponse>> EmployeeRequestsAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollSalaryRevisionRequests.AsNoTracking()
            .Include(request => request.ApprovalInstance)
            .ThenInclude(instance => instance!.Steps)
            .Where(request => request.BusinessId == businessId && request.EmployeeId == employeeId)
            .OrderByDescending(request => request.CreatedAtUtc).ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);
        var results = new List<PayrollSalaryRevisionRequestResponse>(rows.Count);
        foreach (var row in rows)
            results.Add(await MapAsync(row, cancellationToken));
        return results;
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand approval,
        CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.SalaryRevision)
            return null;
        if (approval.TargetType != TargetType || !long.TryParse(approval.TargetId, out var requestId)
            || requestId <= 0 || approval.ScopeType != ApprovalScopeType.Location
            || !long.TryParse(approval.ScopeReference, out var scopeLocationId) || scopeLocationId <= 0)
            throw new ArgumentException("Salary revision approval requires a valid location-scoped request.");

        var head = await db.PayrollSalaryRevisionRequests.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == requestId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Salary revision request was not found in this Business.");
        await LockEmployeeAsync(businessId, head.EmployeeId, cancellationToken);
        var row = await db.PayrollSalaryRevisionRequests.SingleOrDefaultAsync(item =>
                item.BusinessId == businessId && item.Id == requestId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Salary revision request was not found in this Business.");
        if (row.Status != "Draft" || row.CreatedByUserId != requesterUserId
            || row.OperationalLocationProjectionId != scopeLocationId
            || row.Version != approval.TargetVersion || !FixedHashEquals(row.RequestHash, approval.RequestHash))
            throw new ApprovalKernelConflictException(
                "Salary revision request changed, is not a draft, or does not match its approval scope.");
        await EnsureSourceCurrentAsync(row, cancellationToken);
        if (await db.PayrollSalaryRevisionRequests.AnyAsync(other =>
                other.BusinessId == businessId && other.EmployeeId == row.EmployeeId
                && other.Id != row.Id && other.Status == "PendingApproval", cancellationToken))
            throw new ApprovalKernelConflictException(
                "Another salary revision is already pending approval for this employee.");

        row.Status = "PendingApproval";
        row.SubmittedByUserId = requesterUserId;
        row.SubmittedAtUtc = NowUtc();
        row.SubmittedTargetVersion = approval.TargetVersion;
        row.Version = checked(row.Version + 1);
        return new ApprovalTargetSnapshot(approval.TargetVersion, row.RequestHash);
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.SalaryRevision)
            return Task.CompletedTask;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Salary revision approval instance has an invalid target.");
        var request = db.PayrollSalaryRevisionRequests.Local.SingleOrDefault(row =>
                row.BusinessId == businessId && row.Id == requestId)
            ?? throw new ApprovalKernelConflictException("Prepared salary revision request was not tracked.");
        if (request.Status != "PendingApproval" || request.SubmittedTargetVersion != instance.TargetVersion)
            throw new ApprovalKernelConflictException(
                "Prepared salary revision no longer matches its approval instance.");
        request.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.SalaryRevision)
            return;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Salary revision approval instance has an invalid target.");
        var head = await db.PayrollSalaryRevisionRequests.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == requestId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Salary revision target was not found.");
        await LockEmployeeAsync(businessId, head.EmployeeId, cancellationToken);
        var row = await db.PayrollSalaryRevisionRequests.SingleOrDefaultAsync(item =>
                item.BusinessId == businessId && item.Id == requestId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Salary revision target was not found.");
        if (row.Status != "PendingApproval" || row.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Salary revision request is already terminal or mismatched.");

        var now = NowUtc();
        row.DecidedByUserId = actorUserId;
        row.DecidedAtUtc = now;
        row.DecisionReason = reason?.Trim();
        row.Version = checked(row.Version + 1);
        if (instance.Status == ApprovalInstanceStatus.Approved)
        {
            await EnsureSourceCurrentAsync(row, cancellationToken);
            if (await db.PayrollEmployeeSalaryVersions.AnyAsync(salary =>
                    salary.BusinessId == businessId && salary.EmployeeId == row.EmployeeId
                    && salary.EffectiveFrom == row.EffectiveFrom, cancellationToken))
                throw new ApprovalKernelConflictException(
                    "An approved salary version already exists for this effective date.");
            var source = await db.PayrollEmployeeSalaryVersions.AsNoTracking()
                .SingleAsync(salary => salary.BusinessId == businessId
                    && salary.Id == row.SourceSalaryVersionId, cancellationToken);
            row.Status = "Approved";
            db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
            {
                BusinessId = businessId,
                EmployeeId = row.EmployeeId,
                SalarySetupRequestId = source.SalarySetupRequestId,
                SalaryRevisionRequestId = row.Id,
                PredecessorSalaryVersionId = source.Id,
                SalaryStructureId = source.SalaryStructureId,
                SalaryStructureVersion = source.SalaryStructureVersion,
                EffectiveFrom = row.EffectiveFrom,
                OperationalLocationProjectionId = row.OperationalLocationProjectionId,
                StructureSnapshotJson = row.StructureSnapshotJson,
                ComponentSnapshotJson = row.ProposedComponentSnapshotJson,
                ApprovalInstanceId = instance.Id,
                ApprovedByUserId = actorUserId,
                ApprovedAtUtc = now,
                CreatedAtUtc = now
            });
        }
        else if (instance.Status == ApprovalInstanceStatus.Rejected)
            row.Status = "Rejected";
        else if (instance.Status == ApprovalInstanceStatus.Cancelled)
            row.Status = "Cancelled";
        else
            throw new ApprovalKernelConflictException("Salary revision approval did not reach a terminal state.");
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != TargetType || !long.TryParse(targetId, out var requestId))
            return null;
        var row = await db.PayrollSalaryRevisionRequests.AsNoTracking().SingleOrDefaultAsync(item =>
            item.BusinessId == businessId && item.Id == requestId, cancellationToken);
        if (row is null)
            return null;
        var version = row.Status == "Draft" ? row.Version : row.SubmittedTargetVersion;
        return version.HasValue ? new ApprovalTargetSnapshot(version.Value, row.RequestHash) : null;
    }

    private async Task EnsureSourceCurrentAsync(
        PayrollSalaryRevisionRequest request, CancellationToken cancellationToken)
    {
        var sourceId = await db.PayrollEmployeeSalaryVersions.AsNoTracking()
            .Where(row => row.BusinessId == request.BusinessId && row.EmployeeId == request.EmployeeId
                && row.EffectiveFrom < request.EffectiveFrom)
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .Select(row => (long?)row.Id).FirstOrDefaultAsync(cancellationToken);
        if (sourceId != request.SourceSalaryVersionId)
            throw new ApprovalKernelConflictException(
                "The effective salary changed after this revision was proposed; create a new request.");
    }

    private async Task<PayrollSalaryRevisionRequestResponse> MapAsync(
        PayrollSalaryRevisionRequest row, CancellationToken cancellationToken)
    {
        var current = ReadComponents(row.CurrentComponentSnapshotJson);
        var proposed = ReadComponents(row.ProposedComponentSnapshotJson);
        var decisions = row.ApprovalInstanceId is > 0
            ? await db.ApprovalDecisions.AsNoTracking()
                .Where(decision => decision.BusinessId == row.BusinessId
                    && decision.ApprovalInstanceId == row.ApprovalInstanceId)
                .OrderBy(decision => decision.OccurredAtUtc).ThenBy(decision => decision.Id)
                .Select(decision => new PayrollSalaryDecisionResponse(
                    decision.Decision.ToString(), decision.ActorRealUserId,
                    decision.OccurredAtUtc, decision.Reason))
                .ToArrayAsync(cancellationToken)
            : [];
        var steps = row.ApprovalInstance?.Steps.OrderBy(step => step.Sequence)
            .Select(step => new PayrollSalaryApprovalStepResponse(
                step.Sequence, step.Decision.ToString(), step.DecisionByUserId,
                step.DecisionAtUtc, step.Comment)).ToArray() ?? [];
        return new PayrollSalaryRevisionRequestResponse(
            row.Id, row.BusinessId, row.EmployeeId, row.SourceSalaryVersionId, row.EffectiveFrom,
            row.OperationalLocationProjectionId, MapComponents(current), MapComponents(proposed),
            row.Reason, row.Status, row.ApprovalInstanceId, row.CreatedByUserId, row.CreatedAtUtc,
            row.SubmittedByUserId, row.SubmittedAtUtc, row.DecidedByUserId, row.DecidedAtUtc,
            row.DecisionReason, row.Version, steps, decisions);
    }

    private async Task<long> ResolveEmployeeLocationAsync(
        long businessId, long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        var assignments = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary" && row.Status == "Effective"
                && row.EffectiveFrom <= effectiveOn
                && (row.EffectiveTo == null || row.EffectiveTo >= effectiveOn))
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .Take(2).Select(row => row.OperationalLocationProjectionId).ToArrayAsync(cancellationToken);
        if (assignments.Length > 1)
            throw new InvalidOperationException("Employee has overlapping effective Primary location assignments.");
        long? locationId = assignments.Length == 0 ? null : assignments[0];
        if (locationId is not > 0)
        {
            if (await db.HrEmployeeLocationAssignments.AsNoTracking().AnyAsync(row =>
                    row.BusinessId == businessId && row.EmployeeId == employeeId
                    && row.AssignmentType == "Primary", cancellationToken))
                throw new ArgumentException("The employee has no Primary operational location on the effective date.");
            locationId = employee.CurrentPrimaryLocationProjectionId;
        }
        if (locationId is not > 0 || !await db.OperationalLocationProjections.AsNoTracking().AnyAsync(
                row => row.BusinessId == businessId && row.Id == locationId.Value, cancellationToken))
            throw new ArgumentException("The employee has no valid Primary operational location.");
        return locationId.Value;
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Salary revision writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
        {
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE business_id = {businessId} AND id = {employeeId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                throw new KeyNotFoundException("Employee was not found in this Business.");
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException(
                "Salary revision serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollSalaryConflictException(
                "Salary revision was changed by another request.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollSalaryConflictException(
                "A salary revision identity already exists for this employee and effective date.", exception);
        }
    }

    private DateTime NowUtc() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static void Validate(CreatePayrollSalaryRevisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId <= 0 || request.SourceSalaryVersionId <= 0)
            throw new ArgumentException("Employee and source salary version are required.");
        if (request.Components is null || request.Components.Count is < 1 or > 100
            || request.Components.Any(line => line.SalaryComponentId <= 0)
            || request.Components.Select(line => line.SalaryComponentId).Distinct().Count()
                != request.Components.Count)
            throw new ArgumentException("A salary revision requires 1-100 distinct component values.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A reason of at most 1000 characters is required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("An idempotency key of at most 150 characters is required.");
    }

    private static void ValidateValue(decimal? value, string code)
    {
        if (value is < 0 || value > 99_999_999_999_999.9999m
            || value.HasValue && decimal.Round(value.Value, 4) != value.Value)
            throw new ArgumentException(
                $"Component {code} must be non-negative, fit decimal(18,4), and have at most four decimal places.");
    }

    private static object Normalize(CreatePayrollSalaryRevisionRequest request) => new
    {
        request.EmployeeId,
        request.SourceSalaryVersionId,
        request.EffectiveFrom,
        Components = request.Components.OrderBy(row => row.SalaryComponentId)
            .Select(row => new { row.SalaryComponentId, row.Value }).ToArray(),
        Reason = request.Reason.Trim()
    };

    private static IReadOnlyList<PayrollSalarySetupComponentSnapshot> ReadComponents(string json) =>
        JsonSerializer.Deserialize<PayrollSalarySetupComponentSnapshot[]>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored salary component evidence is invalid.");

    private static IReadOnlyList<PayrollSalarySetupComponentResponse> MapComponents(
        IReadOnlyList<PayrollSalarySetupComponentSnapshot> components) =>
        components.Select(row => new PayrollSalarySetupComponentResponse(
            row.SalaryComponentId, row.Code, row.Name, row.ComponentType, row.ValueType, row.Value)).ToArray();

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))));

    private static bool FixedHashEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToUpperInvariant()), Encoding.ASCII.GetBytes(right.ToUpperInvariant()));
}
