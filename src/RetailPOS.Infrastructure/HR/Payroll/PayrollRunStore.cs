using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollRunStore(
    RetailPOSDbContext db,
    PayrollRunValidationStore validation,
    IPayrollRunCalculationStore calculations)
    : IPayrollRunStore, IPayrollRunApprovalWorkflow
{
    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand request, CancellationToken cancellationToken)
    {
        if (request.ProcessType != ApprovalProcessType.PayrollRun
            || request.TargetType != PayrollRunApprovalConstants.TargetType
            || !long.TryParse(request.TargetId, out var runId))
            return null;

        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == runId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Payroll run was not found in this Business.");
        await calculations.CalculateAsync(businessId, run.PayrollPeriodId, run.Id, cancellationToken);
        var currentSnapshot = await BuildApprovalSnapshotAsync(businessId, run, cancellationToken);
        if (run.Status != PayrollRunStatuses.Draft
            || run.Version != request.TargetVersion
            || !FixedHashEquals(currentSnapshot.Hash, request.RequestHash))
            throw new PayrollRunConflictException(
                "Payroll run changed or is not a valid Draft for approval.");

        var response = await MapAsync(run, false, cancellationToken);
        var readiness = await validation.ValidateAsync(businessId, run.PayrollPeriodId, response,
            page: 1, pageSize: 1, cancellationToken);
        if (!readiness.CanSubmit)
            throw new PayrollRunConflictException(
                "Payroll run has blocking validation findings and cannot be submitted.");

        run.Status = PayrollRunStatuses.PendingApproval;
        run.SubmittedByUserId = requesterUserId;
        run.SubmittedAtUtc = DateTime.UtcNow;
        run.SubmittedTargetVersion = run.Version;
        run.ApprovalEvidenceHash = currentSnapshot.Hash;
        run.Version = checked(run.Version + 1);
        return currentSnapshot;
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.PayrollRun)
            return Task.CompletedTask;
        if (instance.TargetType != PayrollRunApprovalConstants.TargetType
            || !long.TryParse(instance.TargetId, out var runId))
            throw new ApprovalKernelConflictException("Payroll run approval instance has an invalid target.");
        var run = db.PayrollRuns.Local.SingleOrDefault(row => row.BusinessId == businessId && row.Id == runId)
            ?? throw new ApprovalKernelConflictException("Prepared Payroll run was not tracked.");
        if (run.Status != PayrollRunStatuses.PendingApproval
            || run.SubmittedTargetVersion != instance.TargetVersion
            || !FixedHashEquals(run.ApprovalEvidenceHash, instance.RequestHash))
            throw new ApprovalKernelConflictException(
                "Prepared Payroll run no longer matches its approval instance.");
        run.ApprovalInstanceId = instance.Id;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.PayrollRun)
            return;
        if (instance.TargetType != PayrollRunApprovalConstants.TargetType
            || !long.TryParse(instance.TargetId, out var runId))
            throw new ApprovalKernelConflictException("Payroll run approval instance has an invalid target.");
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var run = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == runId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Payroll run approval target was not found.");
        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == run.PayrollPeriodId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Payroll period approval target was not found.");
        if (period.Status != PayrollPeriodStatuses.Locked)
            throw new ApprovalKernelConflictException(
                "Payroll run decisions are allowed only while the Payroll period is locked.");
        if (run.Status != PayrollRunStatuses.PendingApproval
            || run.ApprovalInstanceId != instance.Id
            || run.SubmittedTargetVersion != instance.TargetVersion
            || !FixedHashEquals(run.ApprovalEvidenceHash, instance.RequestHash))
            throw new ApprovalKernelConflictException(
                "Payroll run is already terminal or its frozen evidence does not match approval.");
        var snapshot = await BuildApprovalSnapshotAsync(businessId, run, cancellationToken);
        if (!FixedHashEquals(snapshot.Hash, run.ApprovalEvidenceHash))
            throw new ApprovalKernelConflictException("Payroll run financial evidence changed after submission.");

        run.Status = instance.Status switch
        {
            ApprovalInstanceStatus.Approved => PayrollRunStatuses.Approved,
            ApprovalInstanceStatus.Rejected => PayrollRunStatuses.Rejected,
            ApprovalInstanceStatus.Cancelled => PayrollRunStatuses.Cancelled,
            _ => throw new ApprovalKernelConflictException(
                "Payroll run approval did not reach a terminal state.")
        };
        run.DecidedByUserId = actorUserId;
        run.DecidedAtUtc = DateTime.UtcNow;
        run.DecisionReason = reason?.Trim();
        run.Version = checked(run.Version + 1);
        if (run.Status == PayrollRunStatuses.Approved
            && !await db.PayrollRuns.AnyAsync(other =>
                other.BusinessId == businessId && other.PayrollPeriodId == run.PayrollPeriodId
                && other.Id != run.Id && other.Status != PayrollRunStatuses.Approved,
                cancellationToken))
        {
            period.Status = PayrollPeriodStatuses.Approved;
            period.ApprovedAtUtc = run.DecidedAtUtc;
            period.ApprovedByUserId = actorUserId;
            period.Version = checked(period.Version + 1);
        }
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != PayrollRunApprovalConstants.TargetType || !long.TryParse(targetId, out var runId))
            return null;
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == runId, cancellationToken);
        if (run is null)
            return null;
        if (run.Status == PayrollRunStatuses.PendingApproval
            && run.SubmittedTargetVersion.HasValue && run.ApprovalEvidenceHash is not null)
            return new ApprovalTargetSnapshot(run.SubmittedTargetVersion.Value, run.ApprovalEvidenceHash);
        var snapshot = await BuildApprovalSnapshotAsync(businessId, run, cancellationToken);
        return new ApprovalTargetSnapshot(run.Version, snapshot.Hash);
    }

    public async Task<PayrollRunResponse> CreateAsync(
        long businessId,
        long actorUserId,
        long periodId,
        CreatePayrollRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);

        var normalizedKey = request.IdempotencyKey.Trim();
        var payloadHash = HashPayload(periodId, request);
        var replay = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.PayrollPeriodId == periodId && row.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (replay is not null)
        {
            EnsureSamePayload(replay.IdempotencyPayloadHash, payloadHash);
            return await MapAsync(replay, isReplay: true, cancellationToken);
        }

        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == periodId, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (period.Version != request.ExpectedPeriodVersion)
            throw new PayrollRunConflictException("Payroll period changed since it was read.");
        if (period.Status != PayrollPeriodStatuses.Processing)
            throw new PayrollRunConflictException("Payroll runs can only be created for a Processing period.");
        if (request.ScopeType == PayrollRunScopeTypes.Location
            && !await db.OperationalLocationProjections.AsNoTracking().AnyAsync(location =>
                location.BusinessId == businessId && location.Id == request.ScopeId, cancellationToken))
            throw new KeyNotFoundException("Payroll run location was not found in this Business.");

        var monetaryConfigurations = await db.PayrollMonetaryConfigurationVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EffectiveFrom <= period.PeriodEnd)
            .OrderBy(row => row.EffectiveFrom).ThenBy(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        var effectiveMonetaryConfiguration = monetaryConfigurations
            .Where(row => row.EffectiveFrom <= period.PeriodStart)
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.VersionNo)
            .FirstOrDefault();
        if (effectiveMonetaryConfiguration is null
            || monetaryConfigurations.Any(row => row.EffectiveFrom > period.PeriodStart))
            throw new PayrollRunConflictException(
                "A single Payroll monetary configuration must cover the entire period.");
        if (effectiveMonetaryConfiguration.CurrencyCode.Length != 3
            || effectiveMonetaryConfiguration.CurrencyCode.Any(character => !char.IsAsciiLetter(character))
            || effectiveMonetaryConfiguration.CurrencyCode != effectiveMonetaryConfiguration.CurrencyCode.ToUpperInvariant()
            || effectiveMonetaryConfiguration.DecimalPrecision is < 0 or > 6
            || !Enum.TryParse<MidpointRounding>(
                effectiveMonetaryConfiguration.RoundingMode, out var roundingMode)
            || !Enum.IsDefined(roundingMode))
            throw new InvalidOperationException("Payroll monetary configuration is invalid.");

        var scopeKey = request.ScopeType == PayrollRunScopeTypes.Business
            ? PayrollRunScopeTypes.BusinessKey
            : PayrollRunScopeTypes.LocationKey(request.ScopeId!.Value);
        if (await db.PayrollRuns.AnyAsync(row => row.BusinessId == businessId
                && row.PayrollPeriodId == periodId && row.RunType == PayrollRunTypes.Regular
                && row.ScopeKey == scopeKey, cancellationToken))
            throw new PayrollRunConflictException("A Regular Payroll run already exists for this period and scope.");

        var runNo = checked((await db.PayrollRuns.Where(row =>
                    row.BusinessId == businessId && row.PayrollPeriodId == periodId)
                .Select(row => (int?)row.RunNo).MaxAsync(cancellationToken) ?? 0) + 1);
        var now = DateTime.UtcNow;
        var run = new PayrollRun
        {
            BusinessId = businessId,
            PayrollPeriodId = periodId,
            RunNo = runNo,
            RunType = PayrollRunTypes.Regular,
            ScopeType = request.ScopeType,
            ScopeId = request.ScopeId,
            ScopeKey = scopeKey,
            Status = PayrollRunStatuses.Draft,
            CurrencyCode = effectiveMonetaryConfiguration.CurrencyCode,
            DecimalPrecision = effectiveMonetaryConfiguration.DecimalPrecision,
            RoundingMode = effectiveMonetaryConfiguration.RoundingMode,
            MonetaryConfigurationVersionId = effectiveMonetaryConfiguration.Id,
            Version = 1,
            IdempotencyKey = normalizedKey,
            IdempotencyPayloadHash = payloadHash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now
        };
        db.PayrollRuns.Add(run);
        await SaveAsync(cancellationToken);

        var selections = await SelectEmployeesAsync(
            run, period, request.ScopeType, request.ScopeId, now, cancellationToken);
        db.PayrollRunEmployeeSelections.AddRange(selections);
        await SaveAsync(cancellationToken);
        return await MapAsync(run, isReplay: false, cancellationToken);
    }

    public async Task<PayrollRunResponse> CreateCorrectionAsync(
        long businessId,
        long actorUserId,
        long periodId,
        long originalRunId,
        CreatePayrollCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var reference = request.CorrectionReference.Trim();
        var key = request.IdempotencyKey.Trim();
        var employeeIds = request.EmployeeIds.Order().ToArray();
        var payloadHash = HashCorrectionPayload(periodId, originalRunId, request, employeeIds);
        var replay = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.PayrollPeriodId == periodId && row.IdempotencyKey == key,
            cancellationToken);
        if (replay is not null)
        {
            EnsureSamePayload(replay.IdempotencyPayloadHash, payloadHash);
            if (replay.RunType != PayrollRunTypes.Correction
                || replay.OriginalPayrollRunId != originalRunId)
                throw new PayrollRunConflictException(
                    "The idempotency key was already used for a different Payroll operation.");
            return await MapAsync(replay, isReplay: true, cancellationToken);
        }

        if (await db.PayrollRuns.AnyAsync(row =>
                row.BusinessId == businessId && row.CorrectionReference == reference,
                cancellationToken))
            throw new PayrollRunConflictException(
                "The Business-scoped Payroll correction reference is already in use.");

        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == periodId, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (period.Version != request.ExpectedPeriodVersion)
            throw new PayrollRunConflictException("Payroll period changed since it was read.");
        if (period.Status is not (PayrollPeriodStatuses.Locked or PayrollPeriodStatuses.Approved
            or PayrollPeriodStatuses.Posted or PayrollPeriodStatuses.Closed))
            throw new PayrollRunConflictException(
                "A correction can reopen only a locked, approved, posted, or closed period.");

        var original = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == originalRunId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Original Payroll run not found.");
        if (original.Version != request.ExpectedOriginalRunVersion)
            throw new PayrollRunConflictException("Original Payroll run changed since it was read.");
        if (original.Status is not (PayrollRunStatuses.Approved or PayrollRunStatuses.PaymentInProgress
            or PayrollRunStatuses.Paid or PayrollRunStatuses.Completed))
            throw new PayrollRunConflictException(
                "Only an approved or downstream-terminal Payroll run can be corrected.");
        if (await db.PayrollRuns.AnyAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId
                && (row.Status == PayrollRunStatuses.Draft
                    || row.Status == PayrollRunStatuses.PendingApproval),
                cancellationToken))
            throw new PayrollRunConflictException(
                "The period has another unfinished Payroll run and cannot be reopened for correction.");

        var originalSelections = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == originalRunId && row.IsIncluded)
            .Select(row => row.EmployeeId)
            .ToArrayAsync(cancellationToken);
        if (employeeIds.Any(id => !originalSelections.Contains(id)))
            throw new PayrollRunConflictException(
                "Every corrected employee must belong to the original run's included employee set.");
        var originalResults = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == originalRunId
                && row.CalculationVersion == original.CalculationVersion)
            .OrderBy(row => row.EmployeeId)
            .Select(row => new
            {
                row.EmployeeId, row.CalculationSnapshotHash, row.CalculationInputHash,
                row.GrossEarnings, row.TotalDeductions, row.NetPay
            }).ToArrayAsync(cancellationToken);
        if (original.CalculationVersion <= 0 || originalResults.Length != originalSelections.Length
            || originalResults.Any(row => row.CalculationSnapshotHash is null))
            throw new PayrollRunConflictException(
                "Original Payroll run has incomplete immutable calculation evidence.");
        var originalEvidence = JsonSerializer.Serialize(new
        {
            original.Id, original.BusinessId, original.PayrollPeriodId, original.Version,
            original.CalculationVersion, original.CalculationInputHash, Results = originalResults
        });
        var originalEvidenceHash = HashText(originalEvidence);

        var paymentCorrectionRequired = original.Status is PayrollRunStatuses.PaymentInProgress
            or PayrollRunStatuses.Paid or PayrollRunStatuses.Completed;
        var accountingCorrectionRequired = period.Status is PayrollPeriodStatuses.Posted
            or PayrollPeriodStatuses.Closed;
        var run = await CreateSpecialRunAsync(
            businessId, actorUserId, period, PayrollRunTypes.Correction,
            original.ScopeType, original.ScopeId, employeeIds, key, payloadHash,
            original.CurrencyCode, original.DecimalPrecision, original.RoundingMode,
            original.MonetaryConfigurationVersionId,
            correction =>
            {
                correction.CorrectionReference = reference;
                correction.OriginalPayrollRunId = original.Id;
                correction.OriginalCalculationVersion = original.CalculationVersion;
                correction.OriginalEvidenceHash = originalEvidenceHash;
                correction.CorrectionType = request.CorrectionType.Trim();
                correction.CorrectionReason = request.Reason.Trim();
                correction.AffectedEmployeeIdsSnapshotJson = JsonSerializer.Serialize(employeeIds);
                correction.PaymentCorrectionRequired = paymentCorrectionRequired;
                correction.AccountingCorrectionRequired = accountingCorrectionRequired;
                correction.DownstreamReversalRequired = paymentCorrectionRequired
                    || accountingCorrectionRequired;
            },
            cancellationToken);

        var now = DateTime.UtcNow;
        var previousStatus = period.Status;
        var previousVersion = period.Version;
        period.Status = PayrollPeriodStatuses.Processing;
        period.ReopenedAtUtc = now;
        period.ReopenedByUserId = actorUserId;
        period.ReopenReason = request.Reason.Trim();
        period.Version = checked(period.Version + 1);
        db.PayrollPeriodReopenEvents.Add(new PayrollPeriodReopenEvent
        {
            BusinessId = businessId,
            PayrollPeriodId = periodId,
            PreviousStatus = previousStatus,
            Reason = request.Reason.Trim(),
            Purpose = "Correction",
            ActorUserId = actorUserId,
            OccurredAtUtc = now,
            PreviousPeriodVersion = previousVersion,
            ResultPeriodVersion = period.Version,
            OriginalPayrollRunId = originalRunId,
            CorrectionPayrollRunId = run.Id
        });
        await SaveAsync(cancellationToken);
        return await MapAsync(run, isReplay: false, cancellationToken);
    }

    public async Task<PayrollRunResponse> CreateOffCycleAsync(
        long businessId,
        long actorUserId,
        long periodId,
        CreateOffCyclePayrollRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAuditedTransaction();
        await LockBusinessAsync(businessId, cancellationToken);
        var reference = request.RunReference.Trim();
        var key = request.IdempotencyKey.Trim();
        var employeeIds = request.EmployeeIds.Order().ToArray();
        var payloadHash = HashOffCyclePayload(periodId, request, employeeIds);
        var replay = await db.PayrollRuns.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.PayrollPeriodId == periodId && row.IdempotencyKey == key,
            cancellationToken);
        if (replay is not null)
        {
            EnsureSamePayload(replay.IdempotencyPayloadHash, payloadHash);
            if (replay.RunType != PayrollRunTypes.OffCycle)
                throw new PayrollRunConflictException(
                    "The idempotency key was already used for a different Payroll operation.");
            return await MapAsync(replay, isReplay: true, cancellationToken);
        }
        if (await db.PayrollRuns.AnyAsync(row =>
                row.BusinessId == businessId && row.RunType == PayrollRunTypes.OffCycle
                && row.RunReference == reference, cancellationToken))
            throw new PayrollRunConflictException(
                "The Business-scoped Off-Cycle reference is already in use.");

        var period = await db.PayrollPeriods.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == periodId, cancellationToken)
            ?? throw new KeyNotFoundException("Payroll period not found.");
        if (period.Version != request.ExpectedPeriodVersion)
            throw new PayrollRunConflictException("Payroll period changed since it was read.");
        if (period.Status != PayrollPeriodStatuses.Processing)
            throw new PayrollRunConflictException(
                "An Off-Cycle Payroll run can be created only for a Processing period.");
        if (request.ScopeType == PayrollRunScopeTypes.Location
            && !await db.OperationalLocationProjections.AsNoTracking().AnyAsync(location =>
                location.BusinessId == businessId && location.Id == request.ScopeId, cancellationToken))
            throw new KeyNotFoundException("Payroll run location was not found in this Business.");

        var configuration = await GetEffectiveMonetaryConfigurationAsync(
            businessId, period, cancellationToken);
        var run = await CreateSpecialRunAsync(
            businessId, actorUserId, period, PayrollRunTypes.OffCycle,
            request.ScopeType, request.ScopeId, employeeIds, key, payloadHash,
            configuration.CurrencyCode, configuration.DecimalPrecision, configuration.RoundingMode,
            configuration.Id,
            offCycle =>
            {
                offCycle.RunReference = reference;
                offCycle.CorrectionReason = request.Reason.Trim();
                offCycle.AffectedEmployeeIdsSnapshotJson = JsonSerializer.Serialize(employeeIds);
            },
            cancellationToken);
        return await MapAsync(run, isReplay: false, cancellationToken);
    }

    public async Task<PayrollRunCorrectionResponse> GetCorrectionAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken)
    {
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == runId
                && row.RunType == PayrollRunTypes.Correction,
            cancellationToken)
            ?? throw new KeyNotFoundException("Payroll correction was not found.");
        var original = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId
                && row.Id == run.OriginalPayrollRunId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Original Payroll run was not found.");
        var affectedEmployees = JsonSerializer.Deserialize<long[]>(
            run.AffectedEmployeeIdsSnapshotJson ?? "[]") ?? [];
        return new PayrollRunCorrectionResponse(
            businessId, periodId, original.Id, run.Id, run.CorrectionReference!,
            run.CorrectionType!, run.CorrectionReason!, run.CreatedByUserId, run.CreatedAtUtc,
            run.Status, original.CalculationVersion, run.OriginalEvidenceHash!,
            run.CalculationInputHash, run.Version, affectedEmployees,
            run.DownstreamReversalRequired, run.PaymentCorrectionRequired,
            run.AccountingCorrectionRequired);
    }

    private async Task<PayrollRun> CreateSpecialRunAsync(
        long businessId,
        long actorUserId,
        PayrollPeriod period,
        string runType,
        string scopeType,
        long? scopeId,
        IReadOnlyCollection<long> requestedEmployeeIds,
        string idempotencyKey,
        string payloadHash,
        string? currencyCode,
        int? decimalPrecision,
        string? roundingMode,
        long? monetaryConfigurationVersionId,
        Action<PayrollRun> configure,
        CancellationToken cancellationToken)
    {
        if (currencyCode is null || !decimalPrecision.HasValue
            || decimalPrecision is < 0 or > 6
            || !Enum.TryParse<MidpointRounding>(roundingMode, out var parsedRoundingMode)
            || !Enum.IsDefined(parsedRoundingMode))
            throw new PayrollRunConflictException(
                "The Payroll run is missing valid frozen currency and rounding settings.");
        var scopeKey = scopeType == PayrollRunScopeTypes.Business
            ? PayrollRunScopeTypes.BusinessKey
            : PayrollRunScopeTypes.LocationKey(scopeId!.Value);
        var runNo = checked((await db.PayrollRuns.Where(row =>
                    row.BusinessId == businessId && row.PayrollPeriodId == period.Id)
                .Select(row => (int?)row.RunNo).MaxAsync(cancellationToken) ?? 0) + 1);
        var now = DateTime.UtcNow;
        var run = new PayrollRun
        {
            BusinessId = businessId,
            PayrollPeriodId = period.Id,
            RunNo = runNo,
            RunType = runType,
            ScopeType = scopeType,
            ScopeId = scopeId,
            ScopeKey = scopeKey,
            Status = PayrollRunStatuses.Draft,
            CurrencyCode = currencyCode,
            DecimalPrecision = decimalPrecision,
            RoundingMode = roundingMode,
            MonetaryConfigurationVersionId = monetaryConfigurationVersionId,
            Version = 1,
            IdempotencyKey = idempotencyKey,
            IdempotencyPayloadHash = payloadHash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now
        };
        configure(run);
        db.PayrollRuns.Add(run);
        await SaveAsync(cancellationToken);

        var candidates = await SelectEmployeesAsync(
            run, period, scopeType, scopeId, now, cancellationToken);
        var requestedIds = requestedEmployeeIds.ToHashSet();
        var selected = candidates.Where(row => requestedIds.Contains(row.EmployeeId)).ToArray();
        if (selected.Length != requestedIds.Count || selected.Any(row => !row.IsIncluded))
            throw new PayrollRunConflictException(
                "One or more requested employees are outside the authorized scope or are not eligible for this period.");
        foreach (var selection in selected)
            selection.PayrollRunId = run.Id;
        db.PayrollRunEmployeeSelections.AddRange(selected);
        await SaveAsync(cancellationToken);
        return run;
    }

    private async Task<PayrollMonetaryConfigurationVersion> GetEffectiveMonetaryConfigurationAsync(
        long businessId, PayrollPeriod period, CancellationToken cancellationToken)
    {
        var configurations = await db.PayrollMonetaryConfigurationVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EffectiveFrom <= period.PeriodEnd)
            .OrderBy(row => row.EffectiveFrom).ThenBy(row => row.VersionNo)
            .ToArrayAsync(cancellationToken);
        var effective = configurations
            .Where(row => row.EffectiveFrom <= period.PeriodStart)
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.VersionNo)
            .FirstOrDefault();
        if (effective is null || configurations.Any(row => row.EffectiveFrom > period.PeriodStart))
            throw new PayrollRunConflictException(
                "A single Payroll monetary configuration must cover the entire period.");
        if (effective.CurrencyCode.Length != 3
            || effective.CurrencyCode.Any(character => !char.IsAsciiLetter(character))
            || effective.CurrencyCode != effective.CurrencyCode.ToUpperInvariant()
            || effective.DecimalPrecision is < 0 or > 6
            || !Enum.TryParse<MidpointRounding>(effective.RoundingMode, out var roundingMode)
            || !Enum.IsDefined(roundingMode))
            throw new InvalidOperationException("Payroll monetary configuration is invalid.");
        return effective;
    }

    public async Task<PayrollRunResponse> GetAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken)
    {
        var run = await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == runId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Payroll run not found.");
        return await MapAsync(run, isReplay: false, cancellationToken);
    }

    public async Task<PayrollRunEligibilityPageResponse> GetEligibilityPageAsync(
        long businessId,
        long periodId,
        long runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 200 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Payroll eligibility page is outside the supported range.");
        var runExists = await db.PayrollRuns.AsNoTracking().AnyAsync(row =>
            row.BusinessId == businessId && row.PayrollPeriodId == periodId && row.Id == runId,
            cancellationToken);
        if (!runExists)
            throw new KeyNotFoundException("Payroll run not found.");
        var query = db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == runId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(row => row.EmployeeCodeSnapshot).ThenBy(row => row.EmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PayrollRunEligibilityPageResponse(
            runId, page, pageSize, total, rows.Select(row => new PayrollRunEmployeeSelectionResponse(
                row.EmployeeId, row.EmployeeCodeSnapshot, row.EmployeeNameSnapshot,
                row.LocationIdSnapshot, row.EligibilityDate, row.EmploymentStatusSnapshot,
                row.EmploymentStartDateSnapshot, row.EmploymentEndDateSnapshot,
                row.IsIncluded, row.ExclusionReasonCode)).ToArray());
    }

    private async Task<List<PayrollRunEmployeeSelection>> SelectEmployeesAsync(
        PayrollRun run,
        PayrollPeriod period,
        string scopeType,
        long? scopeId,
        DateTime selectedAtUtc,
        CancellationToken cancellationToken)
    {
        var employees = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId)
            .OrderBy(row => row.EmployeeCode).ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (employees.Length == 0)
            return [];

        var employeeIds = employees.Select(row => row.Id).ToList();
        var employmentPeriods = await db.HrEmploymentPeriods.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && employeeIds.Contains(row.EmployeeId)
                && row.StartDate <= period.PeriodEnd
                && (!row.EndDate.HasValue || row.EndDate.Value >= period.PeriodStart))
            .OrderBy(row => row.EmployeeId).ThenBy(row => row.StartDate).ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var locations = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && employeeIds.Contains(row.EmployeeId)
                && row.AssignmentType == "Primary" && (row.Status == "Effective" || row.Status == "Closed")
                && row.EffectiveFrom <= period.PeriodEnd
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= period.PeriodStart))
            .OrderBy(row => row.EmployeeId).ThenBy(row => row.EffectiveFrom).ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var projections = await db.OperationalLocationProjections.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId)
            .Select(row => row.Id).ToArrayAsync(cancellationToken);
        var projectionIds = projections.ToHashSet();
        var locationRowsByEmployee = locations.GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var hasPrimaryHistory = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && employeeIds.Contains(row.EmployeeId)
                && row.AssignmentType == "Primary")
            .Select(row => row.EmployeeId).Distinct().ToArrayAsync(cancellationToken);
        var primaryHistoryIds = hasPrimaryHistory.ToHashSet();

        var lifecycleEvents = await db.HrEmployeeLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && employeeIds.Contains(row.EmployeeId))
            .OrderBy(row => row.EmployeeId).ThenBy(row => row.EffectiveDate)
            .ThenBy(row => row.OccurredAtUtc).ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var eventsByEmployee = lifecycleEvents.GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var periodsByEmployee = employmentPeriods.GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var salaryVersions = await db.PayrollEmployeeSalaryVersions.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && employeeIds.Contains(row.EmployeeId))
            .OrderBy(row => row.EmployeeId).ThenBy(row => row.EffectiveFrom).ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var salariesByEmployee = salaryVersions.GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var results = new List<PayrollRunEmployeeSelection>(employees.Length);
        foreach (var employee in employees)
        {
            var employeePeriods = periodsByEmployee.GetValueOrDefault(employee.Id) ?? [];
            var matchingPeriods = employeePeriods
                .Where(row => row.StartDate <= period.PeriodEnd
                    && (!row.EndDate.HasValue || row.EndDate.Value >= period.PeriodStart))
                .ToArray();
            var eligibilityDate = matchingPeriods.Length == 1
                ? Max(period.PeriodStart, matchingPeriods[0].StartDate)
                : period.PeriodStart;
            var locationId = ResolvePrimaryLocation(employee, eligibilityDate,
                locationRowsByEmployee.GetValueOrDefault(employee.Id) ?? [],
                primaryHistoryIds.Contains(employee.Id), projectionIds);
            if (scopeType == PayrollRunScopeTypes.Location && locationId != scopeId)
                continue;

            var events = eventsByEmployee.GetValueOrDefault(employee.Id) ?? [];
            var status = ResolveEmploymentStatus(employee.CurrentEmploymentStatus, events, eligibilityDate);
            var employeeSalaries = salariesByEmployee.GetValueOrDefault(employee.Id) ?? [];
            var salary = employeeSalaries.Where(row => row.EffectiveFrom <= eligibilityDate)
                .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
                .FirstOrDefault();
            var reason = DetermineExclusionReason(
                matchingPeriods, locationId, status, salary, employeeSalaries, eligibilityDate);

            results.Add(new PayrollRunEmployeeSelection
            {
                BusinessId = run.BusinessId,
                PayrollRunId = run.Id,
                EmployeeId = employee.Id,
                EmployeeCodeSnapshot = employee.EmployeeCode,
                EmployeeNameSnapshot = employee.DisplayName,
                LocationIdSnapshot = locationId,
                EmploymentPeriodId = matchingPeriods.Length == 1 ? matchingPeriods[0].Id : null,
                EmploymentStartDateSnapshot = matchingPeriods.Length == 1
                    ? matchingPeriods[0].StartDate : null,
                EmploymentEndDateSnapshot = matchingPeriods.Length == 1
                    ? matchingPeriods[0].EndDate : null,
                EligibilityDate = eligibilityDate,
                EmploymentStatusSnapshot = status,
                SalaryVersionId = salary?.Id,
                SalaryEffectiveFromSnapshot = salary?.EffectiveFrom,
                IsIncluded = reason is null,
                ExclusionReasonCode = reason,
                SelectedAtUtc = selectedAtUtc
            });
        }

        return results;
    }

    private static string? DetermineExclusionReason(
        IReadOnlyCollection<HrEmploymentPeriod> employmentPeriods,
        long? locationId,
        string employmentStatus,
        Core.Entities.HR.Payroll.PayrollEmployeeSalaryVersion? salary,
        IReadOnlyCollection<Core.Entities.HR.Payroll.PayrollEmployeeSalaryVersion> salaries,
        DateOnly eligibilityDate)
    {
        if (employmentPeriods.Count == 0)
            return "NoEmploymentOverlap";
        if (employmentPeriods.Count > 1)
            return "AmbiguousEmploymentPeriod";
        if (locationId is null)
            return "MissingPrimaryLocation";
        if (employmentStatus is not ("Active" or "Probation" or "Exited"))
            return "InactiveOrSuspended";
        if (salary is not null)
            return null;
        return salaries.Any(row => row.EffectiveFrom > eligibilityDate)
            ? "SalaryNotYetEffective"
            : "NoApprovedSalary";
    }

    private static long? ResolvePrimaryLocation(
        HrEmployee employee,
        DateOnly asOf,
        IReadOnlyCollection<HrEmployeeLocationAssignment> assignments,
        bool hasPrimaryHistory,
        IReadOnlySet<long> validProjectionIds)
    {
        var effectiveAssignments = assignments.Where(row => row.EffectiveFrom <= asOf
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= asOf))
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .Take(2).ToArray();
        if (effectiveAssignments.Length > 1)
            return null;
        var locationId = effectiveAssignments.SingleOrDefault()?.OperationalLocationProjectionId;
        if (locationId is null && !hasPrimaryHistory)
            locationId = employee.CurrentPrimaryLocationProjectionId;
        return locationId is > 0 && validProjectionIds.Contains(locationId.Value)
            ? locationId
            : null;
    }

    private static string ResolveEmploymentStatus(
        string currentStatus,
        IReadOnlyCollection<HrEmployeeLifecycleEvent> events,
        DateOnly eligibilityDate)
    {
        var prior = events.Where(row => row.EffectiveDate <= eligibilityDate)
            .OrderByDescending(row => row.EffectiveDate).ThenByDescending(row => row.OccurredAtUtc)
            .ThenByDescending(row => row.Id).FirstOrDefault();
        if (prior is not null)
            return ReadEmploymentStatus(prior.NewStateJson);
        var future = events.Where(row => row.EffectiveDate > eligibilityDate)
            .OrderBy(row => row.EffectiveDate).ThenBy(row => row.OccurredAtUtc)
            .ThenBy(row => row.Id).FirstOrDefault();
        if (future is not null)
            return ReadEmploymentStatus(future.PreviousStateJson);
        return currentStatus;
    }

    private static string ReadEmploymentStatus(string stateJson)
    {
        try
        {
            using var document = JsonDocument.Parse(stateJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("employmentStatus", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    return property.Value.GetString()!;
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Employee lifecycle status evidence is invalid.", exception);
        }
        throw new InvalidOperationException("Employee lifecycle status evidence is incomplete.");
    }

    private async Task<PayrollRunResponse> MapAsync(
        PayrollRun run, bool isReplay, CancellationToken cancellationToken)
    {
        var period = await db.PayrollPeriods.AsNoTracking().SingleAsync(row =>
            row.BusinessId == run.BusinessId && row.Id == run.PayrollPeriodId, cancellationToken);
        var counts = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == run.BusinessId && row.PayrollRunId == run.Id)
            .GroupBy(row => row.IsIncluded)
            .Select(group => new { Included = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var included = counts.SingleOrDefault(row => row.Included)?.Count ?? 0;
        var excluded = counts.SingleOrDefault(row => !row.Included)?.Count ?? 0;
        return new PayrollRunResponse(run.Id, run.BusinessId, run.PayrollPeriodId, run.RunNo,
            run.RunType, run.ScopeType, run.ScopeId, run.Status, run.Version, run.CurrencyCode,
            run.DecimalPrecision, run.RoundingMode, run.MonetaryConfigurationVersionId,
            period.PeriodStart, period.PeriodEnd,
            included, excluded, run.CreatedAtUtc, run.CreatedByUserId, isReplay,
            run.ApprovalInstanceId, run.SubmittedByUserId, run.SubmittedAtUtc,
            run.DecidedByUserId, run.DecidedAtUtc, run.DecisionReason,
            run.RunReference, run.CorrectionReference, run.OriginalPayrollRunId,
            run.OriginalCalculationVersion, run.CorrectionType, run.CorrectionReason,
            run.OriginalEvidenceHash, run.DownstreamReversalRequired,
            run.PaymentCorrectionRequired, run.AccountingCorrectionRequired);
    }

    private async Task<ApprovalTargetSnapshot> BuildApprovalSnapshotAsync(
        long businessId, PayrollRun run, CancellationToken cancellationToken)
    {
        if (run.CalculationVersion <= 0 || run.CalculationInputHash is null)
            throw new PayrollRunConflictException("Payroll run has no current calculation evidence.");
        var selections = await db.PayrollRunEmployeeSelections.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == run.Id && row.IsIncluded)
            .OrderBy(row => row.EmployeeId)
            .Select(row => new
            {
                row.EmployeeId, row.LocationIdSnapshot, row.IsIncluded, row.ExclusionReasonCode,
                row.SalaryVersionId, row.SalaryEffectiveFromSnapshot
            }).ToArrayAsync(cancellationToken);
        var results = await db.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.PayrollRunId == run.Id
                && row.CalculationVersion == run.CalculationVersion)
            .OrderBy(row => row.EmployeeId)
            .Select(row => new
            {
                row.EmployeeId, row.Status, row.GrossEarnings, row.TotalDeductions, row.NetPay,
                row.CalculationSnapshotHash
            }).ToArrayAsync(cancellationToken);
        if (selections.Length == 0
            || results.Length != selections.Length
            || results.Any(row => row.Status != PayrollEmployeeResultStatuses.Calculated
                || row.CalculationSnapshotHash is null))
            throw new PayrollRunConflictException(
                "Payroll run has incomplete or non-payable financial evidence.");
        var evidence = JsonSerializer.Serialize(new
        {
            run.Id, run.BusinessId, run.PayrollPeriodId, run.RunType, run.RunReference,
            run.CorrectionReference, run.OriginalPayrollRunId, run.OriginalCalculationVersion,
            run.OriginalEvidenceHash, run.CorrectionType, run.CorrectionReason,
            run.AffectedEmployeeIdsSnapshotJson, run.DownstreamReversalRequired,
            run.PaymentCorrectionRequired, run.AccountingCorrectionRequired, run.ScopeKey,
            run.CalculationVersion,
            run.CalculationInputHash, run.CurrencyCode, run.DecimalPrecision, run.RoundingMode,
            Selections = selections, Results = results
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        return new ApprovalTargetSnapshot(run.Version, hash);
    }

    private static bool FixedHashEquals(string? left, string? right) =>
        left is not null && right is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private async Task LockBusinessAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll run writes require the audited transaction boundary.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 8)})", cancellationToken);
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payroll run writes require the audited transaction boundary.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollRunConflictException("Payroll run changed concurrently.", exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new PayrollRunConflictException(
                "Payroll run or employee selection conflicts with another request.", exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string HashPayload(long periodId, CreatePayrollRunRequest request)
    {
        var normalized = string.Join('|',
            periodId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.ScopeType,
            request.ScopeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            request.ExpectedPeriodVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string HashCorrectionPayload(
        long periodId,
        long originalRunId,
        CreatePayrollCorrectionRequest request,
        IReadOnlyList<long> employeeIds)
    {
        var normalized = string.Join('|',
            periodId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            originalRunId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.CorrectionReference.Trim(),
            request.CorrectionType.Trim(),
            request.Reason.Trim(),
            request.ExpectedPeriodVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.ExpectedOriginalRunVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', employeeIds));
        return HashText(normalized);
    }

    private static string HashOffCyclePayload(
        long periodId,
        CreateOffCyclePayrollRunRequest request,
        IReadOnlyList<long> employeeIds)
    {
        var normalized = string.Join('|',
            periodId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.RunReference.Trim(),
            request.Reason.Trim(),
            request.ScopeType,
            request.ScopeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            request.ExpectedPeriodVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', employeeIds));
        return HashText(normalized);
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureSamePayload(string priorHash, string payloadHash)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(priorHash), Convert.FromHexString(payloadHash)))
            throw new PayrollRunConflictException(
                "The idempotency key was already used with a different Payroll run request.");
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;
}
