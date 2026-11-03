// =====================================================================
// NEW — SaleCompleted event hook.
// Publish via ISaleEventPublisher after a sale is persisted.
// Subscribers (notifications, loyalty-tier recalc, external integrations)
// implement this interface without coupling to the Application sale command.
//
// Design: fire-and-forget within the same process; no outbox / message broker.
// For cross-service reliability, swap the in-process implementation with an
// outbox pattern or a message broker (e.g. MassTransit) later.
// =====================================================================
namespace RetailPOS.API.Services;

/// <summary>Represents a sale that has been fully persisted and committed.</summary>
public sealed class SaleCompletedEvent
{
    public long   SaleId        { get; init; }
    public string SaleNumber    { get; init; } = string.Empty;
    public long   OutletId      { get; init; }
    public long   CashierId     { get; init; }
    public long?  CustomerId    { get; init; }
    public decimal TotalAmount  { get; init; }
    public DateTime CompletedAt { get; init; }
    /// <summary>Number of loyalty points awarded this sale (0 if no customer).</summary>
    public int LoyaltyPointsAwarded { get; init; }
}

public sealed class CashSessionOpenedEvent
{
    public long CashSessionId { get; init; }
    public long OutletId { get; init; }
    public long TerminalId { get; init; }
    public long OpenedByUserId { get; init; }
    public DateTime BusinessDate { get; init; }
    public DateTime OpenedAt { get; init; }
}

public sealed class CashSessionClosedEvent
{
    public long CashSessionId { get; init; }
    public long OutletId { get; init; }
    public long TerminalId { get; init; }
    public long ClosedByUserId { get; init; }
    public DateTime ClosedAt { get; init; }
    public decimal DifferenceAmount { get; init; }
}

public sealed class CashSessionExpiredEvent
{
    public long CashSessionId { get; init; }
    public long OutletId { get; init; }
    public long TerminalId { get; init; }
    public DateTime OpenedAt { get; init; }
    public DateTime CheckedAt { get; init; }
}

public sealed class SaleBlockedByExpiredSessionEvent
{
    public long OutletId { get; init; }
    public long TerminalId { get; init; }
    public long CashSessionId { get; init; }
    public DateTime CheckedAt { get; init; }
}

/// <summary>
/// Publish a <see cref="SaleCompletedEvent"/> after a new sale is committed.
/// The default implementation logs and no-ops; replace or chain implementations
/// for notifications, BI streaming, loyalty-tier re-evaluation, etc.
/// </summary>
public interface ISaleEventPublisher
{
    /// <summary>
    /// Called by the Sale application flow immediately after commit.
    /// Must be fast (fire-and-forget) — never block the HTTP response.
    /// </summary>
    Task PublishSaleCompletedAsync(SaleCompletedEvent evt);
    Task PublishCashSessionOpenedAsync(CashSessionOpenedEvent evt);
    Task PublishCashSessionClosedAsync(CashSessionClosedEvent evt);
    Task PublishCashSessionExpiredAsync(CashSessionExpiredEvent evt);
    Task PublishSaleBlockedByExpiredSessionAsync(SaleBlockedByExpiredSessionEvent evt);
}
