using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.Security.Claims;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.Queries;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Endpoints for parking (holding) and recalling a POS cart without committing a sale.
/// </summary>
[ApiController]
[Route("api/held-sales")]
[Authorize(Policy = "sales.view")]
public class HeldSalesController(ISender sender, IFeatureEntitlementService featureEntitlement) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "sales.create")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<HeldSaleDto>>> Hold(
        [FromBody] HoldSaleDto dto,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await featureEntitlement.EnsureFeatureEnabledAsync(
            "sales.hold",
            cancellationToken: cancellationToken);

        // The held cart belongs to the authenticated cashier; a submitted identity is only accepted when it matches.
        var currentUserId = TryGetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value <= 0)
            return Unauthorized(ApiResponse<HeldSaleDto>.ErrorResponse("User identity not found"));
        if (dto.CashierId.HasValue && dto.CashierId.Value != currentUserId.Value)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<HeldSaleDto>.ErrorResponse("Cashier identity must match the authenticated user."));

        var held = await sender.Send(
            new HoldSaleCommand(
                dto.OutletId,
                currentUserId.Value,
                dto.CustomerId,
                dto.Items?.Select(item => new HoldSaleItem(
                    item.VariantId,
                    item.ProductName,
                    item.VariantSku,
                    item.Quantity,
                    item.UnitPrice,
                    item.DiscountAmount,
                    item.AppliedRuleName,
                    item.SellingOptionId,
                    item.ExpectedConfigVersionId,
                    item.FulfilmentState,
                    item.IncludePackaging,
                    item.UnitLabel)).ToArray() ?? [],
                dto.DiscountPercent,
                dto.PaymentMethod,
                dto.Note),
            cancellationToken);

        var response = MapToDto(held);
        return CreatedAtAction(
            nameof(GetById),
            new { id = held.Id },
            ApiResponse<HeldSaleDto>.SuccessResponse(response, "Sale held successfully"));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<HeldSaleDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var held = await sender.Send(new GetHeldSaleByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<HeldSaleDto>.SuccessResponse(MapToDto(held)));
    }

    [HttpGet("outlet/{outletId}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<HeldSaleListDto>>> GetByOutlet(
        long outletId,
        CancellationToken cancellationToken)
    {
        var list = await sender.Send(new GetHeldSalesByOutletQuery(outletId), cancellationToken);
        return Ok(ApiResponse<HeldSaleListDto>.SuccessResponse(MapToDto(list)));
    }

    [HttpGet("cashier/{cashierId}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<HeldSaleListDto>>> GetByCashier(
        long cashierId,
        CancellationToken cancellationToken)
    {
        // Other cashiers' parked carts are not readable through this endpoint; outlet recall uses /outlet/{id}.
        var currentUserId = TryGetCurrentUserId();
        if (!currentUserId.HasValue || currentUserId.Value != cashierId)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<HeldSaleListDto>.ErrorResponse("You can only list your own held sales."));

        var list = await sender.Send(new GetHeldSalesByCashierQuery(cashierId), cancellationToken);
        return Ok(ApiResponse<HeldSaleListDto>.SuccessResponse(MapToDto(list)));
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "sales.create")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteHeldSaleCommand(id), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Held sale deleted"));
    }

    private long? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static HeldSaleListDto MapToDto(HeldSaleListReadModel readModel) => new()
    {
        HeldSales = readModel.HeldSales.Select(MapToDto).ToList(),
        TotalCount = readModel.TotalCount
    };

    private static HeldSaleDto MapToDto(HeldSaleReadModel readModel) => new()
    {
        Id = readModel.Id,
        OutletId = readModel.OutletId,
        OutletName = readModel.OutletName,
        CashierId = readModel.CashierId,
        CashierName = readModel.CashierName,
        CustomerId = readModel.CustomerId,
        CustomerName = readModel.CustomerName,
        Items = readModel.Items.Select(item => new HeldSaleItemDto
        {
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            VariantSku = item.VariantSku,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            DiscountAmount = item.DiscountAmount,
            AppliedRuleName = item.AppliedRuleName,
            SellingOptionId = item.SellingOptionId,
            ExpectedConfigVersionId = item.ExpectedConfigVersionId,
            FulfilmentState = item.FulfilmentState,
            IncludePackaging = item.IncludePackaging,
            UnitLabel = item.UnitLabel
        }).ToList(),
        DiscountPercent = readModel.DiscountPercent,
        PaymentMethod = readModel.PaymentMethod,
        Note = readModel.Note,
        HeldAt = readModel.HeldAt,
        Subtotal = readModel.Subtotal
    };
}
