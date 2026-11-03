using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Pos.Commands;

public sealed record CashDenominationInput(decimal Value, int Count);

public sealed record CashHandoverInput(
    string Method,
    decimal Amount,
    long? RecipientUserId = null,
    string? RecipientName = null,
    string? BankName = null,
    string? BankAccountReference = null,
    string? DepositReference = null,
    DateTime? DepositDate = null,
    string? Notes = null);

/// <summary>FR-CLOSE-03/04: counted cash, optional denominations and handover lines must reconcile.</summary>
public static class CashSessionCloseEvidenceValidator
{
    public static void Validate(
        decimal countedCash,
        IReadOnlyList<CashDenominationInput> denominations,
        IReadOnlyList<CashHandoverInput> handovers)
    {
        if (countedCash < 0 || decimal.Round(countedCash, 2) != countedCash)
            throw new CashSessionException(
                CashSessionErrorCodes.CountInvalid,
                "Counted cash must be zero or a positive amount with at most two decimals.");

        if (denominations.Count > 0)
        {
            decimal total = 0m;
            foreach (var denomination in denominations)
            {
                if (denomination.Value <= 0 || denomination.Count < 0)
                    throw new CashSessionException(
                        CashSessionErrorCodes.DenominationMismatch,
                        "Each denomination needs a positive value and a non-negative count.");
                total += denomination.Value * denomination.Count;
            }

            if (decimal.Round(total, 2) != countedCash)
                throw new CashSessionException(
                    CashSessionErrorCodes.DenominationMismatch,
                    $"The denomination breakdown totals {total:F2} but the counted cash is {countedCash:F2}.");
        }

        decimal handedOver = 0m;
        foreach (var handover in handovers)
        {
            if (!CashHandoverMethods.All.Contains(handover.Method))
                throw new CashSessionException(
                    CashSessionErrorCodes.HandoverInvalid,
                    "Handover method must be cash to manager, cash to owner or bank deposit.");
            if (handover.Amount <= 0 || decimal.Round(handover.Amount, 2) != handover.Amount)
                throw new CashSessionException(
                    CashSessionErrorCodes.HandoverInvalid,
                    "Each handover amount must be positive with at most two decimals.");

            if (handover.Method == CashHandoverMethods.BankDeposit)
            {
                if (string.IsNullOrWhiteSpace(handover.BankName) && string.IsNullOrWhiteSpace(handover.BankAccountReference))
                    throw new CashSessionException(
                        CashSessionErrorCodes.HandoverInvalid,
                        "A bank deposit needs the bank or the bank account reference.");
            }
            else if (!handover.RecipientUserId.HasValue && string.IsNullOrWhiteSpace(handover.RecipientName))
            {
                throw new CashSessionException(
                    CashSessionErrorCodes.HandoverInvalid,
                    "A cash handover needs the person who took the cash.");
            }

            handedOver += handover.Amount;
        }

        if (handedOver > countedCash)
            throw new CashSessionException(
                CashSessionErrorCodes.HandoverExceedsCount,
                $"Handed-over cash ({handedOver:F2}) cannot exceed the counted cash ({countedCash:F2}).");
    }
}
