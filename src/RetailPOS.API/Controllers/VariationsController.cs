using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Variation;
using RetailPOS.API.Models;
using RetailPOS.Application.Variations;
using RetailPOS.Application.Variations.Commands;
using RetailPOS.Application.Variations.Queries;
using MediatR;

namespace RetailPOS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Authenticated by default; reads also serve the Product Edit variant section, management keeps variations.view.
    [Authorize]
    public class VariationsController : ControllerBase
    {
        private readonly ISender _sender;

        public VariationsController(ISender sender) => _sender = sender;

        [HttpGet]
        [Authorize(Policy = LookupAuthorizationPolicies.VariationList)]
        public async Task<ActionResult<ApiResponse<List<VariationDto>>>> GetAll(
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            var variations = await _sender.Send(new GetVariationsQuery(includeInactive), cancellationToken);
            return Ok(ApiResponse<List<VariationDto>>.SuccessResponse(variations.Select(MapVariation).ToList()));
        }

        [HttpGet("{id}")]
        [Authorize(Policy = LookupAuthorizationPolicies.VariationList)]
        public async Task<ActionResult<ApiResponse<VariationDto>>> GetById(
            long id,
            CancellationToken cancellationToken)
        {
            var variation = await _sender.Send(new GetVariationByIdQuery(id), cancellationToken);
            return Ok(ApiResponse<VariationDto>.SuccessResponse(MapVariation(variation)));
        }

        [HttpPost]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.create")]
        public async Task<ActionResult<ApiResponse<VariationDto>>> Create(
            [FromBody] CreateVariationDto dto,
            CancellationToken cancellationToken)
        {
            var variation = await _sender.Send(
                new CreateVariationCommand(new VariationWriteModel(
                    dto.Name,
                    dto.DisplayOrder,
                    dto.IsActive,
                    dto.AutoSelectAllOptions,
                    dto.Options.Select(option => new VariationOptionWriteModel(
                        option.Name,
                        option.PriceAdjustment,
                        option.DisplayOrder,
                        option.IsActive)).ToList())),
                cancellationToken);
            return CreatedAtAction(
                nameof(GetById), 
                new { id = variation.Id },
                ApiResponse<VariationDto>.SuccessResponse(MapVariation(variation), "Variation created successfully"));
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.edit")]
        public async Task<ActionResult<ApiResponse<VariationDto>>> Update(
            long id,
            [FromBody] UpdateVariationDto dto,
            CancellationToken cancellationToken)
        {
            var variation = await _sender.Send(
                new UpdateVariationCommand(
                    id,
                    new VariationWriteModel(
                        dto.Name,
                        dto.DisplayOrder,
                        dto.IsActive,
                        dto.AutoSelectAllOptions,
                        [])),
                cancellationToken);
            return Ok(ApiResponse<VariationDto>.SuccessResponse(
                MapVariation(variation),
                "Variation updated successfully"));
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.delete")]
        public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
        {
            await _sender.Send(new DeleteVariationCommand(id), cancellationToken);
            return Ok(ApiResponse.SuccessResponse("Variation deleted successfully"));
        }

        [HttpPost("{variationId}/options")]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.create")]
        public async Task<ActionResult<ApiResponse<VariationOptionDto>>> CreateOption(
            long variationId,
            [FromBody] CreateVariationOptionDto dto,
            CancellationToken cancellationToken)
        {
            var option = await _sender.Send(
                new CreateVariationOptionCommand(
                    variationId,
                    new VariationOptionWriteModel(dto.Name, dto.PriceAdjustment, dto.DisplayOrder, dto.IsActive)),
                cancellationToken);
            return CreatedAtAction(
                nameof(GetById), 
                new { id = variationId },
                ApiResponse<VariationOptionDto>.SuccessResponse(MapOption(option), "Option created successfully"));
        }

        [HttpPut("options/{optionId}")]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.edit")]
        public async Task<ActionResult<ApiResponse<VariationOptionDto>>> UpdateOption(
            long optionId,
            [FromBody] UpdateVariationOptionDto dto,
            CancellationToken cancellationToken)
        {
            var option = await _sender.Send(
                new UpdateVariationOptionCommand(
                    optionId,
                    new VariationOptionWriteModel(dto.Name, dto.PriceAdjustment, dto.DisplayOrder, dto.IsActive)),
                cancellationToken);
            return Ok(ApiResponse<VariationOptionDto>.SuccessResponse(
                MapOption(option),
                "Option updated successfully"));
        }

        [HttpDelete("options/{optionId}")]
        [Authorize(Policy = "variations.view")]
        [Authorize(Policy = "variations.delete")]
        public async Task<ActionResult<ApiResponse>> DeleteOption(
            long optionId,
            CancellationToken cancellationToken)
        {
            await _sender.Send(new DeleteVariationOptionCommand(optionId), cancellationToken);
            return Ok(ApiResponse.SuccessResponse("Option deleted successfully"));
        }

        private static VariationDto MapVariation(VariationReadModel variation) => new()
        {
            Id = variation.Id,
            Name = variation.Name,
            DisplayOrder = variation.DisplayOrder,
            IsActive = variation.IsActive,
            AutoSelectAllOptions = variation.AutoSelectAllOptions,
            Options = variation.Options.Select(MapOption).ToList()
        };

        private static VariationOptionDto MapOption(VariationOptionReadModel option) => new()
        {
            Id = option.Id,
            VariationId = option.VariationId,
            VariationName = option.VariationName,
            Name = option.Name,
            PriceAdjustment = option.PriceAdjustment,
            DisplayOrder = option.DisplayOrder,
            IsActive = option.IsActive
        };
    }
}
