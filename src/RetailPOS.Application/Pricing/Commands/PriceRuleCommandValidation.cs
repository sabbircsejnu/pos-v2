using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Commands;

internal static class PriceRuleCommandValidation
{
    private static readonly HashSet<string> ValidRuleTypes =
        new(StringComparer.OrdinalIgnoreCase) { "variant", "product", "category", "outlet", "global" };

    private static readonly HashSet<string> ValidDiscountTypes =
        new(StringComparer.OrdinalIgnoreCase) { "percentage", "fixed" };

    public static async Task ValidateAsync(
        PriceRuleWriteData rule,
        IPriceRuleCommandStore store,
        CancellationToken cancellationToken)
    {
        if (!ValidRuleTypes.Contains(rule.RuleType))
        {
            throw new InvalidOperationException(
                $"Invalid RuleType '{rule.RuleType}'. Must be one of: variant, product, category, outlet, global");
        }

        if (!ValidDiscountTypes.Contains(rule.DiscountType))
        {
            throw new InvalidOperationException(
                $"Invalid DiscountType '{rule.DiscountType}'. Must be 'percentage' or 'fixed'");
        }

        var normalizedRuleType = rule.RuleType.ToLower();
        var normalizedDiscountType = rule.DiscountType.ToLower();

        if (normalizedRuleType != "global" && rule.TargetId is null)
        {
            throw new InvalidOperationException(
                $"TargetId is required for RuleType '{rule.RuleType}'");
        }

        if (normalizedDiscountType == "percentage" && rule.DiscountValue > 100)
        {
            throw new InvalidOperationException("Percentage discount cannot exceed 100%");
        }

        if (rule.ValidFrom.HasValue && rule.ValidTo.HasValue && rule.ValidFrom > rule.ValidTo)
        {
            throw new InvalidOperationException("ValidFrom must be earlier than or equal to ValidTo");
        }

        if (rule.TargetId.HasValue &&
            !await store.TargetExistsAsync(normalizedRuleType, rule.TargetId.Value, cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Target {rule.RuleType} with ID {rule.TargetId.Value} not found");
        }
    }

    public static PriceRuleWriteData Normalize(PriceRuleWriteData rule) =>
        rule with
        {
            Name = rule.Name.Trim(),
            Description = rule.Description?.Trim(),
            RuleType = rule.RuleType.ToLower(),
            DiscountType = rule.DiscountType.ToLower(),
            ValidFrom = ToUtcOrNull(rule.ValidFrom),
            ValidTo = ToUtcOrNull(rule.ValidTo)
        };

    private static DateTime? ToUtcOrNull(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : value.Value.ToUniversalTime();
    }
}
