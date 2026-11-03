using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Settings;
using RetailPOS.API.Models;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Settings.Commands;
using RetailPOS.Application.Settings.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IBusinessScope _businessScope;

    public SettingsController(ISender sender, IBusinessScope businessScope)
    {
        _sender = sender;
        _businessScope = businessScope;
    }

    /// <summary>Gets all system settings.</summary>
    [HttpGet]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> GetAll(CancellationToken cancellationToken)
    {
        var settings = await _sender.Send(new GetSystemSettingsQuery(), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(settings));
    }

    /// <summary>Gets company settings.</summary>
    [HttpGet("company")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<CompanySettings>>> GetCompany(CancellationToken cancellationToken)
    {
        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Company;
        return Ok(ApiResponse<CompanySettings>.SuccessResponse(settings));
    }

    /// <summary>Gets the UI date format without exposing company administration settings.</summary>
    [HttpGet("formatting")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UiFormattingSettings>>> GetFormatting(CancellationToken cancellationToken)
    {
        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Company;
        return Ok(ApiResponse<UiFormattingSettings>.SuccessResponse(new UiFormattingSettings(settings.DateFormat)));
    }

    /// <summary>Updates company settings.</summary>
    [HttpPut("company")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateCompany(
        [FromBody] CompanySettings settings,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateCompanySettingsCommand(settings), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Company settings updated"));
    }

    /// <summary>Company-controlled presentation settings available to authenticated UI users.</summary>
    public sealed record UiFormattingSettings(string DateFormat);

    /// <summary>Gets currency settings.</summary>
    [HttpGet("currency")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CurrencySettings>>> GetCurrency(CancellationToken cancellationToken)
    {
        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Currency;
        return Ok(ApiResponse<CurrencySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates currency settings.</summary>
    [HttpPut("currency")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateCurrency(
        [FromBody] CurrencySettings settings,
        CancellationToken cancellationToken)
    {
        var validation = ValidateCurrency(settings);
        if (validation != null) return BadRequest(validation);

        var result = await _sender.Send(new UpdateCurrencySettingsCommand(settings), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Currency settings updated"));
    }

    private static ApiResponse<SystemSettings>? ValidateCurrency(CurrencySettings settings)
    {
        if (settings == null)
        {
            return ApiResponse<SystemSettings>.ErrorResponse("Currency settings are required");
        }

        if (string.IsNullOrWhiteSpace(settings.CurrencyCode))
        {
            return ApiResponse<SystemSettings>.ErrorResponse("Currency code is required");
        }

        if (string.IsNullOrWhiteSpace(settings.CurrencySymbol))
        {
            return ApiResponse<SystemSettings>.ErrorResponse("Currency symbol is required");
        }

        if (settings.DecimalPlaces < 0 || settings.DecimalPlaces > 4)
        {
            return ApiResponse<SystemSettings>.ErrorResponse("Decimal places must be between 0 and 4");
        }

        var pos = (settings.SymbolPosition ?? string.Empty).Trim().ToLowerInvariant();
        if (pos != "before" && pos != "after")
        {
            return ApiResponse<SystemSettings>.ErrorResponse("Symbol position must be either 'before' or 'after'");
        }

        settings.CurrencyCode = settings.CurrencyCode.Trim().ToUpperInvariant();
        settings.SymbolPosition = pos;
        settings.ThousandsSeparator ??= string.Empty;
        settings.DecimalSeparator = string.IsNullOrEmpty(settings.DecimalSeparator) ? "." : settings.DecimalSeparator;

        return null;
    }

    /// <summary>Gets tax settings (Business-level).</summary>
    [HttpGet("tax")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<TaxSettings>>> GetTax(CancellationToken cancellationToken)
    {
        if (!User.HasPermission("settings.view") && !User.HasPermission("sales.create"))
            return Forbid();
        var settings = await _sender.Send(new GetBusinessPosSettingsQuery(_businessScope.RequireBusinessId()), cancellationToken);
        return Ok(ApiResponse<TaxSettings>.SuccessResponse(ToTaxSettings(settings)));
    }

    /// <summary>Updates the Business tax settings; the legacy tax settings are no longer used by checkout.</summary>
    [HttpPut("tax")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateTax(
        [FromBody] TaxSettings settings,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateBusinessPosSettingsCommand(
                _businessScope.RequireBusinessId(),
                new BusinessPosSettingsUpdate(
                    TaxEnabled: settings.TaxEnabled,
                    DefaultTaxRate: settings.DefaultTaxRate,
                    TaxName: settings.TaxName,
                    PricesIncludeTax: settings.PricesIncludeTax),
                TryGetCurrentUserId()),
            cancellationToken);
        var result = await _sender.Send(new GetSystemSettingsQuery(), cancellationToken);
        result.Tax = ToTaxSettings(updated);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Tax settings updated"));
    }

    /// <summary>Gets the Business POS settings (cash-session limit, time zone, tax and manual-discount limit).</summary>
    [HttpGet("pos")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<BusinessPosSettingsDto>>> GetPos(CancellationToken cancellationToken)
    {
        if (!User.HasPermission("settings.view")
            && !User.HasPermission("sales.create")
            && !User.HasPermission("sales.discount.configure"))
            return Forbid();

        var settings = await _sender.Send(new GetBusinessPosSettingsQuery(_businessScope.RequireBusinessId()), cancellationToken);
        return Ok(ApiResponse<BusinessPosSettingsDto>.SuccessResponse(ToDto(settings)));
    }

    /// <summary>
    /// Updates the Business POS settings. The manual-discount limit needs <c>sales.discount.configure</c>; every other
    /// setting needs <c>settings.edit</c>. Cash-session enforcement of the duration is introduced with the session lifecycle.
    /// </summary>
    [HttpPut("pos")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<BusinessPosSettingsDto>>> UpdatePos(
        [FromBody] UpdateBusinessPosSettingsDto dto,
        CancellationToken cancellationToken)
    {
        var changesDiscountLimit = dto.MaxManualDiscountPercent.HasValue;
        var changesSelfApproval = dto.AllowOwnerExpenseSelfApproval.HasValue;
        var changesOtherSettings = dto.MaxCashSessionDurationHours.HasValue
            || dto.DefaultTimeZoneId is not null
            || dto.TaxEnabled.HasValue
            || dto.DefaultTaxRate.HasValue
            || dto.TaxName is not null
            || dto.PricesIncludeTax.HasValue;
        if (!changesDiscountLimit && !changesOtherSettings && !changesSelfApproval)
            return BadRequest(ApiResponse<BusinessPosSettingsDto>.ErrorResponse("No settings were supplied."));
        if (changesOtherSettings && !User.HasPermission("settings.edit"))
            return Forbid();
        if (changesDiscountLimit && !User.HasPermission("sales.discount.configure"))
            return Forbid();
        // The maker-checker exception is never implied by a wildcard: the exact permission must be granted.
        if (changesSelfApproval && !User.HasExplicitPermission("expenses.self_approve"))
            return Forbid();

        var updated = await _sender.Send(
            new UpdateBusinessPosSettingsCommand(
                _businessScope.RequireBusinessId(),
                new BusinessPosSettingsUpdate(
                    dto.MaxCashSessionDurationHours,
                    dto.DefaultTimeZoneId,
                    dto.TaxEnabled,
                    dto.DefaultTaxRate,
                    dto.TaxName,
                    dto.PricesIncludeTax,
                    dto.MaxManualDiscountPercent,
                    dto.AllowOwnerExpenseSelfApproval),
                TryGetCurrentUserId()),
            cancellationToken);
        return Ok(ApiResponse<BusinessPosSettingsDto>.SuccessResponse(ToDto(updated), "POS settings updated"));
    }

    private BusinessPosSettingsDto ToDto(BusinessPosSettingsSnapshot settings) =>
        new(
            settings.MaxCashSessionDurationHours,
            settings.DefaultTimeZoneId,
            settings.TaxEnabled,
            settings.DefaultTaxRate,
            settings.TaxName,
            settings.PricesIncludeTax,
            settings.MaxManualDiscountPercent,
            settings.Version,
            settings.UpdatedAt,
            settings.IsPersisted,
            User.HasPermission("sales.discount.apply"),
            User.HasPermission("sales.discount.override_limit"),
            settings.AllowOwnerExpenseSelfApproval);

    private static TaxSettings ToTaxSettings(BusinessPosSettingsSnapshot settings) =>
        new()
        {
            TaxEnabled = settings.TaxEnabled,
            DefaultTaxRate = settings.DefaultTaxRate,
            TaxName = settings.TaxName,
            PricesIncludeTax = settings.PricesIncludeTax
        };

    private long? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    /// <summary>Gets receipt settings.</summary>
    [HttpGet("receipt")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ReceiptSettings>>> GetReceipt(CancellationToken cancellationToken)
    {
        if (!User.HasPermission("settings.view") && !User.HasPermission("sales.create"))
            return Forbid();
        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Receipt;
        return Ok(ApiResponse<ReceiptSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates receipt settings.</summary>
    [HttpPut("receipt")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateReceipt(
        [FromBody] ReceiptSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateReceiptSettingsCommand(settings), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Receipt settings updated"));
    }

    /// <summary>Gets invoice number settings.</summary>
    [HttpGet("invoice-number")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<InvoiceNumberSettings>>> GetInvoiceNumber(
        CancellationToken cancellationToken)
    {
        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).InvoiceNumber;
        return Ok(ApiResponse<InvoiceNumberSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates invoice number settings.</summary>
    [HttpPut("invoice-number")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateInvoiceNumber(
        [FromBody] InvoiceNumberSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Prefix))
            return BadRequest(ApiResponse<SystemSettings>.ErrorResponse("Invoice prefix is required"));

        if (settings.NextNumber < 1)
            return BadRequest(ApiResponse<SystemSettings>.ErrorResponse("NextNumber must be greater than 0"));

        if (settings.Padding < 3 || settings.Padding > 10)
            return BadRequest(ApiResponse<SystemSettings>.ErrorResponse("Padding must be between 3 and 10"));

        var result = await _sender.Send(new UpdateInvoiceNumberSettingsCommand(settings), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Invoice number settings updated"));
    }

    /// <summary>Gets inventory settings.</summary>
    [HttpGet("inventory")]
    public async Task<ActionResult<ApiResponse<InventorySettings>>> GetInventory(CancellationToken cancellationToken)
    {
        var canViewInventorySettings = User.HasPermission("settings.view")
            || User.HasPermission("inventory.view")
            || User.HasPermission("stock_adjustments.view")
            || User.HasPermission("stock_adjustments.create");

        if (!canViewInventorySettings)
        {
            return Forbid();
        }

        var settings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Inventory;
        return Ok(ApiResponse<InventorySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates inventory settings.</summary>
    [HttpPut("inventory")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateInventory(
        [FromBody] InventorySettings settings,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new UpdateInventorySettingsCommand(settings), cancellationToken);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Inventory settings updated"));
    }
}
