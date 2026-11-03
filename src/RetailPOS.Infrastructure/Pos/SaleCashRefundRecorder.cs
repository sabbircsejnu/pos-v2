using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class SaleCashRefundRecorder(
    RetailPOSDbContext dbContext,
    ICashSessionStore store,
    ICashSessionMovementRecorder movements) : ISaleCashRefundRecorder
{
    public async Task<SaleCashRefundResult?> RecordAsync(
        SaleCashRefundRequest request,
        CancellationToken cancellationToken)
    {
        var sale = request.Sale;
        var cashRetained = CashRetained(sale);
        if (cashRetained <= 0)
            return null;

        var alreadyRefunded = await dbContext.CashSessionMovements
            .AsNoTracking()
            .Where(movement => movement.OriginalSaleId == sale.Id
                && movement.MovementType == CashSessionMovementTypes.CashRefund)
            .SumAsync(movement => -movement.CashEffect, cancellationToken);
        var remaining = cashRetained - alreadyRefunded;
        if (remaining <= 0)
            return null;

        decimal amount;
        if (request.FullReversal)
        {
            amount = remaining;
        }
        else
        {
            // Line-level return value (tax, discount and rounding reversal) belongs to the Returns/Exchange SRD, which
            // is not implemented; the dispensed amount must therefore be stated explicitly and is audited as such.
            amount = request.RequestedAmount
                ?? throw new CashSessionException(
                    CashSessionErrorCodes.CashRefundPolicyUndefined,
                    "A partial refund of a sale paid in cash needs the cash amount that is actually paid back.");
            if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > remaining)
                throw new CashSessionException(
                    CashSessionErrorCodes.CashRefundPolicyUndefined,
                    $"The cash refund must be between 0.01 and the remaining cash of the sale ({remaining:F2}).");
        }

        var dispensingSessionId = await ResolveDispensingSessionAsync(sale, request.DispensingTerminalId, cancellationToken);
        var kindKey = request.Kind == SaleCashRefundKind.Void ? "void" : "refund";
        var result = await movements.RecordAsync(
            new CashSessionMovementRequest(
                dispensingSessionId,
                CashSessionMovementTypes.CashRefund,
                -amount,
                $"sale_{kindKey}",
                sale.Id,
                $"sale:{sale.Id}:{kindKey}-cash-refund",
                sale.Id,
                sale.CashSessionId,
                request.Reason,
                request.ActorUserId),
            cancellationToken);
        return new SaleCashRefundResult(result.Movement.Id, dispensingSessionId, -result.Movement.CashEffect, result.Created);
    }

    private async Task<long> ResolveDispensingSessionAsync(Sale sale, long? terminalId, CancellationToken cancellationToken)
    {
        if (terminalId is > 0)
        {
            return await store.FindOpenSessionIdAsync(sale.OutletId, terminalId.Value, cancellationToken)
                ?? throw NoSession();
        }

        // Same-session cancellation: the original session pays the cash back while it is still open.
        if (sale.CashSessionId.HasValue)
        {
            var original = await store.FindAsync(sale.CashSessionId.Value, cancellationToken);
            if (original is { Status: CashSessionStatuses.Open })
                return original.Id;
        }

        var open = await store.ListOpenSessionIdsAsync(sale.OutletId, cancellationToken);
        return open.Count == 1 ? open[0] : throw NoSession();
    }

    private static CashSessionException NoSession() =>
        new(
            CashSessionErrorCodes.CashRefundSessionRequired,
            "A cash refund needs an open cash session at this outlet to pay the cash from. Open or select a session first.",
            409);

    private static decimal CashRetained(Sale sale)
    {
        if (sale.Payments.Count == 0)
            return string.Equals(sale.PaymentMethod, "cash", StringComparison.OrdinalIgnoreCase) ? sale.TotalAmount : 0m;

        return sale.Payments
            .Where(payment => string.Equals(payment.Method, "cash", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => payment.NetCashRetained ?? payment.Amount);
    }
}
