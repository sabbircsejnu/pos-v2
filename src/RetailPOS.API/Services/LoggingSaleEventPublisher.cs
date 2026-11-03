// =====================================================================
// NEW — Default in-process sale event publisher.
// Logs the event and invokes any registered handlers.
// Replace with a message-broker implementation for production-grade
// cross-service delivery.
// =====================================================================
namespace RetailPOS.API.Services;

/// <summary>
/// Default <see cref="ISaleEventPublisher"/> implementation.
/// Logs the completed event and is a no-op for external delivery.
/// Extend by registering additional <see cref="ISaleCompletedHandler"/> services.
/// </summary>
public sealed class LoggingSaleEventPublisher : ISaleEventPublisher
{
    private readonly IEnumerable<ISaleCompletedHandler> _handlers;
    private readonly ILogger<LoggingSaleEventPublisher> _logger;

    public LoggingSaleEventPublisher(
        IEnumerable<ISaleCompletedHandler> handlers,
        ILogger<LoggingSaleEventPublisher> logger)
    {
        _handlers = handlers;
        _logger   = logger;
    }

    public async Task PublishSaleCompletedAsync(SaleCompletedEvent evt)
    {
        _logger.LogInformation(
            "SaleCompleted: Sale={SaleNumber} Outlet={OutletId} Customer={CustomerId} Total={Total} Points={Points}",
            evt.SaleNumber, evt.OutletId, evt.CustomerId, evt.TotalAmount, evt.LoyaltyPointsAwarded);

        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(evt);
            }
            catch (Exception ex)
            {
                // Never let a handler failure break the sale response
                _logger.LogWarning(ex, "SaleCompleted handler {Handler} threw an exception.", handler.GetType().Name);
            }
        }
    }

    public Task PublishCashSessionOpenedAsync(CashSessionOpenedEvent evt)
    {
        _logger.LogInformation(
            "CashSessionOpened: Session={SessionId} Outlet={OutletId} Terminal={TerminalId} BusinessDate={BusinessDate}",
            evt.CashSessionId,
            evt.OutletId,
            evt.TerminalId,
            evt.BusinessDate);
        return Task.CompletedTask;
    }

    public Task PublishCashSessionClosedAsync(CashSessionClosedEvent evt)
    {
        _logger.LogInformation(
            "CashSessionClosed: Session={SessionId} Outlet={OutletId} Terminal={TerminalId} Difference={Difference}",
            evt.CashSessionId,
            evt.OutletId,
            evt.TerminalId,
            evt.DifferenceAmount);
        return Task.CompletedTask;
    }

    public Task PublishCashSessionExpiredAsync(CashSessionExpiredEvent evt)
    {
        _logger.LogWarning(
            "CashSessionExpired: Session={SessionId} Outlet={OutletId} Terminal={TerminalId} OpenedAt={OpenedAt}",
            evt.CashSessionId,
            evt.OutletId,
            evt.TerminalId,
            evt.OpenedAt);
        return Task.CompletedTask;
    }

    public Task PublishSaleBlockedByExpiredSessionAsync(SaleBlockedByExpiredSessionEvent evt)
    {
        _logger.LogWarning(
            "SaleBlockedByExpiredSession: Session={SessionId} Outlet={OutletId} Terminal={TerminalId}",
            evt.CashSessionId,
            evt.OutletId,
            evt.TerminalId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Optional extension point: implement this interface and register it with DI
/// to react to every completed sale (e.g. send receipt email, push to analytics).
/// </summary>
public interface ISaleCompletedHandler
{
    Task HandleAsync(SaleCompletedEvent evt);
}
