using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos;

/// <summary>
/// FR-CLOSE-02 / LLD §25: expected drawer cash from authoritative persisted facts.
/// <code>Expected = Opening + Σ NetCashRetained(sales in this session) + Σ cash movements of this session</code>
/// Cash received is a historical fact: it stays counted even when the sale is later voided or refunded, because the
/// physical outflow (if any) is its own movement assigned to the session that dispensed the cash. That is what makes a
/// void or refund impossible to subtract twice. Change is never part of retained cash and card/mobile never enter the drawer.
/// </summary>
public static class CashSessionExpectedCashCalculator
{
    private static readonly HashSet<string> ReceivedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "completed", "voided", "refunded", "partially_refunded" };

    public static CashSessionCloseSummaryReadModel Calculate(
        CashSessionFacts facts,
        DateTime closingAtUtc)
    {
        var session = facts.Session;
        decimal cashRetained = 0m, cashTendered = 0m, changeGiven = 0m;
        decimal card = 0m, mobile = 0m, other = 0m;
        var saleCount = 0;

        foreach (var sale in facts.Sales.Where(sale => ReceivedStatuses.Contains((sale.Status ?? string.Empty).Trim())))
        {
            saleCount++;
            var payments = sale.Payments.Count > 0
                ? sale.Payments
                : [new CashSessionPaymentFact(sale.PaymentMethod, sale.TotalAmount, null, null, null)];

            foreach (var payment in payments)
            {
                var method = (payment.Method ?? string.Empty).Trim().ToLowerInvariant();
                switch (method)
                {
                    case "cash":
                        // Legacy rows without D14 evidence kept the allocated amount in the drawer.
                        cashRetained += payment.NetCashRetained ?? payment.Amount;
                        cashTendered += payment.Tendered ?? payment.Amount;
                        changeGiven += payment.ChangeGiven ?? 0m;
                        break;
                    case "card":
                        card += payment.Amount;
                        break;
                    case "mobile":
                        mobile += payment.Amount;
                        break;
                    default:
                        other += payment.Amount;
                        break;
                }
            }
        }

        decimal drawerExpenses = 0m, cashRefunds = 0m, corrections = 0m;
        foreach (var movement in facts.Movements)
        {
            switch (movement.MovementType)
            {
                case CashSessionMovementTypes.CashRefund:
                    cashRefunds += -movement.CashEffect;
                    break;
                case CashSessionMovementTypes.DrawerExpense:
                case CashSessionMovementTypes.DrawerExpenseReversal:
                    drawerExpenses += -movement.CashEffect;
                    break;
                case CashSessionMovementTypes.Correction:
                    corrections += movement.CashEffect;
                    break;
                // Exchange cash is reported inside the existing totals so the close snapshot keeps its shape and its
                // components still add up to the expected cash: money collected is cash received, money paid back is a
                // cash refund to a customer.
                case CashSessionMovementTypes.ExchangeCashIn:
                    cashRetained += movement.CashEffect;
                    break;
                case CashSessionMovementTypes.ExchangeCashOut:
                    cashRefunds += -movement.CashEffect;
                    break;
                default:
                    // A movement this calculator does not know would silently vanish from the drawer total.
                    throw new InvalidOperationException($"Cash movement type '{movement.MovementType}' is not supported by the drawer calculation.");
            }
        }

        var expected = Round(session.OpeningCashAmount + cashRetained - cashRefunds - drawerExpenses + corrections);
        var closing = UtcTime.Normalize(closingAtUtc);
        return new CashSessionCloseSummaryReadModel(
            session.Id,
            session.OutletId,
            session.TerminalId,
            session.BusinessDate,
            UtcTime.Normalize(session.OpenedAt),
            closing,
            CashSessionTime.ElapsedSeconds(session.OpenedAt, closing),
            session.OpeningCashAmount,
            Round(cashRetained),
            Round(cashTendered),
            Round(changeGiven),
            Round(card),
            Round(mobile),
            Round(other),
            Round(drawerExpenses),
            Round(cashRefunds),
            Round(corrections),
            expected,
            saleCount,
            CashSessionTime.IsOverdue(session.OpenedAt, session.DurationSnapshotHours, closing));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
