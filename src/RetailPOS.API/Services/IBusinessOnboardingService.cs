using RetailPOS.API.DTOs.Business;

namespace RetailPOS.API.Services;

public interface IBusinessOnboardingService
{
    Task<CreateBusinessResponseDto> CreateBusinessAsync(CreateBusinessRequestDto request);
}
