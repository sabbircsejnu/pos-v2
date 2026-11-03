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

public sealed class PayrollEmployeeLoanStore(
    RetailPOSDbContext db,
    IPayrollSalarySetupStore salaries,
    TimeProvider? clock = null) :
    IPayrollEmployeeLoanStore, IApprovalKernelWorkflow, IApprovalTargetSnapshotReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string TargetType = PayrollEmployeeLoanApprovalConstants.TargetType;

    public Task<PayrollEmployeeLoanResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollEmployeeLoanRequest request,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(businessId, actorUserId, request, null, null, null, cancellationToken);

    public Task<PayrollEmployeeLoanResponse> CreateOpeningAsync(
        long businessId, long actorUserId, CreatePayrollEmployeeLoanRequest request,
        DateOnly historicalDisbursedDate, decimal openingBalanceAmount,
        string importIdentity, CancellationToken cancellationToken)
    {
        if (historicalDisbursedDate == default || openingBalanceAmount < 0
            || openingBalanceAmount > request.PrincipalAmount
            || string.IsNullOrWhiteSpace(importIdentity) || importIdentity.Length > 150)
            throw new ArgumentException("Opening loan balance evidence is invalid.");
        return CreateCoreAsync(businessId, actorUserId, request, historicalDisbursedDate,
            openingBalanceAmount, importIdentity, cancellationToken);
    }

    private async Task<PayrollEmployeeLoanResponse> CreateCoreAsync(
        long businessId, long actorUserId, CreatePayrollEmployeeLoanRequest request,
        DateOnly? historicalDisbursedDate, decimal? openingBalanceAmount,
        string? importIdentity, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var schedule = BuildSchedule(request.PrincipalAmount, request.RecoveryStartDate,
            request.RecoveryMode, request.FixedInstallment, request.Schedule, openingBalanceAmount);
        await LockEmployeeAsync(businessId, request.EmployeeId, cancellationToken);

        var payloadHash = importIdentity is null
            ? Hash(Normalize(request))
            : Hash(new
            {
                Request = Normalize(request),
                HistoricalDisbursedDate = historicalDisbursedDate,
                OpeningBalanceAmount = openingBalanceAmount,
                ImportIdentity = importIdentity
            });
        var prior = await db.PayrollEmployeeLoans.SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.CreatedByUserId == actorUserId
                && row.EmployeeId == request.EmployeeId
                && row.IdempotencyKey == request.IdempotencyKey.Trim(), cancellationToken);
        if (prior is not null)
        {
            EnsureSameHash(prior.IdempotencyPayloadHash, payloadHash,
                "The loan idempotency key was already used with a different payload.");
            return (await MapAsync(prior, cancellationToken)) with { IsReplay = true };
        }

        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == request.EmployeeId
                && period.StartDate <= request.RecoveryStartDate
                && (period.EndDate == null || period.EndDate >= request.RecoveryStartDate),
                cancellationToken))
            throw new ArgumentException("The employee is not active and employed on the recovery start date.");

        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, request.EmployeeId, request.RecoveryStartDate, cancellationToken);
        var principal = RoundAmount(request.PrincipalAmount);
        var normalizedReference = NormalizeOptional(request.Reference);
        var normalizedReason = request.Reason.Trim();
        var requestHash = Hash(new
        {
            businessId,
            request.EmployeeId,
            locationId,
            LoanType = request.LoanType.Trim(),
            PrincipalAmount = principal,
            request.RecoveryStartDate,
            RecoveryMode = request.RecoveryMode.Trim(),
            FixedInstallment = request.FixedInstallment,
            Schedule = schedule,
            IsOpeningBalance = importIdentity is not null,
            HistoricalDisbursedDate = historicalDisbursedDate,
            OpeningBalanceAmount = openingBalanceAmount,
            ImportIdentity = importIdentity,
            Reason = normalizedReason,
            Reference = normalizedReference
        });
        var row = new PayrollEmployeeLoan
        {
            BusinessId = businessId,
            EmployeeId = request.EmployeeId,
            OperationalLocationProjectionId = locationId,
            LoanType = request.LoanType.Trim(),
            PrincipalAmount = principal,
            IsOpeningBalance = importIdentity is not null,
            OpeningDisbursedDate = historicalDisbursedDate,
            OpeningBalanceAmount = openingBalanceAmount,
            RecoveryStartDate = request.RecoveryStartDate,
            RecoveryMode = request.RecoveryMode.Trim(),
            FixedInstallment = request.FixedInstallment,
            ProposedScheduleJson = JsonSerializer.Serialize(schedule, JsonOptions),
            RequestHash = requestHash,
            IdempotencyKey = request.IdempotencyKey.Trim(),
            IdempotencyPayloadHash = payloadHash,
            Reason = normalizedReason,
            Reference = normalizedReference,
            Status = "Draft",
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc(),
            Version = 1
        };
        db.PayrollEmployeeLoans.Add(row);
        await SaveChangesAsync(cancellationToken);
        return await MapAsync(row, cancellationToken);
    }

    public async Task<PayrollEmployeeLoanResponse> RequestAsync(
        long businessId, long id, CancellationToken cancellationToken)
    {
        var row = await GetLoanAsync(businessId, id, cancellationToken)
            ?? throw new KeyNotFoundException("Employee loan was not found in this Business.");
        return await MapAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollEmployeeLoanResponse>> EmployeeLoansAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var ids = await db.PayrollEmployeeLoans.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(cancellationToken);
        var rows = new List<PayrollEmployeeLoanResponse>(ids.Length);
        foreach (var id in ids)
            rows.Add(await RequestAsync(businessId, id, cancellationToken));
        return rows;
    }

    public async Task<PayrollEmployeeLoanResponse> UpdateScheduleAsync(
        long businessId, long actorUserId, long loanId, UpdatePayrollLoanScheduleRequest request,
        CancellationToken cancellationToken)
    {
        ValidateScheduleRequest(request);
        await LockLoanAsync(businessId, loanId, cancellationToken);
        var row = await db.PayrollEmployeeLoans.SingleOrDefaultAsync(
            loan => loan.BusinessId == businessId && loan.Id == loanId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee loan was not found in this Business.");
        var key = request.IdempotencyKey.Trim();
        var hash = Hash(Normalize(request));
        var prior = await db.PayrollLoanScheduleVersions.AsNoTracking().SingleOrDefaultAsync(version =>
                version.BusinessId == businessId && version.LoanId == loanId
                && version.CreatedByUserId == actorUserId && version.IdempotencyKey == key,
                cancellationToken);
        if (prior is not null)
        {
            EnsureSameHash(prior.IdempotencyPayloadHash, hash,
                "The schedule idempotency key was already used with a different payload.");
            return (await MapAsync(row, cancellationToken)) with { IsReplay = true };
        }
        if (row.Status != "Active")
            throw new PayrollSalaryConflictException("Only an active employee loan can have its recovery schedule changed.");
        if (row.Version != request.ExpectedLoanVersion)
            throw new PayrollSalaryConflictException("Employee loan changed since the schedule was read.");

        var schedule = BuildSchedule(row.OutstandingAmount, request.RecoveryStartDate,
            request.RecoveryMode, request.FixedInstallment, request.Schedule);
        var nextVersion = checked((await db.PayrollLoanScheduleVersions.AsNoTracking()
            .Where(version => version.BusinessId == businessId && version.LoanId == loanId)
            .Select(version => (int?)version.VersionNo).MaxAsync(cancellationToken) ?? 0) + 1);
        var version = NewScheduleVersion(
            businessId, loanId, nextVersion, request.RecoveryStartDate, request.RecoveryMode,
            request.FixedInstallment, key, hash, actorUserId, schedule);
        row.CurrentScheduleVersionNo = nextVersion;
        row.Version = checked(row.Version + 1);
        db.PayrollLoanScheduleVersions.Add(version);
        await SaveChangesAsync(cancellationToken);
        return await MapAsync(row, cancellationToken);
    }

    public async Task<PayrollEmployeeLoanResponse> RecordRepaymentAsync(
        long businessId, long actorUserId, long loanId, RecordPayrollLoanRepaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRepaymentRequest(request);
        await LockLoanAsync(businessId, loanId, cancellationToken);
        var row = await db.PayrollEmployeeLoans.SingleOrDefaultAsync(
            loan => loan.BusinessId == businessId && loan.Id == loanId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee loan was not found in this Business.");
        var key = request.IdempotencyKey.Trim();
        var hash = Hash(Normalize(request));
        var prior = await db.PayrollLoanRepayments.AsNoTracking().SingleOrDefaultAsync(repayment =>
                repayment.BusinessId == businessId && repayment.LoanId == loanId
                && repayment.RecordedByUserId == actorUserId && repayment.IdempotencyKey == key,
                cancellationToken);
        if (prior is not null)
        {
            EnsureSameHash(prior.IdempotencyPayloadHash, hash,
                "The repayment idempotency key was already used with a different payload.");
            return (await MapAsync(row, cancellationToken)) with { IsReplay = true };
        }
        if (row.Status != "Active" || row.OutstandingAmount <= 0)
            throw new PayrollSalaryConflictException("Only an active loan with an outstanding balance can be repaid.");

        var amount = RoundAmount(request.Amount);
        if (amount > row.OutstandingAmount)
            throw new ArgumentException("A repayment cannot exceed the outstanding loan balance.");
        var repayment = new PayrollLoanRepayment
        {
            BusinessId = businessId,
            LoanId = loanId,
            Amount = amount,
            PaymentDate = request.PaymentDate,
            Reference = NormalizeOptional(request.Reference),
            Reason = request.Reason.Trim(),
            IdempotencyKey = key,
            IdempotencyPayloadHash = hash,
            RecordedByUserId = actorUserId,
            RecordedAtUtc = NowUtc()
        };

        if (row.CurrentScheduleVersionNo is > 0)
        {
            var version = await db.PayrollLoanScheduleVersions.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.BusinessId == businessId && item.LoanId == loanId
                    && item.VersionNo == row.CurrentScheduleVersionNo, cancellationToken)
                ?? throw new PayrollSalaryConflictException("The current recovery schedule is unavailable.");
            if (version.RecoveryMode != "Manual")
            {
                var lines = await db.PayrollLoanScheduleLines.AsNoTracking()
                    .Where(line => line.BusinessId == businessId && line.LoanId == loanId
                        && line.ScheduleVersionId == version.Id)
                    .OrderBy(line => line.DueDate).ThenBy(line => line.InstallmentNo)
                    .ToArrayAsync(cancellationToken);
                if (lines.Length == 0)
                    throw new PayrollSalaryConflictException("The current recovery schedule has no installments.");
                var recovered = await db.PayrollLoanRepaymentAllocations.AsNoTracking()
                    .Where(allocation => allocation.BusinessId == businessId && allocation.LoanId == loanId
                        && allocation.ScheduleVersionId == version.Id)
                    .GroupBy(allocation => allocation.InstallmentNo)
                    .Select(group => new { InstallmentNo = group.Key, Amount = group.Sum(item => item.Amount) })
                    .ToDictionaryAsync(item => item.InstallmentNo, item => item.Amount, cancellationToken);

                var remaining = amount;
                foreach (var line in lines)
                {
                    var lineRemaining = line.PlannedAmount - recovered.GetValueOrDefault(line.InstallmentNo);
                    if (lineRemaining <= 0)
                        continue;
                    var applied = Math.Min(remaining, lineRemaining);
                    if (applied <= 0)
                        break;
                    repayment.Allocations.Add(new PayrollLoanRepaymentAllocation
                    {
                        BusinessId = businessId,
                        LoanId = loanId,
                        ScheduleVersionId = line.ScheduleVersionId,
                        InstallmentNo = line.InstallmentNo,
                        Amount = applied
                    });
                    remaining -= applied;
                    if (remaining == 0)
                        break;
                }
                if (remaining != 0)
                    throw new PayrollSalaryConflictException(
                        "The current recovery schedule does not cover the outstanding balance; update the schedule before repayment.");
            }
        }

        row.OutstandingAmount = RoundAmount(row.OutstandingAmount - amount);
        row.Status = row.OutstandingAmount == 0 ? "Settled" : "Active";
        row.Version = checked(row.Version + 1);
        db.PayrollLoanRepayments.Add(repayment);
        await SaveChangesAsync(cancellationToken);
        return await MapAsync(row, cancellationToken);
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand approval,
        CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.EmployeeLoan)
            return null;
        if (approval.TargetType != TargetType || !long.TryParse(approval.TargetId, out var loanId)
            || loanId <= 0 || approval.ScopeType != ApprovalScopeType.Location
            || !long.TryParse(approval.ScopeReference, out var scopeLocationId) || scopeLocationId <= 0)
            throw new ArgumentException("Employee loan approval requires a valid location-scoped target.");

        var head = await db.PayrollEmployeeLoans.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == loanId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Employee loan was not found in this Business.");
        await LockEmployeeAsync(businessId, head.EmployeeId, cancellationToken);
        var row = await db.PayrollEmployeeLoans.SingleOrDefaultAsync(
            loan => loan.BusinessId == businessId && loan.Id == loanId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Employee loan was not found in this Business.");
        if (row.Status != "Draft" || row.CreatedByUserId != requesterUserId
            || row.OperationalLocationProjectionId != scopeLocationId
            || row.Version != approval.TargetVersion || !FixedHashEquals(row.RequestHash, approval.RequestHash))
            throw new ApprovalKernelConflictException(
                "Employee loan changed, is not a draft, or does not match its approval scope.");

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
        if (instance.ProcessType != ApprovalProcessType.EmployeeLoan)
            return Task.CompletedTask;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var loanId))
            throw new ApprovalKernelConflictException("Employee loan approval instance has an invalid target.");
        var row = db.PayrollEmployeeLoans.Local.SingleOrDefault(loan =>
                loan.BusinessId == businessId && loan.Id == loanId)
            ?? throw new ApprovalKernelConflictException("Prepared employee loan was not tracked.");
        if (row.Status != "PendingApproval" || row.SubmittedTargetVersion != instance.TargetVersion)
            throw new ApprovalKernelConflictException(
                "Prepared employee loan no longer matches its approval instance.");
        row.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.EmployeeLoan)
            return;
        if (instance.TargetType != TargetType || !long.TryParse(instance.TargetId, out var loanId))
            throw new ApprovalKernelConflictException("Employee loan approval instance has an invalid target.");
        var head = await db.PayrollEmployeeLoans.AsNoTracking().SingleOrDefaultAsync(row =>
                row.BusinessId == businessId && row.Id == loanId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Employee loan target was not found.");
        await LockEmployeeAsync(businessId, head.EmployeeId, cancellationToken);
        var row = await db.PayrollEmployeeLoans.SingleOrDefaultAsync(
            loan => loan.BusinessId == businessId && loan.Id == loanId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Employee loan target was not found.");
        if (row.Status != "PendingApproval" || row.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Employee loan is already terminal or mismatched.");

        row.DecidedByUserId = actorUserId;
        row.DecidedAtUtc = NowUtc();
        row.DecisionReason = NormalizeOptional(reason);
        row.Version = checked(row.Version + 1);
        if (instance.Status == ApprovalInstanceStatus.Approved)
        {
            var schedule = ReadSchedule(row.ProposedScheduleJson);
            row.Status = row.IsOpeningBalance && row.OpeningBalanceAmount == 0 ? "Settled" : "Active";
            row.ApprovedAmount = row.PrincipalAmount;
            row.DisbursedDate = row.IsOpeningBalance
                ? null
                : DateOnly.FromDateTime(NowUtc());
            row.OutstandingAmount = row.IsOpeningBalance ? row.OpeningBalanceAmount!.Value : row.PrincipalAmount;
            if (schedule.Count > 0 && row.OutstandingAmount > 0)
            {
                var version = NewScheduleVersion(
                    businessId, row.Id, 1, row.RecoveryStartDate, row.RecoveryMode,
                    row.FixedInstallment, $"approval-{instance.Id}",
                    Hash(schedule), actorUserId, schedule);
                row.CurrentScheduleVersionNo = 1;
                db.PayrollLoanScheduleVersions.Add(version);
            }
        }
        else if (instance.Status == ApprovalInstanceStatus.Rejected)
            row.Status = "Rejected";
        else if (instance.Status == ApprovalInstanceStatus.Cancelled)
            row.Status = "Cancelled";
        else
            throw new ApprovalKernelConflictException("Employee loan approval is not terminal.");
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != TargetType || !long.TryParse(targetId, out var loanId) || loanId <= 0)
            return null;
        var row = await db.PayrollEmployeeLoans.AsNoTracking().SingleOrDefaultAsync(
            loan => loan.BusinessId == businessId && loan.Id == loanId, cancellationToken);
        return row is null ? null : new ApprovalTargetSnapshot(
            row.SubmittedTargetVersion ?? row.Version, row.RequestHash);
    }

    private async Task<PayrollEmployeeLoan?> GetLoanAsync(
        long businessId, long loanId, CancellationToken cancellationToken) =>
        await db.PayrollEmployeeLoans.AsNoTracking()
            .Include(row => row.ApprovalInstance)
            .ThenInclude(instance => instance!.Steps)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == loanId,
                cancellationToken);

    private async Task<PayrollEmployeeLoanResponse> MapAsync(
        PayrollEmployeeLoan row, CancellationToken cancellationToken)
    {
        var versions = await db.PayrollLoanScheduleVersions.AsNoTracking()
            .Include(version => version.Lines)
            .Where(version => version.BusinessId == row.BusinessId && version.LoanId == row.Id)
            .OrderBy(version => version.VersionNo)
            .ToArrayAsync(cancellationToken);
        var allocations = await db.PayrollLoanRepaymentAllocations.AsNoTracking()
            .Where(allocation => allocation.BusinessId == row.BusinessId && allocation.LoanId == row.Id)
            .GroupBy(allocation => new { allocation.ScheduleVersionId, allocation.InstallmentNo })
            .Select(group => new
            {
                group.Key.ScheduleVersionId,
                group.Key.InstallmentNo,
                Amount = group.Sum(item => item.Amount)
            })
            .ToArrayAsync(cancellationToken);
        var recovered = allocations.ToDictionary(
            item => (item.ScheduleVersionId, item.InstallmentNo), item => item.Amount);
        var scheduleHistory = versions.Select(version => new PayrollLoanScheduleVersionResponse(
            version.Id,
            version.VersionNo,
            version.RecoveryStartDate,
            version.RecoveryMode,
            version.FixedInstallment,
            row.CurrentScheduleVersionNo == version.VersionNo ? "Current" : "Superseded",
            version.CreatedByUserId,
            version.CreatedAtUtc,
            version.Lines.OrderBy(line => line.InstallmentNo).Select(line =>
            {
                var actual = recovered.GetValueOrDefault((version.Id, line.InstallmentNo));
                var status = actual >= line.PlannedAmount
                    ? "Recovered"
                    : row.OutstandingAmount == 0 ? "Settled" : "Planned";
                return new PayrollLoanScheduleLineResponse(
                    line.InstallmentNo, line.DueDate, line.PlannedAmount, actual, status);
            }).ToArray())).ToArray();
        var repayments = await db.PayrollLoanRepayments.AsNoTracking()
            .Where(repayment => repayment.BusinessId == row.BusinessId && repayment.LoanId == row.Id)
            .OrderBy(repayment => repayment.PaymentDate).ThenBy(repayment => repayment.Id)
            .Select(repayment => new PayrollLoanRepaymentResponse(
                repayment.Id, repayment.Amount, repayment.PaymentDate, repayment.Reference,
                repayment.Reason, repayment.RecordedByUserId, repayment.RecordedAtUtc))
            .ToArrayAsync(cancellationToken);
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
        var currentSchedule = versions.SingleOrDefault(version =>
            version.VersionNo == row.CurrentScheduleVersionNo);
        return new PayrollEmployeeLoanResponse(
            row.Id, row.BusinessId, row.EmployeeId, row.OperationalLocationProjectionId,
            row.LoanType, row.PrincipalAmount, row.ApprovedAmount, row.DisbursedDate,
            currentSchedule?.RecoveryStartDate ?? row.RecoveryStartDate,
            currentSchedule?.RecoveryMode ?? row.RecoveryMode,
            currentSchedule is null ? row.FixedInstallment : currentSchedule.FixedInstallment,
            row.OutstandingAmount, row.Status, row.Reason, row.Reference,
            row.ApprovalInstanceId, row.CreatedByUserId, row.CreatedAtUtc,
            row.SubmittedByUserId, row.SubmittedAtUtc, row.DecidedByUserId, row.DecidedAtUtc,
            row.DecisionReason, row.Version, steps, decisions, scheduleHistory, repayments,
            IsOpeningBalance: row.IsOpeningBalance,
            OpeningDisbursedDate: row.OpeningDisbursedDate,
            OpeningBalanceAmount: row.OpeningBalanceAmount);
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
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
                "Employee loan serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private async Task LockLoanAsync(long businessId, long loanId, CancellationToken cancellationToken)
    {
        EnsureAuditedTransaction();
        if (db.Database.IsNpgsql())
        {
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM payroll_employee_loans WHERE business_id = {businessId} AND id = {loanId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                throw new KeyNotFoundException("Employee loan was not found in this Business.");
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException(
                "Employee loan serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private void EnsureAuditedTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Employee loan writes require the audited transaction boundary.");
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PayrollSalaryConflictException(
                "Employee loan changed by another request; reload and retry.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PayrollSalaryConflictException(
                "A conflicting employee loan, schedule or repayment already exists.", exception);
        }
    }

    private PayrollLoanScheduleVersion NewScheduleVersion(
        long businessId, long loanId, int versionNo, DateOnly recoveryStartDate,
        string recoveryMode, decimal? fixedInstallment, string idempotencyKey,
        string idempotencyPayloadHash, long actorUserId,
        IReadOnlyList<PayrollLoanScheduleLineInput> lines) =>
        new()
        {
            BusinessId = businessId,
            LoanId = loanId,
            VersionNo = versionNo,
            RecoveryStartDate = recoveryStartDate,
            RecoveryMode = recoveryMode.Trim(),
            FixedInstallment = fixedInstallment,
            IdempotencyKey = idempotencyKey,
            IdempotencyPayloadHash = idempotencyPayloadHash,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = NowUtc(),
            Lines = lines.Select((line, index) => new PayrollLoanScheduleLine
            {
                BusinessId = businessId,
                LoanId = loanId,
                InstallmentNo = index + 1,
                DueDate = line.DueDate,
                PlannedAmount = RoundAmount(line.PlannedAmount)
            }).ToList()
        };

    private static IReadOnlyList<PayrollLoanScheduleLineInput> BuildSchedule(
        decimal targetAmount, DateOnly recoveryStartDate, string recoveryMode,
        decimal? fixedInstallment, IReadOnlyList<PayrollLoanScheduleLineInput> supplied,
        decimal? customTargetAmount = null)
    {
        targetAmount = customTargetAmount ?? targetAmount;
        var mode = recoveryMode.Trim();
        if (mode == "Manual")
        {
            if (fixedInstallment.HasValue || supplied.Count != 0)
                throw new ArgumentException("Manual recovery cannot include installments or a fixed amount.");
            return [];
        }
        if (mode == "FixedMonthly")
        {
            if (!fixedInstallment.HasValue || fixedInstallment.Value <= 0
                || supplied.Count != 0)
                throw new ArgumentException(
                    "Fixed monthly recovery requires a positive installment and no custom schedule.");
            ValidateAmount(fixedInstallment.Value, nameof(fixedInstallment));
            var lines = new List<PayrollLoanScheduleLineInput>();
            var remaining = targetAmount;
            for (var index = 0; remaining > 0 && index < 120; index++)
            {
                var amount = Math.Min(fixedInstallment.Value, remaining);
                lines.Add(new PayrollLoanScheduleLineInput(recoveryStartDate.AddMonths(index), amount));
                remaining = RoundAmount(remaining - amount);
            }
            if (remaining != 0)
                throw new ArgumentException("The fixed installment must recover the balance within 120 months.");
            return lines;
        }
        if (mode != "CustomSchedule" || fixedInstallment.HasValue)
            throw new ArgumentException("Recovery mode must be FixedMonthly, Manual, or CustomSchedule.");
        if (targetAmount == 0 && supplied.Count == 0)
            return [];
        ValidateScheduleLines(supplied, targetAmount, recoveryStartDate);
        return supplied.OrderBy(line => line.DueDate).ToArray();
    }

    private static IReadOnlyList<PayrollLoanScheduleLineInput> ReadSchedule(string json) =>
        JsonSerializer.Deserialize<PayrollLoanScheduleLineInput[]>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored employee loan schedule evidence is invalid.");

    private static void ValidateRequest(CreatePayrollEmployeeLoanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId <= 0 || request.LoanType is not ("Loan" or "Advance"))
            throw new ArgumentException("A valid employee and loan type (Loan or Advance) are required.");
        ValidateAmount(request.PrincipalAmount, nameof(request.PrincipalAmount));
        if (string.IsNullOrWhiteSpace(request.RecoveryMode)
            || request.RecoveryStartDate == default)
            throw new ArgumentException("A recovery start date is required.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A reason of at most 1000 characters is required.");
        if (request.Reference?.Trim().Length > 150)
            throw new ArgumentException("Reference must be at most 150 characters.");
        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.Schedule is null)
            throw new ArgumentException("Schedule must be supplied, even when recovery is manual.");
    }

    private static void ValidateScheduleRequest(UpdatePayrollLoanScheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedLoanVersion <= 0 || request.RecoveryStartDate == default
            || string.IsNullOrWhiteSpace(request.RecoveryMode) || request.Schedule is null)
            throw new ArgumentException("A valid loan version, recovery start date, and schedule are required.");
        ValidateIdempotencyKey(request.IdempotencyKey);
    }

    private void ValidateRepaymentRequest(RecordPayrollLoanRepaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAmount(request.Amount, nameof(request.Amount));
        if (request.PaymentDate == default || request.PaymentDate > DateOnly.FromDateTime(NowUtc()))
            throw new ArgumentException("A valid non-future repayment date is required.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A repayment reason of at most 1000 characters is required.");
        if (request.Reference?.Trim().Length > 150)
            throw new ArgumentException("Reference must be at most 150 characters.");
        ValidateIdempotencyKey(request.IdempotencyKey);
    }

    private static void ValidateScheduleLines(
        IReadOnlyList<PayrollLoanScheduleLineInput> lines, decimal targetAmount, DateOnly startDate)
    {
        if (lines.Count is < 1 or > 120)
            throw new ArgumentException("A custom schedule must contain between 1 and 120 installments.");
        if (lines.Any(line => line.DueDate < startDate))
            throw new ArgumentException("Schedule due dates cannot precede the recovery start date.");
        if (lines.Select(line => line.DueDate).Distinct().Count() != lines.Count)
            throw new ArgumentException("A schedule cannot contain duplicate due dates.");
        foreach (var line in lines)
            ValidateAmount(line.PlannedAmount, nameof(line.PlannedAmount));
        if (RoundAmount(lines.Sum(line => line.PlannedAmount)) != targetAmount)
            throw new ArgumentException("Schedule installments must equal the full amount to be recovered.");
    }

    private static void ValidateAmount(decimal value, string name)
    {
        if (value <= 0 || value > 99_999_999_999_999.9999m || RoundAmount(value) != value)
            throw new ArgumentOutOfRangeException(
                name, "Amount must be positive, fit decimal(18,4), and have at most four decimal places.");
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 150)
            throw new ArgumentException("An idempotency key of at most 150 characters is required.");
    }

    private static decimal RoundAmount(decimal value) => decimal.Round(value, 4, MidpointRounding.ToEven);

    private static object Normalize(CreatePayrollEmployeeLoanRequest request) => new
    {
        request.EmployeeId,
        LoanType = request.LoanType.Trim(),
        request.PrincipalAmount,
        request.RecoveryStartDate,
        RecoveryMode = request.RecoveryMode.Trim(),
        request.FixedInstallment,
        Schedule = request.Schedule.OrderBy(line => line.DueDate)
            .Select(line => new { line.DueDate, line.PlannedAmount }).ToArray(),
        Reason = request.Reason.Trim(),
        Reference = NormalizeOptional(request.Reference)
    };

    private static object Normalize(UpdatePayrollLoanScheduleRequest request) => new
    {
        request.ExpectedLoanVersion,
        request.RecoveryStartDate,
        RecoveryMode = request.RecoveryMode.Trim(),
        request.FixedInstallment,
        Schedule = request.Schedule.OrderBy(line => line.DueDate)
            .Select(line => new { line.DueDate, line.PlannedAmount }).ToArray()
    };

    private static object Normalize(RecordPayrollLoanRepaymentRequest request) => new
    {
        request.Amount,
        request.PaymentDate,
        Reason = request.Reason.Trim(),
        Reference = NormalizeOptional(request.Reference)
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, JsonOptions))));

    private static bool FixedHashEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left.ToUpperInvariant()), Encoding.ASCII.GetBytes(right.ToUpperInvariant()));

    private static void EnsureSameHash(string left, string right, string message)
    {
        if (!FixedHashEquals(left, right))
            throw new PayrollSalaryConflictException(message);
    }

    private DateTime NowUtc() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
}
