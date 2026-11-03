namespace RetailPOS.Application.Pos.Abstractions;

public interface ICashSessionExpiryPublisher
{
    Task PublishCashSessionExpiredAsync(CashSessionExpiredNotice notice, CancellationToken cancellationToken);

    Task PublishSaleBlockedByExpiredSessionAsync(
        SaleBlockedByExpiredSessionNotice notice,
        CancellationToken cancellationToken);
}

public sealed record CashSessionExpiredNotice(
    long CashSessionId,
    long OutletId,
    long TerminalId,
    DateTime OpenedAt,
    DateTime CheckedAt);

public sealed record SaleBlockedByExpiredSessionNotice(
    long OutletId,
    long TerminalId,
    long CashSessionId,
    DateTime CheckedAt);
