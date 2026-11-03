using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.Application.Pos.Abstractions;

public sealed record CashSessionListFilter(
    long OutletId,
    long? TerminalId,
    DateTime? BusinessDateFrom,
    DateTime? BusinessDateTo,
    string? State,
    long? OpenedByUserId,
    int Page,
    int PageSize);

public sealed record CashSessionListItem(
    long Id,
    long OutletId,
    string OutletName,
    long TerminalId,
    string TerminalName,
    DateTime BusinessDate,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal OpeningCash,
    decimal? ExpectedCash,
    decimal? CountedCash,
    decimal? Variance,
    string Status,
    string State,
    int DurationHours,
    long OpenedByUserId,
    string? OpenedByName,
    long? ClosedByUserId,
    string? ClosedByName,
    string? VarianceStatus);

public sealed record CashSessionListPage(
    IReadOnlyList<CashSessionListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTime? ServerNowUtc = null);

public sealed record CashSessionHandoverReadModel(
    long Id,
    string Method,
    decimal Amount,
    string Status,
    long GivenByUserId,
    string? GivenByName,
    long? RecipientUserId,
    string? RecipientName,
    string? BankName,
    string? BankAccountReference,
    string? DepositReference,
    DateTime? DepositDate,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    long? ConfirmedByUserId);

public sealed record CashSessionMovementReadModel(
    long Id,
    string MovementType,
    decimal CashEffect,
    string SourceType,
    long? SourceId,
    long? OriginalSaleId,
    long? OriginalCashSessionId,
    string? Notes,
    DateTime RecordedAtUtc,
    long RecordedByUserId);

public sealed record CashSessionAttachmentReadModel(
    long Id,
    long? HandoverId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    long UploadedByUserId,
    DateTime UploadedAtUtc);

public sealed record CashSessionSaleReadModel(
    long SaleId,
    string SaleNumber,
    string Status,
    decimal TotalAmount,
    decimal CashRetained,
    decimal NonCash,
    DateTime CreatedAt,
    string? CashierName);

public sealed record CashDifferenceApprovalReadModel(
    long Id,
    long CashSessionId,
    decimal VarianceAmount,
    string Reason,
    string Status,
    string PostingStatus,
    int PostingAttempts,
    string? LastPostingError,
    long? JournalEntryId,
    DateTime CreatedAtUtc,
    long CreatedByUserId,
    DateTime? ReviewedAtUtc,
    long? ReviewedByUserId,
    string? ReviewNotes,
    DateTime? PostedAtUtc);

public sealed record CashSessionCloseSnapshotReadModel(
    decimal OpeningCash,
    decimal CashSalesRetained,
    decimal CashTendered,
    decimal ChangeGiven,
    decimal CardTotal,
    decimal MobileTotal,
    decimal DrawerExpenses,
    decimal CashRefunds,
    decimal Corrections,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    string? VarianceReason,
    decimal HandedOverTotal,
    decimal RetainedAtOutlet,
    string? DenominationsJson,
    string? Notes,
    long ClosedByUserId,
    DateTime ClosedAtUtc,
    bool WasOverdueAtClose,
    string SnapshotHash);

public sealed record CashSessionTimelineEntry(DateTime AtUtc, string Kind, string Title, string? Detail, long? ActorUserId);

public sealed record CashSessionDetailReadModel(
    CashSessionReadModel Session,
    string OutletName,
    string TerminalName,
    string? OpenedByName,
    string? ClosedByName,
    CashSessionCloseSummaryReadModel Summary,
    CashSessionCloseSnapshotReadModel? CloseSnapshot,
    IReadOnlyList<CashSessionHandoverReadModel> Handovers,
    IReadOnlyList<CashSessionMovementReadModel> Movements,
    IReadOnlyList<CashSessionAttachmentReadModel> Attachments,
    IReadOnlyList<CashSessionSaleReadModel> Sales,
    CashDifferenceApprovalReadModel? Variance,
    IReadOnlyList<CashSessionTimelineEntry> Timeline);

public sealed record CashDifferenceListFilter(string? Status, string? PostingStatus, int Page, int PageSize);

public sealed record CashDifferenceListItem(
    CashDifferenceApprovalReadModel Approval,
    long OutletId,
    string OutletName,
    DateTime BusinessDate,
    long TerminalId);

public sealed record CashDifferenceListPage(IReadOnlyList<CashDifferenceListItem> Items, int Page, int PageSize, int TotalCount);

/// <summary>Renders the closing summary of a closed session from its immutable close snapshot.</summary>
public interface ICashSessionCloseSummaryPdfRenderer
{
    byte[] Render(CashSessionDetailReadModel detail, DateTime generatedAtUtc);
}

public interface ICashSessionQueryStore
{
    Task<CashSessionListPage> ListAsync(CashSessionListFilter filter, CancellationToken cancellationToken);

    Task<CashSessionDetailReadModel?> GetDetailAsync(long sessionId, DateTime nowUtc, CancellationToken cancellationToken);

    Task<CashDifferenceListPage> ListVariancesAsync(
        long businessId,
        IReadOnlyCollection<long>? outletIds,
        CashDifferenceListFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Totals of the outlet's sales whose server-assigned business date equals <paramref name="businessDate"/>.</summary>
    Task<BusinessDaySalesTotals> GetBusinessDaySalesAsync(long outletId, DateTime businessDate, CancellationToken cancellationToken);
}
