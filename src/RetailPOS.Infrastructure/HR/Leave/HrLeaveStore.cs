using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Leave;

public sealed class HrLeaveStore(RetailPOSDbContext db) : IHrLeaveStore
{
    public async Task<IReadOnlyList<HrLeaveTypeModel>> GetTypesAsync(long businessId, CancellationToken ct) =>
        await db.HrLeaveTypes.AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderBy(row => row.Name).ThenBy(row => row.Id)
            .Select(row => Map(row))
            .ToArrayAsync(ct);

    public async Task<HrLeaveTypeModel> CreateTypeAsync(
        long businessId, HrLeaveTypeInput input, CancellationToken ct)
    {
        var type = new HrLeaveType
        {
            BusinessId = businessId,
            Code = input.Code,
            Name = input.Name,
            IsPaid = input.IsPaid,
            RequiresDocument = input.RequiresDocument,
            IsActive = true,
            Version = 1
        };
        db.HrLeaveTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return Map(type);
    }

    public async Task<IReadOnlyList<HrLeavePolicyModel>> GetPoliciesAsync(
        long businessId, long? leaveTypeId, CancellationToken ct)
    {
        var query = db.HrLeavePolicyVersions.AsNoTracking().Where(row => row.BusinessId == businessId);
        if (leaveTypeId.HasValue)
            query = query.Where(row => row.LeaveTypeId == leaveTypeId.Value);
        return await query.OrderBy(row => row.LeaveTypeId).ThenBy(row => row.Code)
            .ThenBy(row => row.ScopeType).ThenBy(row => row.ScopeReference).ThenBy(row => row.VersionNo)
            .Select(row => Map(row))
            .ToArrayAsync(ct);
    }

    public async Task<HrLeavePolicyModel> CreatePolicyVersionAsync(
        long businessId, HrLeavePolicyInput input, CancellationToken ct)
    {
        var accrualMethod = input.AccrualMethod
            ?? throw new ArgumentException("Leave policy must explicitly select an accrual method.", nameof(input));
        var (effectiveFromYearStart, _) = Application.HR.Leave.HrLeaveRules.GetLeaveYear(
            input.EffectiveFrom, input.LeaveYearStartMonth, input.LeaveYearStartDay);
        if (effectiveFromYearStart != input.EffectiveFrom)
            throw new ArgumentException("Leave policy versions can take effect only at a leave-year boundary.", nameof(input));
        if (input.EffectiveTo is DateOnly effectiveTo)
        {
            var (_, effectiveToYearEnd) = Application.HR.Leave.HrLeaveRules.GetLeaveYear(
                effectiveTo, input.LeaveYearStartMonth, input.LeaveYearStartDay);
            if (effectiveToYearEnd != effectiveTo)
                throw new ArgumentException("Leave policy versions can end only at a leave-year boundary.", nameof(input));
        }

        var type = await db.HrLeaveTypes.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == input.LeaveTypeId && row.IsActive, ct)
            ?? throw new KeyNotFoundException("Active leave type was not found in this Business.");
        if (input.ScopeType == HrLeavePolicyScopeType.Employee)
        {
            var employeeId = long.Parse(input.ScopeReference!, System.Globalization.CultureInfo.InvariantCulture);
            if (!await db.HrEmployees.AnyAsync(
                    row => row.BusinessId == businessId && row.Id == employeeId, ct))
                throw new KeyNotFoundException("Leave policy employee was not found in this Business.");
        }
        if (input.ScopeType == HrLeavePolicyScopeType.Location)
        {
            var locationId = long.Parse(input.ScopeReference!, System.Globalization.CultureInfo.InvariantCulture);
            if (!await db.OperationalLocationProjections.AnyAsync(
                    row => row.BusinessId == businessId && row.Id == locationId, ct))
                throw new KeyNotFoundException("Leave policy operational location was not found in this Business.");
        }

        var versions = await db.HrLeavePolicyVersions
            .Where(row => row.BusinessId == businessId && row.LeaveTypeId == type.Id
                && row.Code == input.Code)
            .OrderByDescending(row => row.VersionNo)
            .Take(1)
            .Select(row => row.VersionNo)
            .ToArrayAsync(ct);
        if (input.VersionNo != (versions.SingleOrDefault() + 1))
            throw new InvalidOperationException("Leave policy versions must be created sequentially.");

        var scopePolicies = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.LeaveTypeId == type.Id
                && row.ScopeType == input.ScopeType && row.ScopeReference == input.ScopeReference)
            .Select(row => new { row.LeaveYearStartMonth, row.LeaveYearStartDay })
            .ToArrayAsync(ct);
        if (scopePolicies.Any(row => row.LeaveYearStartMonth != input.LeaveYearStartMonth
            || row.LeaveYearStartDay != input.LeaveYearStartDay))
            throw new InvalidOperationException("Leave-year boundaries cannot change between policy versions at the same scope.");

        var candidateEffectiveTo = input.EffectiveTo ?? DateOnly.MaxValue;
        var competing = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.LeaveTypeId == type.Id
                && row.ScopeType == input.ScopeType && row.ScopeReference == input.ScopeReference
                && row.EffectiveFrom <= candidateEffectiveTo
                && (row.EffectiveTo == null || row.EffectiveTo >= input.EffectiveFrom))
            .AnyAsync(ct);
        if (competing)
            throw new InvalidOperationException("Leave policy effective periods cannot overlap within one scope.");

        var policy = new HrLeavePolicyVersion
        {
            BusinessId = businessId,
            LeaveTypeId = type.Id,
            Code = input.Code,
            Name = input.Name,
            ScopeType = input.ScopeType,
            ScopeReference = input.ScopeReference,
            VersionNo = input.VersionNo,
            AccrualMethod = accrualMethod,
            AnnualEntitlement = input.AnnualEntitlement,
            CarryForwardAllowed = input.CarryForwardAllowed,
            CarryForwardCap = input.CarryForwardCap,
            CarryForwardExpiryMonths = input.CarryForwardExpiryMonths,
            MaximumBalance = input.MaximumBalance,
            NegativeBalanceAllowed = input.NegativeBalanceAllowed,
            PayrollImpactCode = input.PayrollImpactCode,
            LeaveYearStartMonth = input.LeaveYearStartMonth,
            LeaveYearStartDay = input.LeaveYearStartDay,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive
        };
        db.HrLeavePolicyVersions.Add(policy);
        await db.SaveChangesAsync(ct);
        return Map(policy);
    }

    public async Task<IReadOnlyList<HrLeaveBalanceModel>> GetBalancesAsync(
        long businessId, long employeeId, long? leaveTypeId, CancellationToken ct)
    {
        var query = db.HrLeaveEntitlements.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId);
        if (leaveTypeId.HasValue)
            query = query.Where(row => row.LeaveTypeId == leaveTypeId.Value);
        var entitlements = await query.OrderBy(row => row.LeaveYearStart).ThenBy(row => row.LeaveTypeId)
            .ToArrayAsync(ct);
        if (entitlements.Length == 0)
            return [];
        var entitlementIds = entitlements.Select(row => row.Id).ToList();
        var entries = await db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId && entitlementIds.Contains(row.EntitlementId))
            .ToArrayAsync(ct);
        var lifecycleEvents = await db.HrLeaveYearLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == businessId && entitlementIds.Contains(row.EntitlementId))
            .OrderBy(row => row.Id)
            .ToArrayAsync(ct);
        var policyIds = entitlements.Select(row => row.PolicyVersionId).Distinct().ToList();
        var policies = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && policyIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, ct);
        var leaveTypeIds = entitlements.Select(row => row.LeaveTypeId).Distinct().ToList();
        var types = await db.HrLeaveTypes.AsNoTracking()
            .Where(row => row.BusinessId == businessId && leaveTypeIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, ct);

        return entitlements.Select(entitlement =>
        {
            var rows = entries.Where(entry => entry.EntitlementId == entitlement.Id).ToArray();
            var total = rows.Sum(row => row.Quantity);
            var type = types[entitlement.LeaveTypeId];
            var policy = policies[entitlement.PolicyVersionId];
            var actions = lifecycleEvents.Where(item => item.EntitlementId == entitlement.Id).ToArray();
            var lastClose = actions.LastOrDefault(item => item.Action == HrLeaveYearLifecycleAction.Closed);
            var lastAction = actions.LastOrDefault();
            var carryForwardExpiresOn = rows
                .Where(row => row.TransactionType == HrLeaveLedgerTransactionType.CarryForward
                    && row.Quantity > 0 && row.ExpiresOn.HasValue)
                .OrderBy(row => row.ExpiresOn)
                .Select(row => row.ExpiresOn)
                .FirstOrDefault() ?? lastClose?.CarryForwardExpiresOn;
            return new HrLeaveBalanceModel(
                employeeId, entitlement.LeaveTypeId, type.Code, type.Name,
                entitlement.LeaveYearStart, entitlement.LeaveYearEnd, policy.VersionNo,
                policy.AccrualMethod,
                Sum(rows, HrLeaveLedgerTransactionType.Opening),
                Sum(rows, HrLeaveLedgerTransactionType.Accrual),
                -Sum(rows, HrLeaveLedgerTransactionType.Consumption),
                Sum(rows, HrLeaveLedgerTransactionType.Restoration),
                Sum(rows, HrLeaveLedgerTransactionType.Adjustment)
                    + Sum(rows, HrLeaveLedgerTransactionType.CloseCorrection),
                Sum(rows, HrLeaveLedgerTransactionType.CarryForward),
                -Sum(rows, HrLeaveLedgerTransactionType.Expiry),
                total, entitlement.Version, entitlement.Status.ToString(), entitlement.CloseVersion,
                lastClose?.BalanceBeforeClose, lastClose?.CarryForwardAmount,
                lastClose?.ExpiredAmount, carryForwardExpiresOn,
                lastAction is null ? null : UtcOffset(lastAction.CreatedAtUtc),
                lastAction?.Action.ToString(), lastAction?.Reason);
        }).ToArray();
    }

    public async Task<IReadOnlyList<HrLeaveLedgerModel>> GetLedgerAsync(
        long businessId, long employeeId, long? leaveTypeId, CancellationToken ct)
    {
        var query = db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId);
        if (leaveTypeId.HasValue)
            query = query.Where(row => row.LeaveTypeId == leaveTypeId.Value);
        var rows = await query.Join(db.HrLeaveEntitlements.AsNoTracking(),
                entry => new { entry.BusinessId, Id = entry.EntitlementId },
                entitlement => new { entitlement.BusinessId, Id = entitlement.Id },
                (entry, entitlement) => new
                {
                    Entry = entry,
                    entitlement.LeaveYearStart
                })
            .OrderBy(row => row.Entry.EffectiveDate).ThenBy(row => row.Entry.Id)
            .ToArrayAsync(ct);
        return rows.Select(row => new HrLeaveLedgerModel(
                    row.Entry.Id, row.Entry.EmployeeId, row.Entry.LeaveTypeId, row.LeaveYearStart,
                    row.Entry.TransactionType, row.Entry.Quantity, row.Entry.EffectiveDate,
                    row.Entry.SourceType, row.Entry.SourceId, row.Entry.Reason, row.Entry.ActorRealUserId,
                    new DateTimeOffset(DateTime.SpecifyKind(row.Entry.CreatedAtUtc, DateTimeKind.Utc))))
            .ToArray();
    }

    public async Task<HrLeavePolicyContext> ResolvePolicyAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly effectiveOn, CancellationToken ct)
    {
        var employee = await db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == employeeId)
            .Select(row => new
            {
                row.JoiningDate,
                row.CurrentPrimaryLocationProjectionId
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");

        var hasAssignments = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .AnyAsync(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary", ct);
        var assignment = await db.HrEmployeeLocationAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == "Primary"
                && (row.Status == "Effective" || row.Status == "Closed")
                && row.EffectiveFrom <= effectiveOn
                && (row.EffectiveTo == null || row.EffectiveTo >= effectiveOn))
            .Select(row => new
            {
                row.OperationalLocationProjectionId,
                row.SourceEntityTypeSnapshot
            })
            .Take(2)
            .ToArrayAsync(ct);
        if (assignment.Length > 1)
            throw new InvalidOperationException("Employee has overlapping primary locations on the leave policy date.");

        var locationId = assignment.SingleOrDefault()?.OperationalLocationProjectionId
            ?? (hasAssignments ? null : employee.CurrentPrimaryLocationProjectionId);
        var locationType = assignment.SingleOrDefault()?.SourceEntityTypeSnapshot;
        if (locationType is null && locationId.HasValue)
        {
            locationType = await db.OperationalLocationProjections.AsNoTracking()
                .Where(row => row.BusinessId == businessId && row.Id == locationId.Value)
                .Select(row => row.SourceEntityType)
                .SingleOrDefaultAsync(ct);
        }
        var employeeReference = employeeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var locationReference = locationId?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var candidates = await db.HrLeavePolicyVersions.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.LeaveTypeId == leaveTypeId
                && row.IsActive && row.EffectiveFrom <= effectiveOn
                && (row.EffectiveTo == null || row.EffectiveTo >= effectiveOn)
                && (row.ScopeType == HrLeavePolicyScopeType.Employee && row.ScopeReference == employeeReference
                    || row.ScopeType == HrLeavePolicyScopeType.Location && row.ScopeReference == locationReference
                    || row.ScopeType == HrLeavePolicyScopeType.LocationType && row.ScopeReference == locationType
                    || row.ScopeType == HrLeavePolicyScopeType.Business && row.ScopeReference == null))
            .ToArrayAsync(ct);
        var ordered = candidates.OrderByDescending(row => Specificity(row.ScopeType)).ToArray();
        if (ordered.Length == 0)
            throw new InvalidOperationException("No effective leave policy is configured for this employee, type and date.");
        var specificity = Specificity(ordered[0].ScopeType);
        if (ordered.Count(row => Specificity(row.ScopeType) == specificity) > 1)
            throw new InvalidOperationException("Multiple effective leave policies match the employee and scope.");
        return new HrLeavePolicyContext(ordered[0], employee.JoiningDate, locationId, locationType);
    }

    public async Task<HrLeaveLedgerWriteResult> PostLedgerEntriesAsync(
        long businessId,
        long employeeId,
        long leaveTypeId,
        HrLeavePolicyContext context,
        IReadOnlyList<HrLeaveLedgerWrite> writes,
        long actorUserId,
        CancellationToken ct,
        bool allowClosingReview = false)
    {
        if (writes.Count == 0)
            return new HrLeaveLedgerWriteResult([], true);
        await LockEmployeeAsync(businessId, employeeId, ct);

        var type = await db.HrLeaveTypes.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == leaveTypeId && row.IsActive, ct)
            ?? throw new KeyNotFoundException("Active leave type was not found in this Business.");
        var (yearStart, yearEnd) = Application.HR.Leave.HrLeaveRules.GetLeaveYear(
            writes[0].EffectiveDate, context.Policy.LeaveYearStartMonth, context.Policy.LeaveYearStartDay);
        if (writes.Any(write => write.EffectiveDate < yearStart || write.EffectiveDate > yearEnd))
            throw new InvalidOperationException("Ledger entries must belong to one leave year.");

        var entitlement = await db.HrLeaveEntitlements.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId
            && row.LeaveTypeId == leaveTypeId && row.LeaveYearStart == yearStart, ct);
        if (entitlement is null)
        {
            entitlement = new HrLeaveEntitlement
            {
                BusinessId = businessId,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                PolicyVersionId = context.Policy.Id,
                LeaveYearStart = yearStart,
                LeaveYearEnd = yearEnd,
                JoiningDateSnapshot = context.JoiningDate,
                PrimaryLocationProjectionIdSnapshot = context.PrimaryLocationProjectionId,
                LocationTypeSnapshot = context.LocationType,
                Version = 1
            };
            db.HrLeaveEntitlements.Add(entitlement);
            await db.SaveChangesAsync(ct);
        }
        else if (entitlement.PolicyVersionId != context.Policy.Id)
            throw new InvalidOperationException(
                "The leave-year entitlement is already bound to another policy version; historical entitlement cannot be rewritten.");
        if (entitlement.Status != HrLeaveEntitlementStatus.Open
            && !(allowClosingReview && entitlement.Status == HrLeaveEntitlementStatus.ClosingReview))
            throw new InvalidOperationException("Leave ledger entries cannot be added while this leave year is not open.");
        await ExpireCarryForwardBalancesAsync(
            businessId, employeeId, leaveTypeId,
            writes.Max(write => write.EffectiveDate), context, actorUserId, ct);

        var currentEntries = await db.HrLeaveLedgerEntries
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id)
            .ToArrayAsync(ct);
        var accepted = new List<HrLeaveLedgerEntry>(writes.Count);
        var isReplay = true;
        var currentTotal = currentEntries.Sum(row => row.Quantity);
        foreach (var write in writes)
        {
            var prior = await db.HrLeaveLedgerEntries.AsNoTracking().SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.IdempotencyKey == write.IdempotencyKey, ct);
            if (prior is not null)
            {
                if (!string.Equals(prior.PayloadHash, write.PayloadHash, StringComparison.Ordinal)
                    || prior.EmployeeId != employeeId || prior.LeaveTypeId != leaveTypeId)
                    throw new InvalidOperationException("Leave ledger idempotency key was already used with a different payload.");
                accepted.Add(prior);
                continue;
            }

            if (write.TransactionType == HrLeaveLedgerTransactionType.Opening
                && currentEntries.Any(row => row.TransactionType == HrLeaveLedgerTransactionType.Opening))
                throw new InvalidOperationException("An opening balance can be posted only once per leave year.");
            currentTotal += write.Quantity;
            if (!context.Policy.NegativeBalanceAllowed && currentTotal < 0)
                throw new InvalidOperationException("This leave policy does not permit a negative balance.");
            if (context.Policy.MaximumBalance is decimal maximum && currentTotal > maximum)
                throw new InvalidOperationException("Leave balance exceeds the policy maximum.");
            var entry = new HrLeaveLedgerEntry
            {
                BusinessId = businessId,
                EntitlementId = entitlement.Id,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                PolicyVersionId = context.Policy.Id,
                TransactionType = write.TransactionType,
                Quantity = write.Quantity,
                EffectiveDate = write.EffectiveDate,
                SourceType = write.SourceType,
                SourceId = write.SourceId,
                IdempotencyKey = write.IdempotencyKey,
                PayloadHash = write.PayloadHash,
                Reason = write.Reason,
                ExpiresOn = write.ExpiresOn,
                ActorRealUserId = actorUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.HrLeaveLedgerEntries.Add(entry);
            accepted.Add(entry);
            currentEntries = [.. currentEntries, entry];
            isReplay = false;
        }
        if (!isReplay)
        {
            entitlement.Version++;
            await db.SaveChangesAsync(ct);
        }
        return new HrLeaveLedgerWriteResult(accepted, isReplay);
    }

    public async Task<HrLeaveYearLifecycleModel> ReviewLeaveYearCloseAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, long actorUserId, CancellationToken ct)
    {
        await LockEmployeeAsync(businessId, employeeId, ct);
        var entitlement = await FindEntitlementAsync(
            businessId, employeeId, leaveTypeId, leaveYearStart, ct);
        var payloadHash = LifecyclePayloadHash(
            HrLeaveYearLifecycleAction.ClosingReview, employeeId, leaveTypeId,
            leaveYearStart, expectedVersion, "");
        var prior = await FindLifecycleActionAsync(businessId, entitlement.Id, idempotencyKey, ct);
        if (prior is not null)
            return await ReplayLifecycleAsync(entitlement, prior, payloadHash, ct);
        EnsureVersion(entitlement, expectedVersion);
        if (entitlement.Status != HrLeaveEntitlementStatus.Open)
            throw new InvalidOperationException("Only an open leave year can enter closing review.");
        if (DateOnly.FromDateTime(DateTime.UtcNow) <= entitlement.LeaveYearEnd)
            throw new InvalidOperationException("A leave year cannot enter closing review before its end date.");
        await EnsureNoPendingLeaveRequestsAsync(
            businessId, employeeId, leaveTypeId, entitlement.LeaveYearStart, entitlement.LeaveYearEnd, ct);

        var policy = await FindEntitlementPolicyAsync(businessId, entitlement, ct);
        var context = await ResolvePolicyAsync(
            businessId, employeeId, leaveTypeId, entitlement.LeaveYearStart, ct);
        if (context.Policy.Id != policy.Id)
            throw new InvalidOperationException("The leave-year policy context no longer matches its frozen entitlement.");

        var due = Application.HR.Leave.HrLeaveRules.GetAccrualDues(
            policy.AccrualMethod, policy.AnnualEntitlement, entitlement.JoiningDateSnapshot,
            entitlement.LeaveYearStart, entitlement.LeaveYearEnd, entitlement.LeaveYearEnd);
        var accrualWrites = due.Select(item => Application.HR.Leave.PostHrLeaveLedgerEntryHandler.MakeWrite(
            HrLeaveLedgerTransactionType.Accrual, item.Quantity, item.DueDate,
            "Accrual", $"{employeeId}:{leaveTypeId}:{policy.Id}:{item.DueDate:yyyyMMdd}",
            $"leave-accrual:{employeeId}:{leaveTypeId}:{policy.Id}:{item.DueDate:yyyyMMdd}",
            $"Policy {policy.Code} version {policy.VersionNo} accrual.")).ToArray();
        if (accrualWrites.Length > 0)
            await PostLedgerEntriesAsync(
                businessId, employeeId, leaveTypeId, context, accrualWrites, actorUserId, ct);
        await ExpireCarryForwardBalancesAsync(
            businessId, employeeId, leaveTypeId, entitlement.LeaveYearEnd, context, actorUserId, ct);

        var previousClose = await FindLatestClosedEventAsync(businessId, entitlement.Id, ct);
        var entries = await db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id)
            .ToArrayAsync(ct);
        var balanceBeforeClose = entries.Sum(row => row.Quantity) + (previousClose?.ExpiredAmount ?? 0m);
        var calculation = Application.HR.Leave.HrLeaveYearCloseRules.Calculate(
            balanceBeforeClose, policy, entitlement.LeaveYearEnd);
        entitlement.Status = HrLeaveEntitlementStatus.ClosingReview;
        entitlement.Version++;
        var review = CreateLifecycleEvent(
            entitlement, HrLeaveYearLifecycleAction.ClosingReview,
            entitlement.CloseVersion + 1, calculation, null, idempotencyKey,
            payloadHash, "", actorUserId);
        db.HrLeaveYearLifecycleEvents.Add(review);
        await db.SaveChangesAsync(ct);
        return MapLifecycle(entitlement, policy.VersionNo, review, false);
    }

    public async Task<HrLeaveYearLifecycleModel> CloseLeaveYearAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, long actorUserId, CancellationToken ct)
    {
        await LockEmployeeAsync(businessId, employeeId, ct);
        var entitlement = await FindEntitlementAsync(
            businessId, employeeId, leaveTypeId, leaveYearStart, ct);
        var payloadHash = LifecyclePayloadHash(
            HrLeaveYearLifecycleAction.Closed, employeeId, leaveTypeId,
            leaveYearStart, expectedVersion, "");
        var prior = await FindLifecycleActionAsync(businessId, entitlement.Id, idempotencyKey, ct);
        if (prior is not null)
            return await ReplayLifecycleAsync(entitlement, prior, payloadHash, ct);
        EnsureVersion(entitlement, expectedVersion);
        if (entitlement.Status != HrLeaveEntitlementStatus.ClosingReview)
            throw new InvalidOperationException("A leave year must be in closing review before it can be closed.");
        await EnsureNoPendingLeaveRequestsAsync(
            businessId, employeeId, leaveTypeId, entitlement.LeaveYearStart, entitlement.LeaveYearEnd, ct);

        var review = await db.HrLeaveYearLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id
                && row.Action == HrLeaveYearLifecycleAction.ClosingReview
                && row.CloseVersion == entitlement.CloseVersion + 1)
            .OrderByDescending(row => row.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The current leave-year close review evidence is missing.");
        var policy = await FindEntitlementPolicyAsync(businessId, entitlement, ct);
        var previousClose = await FindLatestClosedEventAsync(businessId, entitlement.Id, ct);
        var oldCarryForward = previousClose?.CarryForwardAmount ?? 0m;
        var oldExpired = previousClose?.ExpiredAmount ?? 0m;
        var sourceId = $"{entitlement.Id}:{entitlement.CloseVersion + 1}";
        var closeWrites = new List<HrLeaveLedgerWrite>(2);
        var expiryDelta = review.ExpiredAmount - oldExpired;
        if (expiryDelta > 0)
            closeWrites.Add(Application.HR.Leave.PostHrLeaveLedgerEntryHandler.MakeWrite(
                HrLeaveLedgerTransactionType.Expiry, -expiryDelta, entitlement.LeaveYearEnd,
                "LeaveYearClose", sourceId, $"leave-year-close:{sourceId}:expiry",
                $"Leave-year close expiry under policy {policy.Code} version {policy.VersionNo}."));
        else if (expiryDelta < 0)
            closeWrites.Add(Application.HR.Leave.PostHrLeaveLedgerEntryHandler.MakeWrite(
                HrLeaveLedgerTransactionType.CloseCorrection, -expiryDelta, entitlement.LeaveYearEnd,
                "LeaveYearCloseCorrection", sourceId, $"leave-year-close:{sourceId}:expiry-correction",
                "Reversal of prior leave-year close expiry."));

        if (closeWrites.Count > 0)
        {
            var currentContext = await ResolvePolicyAsync(
                businessId, employeeId, leaveTypeId, entitlement.LeaveYearStart, ct);
            await PostLedgerEntriesAsync(
                businessId, employeeId, leaveTypeId, currentContext, closeWrites,
                actorUserId, ct, allowClosingReview: true);
        }

        var carryForwardDelta = review.CarryForwardAmount - oldCarryForward;
        if (carryForwardDelta != 0)
        {
            var nextYearStart = entitlement.LeaveYearEnd.AddDays(1);
            var nextYearContext = await ResolvePolicyAsync(
                businessId, employeeId, leaveTypeId, nextYearStart, ct);
            var (resolvedNextStart, _) = Application.HR.Leave.HrLeaveRules.GetLeaveYear(
                nextYearStart, nextYearContext.Policy.LeaveYearStartMonth,
                nextYearContext.Policy.LeaveYearStartDay);
            if (resolvedNextStart != nextYearStart)
                throw new InvalidOperationException("The next leave-year policy boundary does not match the closing year.");
            var transaction = carryForwardDelta > 0
                ? HrLeaveLedgerTransactionType.CarryForward
                : HrLeaveLedgerTransactionType.CloseCorrection;
            var carryWrite = Application.HR.Leave.PostHrLeaveLedgerEntryHandler.MakeWrite(
                transaction, carryForwardDelta, nextYearStart,
                carryForwardDelta > 0 ? "LeaveYearClose" : "LeaveYearCloseCorrection",
                sourceId, $"leave-year-close:{sourceId}:carry-forward",
                carryForwardDelta > 0
                    ? "Carry-forward from the prior leave year."
                    : "Correction of prior leave-year carry-forward.",
                carryForwardDelta > 0 ? review.CarryForwardExpiresOn : null);
            await PostLedgerEntriesAsync(
                businessId, employeeId, leaveTypeId, nextYearContext,
                [carryWrite], actorUserId, ct);
        }

        entitlement.Status = HrLeaveEntitlementStatus.Closed;
        entitlement.CloseVersion++;
        entitlement.Version++;
        var closed = CreateLifecycleEvent(
            entitlement, HrLeaveYearLifecycleAction.Closed,
            entitlement.CloseVersion,
            new HrLeaveYearCloseCalculation(
                review.BalanceBeforeClose, review.CarryForwardAmount,
                review.ExpiredAmount, review.CarryForwardExpiresOn),
            null, idempotencyKey, payloadHash, "", actorUserId);
        db.HrLeaveYearLifecycleEvents.Add(closed);
        await db.SaveChangesAsync(ct);
        return MapLifecycle(entitlement, policy.VersionNo, closed, false);
    }

    public async Task<HrLeaveYearLifecycleModel> ReopenLeaveYearAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, string reason, long actorUserId, CancellationToken ct)
    {
        await LockEmployeeAsync(businessId, employeeId, ct);
        var entitlement = await FindEntitlementAsync(
            businessId, employeeId, leaveTypeId, leaveYearStart, ct);
        var payloadHash = LifecyclePayloadHash(
            HrLeaveYearLifecycleAction.Reopened, employeeId, leaveTypeId,
            leaveYearStart, expectedVersion, reason);
        var prior = await FindLifecycleActionAsync(businessId, entitlement.Id, idempotencyKey, ct);
        if (prior is not null)
            return await ReplayLifecycleAsync(entitlement, prior, payloadHash, ct);
        EnsureVersion(entitlement, expectedVersion);
        if (entitlement.Status != HrLeaveEntitlementStatus.Closed)
            throw new InvalidOperationException("Only a closed leave year can be reopened.");
        var latestClose = await FindLatestClosedEventAsync(businessId, entitlement.Id, ct)
            ?? throw new InvalidOperationException("The closed leave year has no close evidence.");
        var policy = await FindEntitlementPolicyAsync(businessId, entitlement, ct);
        entitlement.Status = HrLeaveEntitlementStatus.Open;
        entitlement.Version++;
        var reopened = CreateLifecycleEvent(
            entitlement, HrLeaveYearLifecycleAction.Reopened,
            entitlement.CloseVersion,
            new HrLeaveYearCloseCalculation(
                latestClose.BalanceBeforeClose, latestClose.CarryForwardAmount,
                latestClose.ExpiredAmount, latestClose.CarryForwardExpiresOn),
            null, idempotencyKey, payloadHash, reason, actorUserId);
        db.HrLeaveYearLifecycleEvents.Add(reopened);
        await db.SaveChangesAsync(ct);
        return MapLifecycle(entitlement, policy.VersionNo, reopened, false);
    }

    public async Task<HrLeaveLedgerWriteResult> ExpireCarryForwardBalancesAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly throughDate,
        HrLeavePolicyContext context, long actorUserId, CancellationToken ct)
    {
        await LockEmployeeAsync(businessId, employeeId, ct);
        var (yearStart, yearEnd) = Application.HR.Leave.HrLeaveRules.GetLeaveYear(
            throughDate, context.Policy.LeaveYearStartMonth, context.Policy.LeaveYearStartDay);
        var entitlement = await db.HrLeaveEntitlements.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId
            && row.LeaveTypeId == leaveTypeId && row.LeaveYearStart == yearStart, ct);
        if (entitlement is null)
            return new HrLeaveLedgerWriteResult([], true);
        if (entitlement.Status is not (HrLeaveEntitlementStatus.Open or HrLeaveEntitlementStatus.ClosingReview))
            throw new InvalidOperationException("Carry-forward expiry cannot change a closed leave year.");

        var credits = await db.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id
                && row.TransactionType == HrLeaveLedgerTransactionType.CarryForward
                && row.Quantity > 0 && row.ExpiresOn != null && row.ExpiresOn <= throughDate)
            .OrderBy(row => row.ExpiresOn).ThenBy(row => row.Id)
            .ToArrayAsync(ct);
        if (credits.Length == 0)
            return new HrLeaveLedgerWriteResult([], true);
        var creditIds = credits.Select(row => row.Id).ToList();
        var processedIds = await db.HrLeaveYearLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id
                && row.Action == HrLeaveYearLifecycleAction.CarryForwardExpired
                && row.SourceLedgerEntryId != null && creditIds.Contains(row.SourceLedgerEntryId.Value))
            .Select(row => row.SourceLedgerEntryId!.Value)
            .ToArrayAsync(ct);
        var processed = processedIds.ToHashSet();
        var balanceRows = await db.HrLeaveLedgerEntries
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlement.Id)
            .ToArrayAsync(ct);
        var available = Math.Max(0m, balanceRows.Sum(row => row.Quantity));
        var expiredRows = new List<HrLeaveLedgerEntry>();
        var hasNewEvidence = false;
        foreach (var credit in credits)
        {
            if (processed.Contains(credit.Id))
                continue;
            var quantity = Math.Min(available, credit.Quantity);
            var sourceId = credit.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var key = $"leave-carry-forward-expiry:{credit.Id}";
            if (quantity > 0)
            {
                var write = Application.HR.Leave.PostHrLeaveLedgerEntryHandler.MakeWrite(
                    HrLeaveLedgerTransactionType.Expiry, -quantity, credit.ExpiresOn!.Value,
                    "CarryForwardExpiry", sourceId, key,
                    "Expired unused carry-forward balance.");
                var row = new HrLeaveLedgerEntry
                {
                    BusinessId = businessId,
                    EntitlementId = entitlement.Id,
                    EmployeeId = employeeId,
                    LeaveTypeId = leaveTypeId,
                    PolicyVersionId = entitlement.PolicyVersionId,
                    TransactionType = write.TransactionType,
                    Quantity = write.Quantity,
                    EffectiveDate = write.EffectiveDate,
                    SourceType = write.SourceType,
                    SourceId = write.SourceId,
                    IdempotencyKey = write.IdempotencyKey,
                    PayloadHash = write.PayloadHash,
                    Reason = write.Reason,
                    ActorRealUserId = actorUserId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.HrLeaveLedgerEntries.Add(row);
                expiredRows.Add(row);
                available -= quantity;
            }
            var expiration = new HrLeaveYearLifecycleEvent
            {
                BusinessId = businessId,
                EntitlementId = entitlement.Id,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                LeaveYearStart = entitlement.LeaveYearStart,
                CloseVersion = entitlement.CloseVersion,
                Action = HrLeaveYearLifecycleAction.CarryForwardExpired,
                BalanceBeforeClose = balanceRows.Sum(row => row.Quantity),
                ExpiredAmount = quantity,
                SourceLedgerEntryId = credit.Id,
                IdempotencyKey = key,
                PayloadHash = LifecyclePayloadHash(
                    HrLeaveYearLifecycleAction.CarryForwardExpired, employeeId, leaveTypeId,
                    entitlement.LeaveYearStart, credit.Id, quantity.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                Reason = "Carry-forward expired on its policy-defined expiry date.",
                ActorRealUserId = actorUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.HrLeaveYearLifecycleEvents.Add(expiration);
            hasNewEvidence = true;
        }
        if (!hasNewEvidence)
            return new HrLeaveLedgerWriteResult([], true);
        entitlement.Version++;
        await db.SaveChangesAsync(ct);
        return new HrLeaveLedgerWriteResult(expiredRows, false);
    }

    private async Task<HrLeaveEntitlement> FindEntitlementAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart, CancellationToken ct) =>
        await db.HrLeaveEntitlements.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId
            && row.LeaveTypeId == leaveTypeId && row.LeaveYearStart == leaveYearStart, ct)
        ?? throw new KeyNotFoundException("Leave-year entitlement was not found in this Business.");

    private async Task<HrLeavePolicyVersion> FindEntitlementPolicyAsync(
        long businessId, HrLeaveEntitlement entitlement, CancellationToken ct) =>
        await db.HrLeavePolicyVersions.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == entitlement.PolicyVersionId, ct)
        ?? throw new InvalidOperationException("The leave-year policy snapshot is missing.");

    private Task<HrLeaveYearLifecycleEvent?> FindLifecycleActionAsync(
        long businessId, long entitlementId, string idempotencyKey, CancellationToken ct) =>
        db.HrLeaveYearLifecycleEvents.AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EntitlementId == entitlementId
            && row.IdempotencyKey == idempotencyKey, ct);

    private Task<HrLeaveYearLifecycleEvent?> FindLatestClosedEventAsync(
        long businessId, long entitlementId, CancellationToken ct) =>
        db.HrLeaveYearLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EntitlementId == entitlementId
                && row.Action == HrLeaveYearLifecycleAction.Closed)
            .OrderByDescending(row => row.Id)
            .FirstOrDefaultAsync(ct);

    private async Task EnsureNoPendingLeaveRequestsAsync(
        long businessId, long employeeId, long leaveTypeId,
        DateOnly leaveYearStart, DateOnly leaveYearEnd, CancellationToken ct)
    {
        var hasPending = await db.HrLeaveRequests.AsNoTracking().AnyAsync(request =>
            request.BusinessId == businessId && request.EmployeeId == employeeId
            && request.LeaveTypeId == leaveTypeId && request.Status == "PendingApproval"
            && db.HrLeaveRequestDays.Any(day => day.BusinessId == businessId
                && day.LeaveRequestId == request.Id
                && day.WorkDate >= leaveYearStart && day.WorkDate <= leaveYearEnd), ct);
        if (hasPending)
            throw new InvalidOperationException("Pending leave requests must be decided before closing the leave year.");
    }

    private async Task<HrLeaveYearLifecycleModel> ReplayLifecycleAsync(
        HrLeaveEntitlement entitlement, HrLeaveYearLifecycleEvent prior,
        string expectedPayloadHash, CancellationToken ct)
    {
        if (!string.Equals(prior.PayloadHash, expectedPayloadHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Leave-year idempotency key was used with a different payload.");
        var policy = await FindEntitlementPolicyAsync(entitlement.BusinessId, entitlement, ct);
        return MapLifecycle(entitlement, policy.VersionNo, prior, true);
    }

    private static HrLeaveYearLifecycleModel MapLifecycle(
        HrLeaveEntitlement entitlement, int policyVersionNo,
        HrLeaveYearLifecycleEvent lifecycleEvent, bool isReplay) =>
        new(entitlement.EmployeeId, entitlement.LeaveTypeId, entitlement.LeaveYearStart,
            entitlement.LeaveYearEnd, entitlement.Status.ToString(), entitlement.CloseVersion,
            entitlement.Version, policyVersionNo, lifecycleEvent.BalanceBeforeClose,
            lifecycleEvent.CarryForwardAmount, lifecycleEvent.ExpiredAmount,
            lifecycleEvent.CarryForwardExpiresOn,
            UtcOffset(lifecycleEvent.CreatedAtUtc), lifecycleEvent.Action.ToString(),
            lifecycleEvent.Reason, isReplay);

    private static void EnsureVersion(HrLeaveEntitlement entitlement, long expectedVersion)
    {
        if (entitlement.Version != expectedVersion)
            throw new InvalidOperationException("Leave-year entitlement changed; reload before continuing.");
    }

    private static HrLeaveYearLifecycleEvent CreateLifecycleEvent(
        HrLeaveEntitlement entitlement, HrLeaveYearLifecycleAction action, int closeVersion,
        Application.HR.Leave.HrLeaveYearCloseCalculation calculation, long? sourceLedgerEntryId,
        string idempotencyKey, string payloadHash, string reason, long actorUserId) =>
        new()
        {
            BusinessId = entitlement.BusinessId,
            EntitlementId = entitlement.Id,
            EmployeeId = entitlement.EmployeeId,
            LeaveTypeId = entitlement.LeaveTypeId,
            LeaveYearStart = entitlement.LeaveYearStart,
            CloseVersion = closeVersion,
            Action = action,
            BalanceBeforeClose = calculation.BalanceBeforeClose,
            CarryForwardAmount = calculation.CarryForwardAmount,
            ExpiredAmount = calculation.ExpiredAmount,
            CarryForwardExpiresOn = calculation.CarryForwardExpiresOn,
            SourceLedgerEntryId = sourceLedgerEntryId,
            IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash,
            Reason = reason,
            ActorRealUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static string LifecyclePayloadHash(
        HrLeaveYearLifecycleAction action, long employeeId, long leaveTypeId,
        DateOnly leaveYearStart, object expectedVersion, string reason)
    {
        var canonical = System.Text.Json.JsonSerializer.Serialize(new
        {
            Action = action.ToString(), EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            LeaveYearStart = leaveYearStart, ExpectedVersion = expectedVersion, Reason = reason
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task LockEmployeeAsync(long businessId, long employeeId, CancellationToken ct)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            if (db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("PostgreSQL leave-year changes require an owning transaction.");
            var lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_employees WHERE id = {employeeId} AND business_id = {businessId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (lockedId == 0)
                throw new KeyNotFoundException("Employee was not found in this Business.");
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException("Leave-year serialization is configured only for PostgreSQL and SQLite tests.");
    }

    private static DateTimeOffset UtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static int Specificity(HrLeavePolicyScopeType scopeType) => scopeType switch
    {
        HrLeavePolicyScopeType.Employee => 4,
        HrLeavePolicyScopeType.Location => 3,
        HrLeavePolicyScopeType.LocationType => 2,
        HrLeavePolicyScopeType.Business => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(scopeType))
    };

    private static decimal Sum(IEnumerable<HrLeaveLedgerEntry> rows, HrLeaveLedgerTransactionType type) =>
        rows.Where(row => row.TransactionType == type).Sum(row => row.Quantity);

    private static HrLeaveTypeModel Map(HrLeaveType row) =>
        new(row.Id, row.Code, row.Name, row.IsPaid, row.RequiresDocument, row.IsActive, row.Version);

    private static HrLeavePolicyModel Map(HrLeavePolicyVersion row) =>
        new(row.Id, row.LeaveTypeId, row.Code, row.Name, row.ScopeType, row.ScopeReference,
            row.VersionNo, row.AccrualMethod, row.AnnualEntitlement, row.CarryForwardAllowed,
            row.CarryForwardCap, row.CarryForwardExpiryMonths, row.MaximumBalance,
            row.NegativeBalanceAllowed, row.PayrollImpactCode, row.LeaveYearStartMonth,
            row.LeaveYearStartDay, row.EffectiveFrom, row.EffectiveTo, row.IsActive);
}
