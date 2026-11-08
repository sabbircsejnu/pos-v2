namespace RetailPOS.Core.Entities;

public static class CashSessionStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public static class CashSessionMovementTypes
{
    /// <summary>Cash physically paid back to a customer from the drawer (D1). Always an outflow.</summary>
    public const string CashRefund = "cash_refund";

    /// <summary>An approved POS Drawer expense actually paid out of the drawer (D7). Always an outflow.</summary>
    public const string DrawerExpense = "drawer_expense";

    /// <summary>A reversal of a previously recorded drawer expense. Always an inflow.</summary>
    public const string DrawerExpenseReversal = "drawer_expense_reversal";

    /// <summary>An authorised correction explicitly classified by the SRS; signed.</summary>
    public const string Correction = "correction";

    /// <summary>Cash collected for a product exchange's positive difference (Exchange EX-07). Always an inflow.</summary>
    public const string ExchangeCashIn = "exchange_cash_in";

    /// <summary>Cash paid back for a product exchange's negative difference (Exchange EX-31, D-02). Always an outflow.</summary>
    public const string ExchangeCashOut = "exchange_cash_out";

    public static readonly string[] All =
        [CashRefund, DrawerExpense, DrawerExpenseReversal, Correction, ExchangeCashIn, ExchangeCashOut];
}

/// <summary>
/// Append-only physical cash fact assigned to the Cash Session that actually received or dispensed the cash.
/// <see cref="SourceKey"/> is unique per Business so a retried operation can never produce a second effect.
/// </summary>
public class CashSessionMovement
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public long CashSessionId { get; set; }
    public string MovementType { get; set; } = string.Empty;

    /// <summary>Signed effect on the physical drawer: negative is an outflow.</summary>
    public decimal CashEffect { get; set; }

    public string SourceType { get; set; } = string.Empty;
    public long? SourceId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public long? OriginalSaleId { get; set; }
    public long? OriginalCashSessionId { get; set; }
    public string? Notes { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public long RecordedByUserId { get; set; }

    public virtual CashSession CashSession { get; set; } = null!;
}

/// <summary>
/// The immutable closing snapshot of a Cash Session. It is written once, in the close transaction, and is
/// protected by database triggers against update and delete.
/// </summary>
public class CashSessionCloseSnapshot
{
    public long CashSessionId { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal CashSalesRetained { get; set; }
    public decimal CashTendered { get; set; }
    public decimal ChangeGiven { get; set; }
    public decimal CardTotal { get; set; }
    public decimal MobileTotal { get; set; }
    public decimal DrawerExpenses { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal Corrections { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }
    public decimal Variance { get; set; }
    public string? VarianceReason { get; set; }
    public decimal HandedOverTotal { get; set; }
    public decimal RetainedAtOutlet { get; set; }
    public string? DenominationsJson { get; set; }
    public string? Notes { get; set; }
    public long ClosedByUserId { get; set; }
    public DateTime ClosedAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int DurationSnapshotHours { get; set; }
    public bool WasOverdueAtClose { get; set; }
    public string SnapshotHash { get; set; } = string.Empty;

    public virtual CashSession CashSession { get; set; } = null!;
}

public static class CashHandoverMethods
{
    public const string CashToManager = "cash_to_manager";
    public const string CashToOwner = "cash_to_owner";
    public const string BankDeposit = "bank_deposit";

    public static readonly string[] All = [CashToManager, CashToOwner, BankDeposit];
}

public static class CashHandoverStatuses
{
    /// <summary>Cash handed to a person, awaiting nothing further.</summary>
    public const string Handed = "handed";

    /// <summary>A bank deposit that has not been verified against the bank yet (never labelled confirmed).</summary>
    public const string PendingBankVerification = "pending_bank_verification";

    public const string Confirmed = "confirmed";
}

/// <summary>A custody transfer recorded at close; it is not an operating expense.</summary>
public class CashSessionHandover
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public long CashSessionId { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long GivenByUserId { get; set; }
    public long? RecipientUserId { get; set; }
    public string? RecipientName { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountReference { get; set; }
    public string? DepositReference { get; set; }
    public DateTime? DepositDate { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = CashHandoverStatuses.Handed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public long? ConfirmedByUserId { get; set; }

    public virtual CashSession CashSession { get; set; } = null!;
}

/// <summary>Evidence file metadata. Attachments never change the closed monetary snapshot.</summary>
public class CashSessionAttachment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public long CashSessionId { get; set; }
    public long? HandoverId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual CashSession CashSession { get; set; } = null!;
}

public static class CashDifferenceStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class CashDifferencePostingStatuses
{
    /// <summary>Nothing to post yet (pending decision) or the decision was a rejection.</summary>
    public const string NotRequired = "not_required";

    public const string Pending = "pending";
    public const string Posted = "posted";
    public const string Failed = "failed";
}

/// <summary>
/// D2: the variance fact created at close. Approval comes first; only then does an idempotent asynchronous
/// worker post the cash-difference journal. The closed session is never reopened or rewritten.
/// </summary>
public class CashDifferenceApproval
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public long CashSessionId { get; set; }

    /// <summary>Counted minus expected (positive = surplus). Immutable.</summary>
    public decimal VarianceAmount { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = CashDifferenceStatuses.Pending;
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }
    public int DecisionVersion { get; set; }

    public string PostingStatus { get; set; } = CashDifferencePostingStatuses.NotRequired;
    public int PostingAttempts { get; set; }
    public DateTime? NextPostingAttemptAtUtc { get; set; }
    public string? LastPostingError { get; set; }
    public long? JournalEntryId { get; set; }
    public DateTime? PostedAtUtc { get; set; }

    public virtual CashSession CashSession { get; set; } = null!;
}
