using RetailPOS.API.DTOs.Variation;
using RetailPOS.API.Exceptions;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services
{
    public interface IVariationService
    {
        Task<List<VariationDto>> GetAllVariationsAsync(bool includeInactive = false);
        Task<VariationDto> GetVariationByIdAsync(long id);
        Task<VariationDto> CreateVariationAsync(CreateVariationDto dto);
        Task<VariationDto> UpdateVariationAsync(long id, UpdateVariationDto dto);
        Task DeleteVariationAsync(long id);
        Task<VariationOptionDto> CreateOptionAsync(long variationId, CreateVariationOptionDto dto);
        Task<VariationOptionDto> UpdateOptionAsync(long id, UpdateVariationOptionDto dto);
        Task DeleteOptionAsync(long id);
    }

    public class VariationService : IVariationService
    {
        private readonly IVariationRepository _variationRepo;
        private readonly IVariationOptionRepository _optionRepo;

        public VariationService(IVariationRepository variationRepo, IVariationOptionRepository optionRepo)
        {
            _variationRepo = variationRepo;
            _optionRepo = optionRepo;
        }

        public async Task<List<VariationDto>> GetAllVariationsAsync(bool includeInactive = false)
        {
            var variations = await _variationRepo.GetAllAsync(includeInactive);
            return variations.Select(MapToDto).ToList();
        }

        public async Task<VariationDto> GetVariationByIdAsync(long id)
        {
            var variation = await _variationRepo.GetByIdAsync(id);
            if (variation == null) throw new Exception("Variation not found");
            return MapToDto(variation);
        }

        public async Task<VariationDto> CreateVariationAsync(CreateVariationDto dto)
        {
            if (await _variationRepo.NameExistsAsync(dto.Name))
                throw new Exception($"Variation with name '{dto.Name}' already exists");

            var variation = new Variation
            {
                Name = dto.Name,
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive,
                AutoSelectAllOptions = dto.AutoSelectAllOptions
            };

            variation = await _variationRepo.CreateAsync(variation);

            // Create options
            foreach (var optionDto in dto.Options)
            {
                var option = new VariationOption
                {
                    VariationId = variation.Id,
                    Name = optionDto.Name,
                    PriceAdjustment = optionDto.PriceAdjustment,
                    DisplayOrder = optionDto.DisplayOrder,
                    IsActive = optionDto.IsActive
                };
                await _optionRepo.CreateAsync(option);
            }

            variation = await _variationRepo.GetByIdAsync(variation.Id);
            return MapToDto(variation!);
        }

        public async Task<VariationDto> UpdateVariationAsync(long id, UpdateVariationDto dto)
        {
            var variation = await _variationRepo.GetByIdAsync(id);
            if (variation == null) throw new Exception("Variation not found");

            if (await _variationRepo.NameExistsAsync(dto.Name, id))
                throw new Exception($"Variation with name '{dto.Name}' already exists");

            variation.Name = dto.Name;
            variation.DisplayOrder = dto.DisplayOrder;
            variation.IsActive = dto.IsActive;
            variation.AutoSelectAllOptions = dto.AutoSelectAllOptions;

            variation = await _variationRepo.UpdateAsync(variation);
            return MapToDto(variation);
        }

        public async Task DeleteVariationAsync(long id)
        {
            var variation = await _variationRepo.GetByIdAsync(id);
            if (variation == null)
                throw new KeyNotFoundException("Variation not found");

            var products = await _variationRepo.GetProductsUsingVariationAsync(id);
            if (products.Count > 0)
            {
                var refs = products
                    .Select(p => new ResourceReference { Id = p.Id, Name = p.Name })
                    .ToList();
                throw new ResourceInUseException(
                    $"Cannot delete variation '{variation.Name}' because it is used by {products.Count} product(s).",
                    refs);
            }

            await _variationRepo.DeleteAsync(id);
        }

        public async Task<VariationOptionDto> CreateOptionAsync(long variationId, CreateVariationOptionDto dto)
        {
            var option = new VariationOption
            {
                VariationId = variationId,
                Name = dto.Name,
                PriceAdjustment = dto.PriceAdjustment,
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive
            };

            option = await _optionRepo.CreateAsync(option);
            option = await _optionRepo.GetByIdAsync(option.Id);
            return MapOptionToDto(option!);
        }

        public async Task<VariationOptionDto> UpdateOptionAsync(long id, UpdateVariationOptionDto dto)
        {
            var option = await _optionRepo.GetByIdAsync(id);
            if (option == null) throw new Exception("Option not found");

            option.Name = dto.Name;
            option.PriceAdjustment = dto.PriceAdjustment;
            option.DisplayOrder = dto.DisplayOrder;
            option.IsActive = dto.IsActive;

            option = await _optionRepo.UpdateAsync(option);
            return MapOptionToDto(option);
        }

        public async Task DeleteOptionAsync(long id)
        {
            await _optionRepo.DeleteAsync(id);
        }

        private VariationDto MapToDto(Variation variation)
        {
            return new VariationDto
            {
                Id = variation.Id,
                Name = variation.Name,
                DisplayOrder = variation.DisplayOrder,
                IsActive = variation.IsActive,
                AutoSelectAllOptions = variation.AutoSelectAllOptions,
                Options = variation.Options.Select(MapOptionToDto).ToList()
            };
        }

        private VariationOptionDto MapOptionToDto(VariationOption option)
        {
            return new VariationOptionDto
            {
                Id = option.Id,
                VariationId = option.VariationId,
                VariationName = option.Variation?.Name ?? "",
                Name = option.Name,
                PriceAdjustment = option.PriceAdjustment,
                DisplayOrder = option.DisplayOrder,
                IsActive = option.IsActive
            };
        }
    }
}
