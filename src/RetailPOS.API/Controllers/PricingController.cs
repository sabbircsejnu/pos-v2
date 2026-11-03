using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Pricing.Commands;
using RetailPOS.Application.Pricing.Queries;
using RetailPOS.Application.Pricing.ReadModels;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/pricing")]
[Authorize(Policy = "products.view")]
public class PricingController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ILogger<PricingController> _logger;

    public PricingController(
        ISender sender,
        IUserOutletAccessService outletAccess,
        ILogger<PricingController> logger)
    {
        _sender = sender;
        _outletAccess = outletAccess;
        _logger = logger;
    }

    // ── Core price calculation ────────────────────────────────────────────

    /// <summary>
    /// Calculate the effective price for a single cart line.
    /// Called by POS when a product is added, and by reports for margin calculation.
    /// </summary>
    [HttpPost("calculate")]
    [ProducesResponseType(typeof(ApiResponse<PriceCalculationResultDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PriceCalculationResultDto>>> Calculate(
        [FromBody] PriceCalculationRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.OutletId.HasValue)
        {
            try
            {
                dto.OutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(dto.OutletId);
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<PriceCalculationResultDto>.ErrorResponse(exception.Message));
            }
        }

        var result = await CalculateThroughApplicationAsync(
            dto.ProductVariantId,
            dto.OutletId,
            dto.Quantity,
            cancellationToken);
        return Ok(ApiResponse<PriceCalculationResultDto>.SuccessResponse(ToDto(result)));
    }

    /// <summary>
    /// Calculate prices for multiple cart lines in one call.
    /// Use this from the POS screen to avoid N+1 HTTP round trips when building a cart.
    /// </summary>
    [HttpPost("calculate-batch")]
    [ProducesResponseType(typeof(ApiResponse<List<PriceCalculationResultDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<List<PriceCalculationResultDto>>>> CalculateBatch(
        [FromBody] PriceCalculationBatchRequestDto dto,
        CancellationToken cancellationToken)
    {
        foreach (var item in dto.Items)
        {
            if (!item.OutletId.HasValue)
            {
                continue;
            }

            try
            {
                item.OutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(item.OutletId);
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<List<PriceCalculationResultDto>>.ErrorResponse(exception.Message));
            }
        }

        var results = await _sender.Send(
            new CalculatePosCartPricesQuery(
                dto.Items.Select(item => new PosCartPriceLine(
                    item.ProductVariantId,
                    item.OutletId,
                    item.Quantity)).ToArray(),
                ReadFromCache: true),
            cancellationToken);
        return Ok(ApiResponse<List<PriceCalculationResultDto>>.SuccessResponse(
            results.Select(ToDto).ToList()));
    }

    /// <summary>
    /// GET variant for fast POS barcode-scan lookup.
    /// GET /api/pricing/product/{variantId}?outletId=1&amp;quantity=2
    /// </summary>
    [HttpGet("product/{variantId:long}")]
    [ProducesResponseType(typeof(ApiResponse<PriceCalculationResultDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PriceCalculationResultDto>>> GetForVariant(
        long variantId,
        [FromQuery] long? outletId = null,
        [FromQuery] int quantity   = 1,
        CancellationToken cancellationToken = default)
    {
        if (outletId.HasValue)
        {
            try
            {
                outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
            }
            catch (UnauthorizedAccessException exception)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<PriceCalculationResultDto>.ErrorResponse(exception.Message));
            }
        }

        var result = await CalculateThroughApplicationAsync(
            variantId,
            outletId,
            Math.Max(1, quantity),
            cancellationToken);
        return Ok(ApiResponse<PriceCalculationResultDto>.SuccessResponse(ToDto(result)));
    }

    private async Task<PosCartPriceReadModel> CalculateThroughApplicationAsync(
        long variantId,
        long? outletId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var results = await _sender.Send(
            new CalculatePosCartPricesQuery(
                [new PosCartPriceLine(variantId, outletId, quantity)],
                ReadFromCache: true),
            cancellationToken);
        return results.Single();
    }

    private static PriceCalculationResultDto ToDto(PosCartPriceReadModel result) => new()
    {
        ProductVariantId = result.ProductVariantId,
        ProductName = result.ProductName,
        VariantName = result.VariantName,
        BasePrice = result.BasePrice,
        OutletPrice = result.OutletPrice,
        DiscountAmount = result.DiscountAmount,
        FinalPrice = result.FinalPrice,
        TaxRate = result.TaxRate,
        TaxClassification = result.TaxClassification,
        PricesIncludeTax = result.PricesIncludeTax,
        TaxAmount = result.TaxAmount,
        FinalPriceWithTax = result.FinalPriceWithTax,
        OutletOverrideApplied = result.OutletOverrideApplied,
        AppliedRuleId = result.AppliedRuleId,
        AppliedRuleName = result.AppliedRuleName,
        Breakdown = result.Breakdown.Select(line => new PriceBreakdownLineDto
        {
            Label = line.Label,
            Amount = line.Amount
        }).ToList()
    };

    private static PriceRuleDto ToPriceRuleDto(PriceRuleReadModel rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        Description = rule.Description,
        RuleType = rule.RuleType,
        TargetId = rule.TargetId,
        DiscountType = rule.DiscountType,
        DiscountValue = rule.DiscountValue,
        MinQuantity = rule.MinQuantity,
        Priority = rule.Priority,
        ValidFrom = rule.ValidFrom,
        ValidTo = rule.ValidTo,
        IsActive = rule.IsActive,
        CreatedAt = rule.CreatedAt
    };

    private static OutletPriceOverrideDto ToOutletPriceOverrideDto(
        OutletPriceOverrideReadModel priceOverride) => new()
    {
        Id = priceOverride.Id,
        OutletId = priceOverride.OutletId,
        OutletName = priceOverride.OutletName,
        ProductVariantId = priceOverride.ProductVariantId,
        VariantName = priceOverride.VariantName,
        ProductName = priceOverride.ProductName,
        OverrideType = priceOverride.OverrideType,
        OverrideValue = priceOverride.OverrideValue,
        IsActive = priceOverride.IsActive,
        CreatedAt = priceOverride.CreatedAt
    };

    // ── Price Rules ──────────────────────────────────────────────────────

    /// <summary>Search and list price/campaign rules with optional filters.</summary>
    [HttpPost("rules/search")]
    public async Task<ActionResult<ApiResponse<PriceRuleListDto>>> SearchRules(
        [FromBody] PriceRuleSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchPriceRulesQuery(
                searchDto.SearchQuery,
                searchDto.RuleType,
                searchDto.IsActive,
                searchDto.PageNumber,
                searchDto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<PriceRuleListDto>.SuccessResponse(new PriceRuleListDto
        {
            Rules = result.Rules.Select(ToPriceRuleDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    /// <summary>Get a price rule by ID.</summary>
    [HttpGet("rules/{id:long}")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> GetRule(
        long id,
        CancellationToken cancellationToken)
    {
        var rule = await _sender.Send(new GetPriceRuleQuery(id), cancellationToken);
        return Ok(ApiResponse<PriceRuleDto>.SuccessResponse(ToPriceRuleDto(rule)));
    }

    /// <summary>Create a new price/campaign rule.</summary>
    [HttpPost("rules")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> CreateRule(
        [FromBody] CreatePriceRuleDto dto,
        CancellationToken cancellationToken)
    {
        var rule = await _sender.Send(
            new CreatePriceRuleCommand(ToWriteData(dto)),
            cancellationToken);
        _logger.LogInformation("PriceRule created: {Name} (ID: {Id})", rule.Name, rule.Id);
        return CreatedAtAction(
            nameof(GetRule),
            new { id = rule.Id },
            ApiResponse<PriceRuleDto>.SuccessResponse(ToPriceRuleDto(rule), "Price rule created successfully"));
    }

    /// <summary>Update a price rule.</summary>
    [HttpPut("rules/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> UpdateRule(
        long id,
        [FromBody] UpdatePriceRuleDto dto,
        CancellationToken cancellationToken)
    {
        var rule = await _sender.Send(
            new UpdatePriceRuleCommand(id, ToWriteData(dto)),
            cancellationToken);
        _logger.LogInformation("PriceRule updated: {Name} (ID: {Id})", rule.Name, rule.Id);
        return Ok(ApiResponse<PriceRuleDto>.SuccessResponse(ToPriceRuleDto(rule), "Price rule updated successfully"));
    }

    /// <summary>Delete a price rule.</summary>
    [HttpDelete("rules/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> DeleteRule(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeletePriceRuleCommand(id), cancellationToken);
        _logger.LogInformation("PriceRule deleted: ID {Id}", id);
        return Ok(ApiResponse.SuccessResponse("Price rule deleted successfully"));
    }

    private static PriceRuleWriteData ToWriteData(CreatePriceRuleDto dto) =>
        new(
            dto.Name,
            dto.Description,
            dto.RuleType,
            dto.TargetId,
            dto.DiscountType,
            dto.DiscountValue,
            dto.MinQuantity,
            dto.Priority,
            dto.ValidFrom,
            dto.ValidTo,
            dto.IsActive);

    // ── Outlet Price Overrides ────────────────────────────────────────────

    /// <summary>Get all price overrides configured for a specific outlet.</summary>
    [HttpGet("outlet-overrides/outlet/{outletId:long}")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideListDto>>> GetByOutlet(
        long outletId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetOutletPriceOverridesQuery(outletId),
            cancellationToken);
        return Ok(ApiResponse<OutletPriceOverrideListDto>.SuccessResponse(new OutletPriceOverrideListDto
        {
            Overrides = result.Overrides.Select(ToOutletPriceOverrideDto).ToList(),
            TotalCount = result.TotalCount
        }));
    }

    /// <summary>Get an outlet price override by ID.</summary>
    [HttpGet("outlet-overrides/{id:long}")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> GetOverride(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetOutletPriceOverrideQuery(id), cancellationToken);
        return Ok(ApiResponse<OutletPriceOverrideDto>.SuccessResponse(
            ToOutletPriceOverrideDto(result)));
    }

    /// <summary>Create an outlet price override.</summary>
    [HttpPost("outlet-overrides")]
    [Authorize(Policy = "products.edit")]
    [ProducesResponseType(typeof(ApiResponse<OutletPriceOverrideDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> CreateOverride(
        [FromBody] CreateOutletPriceOverrideDto dto,
        CancellationToken cancellationToken)
    {
        OutletPriceOverrideReadModel result;
        try
        {
            result = await _sender.Send(
                new CreateOutletPriceOverrideCommand(
                    new OutletPriceOverrideWriteData(
                        dto.OutletId,
                        dto.ProductVariantId,
                        dto.OverrideType,
                        dto.OverrideValue,
                        dto.IsActive)),
                cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<OutletPriceOverrideDto>.ErrorResponse(exception.Message));
        }

        _logger.LogInformation(
            "OutletPriceOverride created: outlet={OutletId} variant={VariantId} type={Type} value={Value}",
            dto.OutletId,
            dto.ProductVariantId,
            result.OverrideType,
            result.OverrideValue);
        return CreatedAtAction(
            nameof(GetOverride),
            new { id = result.Id },
            ApiResponse<OutletPriceOverrideDto>.SuccessResponse(
                ToOutletPriceOverrideDto(result),
                "Outlet price override created successfully"));
    }

    /// <summary>Update an outlet price override.</summary>
    [HttpPut("outlet-overrides/{id:long}")]
    [Authorize(Policy = "products.edit")]
    [ProducesResponseType(typeof(ApiResponse<OutletPriceOverrideDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> UpdateOverride(
        long id,
        [FromBody] UpdateOutletPriceOverrideDto dto,
        CancellationToken cancellationToken)
    {
        OutletPriceOverrideReadModel result;
        try
        {
            result = await _sender.Send(
                new UpdateOutletPriceOverrideCommand(
                    id,
                    dto.OverrideType,
                    dto.OverrideValue,
                    dto.IsActive),
                cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<OutletPriceOverrideDto>.ErrorResponse(exception.Message));
        }

        _logger.LogInformation("OutletPriceOverride {Id} updated", id);
        return Ok(ApiResponse<OutletPriceOverrideDto>.SuccessResponse(
            ToOutletPriceOverrideDto(result),
            "Outlet price override updated successfully"));
    }

    /// <summary>Delete an outlet price override.</summary>
    [HttpDelete("outlet-overrides/{id:long}")]
    [Authorize(Policy = "products.edit")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse>> DeleteOverride(
        long id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new DeleteOutletPriceOverrideCommand(id), cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.ErrorResponse(exception.Message));
        }

        _logger.LogInformation("OutletPriceOverride {Id} deleted", id);
        return Ok(ApiResponse.SuccessResponse("Outlet price override deleted successfully"));
    }
}
