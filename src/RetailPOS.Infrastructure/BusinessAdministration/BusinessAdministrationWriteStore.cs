using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BusinessAdministration;

public sealed class BusinessAdministrationWriteStore(RetailPOSDbContext dbContext)
    : IBusinessAdministrationWriteStore
{
    public async Task<bool> SetBusinessActiveAsync(
        long businessId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(item => item.Id == businessId, cancellationToken);
        if (business is null)
            return false;

        business.IsActive = isActive;
        business.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateSubscriptionAsync(
        long businessId,
        string? subscriptionPlan,
        DateTime? trialEndsAt,
        DateTime? subscriptionEndsAt,
        int? maxOutlets,
        int? maxUsers,
        CancellationToken cancellationToken)
    {
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(item => item.Id == businessId, cancellationToken);
        if (business is null)
            return false;

        business.SubscriptionPlan = subscriptionPlan;
        business.TrialEndsAt = trialEndsAt;
        business.SubscriptionEndsAt = subscriptionEndsAt;
        business.MaxOutlets = maxOutlets;
        business.MaxUsers = maxUsers;
        business.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateReceiptFooterAsync(
        long businessId,
        bool showPoweredByOnReceipt,
        string? poweredByText,
        CancellationToken cancellationToken)
    {
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(item => item.Id == businessId, cancellationToken);
        if (business is null)
            return false;

        business.ShowPoweredByOnReceipt = showPoweredByOnReceipt;
        business.PoweredByText = poweredByText;
        business.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpsertFeatureSettingsAsync(
        long businessId,
        IReadOnlyList<BusinessFeatureSettingChange> features,
        CancellationToken cancellationToken)
    {
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(item => item.Id == businessId, cancellationToken);
        if (business is null)
            return false;

        if (features.Count == 0)
            return true;

        var keys = features
            .Select(feature => feature.FeatureKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct()
            .ToList();
        var existing = await dbContext.BusinessFeatureSettings
            .Where(setting => setting.BusinessId == businessId && keys.Contains(setting.FeatureKey))
            .ToDictionaryAsync(
                setting => setting.FeatureKey,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);

        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature.FeatureKey))
                continue;

            if (existing.TryGetValue(feature.FeatureKey, out var setting))
            {
                setting.IsEnabled = feature.IsEnabled;
                setting.LimitValue = feature.LimitValue;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                dbContext.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = feature.FeatureKey,
                    IsEnabled = feature.IsEnabled,
                    LimitValue = feature.LimitValue,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        business.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<BusinessOwnerAccessResetResult> ResetBusinessOwnerAccessAsync(
        long businessId,
        string tokenHash,
        DateTime now,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Businesses.AnyAsync(
                business => business.Id == businessId,
                cancellationToken))
            return new BusinessOwnerAccessResetResult(
                BusinessOwnerAccessResetStatus.BusinessNotFound,
                null,
                null);

        var owner = await dbContext.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(
                user => user.BusinessId == businessId &&
                    user.Role != null &&
                    user.Role.Name == "BusinessOwner",
                cancellationToken);
        if (owner is null)
            return new BusinessOwnerAccessResetResult(
                BusinessOwnerAccessResetStatus.OwnerNotFound,
                null,
                null);

        var activeInvitations = await dbContext.UserInvitations
            .Where(invitation =>
                invitation.UserId == owner.Id &&
                invitation.ConsumedAt == null &&
                invitation.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var invitation in activeInvitations)
            invitation.ConsumedAt = now;

        owner.MustResetPassword = true;
        owner.UpdatedAt = now;
        dbContext.UserInvitations.Add(new UserInvitation
        {
            UserId = owner.Id,
            Purpose = "owner-reset",
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BusinessOwnerAccessResetResult(
            BusinessOwnerAccessResetStatus.Reset,
            owner.Id,
            owner.Email);
    }
}
