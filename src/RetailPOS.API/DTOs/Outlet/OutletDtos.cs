namespace RetailPOS.API.DTOs.Outlet;

public class OutletDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? InvoicePrefix { get; set; }
    public bool InvoicePrefixLocked { get; set; }
    public string? TimeZoneId { get; set; }
    public string? InvoicePrintBranchName { get; set; }
    public string? BinNumber { get; set; }
    public string? Mushak { get; set; }
    public string? ReturnExchangePolicy { get; set; }
    public string? VatLawNotification { get; set; }
    public List<OutletTerminalDto> Terminals { get; set; } = [];
}

/// <summary>A POS terminal row of the Outlet form. A null <see cref="Id"/> on input creates the terminal.</summary>
public class OutletTerminalDto
{
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public bool HasHistory { get; set; }
    public bool HasOpenSession { get; set; }
}

/// <summary>One weekday; <see cref="DayOfWeek"/> is 0 (Sunday) to 6 (Saturday).</summary>
public class OutletSalesTargetDayDto
{
    public int DayOfWeek { get; set; }
    public bool IsOffDay { get; set; }
    public decimal? TargetAmount { get; set; }
}

/// <summary>A null <see cref="OverrideAmount"/> resets the period to its calculated amount.</summary>
public class OutletSalesTargetOverrideDto
{
    public string PeriodType { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public decimal? OverrideAmount { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Weekly off days and sales targets, saved with the outlet. Omit the block to leave them unchanged.</summary>
public class OutletSalesTargetsDto
{
    public bool IsEnabled { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public List<OutletSalesTargetDayDto> Days { get; set; } = [];
    public List<OutletSalesTargetOverrideDto>? Overrides { get; set; }
    public int? ExpectedVersionNo { get; set; }
}

public class CreateOutletDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
    public string? InvoicePrefix { get; set; }
    public string? TimeZoneId { get; set; }
    public string? InvoicePrintBranchName { get; set; }
    public string? BinNumber { get; set; }
    public string? Mushak { get; set; }
    public string? ReturnExchangePolicy { get; set; }
    public string? VatLawNotification { get; set; }
    public List<OutletTerminalDto>? Terminals { get; set; }
    public OutletSalesTargetsDto? SalesTargets { get; set; }
}

public class UpdateOutletDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
    public string? InvoicePrefix { get; set; }
    public string? TimeZoneId { get; set; }
    public string? InvoicePrintBranchName { get; set; }
    public string? BinNumber { get; set; }
    public string? Mushak { get; set; }
    public string? ReturnExchangePolicy { get; set; }
    public string? VatLawNotification { get; set; }
    public List<OutletTerminalDto>? Terminals { get; set; }
    public OutletSalesTargetsDto? SalesTargets { get; set; }
}

public class OutletStatsDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalSales { get; set; }
}
