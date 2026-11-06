namespace RetailPOS.Application.OwnerDashboards;

public sealed record TargetOutcome(decimal? Shortfall, decimal? Exceeded, decimal? AchievementPercent, string Status);

/// <summary>Sales and finance arithmetic. Side-effect free; money stays in <see cref="decimal"/>.</summary>
public static class SalesFinanceMath
{
    /// <summary>A missing or zero target is N/A: never 0% achievement and never a divide by zero (SF-014, SRS §9.3).</summary>
    public static TargetOutcome EvaluateTarget(decimal netSales, decimal? target)
    {
        if (target is not > 0m)
            return new TargetOutcome(null, null, null, MetricStatuses.NotConfigured);

        return new TargetOutcome(
            Math.Max(target.Value - netSales, 0m),
            Math.Max(netSales - target.Value, 0m),
            Math.Round(100m * netSales / target.Value, 2, MidpointRounding.AwayFromZero),
            MetricStatuses.Ok);
    }

    /// <summary>Profit is revenue less COGS less expense. Purchases are deliberately not an input (AC-07).</summary>
    public static MetricValue EstimateProfit(
        decimal netSales,
        decimal cogs,
        int missingCostLines,
        int reversalsWithoutEvidence,
        decimal expense)
    {
        if (missingCostLines > 0)
            return new MetricValue(
                null,
                MetricStatuses.Unavailable,
                $"{missingCostLines} sold line(s) have no recorded cost, so COGS is incomplete.");

        if (reversalsWithoutEvidence > 0)
            return new MetricValue(
                null,
                MetricStatuses.Unavailable,
                $"{reversalsWithoutEvidence} refund or void record(s) have no usable amounts.");

        return new MetricValue(
            netSales - cogs - expense,
            MetricStatuses.Estimated,
            "Net sales excl. VAT, less cost of goods sold, less approved expenses. Costs posted only through journals, such as payroll, are not included.");
    }

    /// <summary>Change against the previous equal-length period, shown only when that period had sales.</summary>
    public static decimal? ChangePercent(decimal current, decimal previous) =>
        previous > 0m
            ? Math.Round(100m * (current - previous) / previous, 2, MidpointRounding.AwayFromZero)
            : null;

    public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
    {
        var length = to.DayNumber - from.DayNumber + 1;
        return (from.AddDays(-length), from.AddDays(-1));
    }
}

/// <summary>Chart buckets: daily up to 45 days, then consecutive 7-day buckets from the start of the range.</summary>
public static class DashboardBuckets
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    private const int MaxDailyDays = 45;

    public static string KindFor(DateOnly from, DateOnly to) =>
        to.DayNumber - from.DayNumber + 1 > MaxDailyDays ? Weekly : Daily;

    public static IReadOnlyList<(DateOnly From, DateOnly To)> Build(DateOnly from, DateOnly to)
    {
        var step = KindFor(from, to) == Weekly ? 7 : 1;
        var buckets = new List<(DateOnly, DateOnly)>();
        for (var start = from; start <= to; start = start.AddDays(step))
        {
            var end = start.AddDays(step - 1);
            buckets.Add((start, end > to ? to : end));
        }

        return buckets;
    }
}

/// <summary>
/// Counts the days of an inclusive window on which a variant was in stock at a location, from ledger balances fed in
/// date order. A day counts if stock was held at its start or after any movement on it, so a same-day sell-out counts
/// and a day spent out of stock does not lower velocity (SRS: eligible in-stock selling days).
/// </summary>
public sealed class InStockDayTracker(DateOnly from, DateOnly to)
{
    private int _balance;
    private DateOnly _nextUncounted = from;
    private int _days;

    public bool HasEvidence { get; private set; }
    public int LatestBalance => _balance;

    public void Apply(DateOnly date, int balanceAfter)
    {
        HasEvidence = true;
        if (date > to)
        {
            CountHeldThrough(to);
        }
        else if (date >= from)
        {
            CountHeldThrough(date.AddDays(-1));
            if (date >= _nextUncounted && (_balance > 0 || balanceAfter > 0))
            {
                _days++;
                _nextUncounted = date.AddDays(1);
            }
        }

        _balance = balanceAfter;
    }

    /// <summary>Total eligible days, including the balance held through the end of the window.</summary>
    public int Complete()
    {
        CountHeldThrough(to);
        return _days;
    }

    private void CountHeldThrough(DateOnly last)
    {
        if (last < _nextUncounted)
            return;

        if (_balance > 0)
            _days += last.DayNumber - _nextUncounted.DayNumber + 1;
        _nextUncounted = last.AddDays(1);
    }
}

/// <summary>Rule-based slow-mover classification (LLD §7.3). Showrooms only; warehouses have no demand model.</summary>
public static class SlowMoverClassifier
{
    public static SlowClassificationResult Classify(VariantLocationFact fact, OwnerDashboardPolicy policy)
    {
        var minimumDays = policy.MinEligibleSellingDays;
        if (!fact.HasStockEvidence)
            return new SlowClassificationResult(
                SlowClassifications.InsufficientEvidence,
                null,
                "No reliable stock history for this variant at this showroom.");

        if (fact.EligibleSellingDays < minimumDays)
            return new SlowClassificationResult(
                SlowClassifications.InsufficientEvidence,
                null,
                $"In stock for {fact.EligibleSellingDays} day(s) in the period; {minimumDays} are needed before classifying.");

        var units = Math.Max(fact.NetUnitsSold, 0);
        var velocity = decimal.Round((decimal)units / fact.EligibleSellingDays, 4, MidpointRounding.AwayFromZero);
        if (units == 0)
            return fact.EverSold
                ? new SlowClassificationResult(
                    SlowClassifications.ZeroSales,
                    0m,
                    $"No net units sold in {fact.EligibleSellingDays} in-stock day(s).")
                : new SlowClassificationResult(
                    SlowClassifications.NeverSold,
                    0m,
                    $"No recorded sale at this showroom; in stock for {fact.EligibleSellingDays} day(s) in the period.");

        if (velocity < policy.SlowVelocityPerDay)
            return new SlowClassificationResult(
                SlowClassifications.LowSales,
                velocity,
                $"{velocity:0.##} units per in-stock day, below the {policy.SlowVelocityPerDay:0.##} threshold.");

        return new SlowClassificationResult(
            SlowClassifications.Normal,
            velocity,
            $"{velocity:0.##} units per in-stock day.");
    }

    public static bool IsEligible(VariantLocationFact fact, OwnerDashboardPolicy policy) =>
        fact.IsShowroom && fact.ProductActive && fact.Available >= policy.MinOnHandQty;
}

/// <summary>
/// Deterministic transfer recommendations (LLD §7.4). The same exact variant only; showroom to showroom only. Output is
/// advisory: quantities are bounded here and re-validated by the real transfer workflow.
/// </summary>
public static class TransferOpportunityEngine
{
    private sealed record Candidate(
        VariantLocationFact Source,
        VariantLocationFact Destination,
        decimal SourceVelocity,
        decimal DestinationVelocity,
        decimal? SourceCover,
        decimal DestinationCover,
        int SourceProtected,
        int SourceSpare,
        int DestinationNeed);

    public static IReadOnlyList<TransferOpportunity> Build(
        IReadOnlyList<VariantLocationFact> facts,
        OwnerDashboardPolicy policy,
        Func<VariantLocationFact, bool>? canInitiate = null)
    {
        var candidates = new List<Candidate>();
        var eligible = facts.Where(fact => fact.IsShowroom && fact.ProductActive && fact.HasStockEvidence);
        foreach (var group in eligible.GroupBy(fact => fact.VariantId))
        {
            var rows = group.ToList();
            var destinations = rows.Where(row => IsDestination(row, policy)).ToList();
            if (destinations.Count == 0)
                continue;

            foreach (var destination in destinations)
            {
                foreach (var source in rows.Where(row => row.LocationId != destination.LocationId))
                {
                    var candidate = TryPair(source, destination, policy);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }
            }
        }

        // Most urgent destinations are served first, from the most overstocked sources. Ties resolve on identity so
        // the same input always gives the same output.
        var ordered = candidates
            .OrderBy(candidate => candidate.DestinationCover)
            .ThenByDescending(candidate => candidate.DestinationVelocity)
            .ThenByDescending(candidate => candidate.SourceCover ?? decimal.MaxValue)
            .ThenBy(candidate => candidate.Source.VariantId)
            .ThenBy(candidate => candidate.Source.LocationId)
            .ThenBy(candidate => candidate.Destination.LocationId);

        // Competing pairs share one pool of spare and one pool of need, so suggestions never jointly exceed either.
        var spareLeft = new Dictionary<(long Variant, long Location), int>();
        var needLeft = new Dictionary<(long Variant, long Location), int>();
        var result = new List<TransferOpportunity>();
        foreach (var candidate in ordered)
        {
            var sourceKey = (candidate.Source.VariantId, candidate.Source.LocationId);
            var destinationKey = (candidate.Destination.VariantId, candidate.Destination.LocationId);
            var spare = spareLeft.GetValueOrDefault(sourceKey, candidate.SourceSpare);
            var need = needLeft.GetValueOrDefault(destinationKey, candidate.DestinationNeed);
            var suggested = Math.Min(spare, need);
            var capped = false;
            if (policy.MaxSuggestedQty is int cap && suggested > cap)
            {
                suggested = cap;
                capped = true;
            }

            if (suggested < Math.Max(policy.MinRecommendationQty, 1))
                continue;

            spareLeft[sourceKey] = spare - suggested;
            needLeft[destinationKey] = need - suggested;
            result.Add(ToOpportunity(candidate, suggested, spare, need, capped, policy, canInitiate));
        }

        return result;
    }

    public static string KeyFor(long variantId, long sourceId, long destinationId) =>
        $"{variantId}-{sourceId}-{destinationId}";

    private static bool IsDestination(VariantLocationFact fact, OwnerDashboardPolicy policy)
    {
        if (fact.EligibleSellingDays < policy.MinEligibleSellingDays)
            return false;

        var velocity = Velocity(fact);
        return velocity >= policy.MinDemandVelocityPerDay
            && fact.Available / velocity < policy.DestinationTargetCoverageDays;
    }

    private static Candidate? TryPair(
        VariantLocationFact source,
        VariantLocationFact destination,
        OwnerDashboardPolicy policy)
    {
        if (source.EligibleSellingDays < policy.MinEligibleSellingDays)
            return null;

        var sourceVelocity = Velocity(source);
        var destinationVelocity = Velocity(destination);
        var sourceCover = sourceVelocity > 0m ? source.Available / sourceVelocity : (decimal?)null;
        var hasVelocityGap = sourceVelocity == 0m || destinationVelocity / sourceVelocity >= policy.MinVelocityRatio;
        var hasCoverGap = sourceCover is null || sourceCover >= policy.SourceExcessCoverageDays;
        if (!hasVelocityGap && !hasCoverGap)
            return null;

        // The safety quantity is subtracted exactly once, here: pending outbound is a separate commitment.
        var protectedQty = Math.Max(
            policy.MinimumRetainedUnits,
            (int)decimal.Ceiling(sourceVelocity * policy.SourceRetainedCoverageDays));
        var transferable = Math.Max(0, source.Available - source.PendingOutbound);
        var spare = Math.Max(0, transferable - protectedQty);
        var need = Math.Max(
            0,
            (int)decimal.Ceiling(destinationVelocity * policy.DestinationTargetCoverageDays)
                - destination.Available
                - destination.PendingInbound);
        if (spare <= 0 || need <= 0)
            return null;

        return new Candidate(
            source,
            destination,
            sourceVelocity,
            destinationVelocity,
            sourceCover,
            destination.Available / destinationVelocity,
            protectedQty,
            spare,
            need);
    }

    private static TransferOpportunity ToOpportunity(
        Candidate candidate,
        int suggested,
        int spareAvailable,
        int needRemaining,
        bool capped,
        OwnerDashboardPolicy policy,
        Func<VariantLocationFact, bool>? canInitiate)
    {
        var source = candidate.Source;
        var destination = candidate.Destination;
        var reasons = new List<string> { TransferReasonCodes.SourceExcess };
        if (candidate.SourceVelocity == 0m)
            reasons.Add(TransferReasonCodes.SourceNoSales);
        reasons.Add(destination.Available == 0
            ? TransferReasonCodes.DestinationOutOfStock
            : TransferReasonCodes.DestinationLowCover);
        if (candidate.SourceVelocity == 0m
            || candidate.DestinationVelocity / candidate.SourceVelocity >= policy.MinVelocityRatio)
            reasons.Add(TransferReasonCodes.VelocityGap);
        if (spareAvailable < needRemaining)
            reasons.Add(TransferReasonCodes.LimitedBySourceSpare);
        if (capped)
            reasons.Add(TransferReasonCodes.LimitedByPolicyCap);
        if (destination.PendingInbound > 0)
            reasons.Add(TransferReasonCodes.ReducedByInbound);
        if (source.PendingOutbound > 0)
            reasons.Add(TransferReasonCodes.ReducedByPendingOutbound);

        var priority = candidate.DestinationCover < policy.HighPriorityCoverDays
            ? TransferPriorities.High
            : candidate.DestinationCover < policy.MediumPriorityCoverDays
                ? TransferPriorities.Medium
                : TransferPriorities.Low;

        return new TransferOpportunity(
            KeyFor(source.VariantId, source.LocationId, destination.LocationId),
            source.ProductId,
            source.MainCode,
            source.ProductName,
            source.VariantId,
            source.Sku,
            source.VariantLabel,
            source.CategoryId,
            source.CategoryName,
            Side(source, candidate.SourceVelocity, candidate.SourceCover),
            Side(destination, candidate.DestinationVelocity, candidate.DestinationCover),
            candidate.SourceProtected,
            spareAvailable,
            needRemaining,
            suggested,
            priority,
            reasons,
            canInitiate?.Invoke(source) ?? false);
    }

    private static TransferSideDetail Side(VariantLocationFact fact, decimal velocity, decimal? cover) =>
        new(
            fact.LocationId,
            fact.LocationName,
            fact.Available,
            fact.NetUnitsSold,
            fact.EligibleSellingDays,
            decimal.Round(velocity, 4, MidpointRounding.AwayFromZero),
            cover is null ? null : decimal.Round(cover.Value, 1, MidpointRounding.AwayFromZero),
            fact.PendingOutbound,
            fact.PendingInbound);

    private static decimal Velocity(VariantLocationFact fact) =>
        fact.EligibleSellingDays > 0
            ? (decimal)Math.Max(fact.NetUnitsSold, 0) / fact.EligibleSellingDays
            : 0m;
}
