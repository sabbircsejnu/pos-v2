namespace RetailPOS.Application.HR.Calendars;

// Override and published-roster adapters supply explicit decisions; drafts never supply a decision.
public sealed record HrExplicitDayDecision(string Classification, bool IsWeeklyOff);

public static class HrDayClassification
{
    public static HrDayClassificationResponse Resolve(DateOnly date,
        IReadOnlyList<HrHolidayResponse> holidays, HrWeeklyOffResponse? policy,
        HrExplicitDayDecision? employeeOverride = null, HrExplicitDayDecision? publishedRoster = null)
    {
        var weeklyOff = policy?.Data.Days.Contains(date.DayOfWeek) == true;
        var explicitDecision = employeeOverride ?? publishedRoster;
        if (explicitDecision is not null && explicitDecision.Classification is not
            ("NormalWorkingDay" or "Holiday" or "WeeklyOff"))
            throw new ArgumentException("Explicit day classification is invalid.");
        return new HrDayClassificationResponse(date,
            explicitDecision?.Classification ?? (holidays.Count > 0 ? "Holiday" : weeklyOff ? "WeeklyOff" : "NormalWorkingDay"),
            employeeOverride is not null ? "EmployeeOverride" : publishedRoster is not null ? "PublishedRoster"
                : holidays.Count > 0 ? "Holiday" : policy is not null ? policy.Data.Scope.ScopeType : "Normal",
            holidays.Count > 0, explicitDecision?.IsWeeklyOff ?? weeklyOff, holidays, policy);
    }

    public static int Priority(HrCalendarScope scope) => scope.ScopeType switch
    {
        "Company" => 1, "LocationType" => 2, "Location" => 3,
        _ => throw new ArgumentException("Calendar scope is invalid.")
    };
}
