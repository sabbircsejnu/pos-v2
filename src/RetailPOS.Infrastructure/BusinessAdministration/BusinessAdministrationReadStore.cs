using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BusinessAdministration;

public sealed class BusinessAdministrationReadStore(RetailPOSDbContext dbContext)
    : IBusinessAdministrationReadStore
{
    public async Task<IReadOnlyList<BusinessSummaryReadModel>> ListBusinessesAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = CreateBusinessSummaryQuery();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(term) ||
                (b.Email != null && b.Email.ToLower().Contains(term)) ||
                (b.Phone != null && b.Phone.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
            query = query.Where(b => b.IsActive == isActive.Value);

        var businesses = await query
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return businesses.Select(MapBusinessSummary).ToList();
    }

    public async Task<BusinessSummaryReadModel?> FindBusinessAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var business = await CreateBusinessSummaryQuery()
            .FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken);

        return business is null ? null : MapBusinessSummary(business);
    }

    public async Task<IReadOnlyList<BusinessFeatureSettingReadModel>?> ListFeatureSettingsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Businesses.AnyAsync(b => b.Id == businessId, cancellationToken))
            return null;

        return await dbContext.BusinessFeatureSettings
            .AsNoTracking()
            .Where(setting => setting.BusinessId == businessId)
            .OrderBy(setting => setting.FeatureKey)
            .Select(setting => new BusinessFeatureSettingReadModel(
                setting.FeatureKey,
                setting.IsEnabled,
                setting.LimitValue))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Business> CreateBusinessSummaryQuery() =>
        dbContext.Businesses
            .AsNoTracking()
            .Include(business => business.Users)
                .ThenInclude(user => user.Role)
            .Include(business => business.Outlets)
            .Include(business => business.Warehouses);

    private static BusinessSummaryReadModel MapBusinessSummary(Business business)
    {
        var owner = business.Users
            .FirstOrDefault(user =>
                string.Equals(user.Role?.Name, "BusinessOwner", StringComparison.OrdinalIgnoreCase));

        return new BusinessSummaryReadModel(
            business.Id,
            business.Name,
            business.Email,
            business.Phone,
            business.IsActive,
            business.SubscriptionPlan,
            business.TrialEndsAt,
            business.SubscriptionEndsAt,
            business.MaxOutlets,
            business.MaxUsers,
            business.Users.LongCount(),
            business.Outlets.LongCount(),
            business.Warehouses.LongCount(),
            owner?.Email)
        {
            ShowPoweredByOnReceipt = business.ShowPoweredByOnReceipt,
            PoweredByText = business.PoweredByText
        };
    }
}
