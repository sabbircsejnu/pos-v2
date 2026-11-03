using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Core.Entities;
using AccountingConfigurationEntity = RetailPOS.Core.Entities.AccountingConfiguration;

namespace RetailPOS.Application.AccountingConfiguration.Queries;

public sealed record ValidateRequiredAccountingMappingsQuery(bool BalanceSheetOnly = false)
    : IQuery<IReadOnlyList<string>>;

public sealed class ValidateRequiredAccountingMappingsQueryHandler(
    IAccountingConfigurationTenantScope tenantScope,
    IAccountingConfigurationInitializationStore initializationStore,
    IAccountingPostingAccountReadStore readStore)
    : IRequestHandler<ValidateRequiredAccountingMappingsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(
        ValidateRequiredAccountingMappingsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        var configuration = await initializationStore.EnsureInitializedAsync(
            businessId,
            actorUserId: null,
            cancellationToken);
        var mappings = PostingAccountResolutionPolicy.GetGlobalAccountMappings(configuration)
            .ToDictionary(mapping => mapping.Slot, mapping => mapping.AccountId, StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();
        var requiredMappings = new List<(string Slot, long? AccountId)>();
        foreach (var slot in PostingAccountResolutionPolicy.GetRequiredSlots(request.BalanceSheetOnly))
        {
            if (!mappings.TryGetValue(slot, out var accountId))
            {
                errors.Add($"slot '{slot}' is not supported.");
                continue;
            }

            requiredMappings.Add((slot, accountId));
            if (!accountId.HasValue)
            {
                errors.Add($"slot '{slot}' is not mapped.");
            }
        }

        var accountIds = requiredMappings
            .Where(mapping => mapping.AccountId.HasValue)
            .Select(mapping => mapping.AccountId!.Value)
            .Distinct()
            .ToList();
        if (accountIds.Count == 0)
        {
            return errors;
        }

        var accountById = (await readStore.GetChartAccountsForBusinessAsync(
                businessId,
                accountIds,
                cancellationToken))
            .ToDictionary(account => account.Id);
        foreach (var missingId in accountIds.Where(id => !accountById.ContainsKey(id)))
        {
            errors.Add($"mapped account '{missingId}' does not exist in this business.");
        }

        foreach (var mapping in requiredMappings.Where(mapping => mapping.AccountId.HasValue))
        {
            var accountId = mapping.AccountId!.Value;
            if (!accountById.TryGetValue(accountId, out var account))
            {
                continue;
            }

            if (!account.IsActive)
            {
                errors.Add($"slot '{mapping.Slot}' uses inactive account '{account.AccountCode}' ({account.Id}).");
            }

            var allowedTypes = PostingAccountResolutionPolicy.GetAllowedAccountTypes(mapping.Slot);
            if (!allowedTypes.Any(type =>
                    string.Equals(type, account.AccountType, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"slot '{mapping.Slot}' requires account type(s) [{string.Join(", ", allowedTypes)}] but account '{account.AccountCode}' ({account.Id}) is '{account.AccountType}'.");
            }
        }

        return errors;
    }
}
