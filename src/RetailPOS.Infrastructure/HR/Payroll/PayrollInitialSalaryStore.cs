using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed partial class PayrollSalarySetupStore
{
    public const string TargetType = PayrollSalarySetupApprovalConstants.TargetType;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<long> EmployeePrimaryLocationAsync(
        long businessId, long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken) =>
        await ResolveEmployeeLocationAsync(businessId, employeeId, effectiveOn, cancellationToken);

    public async Task<PayrollSalarySetupRequestResponse> CreateInitialRequestAsync(
        long businessId,
        long actorUserId,
        CreatePayrollSalarySetupRequest request,
        CancellationToken cancellationToken)
    {
        await LockEmployeeAsync(businessId, request.EmployeeId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == employee.Id
                && period.StartDate <= request.EffectiveFrom
                && (period.EndDate == null || period.EndDate >= request.EffectiveFrom),
                cancellationToken))
            throw new ArgumentException("The employee is not active and employed on the requested effective date.");

        var locationId = await ResolveEmployeeLocationAsync(
            businessId, request.EmployeeId, request.EffectiveFrom, cancellationToken);
        var normalized = Normalize(request);
        var idempotencyHash = Hash(normalized);
        var prior = await db.PayrollSalarySetupRequests.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.EmployeeId == request.EmployeeId && row.IdempotencyKey == request.IdempotencyKey.Trim(),
                cancellationToken);
        if (prior is not null)
        {
            if (!FixedHashEquals(prior.IdempotencyPayloadHash, idempotencyHash))
                throw new PayrollSalaryConflictException(
                    "The idempotency key was already used with a different salary setup payload.");
            return (await MapRequestAsync(prior, cancellationToken)) with { IsReplay = true };
        }

        if (await db.PayrollEmployeeSalaryVersions.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == request.EmployeeId, cancellationToken))
            throw new PayrollSalaryConflictException(
                "An effective initial salary already exists for this employee; it cannot be replaced.");
        if (await db.PayrollSalarySetupRequests.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == request.EmployeeId
                && row.Status == "PendingApproval", cancellationToken))
            throw new PayrollSalaryConflictException(
                "An initial salary setup is already pending approval for this employee.");

        var structure = await db.PayrollSalaryStructures.AsNoTracking()
            .Include(row => row.Lines)
            .SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.SalaryStructureId && row.IsActive,
                cancellationToken)
            ?? throw new KeyNotFoundException("Active salary structure was not found in this Business.");
        var requestedIds = request.Components.Select(item => item.SalaryComponentId).Order().ToArray();
        var structureIds = structure.Lines.Select(line => line.SalaryComponentId).Order().ToArray();
        if (!requestedIds.SequenceEqual(structureIds))
            throw new ArgumentException(
                "A proposed value is required for every structure component and no other component is allowed.");

        var componentIds = structure.Lines.Select(line => line.SalaryComponentId).ToList();
        var components = await db.PayrollSalaryComponents.AsNoTracking()
            .Where(component => component.BusinessId == businessId
                && component.IsActive && componentIds.Contains(component.Id))
            .ToDictionaryAsync(component => component.Id, cancellationToken);
        if (components.Count != componentIds.Count)
            throw new ArgumentException("The salary structure references an inactive or invalid component.");

        var inputValues = request.Components.ToDictionary(row => row.SalaryComponentId, row => row.Value);
        var snapshots = structure.Lines.OrderBy(line => line.CalculationOrder).ThenBy(line => line.Id)
            .Select(line =>
            {
                var component = components[line.SalaryComponentId];
                var supplied = inputValues[line.SalaryComponentId];
                var value = supplied ?? line.DefaultValue;
                var calculated = component.ValueType is "Formula" or "Calculated";
                if (calculated && supplied.HasValue)
                    throw new ArgumentException(
                        $"Calculated component {component.Code} cannot have a manually proposed value.");
                if (!calculated && !value.HasValue)
                    throw new ArgumentException($"Component {component.Code} requires a proposed value.");
                ValidateValue(value, component.Code);
                return new PayrollSalarySetupComponentSnapshot(
                    component.Id, component.Code, component.Name, component.ComponentType, component.ValueType,
                    component.TaxTreatmentCode, component.IsRecurring, line.CalculationOrder,
                    line.FormulaOverride ?? component.FormulaExpression, value);
            }).ToArray();

        var structureSnapshot = new SalaryStructureSnapshot(
            structure.Id, structure.Version, structure.Code, structure.Name,
            structure.Lines.OrderBy(line => line.CalculationOrder).ThenBy(line => line.Id)
                .Select(line => new SalaryStructureLineSnapshot(
                    line.SalaryComponentId, line.DefaultValue, line.FormulaOverride, line.CalculationOrder))
                .ToArray());
        var structureJson = JsonSerializer.Serialize(structureSnapshot, JsonOptions);
        var componentJson = JsonSerializer.Serialize(snapshots, JsonOptions);
        var normalizedReason = request.Reason.Trim();
        var requestHash = Hash(new
        {
            businessId,
            request.EmployeeId,
            structureSnapshot,
            request.EffectiveFrom,
            locationId,
            Components = snapshots,
            Reason = normalizedReason
        });

        var row = new PayrollSalarySetupRequest
        {
            BusinessId = businessId,
            EmployeeId = request.EmployeeId,
            SalaryStructureId = structure.Id,
            SalaryStructureVersion = structure.Version,
            EffectiveFrom = request.EffectiveFrom,
            OperationalLocationProjectionId = locationId,
            StructureSnapshotJson = structureJson,
            ComponentSnapshotJson = componentJson,
            RequestHash = requestHash,
            IdempotencyPayloadHash = idempotencyHash,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            Reason = normalizedReason,
            Status = "Draft",
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc(),
            Version = 1
        };
        db.PayrollSalarySetupRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return await MapRequestAsync(row, cancellationToken);
    }

    public async Task<PayrollSalarySetupRequestResponse> RequestAsync(
        long businessId, long id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollSalarySetupRequests.AsNoTracking()
            .Include(request => request.ApprovalInstance)
            .ThenInclude(instance => instance!.Steps)
            .SingleOrDefaultAsync(request => request.BusinessId == businessId && request.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Salary setup request was not found in this Business.");
        return await MapRequestAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollSalarySetupRequestResponse>> EmployeeRequestsAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var rows = await db.PayrollSalarySetupRequests.AsNoTracking()
            .Include(request => request.ApprovalInstance)
            .ThenInclude(instance => instance!.Steps)
            .Where(request => request.BusinessId == businessId && request.EmployeeId == employeeId)
            .OrderByDescending(request => request.CreatedAtUtc).ThenByDescending(request => request.Id)
            .ToListAsync(cancellationToken);
        var result = new List<PayrollSalarySetupRequestResponse>(rows.Count);
        foreach (var row in rows)
            result.Add(await MapRequestAsync(row, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<PayrollEmployeeSalaryResponse>> EmployeeSalaryHistoryAsync(
        long businessId, long employeeId, DateOnly? effectiveOn, CancellationToken cancellationToken)
    {
        var query = db.PayrollEmployeeSalaryVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId);
        if (effectiveOn.HasValue)
            query = query.Where(row => row.EffectiveFrom <= effectiveOn.Value);
        var rows = await query.OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var structure = JsonSerializer.Deserialize<SalaryStructureSnapshot>(
                row.StructureSnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("Stored salary structure evidence is invalid.");
            var components = ReadComponents(row.ComponentSnapshotJson);
            return new PayrollEmployeeSalaryResponse(
                row.Id, row.BusinessId, row.EmployeeId, row.SalarySetupRequestId,
                row.SalaryStructureId, row.SalaryStructureVersion, row.EffectiveFrom,
                row.OperationalLocationProjectionId, structure.Code, structure.Name,
                MapComponents(components), row.ApprovalInstanceId, row.ApprovedByUserId, row.ApprovedAtUtc,
                row.SalaryRevisionRequestId, row.PredecessorSalaryVersionId);
        }).ToArray();
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand approval, CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.SalarySetup)
            return null;
        if (approval.TargetType != TargetType || !long.TryParse(approval.TargetId, out var requestId)
            || requestId <= 0 || approval.ScopeType != ApprovalScopeType.Location
            || !long.TryParse(approval.ScopeReference, out var scopeLocationId) || scopeLocationId <= 0)
            throw new ArgumentException("Salary setup approval requires a valid location-scoped request.");

        var row = await db.PayrollSalarySetupRequests.SingleOrDefaultAsync(request =>
                request.BusinessId == businessId && request.Id == requestId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Salary setup request was not found in this Business.");
        await LockEmployeeAsync(businessId, row.EmployeeId, cancellationToken);
        row = await db.PayrollSalarySetupRequests.SingleOrDefaultAsync(request =>
                request.BusinessId == businessId && request.Id == requestId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Salary setup request was not found in this Business.");
        if (row.Status != "Draft" || row.CreatedByUserId != requesterUserId
            || row.OperationalLocationProjectionId != scopeLocationId
            || row.Version != approval.TargetVersion
            || !FixedHashEquals(row.RequestHash, approval.RequestHash))
            throw new ApprovalKernelConflictException(
                "Salary setup request changed, is not a draft, or does not match its approval scope.");
        if (await db.PayrollEmployeeSalaryVersions.AnyAsync(salary =>
                salary.BusinessId == businessId && salary.EmployeeId == row.EmployeeId, cancellationToken))
            throw new ApprovalKernelConflictException(
                "An effective initial salary already exists for this employee.");
        if (await db.PayrollSalarySetupRequests.AnyAsync(other =>
                other.BusinessId == businessId && other.EmployeeId == row.EmployeeId && other.Id != row.Id
                && other.Status == "PendingApproval", cancellationToken))
            throw new ApprovalKernelConflictException(
                "Another initial salary setup is already pending approval for this employee.");

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
        if (instance.ProcessType != ApprovalProcessType.SalarySetup)
            return Task.CompletedTask;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Salary approval instance has an invalid target.");
        var request = db.PayrollSalarySetupRequests.Local.SingleOrDefault(row =>
                row.BusinessId == businessId && row.Id == requestId)
            ?? throw new ApprovalKernelConflictException("Prepared salary setup request was not tracked.");
        if (request.Status != "PendingApproval" || request.SubmittedTargetVersion != instance.TargetVersion)
            throw new ApprovalKernelConflictException("Prepared salary setup no longer matches its approval instance.");
        request.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.SalarySetup)
            return;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var requestId))
            throw new ApprovalKernelConflictException("Salary approval instance has an invalid target.");
        var requestHead = await db.PayrollSalarySetupRequests.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == requestId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Salary setup target was not found.");
        await LockEmployeeAsync(businessId, requestHead.EmployeeId, cancellationToken);
        var row = await db.PayrollSalarySetupRequests.SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.Id == requestId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Salary setup target was not found.");
        if (row.Status != "PendingApproval" || row.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Salary setup request is already terminal or mismatched.");

        var now = NowUtc();
        row.DecidedByUserId = actorUserId;
        row.DecidedAtUtc = now;
        row.DecisionReason = reason?.Trim();
        row.Version = checked(row.Version + 1);
        if (instance.Status == ApprovalInstanceStatus.Approved)
        {
            if (await db.PayrollEmployeeSalaryVersions.AnyAsync(salary =>
                    salary.BusinessId == businessId && salary.EmployeeId == row.EmployeeId,
                    cancellationToken))
                throw new ApprovalKernelConflictException(
                    "An effective initial salary already exists; approval cannot replace it.");
            row.Status = "Approved";
            db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
            {
                BusinessId = businessId,
                EmployeeId = row.EmployeeId,
                SalarySetupRequestId = row.Id,
                SalaryStructureId = row.SalaryStructureId,
                SalaryStructureVersion = row.SalaryStructureVersion,
                EffectiveFrom = row.EffectiveFrom,
                OperationalLocationProjectionId = row.OperationalLocationProjectionId,
                StructureSnapshotJson = row.StructureSnapshotJson,
                ComponentSnapshotJson = row.ComponentSnapshotJson,
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
            throw new ApprovalKernelConflictException("Salary setup approval did not reach a terminal state.");
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != TargetType || !long.TryParse(targetId, out var requestId))
            return null;
        var row = await db.PayrollSalarySetupRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == requestId, cancellationToken);
        if (row is null)
            return null;
        var targetVersion = row.Status == "Draft" ? row.Version : row.SubmittedTargetVersion;
        return targetVersion.HasValue ? new ApprovalTargetSnapshot(targetVersion.Value, row.RequestHash) : null;
    }

    private async Task<PayrollSalarySetupRequestResponse> MapRequestAsync(
        PayrollSalarySetupRequest row, CancellationToken cancellationToken)
    {
        var structure = JsonSerializer.Deserialize<SalaryStructureSnapshot>(row.StructureSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored salary structure evidence is invalid.");
        var components = ReadComponents(row.ComponentSnapshotJson);
        var decisions = row.ApprovalInstanceId is > 0
            ? await db.ApprovalDecisions.AsNoTracking()
                .Where(decision => decision.BusinessId == row.BusinessId
                    && decision.ApprovalInstanceId == row.ApprovalInstanceId)
                .OrderBy(decision => decision.OccurredAtUtc).ThenBy(decision => decision.Id)
                .Select(decision => new PayrollSalaryDecisionResponse(
                    decision.Decision.ToString(), decision.ActorRealUserId, decision.OccurredAtUtc,
                    decision.Reason))
                .ToArrayAsync(cancellationToken)
            : [];
        var steps = row.ApprovalInstance?.Steps.OrderBy(step => step.Sequence)
            .Select(step => new PayrollSalaryApprovalStepResponse(
                step.Sequence, step.Decision.ToString(), step.DecisionByUserId,
                step.DecisionAtUtc, step.Comment)).ToArray() ?? [];
        return new PayrollSalarySetupRequestResponse(
            row.Id, row.BusinessId, row.EmployeeId, row.SalaryStructureId, row.SalaryStructureVersion,
            row.EffectiveFrom, row.OperationalLocationProjectionId, structure.Code, structure.Name,
            MapComponents(components), row.Reason, row.Status, row.ApprovalInstanceId,
            row.CreatedByUserId, row.CreatedAtUtc, row.SubmittedByUserId, row.SubmittedAtUtc,
            row.DecidedByUserId, row.DecidedAtUtc, row.DecisionReason, row.Version, steps, decisions);
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
            .Take(2).ToArrayAsync(cancellationToken);
        if (assignments.Length > 1)
            throw new InvalidOperationException("Employee has overlapping effective Primary location assignments.");
        var locationId = assignments.SingleOrDefault()?.OperationalLocationProjectionId;
        if (!locationId.HasValue)
        {
            var hasAssignments = await db.HrEmployeeLocationAssignments.AsNoTracking().AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary", cancellationToken);
            if (hasAssignments)
                throw new ArgumentException("The employee has no Primary operational location on the effective date.");
            locationId = employee.CurrentPrimaryLocationProjectionId;
        }
        if (locationId is not > 0 || !await db.Set<OperationalLocationProjection>().AsNoTracking()
                .AnyAsync(location => location.BusinessId == businessId && location.Id == locationId,
                    cancellationToken))
            throw new ArgumentException("The employee has no valid Primary operational location.");
        return locationId.Value;
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Salary setup writes require the audited transaction boundary.");
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
                "Salary setup serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private DateTime NowUtc() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static object Normalize(CreatePayrollSalarySetupRequest request) => new
    {
        request.EmployeeId,
        request.SalaryStructureId,
        request.EffectiveFrom,
        Components = request.Components.OrderBy(row => row.SalaryComponentId)
            .Select(row => new { row.SalaryComponentId, row.Value }).ToArray(),
        Reason = request.Reason.Trim()
    };

    private static void ValidateValue(decimal? value, string code)
    {
        if (value is < 0 || value > 99_999_999_999_999.9999m
            || value.HasValue && decimal.Round(value.Value, 4) != value.Value)
            throw new ArgumentException(
                $"Component {code} must be non-negative, fit decimal(18,4), and have at most four decimal places.");
    }

    private static IReadOnlyList<PayrollSalarySetupComponentSnapshot> ReadComponents(string json) =>
        JsonSerializer.Deserialize<PayrollSalarySetupComponentSnapshot[]>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored salary component evidence is invalid.");

    private static IReadOnlyList<PayrollSalarySetupComponentResponse> MapComponents(
        IReadOnlyList<PayrollSalarySetupComponentSnapshot> components) =>
        components.Select(row => new PayrollSalarySetupComponentResponse(
            row.SalaryComponentId, row.Code, row.Name, row.ComponentType, row.ValueType, row.Value)).ToArray();

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))));

    private static bool FixedHashEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToUpperInvariant()), Encoding.ASCII.GetBytes(right.ToUpperInvariant()));

    private sealed record SalaryStructureSnapshot(
        long Id,
        long Version,
        string Code,
        string Name,
        IReadOnlyList<SalaryStructureLineSnapshot> Lines);

    private sealed record SalaryStructureLineSnapshot(
        long SalaryComponentId,
        decimal? DefaultValue,
        string? FormulaOverride,
        int CalculationOrder);
}
