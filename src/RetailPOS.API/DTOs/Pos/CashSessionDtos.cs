using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Pos;

public class CashSessionDto
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public long OutletId { get; set; }
    public long TerminalId { get; set; }
    public DateTime BusinessDate { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCashAmount { get; set; }
    public decimal? ExpectedClosingCashAmount { get; set; }
    public decimal? ActualClosingCashAmount { get; set; }
    public decimal? DifferenceAmount { get; set; }
    public string Status { get; set; } = "open";
    public long OpenedByUserId { get; set; }
    public long? ClosedByUserId { get; set; }
    public string? OpeningNotes { get; set; }
    public string? ClosingNotes { get; set; }
    public bool IsOverdue { get; set; }
    public int MaxDurationHours { get; set; }
    public long ElapsedSeconds { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public DateTime? CutoffAtUtc { get; set; }

    /// <summary>none | warning | urgent | overdue — derived on the server from the snapshotted duration.</summary>
    public string WarningLevel { get; set; } = "none";

    /// <summary>open | overdue | closed. Overdue is derived and never persisted.</summary>
    public string State { get; set; } = "open";

    /// <summary>The server clock at response time; the UI must not use the browser clock for elapsed time.</summary>
    public DateTime? ServerNowUtc { get; set; }

    public string? VarianceReason { get; set; }
}

public class OpenCashSessionDto
{
    public long OutletId { get; set; }

    /// <summary>Required only when the outlet has several active terminals; one active terminal is auto-selected.</summary>
    public long? TerminalId { get; set; }

    public decimal OpeningCashAmount { get; set; }
    public string? OpeningNotes { get; set; }

    /// <summary>Makes opening idempotent per outlet; a retry with the same key returns the existing session.</summary>
    [MaxLength(64)]
    public string? IdempotencyKey { get; set; }
}

public class CashDenominationDto
{
    public decimal Value { get; set; }
    public int Count { get; set; }
}

public class CashHandoverDto
{
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long? RecipientUserId { get; set; }
    public string? RecipientName { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountReference { get; set; }
    public string? DepositReference { get; set; }
    public DateTime? DepositDate { get; set; }
    public string? Notes { get; set; }
}

public class CloseCashSessionDto
{
    public decimal ActualClosingCashAmount { get; set; }
    public string? ClosingNotes { get; set; }

    /// <summary>Required when the counted cash differs from the expected cash; falls back to <see cref="ClosingNotes"/>.</summary>
    public string? VarianceReason { get; set; }

    public List<CashDenominationDto>? Denominations { get; set; }
    public List<CashHandoverDto>? Handovers { get; set; }
}

public class CashSessionCloseSummaryDto
{
    public long SessionId { get; set; }
    public long OutletId { get; set; }
    public long TerminalId { get; set; }
    public DateTime BusinessDate { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime ClosingAt { get; set; }
    public long OpenDurationSeconds { get; set; }
    public decimal OpeningCash { get; set; }

    /// <summary>Net cash retained in the drawer (D14) — never the tendered amount.</summary>
    public decimal CashSales { get; set; }

    public decimal CashTendered { get; set; }
    public decimal ChangeGiven { get; set; }
    public decimal CardSales { get; set; }
    public decimal MobileSales { get; set; }
    public decimal OtherNonCashSales { get; set; }
    public decimal DrawerExpenses { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal Corrections { get; set; }
    public decimal ExpectedClosingCash { get; set; }
    public int SaleCount { get; set; }
    public bool IsOverdue { get; set; }
}
