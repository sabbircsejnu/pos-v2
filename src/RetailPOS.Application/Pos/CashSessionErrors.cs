namespace RetailPOS.Application.Pos;

/// <summary>Stable machine-readable codes for Cash Session failures (FR-CS-04R, D1–D4, D7).</summary>
public static class CashSessionErrorCodes
{
    public const string Overdue = "CASH_SESSION_OVERDUE";
    public const string Closed = "CASH_SESSION_CLOSED";
    public const string NotOpen = "CASH_SESSION_NOT_OPEN";
    public const string NoActiveSession = "CASH_SESSION_NO_ACTIVE";
    public const string AlreadyOpen = "CASH_SESSION_ALREADY_OPEN";
    public const string TerminalRequired = "CASH_SESSION_TERMINAL_REQUIRED";
    public const string NoActiveTerminal = "CASH_SESSION_NO_ACTIVE_TERMINAL";
    public const string TerminalInvalid = "CASH_SESSION_TERMINAL_INVALID";
    public const string OpeningCashInvalid = "CASH_SESSION_OPENING_CASH_INVALID";
    public const string CountInvalid = "CASH_SESSION_COUNT_INVALID";
    public const string VarianceReasonRequired = "CASH_SESSION_VARIANCE_REASON_REQUIRED";
    public const string DenominationMismatch = "CASH_SESSION_DENOMINATION_MISMATCH";
    public const string HandoverInvalid = "CASH_SESSION_HANDOVER_INVALID";
    public const string HandoverExceedsCount = "CASH_SESSION_HANDOVER_EXCEEDS_COUNT";
    public const string IdempotencyConflict = "CASH_SESSION_IDEMPOTENCY_CONFLICT";
    public const string TimeZoneInvalid = "CASH_SESSION_TIMEZONE_INVALID";
    public const string CashRefundAmountMismatch = "CASH_REFUND_AMOUNT_MISMATCH";
    public const string CashRefundSessionRequired = "CASH_REFUND_SESSION_REQUIRED";
    public const string CashRefundPolicyUndefined = "CASH_REFUND_POLICY_UNDEFINED";
    public const string ExpenseMakerChecker = "EXPENSE_MAKER_CHECKER_VIOLATION";
    public const string ExpenseNotPayable = "EXPENSE_NOT_PAYABLE";
    public const string ExpenseAlreadyDisbursed = "EXPENSE_ALREADY_DISBURSED";
    public const string VarianceNotPending = "CASH_VARIANCE_NOT_PENDING";
    public const string VarianceSelfReview = "CASH_VARIANCE_SELF_REVIEW";
    public const string AttachmentInvalid = "CASH_SESSION_ATTACHMENT_INVALID";
    public const string HandoverNotFound = "CASH_SESSION_HANDOVER_NOT_FOUND";
}

/// <summary>A rejected Cash Session request. Maps to HTTP <see cref="HttpStatus"/> with <see cref="Code"/> as the error code.</summary>
public sealed class CashSessionException(string code, string message, int httpStatus = 400)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}
