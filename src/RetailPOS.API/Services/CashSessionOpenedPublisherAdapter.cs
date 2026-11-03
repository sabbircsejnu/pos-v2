using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.API.Services;

public sealed class CashSessionOpenedPublisherAdapter(ISaleEventPublisher eventPublisher)
    : ICashSessionOpenedPublisher
{
    public Task PublishOpenedAsync(CashSessionSnapshot session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = eventPublisher.PublishCashSessionOpenedAsync(new CashSessionOpenedEvent
        {
            CashSessionId = session.Id,
            OutletId = session.OutletId,
            TerminalId = session.TerminalId,
            OpenedByUserId = session.OpenedByUserId,
            BusinessDate = session.BusinessDate,
            OpenedAt = session.OpenedAt
        });
        return Task.CompletedTask;
    }
}
