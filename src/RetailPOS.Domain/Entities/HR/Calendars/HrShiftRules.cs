namespace RetailPOS.Core.Entities.HR.Calendars;

public static class HrShiftRules
{
    public static void EnsureAssignable(HrShift shift)
    {
        if (!shift.IsActive)
            throw new InvalidOperationException("An inactive shift cannot be newly assigned.");
        Validate(shift);
    }

    public static int DurationMinutes(TimeOnly start, TimeOnly end, bool crossesMidnight)
    {
        if (start.Second != 0 || end.Second != 0 || start.Ticks % TimeSpan.TicksPerMinute != 0
            || end.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Shift times must use whole minutes.");
        var duration = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        if (crossesMidnight)
        {
            if (duration >= 0)
                throw new ArgumentException("A cross-midnight shift must end earlier than its start time.");
            duration += 1440;
        }
        if (duration <= 0)
            throw new ArgumentException("Shift duration must be positive and less than 24 hours.");
        return duration;
    }

    public static void Validate(HrShift shift)
    {
        var duration = DurationMinutes(shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight);
        if (shift.MinimumWorkMinutes <= 0 || shift.MinimumWorkMinutes > duration
            || shift.MaximumAllowedWorkMinutes is <= 0
            || shift.MaximumAllowedWorkMinutes is > 1439
            || shift.MaximumAllowedWorkMinutes.HasValue
                && (shift.MaximumAllowedWorkMinutes < duration
                    || shift.MinimumWorkMinutes > shift.MaximumAllowedWorkMinutes))
            throw new ArgumentException("Minimum/maximum work requirements must contain the scheduled duration.");
        if (shift.AllowedEarlyPunchMinutes < 0 || shift.AllowedLatePunchMinutes < 0
            || duration + (long)shift.AllowedEarlyPunchMinutes + shift.AllowedLatePunchMinutes >= 1440
            || shift.MaximumAllowedWorkMinutes.HasValue
                && shift.MaximumAllowedWorkMinutes + (long)shift.AllowedEarlyPunchMinutes
                    + shift.AllowedLatePunchMinutes >= 1440)
            throw new ArgumentException("Punch windows must not overlap adjacent daily shift windows.");
        if (shift.MinimumRestBeforeNextShiftMinutes is < 0
            || shift.GraceInMinutes < 0 || shift.GraceOutMinutes < 0
            || shift.GraceInMinutes >= duration || shift.GraceOutMinutes >= duration)
            throw new ArgumentException("Rest and grace settings are invalid.");
        if (shift.Breaks.Any(b => b.Sequence <= 0 || b.DurationMinutes <= 0)
            || shift.Breaks.Select(b => b.Sequence).Distinct().Count() != shift.Breaks.Count
            || shift.Breaks.Sum(b => (long)b.DurationMinutes) >= duration
            || duration - shift.Breaks.Where(b => !b.IsPaid).Sum(b => (long)b.DurationMinutes)
                < shift.MinimumWorkMinutes)
            throw new ArgumentException("Breaks must fit the shift and leave the minimum net work requirement.");
    }

    public static (DateTime StartLocal, DateTime EndLocal) Window(
        DateOnly workDate, TimeOnly start, TimeOnly end, bool crossesMidnight)
    {
        DurationMinutes(start, end, crossesMidnight);
        return (workDate.ToDateTime(start),
            workDate.AddDays(crossesMidnight ? 1 : 0).ToDateTime(end));
    }
}
