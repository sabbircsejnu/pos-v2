using RetailPOS.API.DTOs.Accounting;

namespace RetailPOS.API.Services;

public static class PaymentPostingPolicy
{
    public static string ResolveSlot(string? method, DateTime saleDate, DateTime? clearingActivationDate)
    {
        var normalizedMethod = (method ?? string.Empty).Trim().ToLowerInvariant();
        var clearingActive = clearingActivationDate.HasValue && saleDate.Date >= clearingActivationDate.Value.Date;

        return normalizedMethod switch
        {
            "cash" => PostingAccountSlots.Cash,
            "bank" => PostingAccountSlots.Bank,
            "card" when clearingActive => PostingAccountSlots.CardClearing,
            "mobile" when clearingActive => PostingAccountSlots.MobileMfsClearing,
            "mfs" when clearingActive => PostingAccountSlots.MobileMfsClearing,
            "card" => PostingAccountSlots.Bank,
            "mobile" => PostingAccountSlots.Bank,
            "mfs" => PostingAccountSlots.Bank,
            _ => PostingAccountSlots.Bank
        };
    }
}
