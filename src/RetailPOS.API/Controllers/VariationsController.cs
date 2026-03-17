using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Variation;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VariationsController : ControllerBase
    {
        private readonly IVariationService _variationService;
        private readonly ILogger<VariationsController> _logger;

        public VariationsController(IVariationService variationService, ILogger<VariationsController> logger)
        {
            _variationService = variationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<VariationDto>>>> GetAll([FromQuery] bool includeInactive = false)
        {
            var variations = await _variationService.GetAllVariationsAsync(includeInactive);
            return Ok(ApiResponse<List<VariationDto>>.SuccessResponse(variations));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<VariationDto>>> GetById(long id)
        {
            var variation = await _variationService.GetVariationByIdAsync(id);
            return Ok(ApiResponse<VariationDto>.SuccessResponse(variation));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<VariationDto>>> Create([FromBody] CreateVariationDto dto)
        {
            var variation = await _variationService.CreateVariationAsync(dto);
            return CreatedAtAction(
                nameof(GetById), 
                new { id = variation.Id }, 
                ApiResponse<VariationDto>.SuccessResponse(variation, "Variation created successfully"));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<VariationDto>>> Update(long id, [FromBody] UpdateVariationDto dto)
        {
            var variation = await _variationService.UpdateVariationAsync(id, dto);
            return Ok(ApiResponse<VariationDto>.SuccessResponse(variation, "Variation updated successfully"));
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse>> Delete(long id)
        {
            await _variationService.DeleteVariationAsync(id);
            return Ok(ApiResponse.SuccessResponse("Variation deleted successfully"));
        }

        [HttpPost("{variationId}/options")]
        public async Task<ActionResult<ApiResponse<VariationOptionDto>>> CreateOption(long variationId, [FromBody] CreateVariationOptionDto dto)
        {
            var option = await _variationService.CreateOptionAsync(variationId, dto);
            return CreatedAtAction(
                nameof(GetById), 
                new { id = variationId }, 
                ApiResponse<VariationOptionDto>.SuccessResponse(option, "Option created successfully"));
        }

        [HttpPut("options/{optionId}")]
        public async Task<ActionResult<ApiResponse<VariationOptionDto>>> UpdateOption(long optionId, [FromBody] UpdateVariationOptionDto dto)
        {
            var option = await _variationService.UpdateOptionAsync(optionId, dto);
            return Ok(ApiResponse<VariationOptionDto>.SuccessResponse(option, "Option updated successfully"));
        }

        [HttpDelete("options/{optionId}")]
        public async Task<ActionResult<ApiResponse>> DeleteOption(long optionId)
        {
            await _variationService.DeleteOptionAsync(optionId);
            return Ok(ApiResponse.SuccessResponse("Option deleted successfully"));
        }
    }
}
