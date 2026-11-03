using RetailPOS.Application.Pos.Abstractions;

namespace RetailPOS.API.Services;

public sealed class CashSessionExpiryPublisherAdapter(ISaleEventPublisher eventPublisher)
    : ICashSessionExpiryPublisher
{
    public Task PublishCashSessionExpiredAsync(
        CashSessionExpiredNotice notice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = eventPublisher.PublishCashSessionExpiredAsync(new CashSessionExpiredEvent
        {
            CashSessionId = notice.CashSessionId,
            OutletId = notice.OutletId,
            TerminalId = notice.TerminalId,
            OpenedAt = notice.OpenedAt,
            CheckedAt = notice.CheckedAt
        });
        return Task.CompletedTask;
    }

    public Task PublishSaleBlockedByExpiredSessionAsync(
        SaleBlockedByExpiredSessionNotice notice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = eventPublisher.PublishSaleBlockedByExpiredSessionAsync(new SaleBlockedByExpiredSessionEvent
        {
            CashSessionId = notice.CashSessionId,
            OutletId = notice.OutletId,
            TerminalId = notice.TerminalId,
            CheckedAt = notice.CheckedAt
        });
        return Task.CompletedTask;
    }
}
