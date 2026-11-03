using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

public enum SaleCashRefundKind
{
    Void,
    Refund
}

/// <summary>
/// D1: the cash a void/refund physically pays back. The session that dispenses it bears the outflow; the original
/// sale and its original session stay linked and are never rewritten.
/// </summary>
public sealed record SaleCashRefundRequest(
    Sale Sale,
    SaleCashRefundKind Kind,
    long ActorUserId,
    long? DispensingTerminalId,
    bool FullReversal,
    decimal? RequestedAmount,
    string? Reason);

public sealed record SaleCashRefundResult(long MovementId, long DispensingCashSessionId, decimal Amount, bool Created);

public interface ISaleCashRefundRecorder
{
    /// <summary>
    /// Returns null when the sale holds no cash (nothing to dispense). Otherwise locks the dispensing session, records
    /// the exactly-once outflow and returns it. Must run inside the void/refund transaction.
    /// </summary>
    Task<SaleCashRefundResult?> RecordAsync(SaleCashRefundRequest request, CancellationToken cancellationToken);
}
