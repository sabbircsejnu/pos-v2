using RetailPOS.API.DTOs.Business;

namespace RetailPOS.API.Services;

public interface IBusinessOnboardingService
{
    Task<CreateBusinessResponseDto> CreateBusinessAsync(CreateBusinessRequestDto request);
    Task<List<BusinessSummaryDto>> GetBusinessesAsync(string? search = null, bool? isActive = null);
    Task<BusinessSummaryDto> GetBusinessByIdAsync(long businessId);
    Task<BusinessSummaryDto> SetBusinessActiveAsync(long businessId, bool isActive);
    Task<BusinessSummaryDto> UpdateSubscriptionAsync(long businessId, UpdateBusinessSubscriptionDto request);
    Task<List<BusinessFeatureSettingDto>> GetFeatureSettingsAsync(long businessId);
    Task<List<BusinessFeatureSettingDto>> UpsertFeatureSettingsAsync(long businessId, UpdateBusinessFeatureSettingsDto request);
    Task<ResetBusinessOwnerAccessResponseDto> ResetBusinessOwnerAccessAsync(long businessId);
}
