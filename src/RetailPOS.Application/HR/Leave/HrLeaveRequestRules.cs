namespace RetailPOS.Application.HR.Leave;

public static class HrLeaveRequestRules
{
    public static void ValidateDraft(HrLeaveRequestDraftInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.LeaveTypeId <= 0 || input.StartDate == default
            || input.EndDate < input.StartDate
            || input.EndDate.DayNumber - input.StartDate.DayNumber > 366)
            throw new ArgumentException("Leave type and an inclusive date range of at most 367 days are required.");
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000)
            throw new ArgumentException("Leave reason is required and must not exceed 1000 characters.");
    }

    public static decimal UnitsForDay(string classification, bool hasPublishedRoster)
    {
        if (classification is not ("NormalWorkingDay" or "Holiday" or "WeeklyOff"))
            throw new ArgumentException("Leave request day classification is invalid.");
        if (classification == "NormalWorkingDay" && !hasPublishedRoster)
            throw new InvalidOperationException("A published roster is required to request leave on a working day.");
        return classification == "NormalWorkingDay" ? 1m : 0m;
    }
}
