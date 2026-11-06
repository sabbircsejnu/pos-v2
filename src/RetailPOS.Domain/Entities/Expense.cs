namespace RetailPOS.Core.Entities;

public static class ExpenseStatuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public static class ExpensePaymentSources
{
    public const string Bank = "bank";
    public const string PettyCash = "petty_cash";
    public const string PosDrawer = "pos_drawer";

    public static readonly string[] All = [Bank, PettyCash, PosDrawer];

    /// <summary>D7: these sources default to independent maker-checker approval.</summary>
    public static bool RequiresMakerChecker(string? source) => source is PettyCash or PosDrawer;
}

public class Expense
{
    /// <summary>Where the money comes from (D7); null on legacy rows, which behave as <see cref="ExpensePaymentSources.Bank"/>.</summary>
    public string? PaymentSource { get; set; }

    /// <summary>The Cash Session that actually paid a POS Drawer expense out; set only at physical disbursement.</summary>
    public long? DisbursedCashSessionId { get; set; }

    public DateTime? DisbursedAtUtc { get; set; }
    public long? DisbursedByUserId { get; set; }

    /// <summary>True when the Business Owner self-approval exception (D7) was used; kept as audit evidence.</summary>
    public bool SelfApprovedUnderOwnerException { get; set; }

    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string? ExpenseNumber { get; set; }
    public string Category { get; set; } = string.Empty;
    public long? ExpenseCategoryId { get; set; }
    public long? ExpenseAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    public string? PaymentMethodId { get; set; }
    public long? PaymentAccountId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public string? AttachmentPath { get; set; }
    public DateTime ExpenseDate { get; set; }
    public long? OutletId { get; set; }

    /// <summary>Set instead of <see cref="OutletId"/> for a warehouse expense; both null means business-level.</summary>
    public long? WarehouseId { get; set; }

    public string Status { get; set; } = ExpenseStatuses.Draft;
    public long? JournalEntryId { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public long? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Outlet? Outlet { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual ExpenseCategory? ExpenseCategory { get; set; }
    public virtual ChartAccount? ExpenseAccount { get; set; }
    public virtual ChartAccount? PaymentAccount { get; set; }
    public virtual JournalEntry? JournalEntry { get; set; }
    public virtual ICollection<ExpenseAttachment> Attachments { get; set; } = new List<ExpenseAttachment>();
}
