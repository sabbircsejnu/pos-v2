using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.Application.BusinessAdministration.Abstractions;

public interface IBusinessAdministrationReadStore
{
    Task<IReadOnlyList<BusinessSummaryReadModel>> ListBusinessesAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);

    Task<BusinessSummaryReadModel?> FindBusinessAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BusinessFeatureSettingReadModel>?> ListFeatureSettingsAsync(
        long businessId,
        CancellationToken cancellationToken);
}
