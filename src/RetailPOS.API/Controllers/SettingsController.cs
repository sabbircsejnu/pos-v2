using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.API.Settings;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Gets all system settings.</summary>
    [HttpGet]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> GetAll()
    {
        var settings = await _settingsService.GetAllSettingsAsync();
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(settings));
    }

    /// <summary>Gets company settings.</summary>
    [HttpGet("company")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<CompanySettings>>> GetCompany()
    {
        var settings = await _settingsService.GetCompanySettingsAsync();
        return Ok(ApiResponse<CompanySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates company settings.</summary>
    [HttpPut("company")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateCompany([FromBody] CompanySettings settings)
    {
        var result = await _settingsService.UpdateCompanySettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Company settings updated"));
    }

    /// <summary>Gets currency settings.</summary>
    [HttpGet("currency")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CurrencySettings>>> GetCurrency()
    {
        var settings = await _settingsService.GetCurrencySettingsAsync();
        return Ok(ApiResponse<CurrencySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates currency settings.</summary>
    [HttpPut("currency")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateCurrency([FromBody] CurrencySettings settings)
    {
        var validation = ValidateCurrency(settings);
        if (validation != null) return BadRequest(validation);

        var result = await _settingsService.UpdateCurrencySettingsAsync(settings);
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

    /// <summary>Gets tax settings.</summary>
    [HttpGet("tax")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<TaxSettings>>> GetTax()
    {
        var settings = await _settingsService.GetTaxSettingsAsync();
        return Ok(ApiResponse<TaxSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates tax settings.</summary>
    [HttpPut("tax")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateTax([FromBody] TaxSettings settings)
    {
        var result = await _settingsService.UpdateTaxSettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Tax settings updated"));
    }

    /// <summary>Gets receipt settings.</summary>
    [HttpGet("receipt")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<ReceiptSettings>>> GetReceipt()
    {
        var settings = await _settingsService.GetReceiptSettingsAsync();
        return Ok(ApiResponse<ReceiptSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates receipt settings.</summary>
    [HttpPut("receipt")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateReceipt([FromBody] ReceiptSettings settings)
    {
        var result = await _settingsService.UpdateReceiptSettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Receipt settings updated"));
    }

    /// <summary>Gets inventory settings.</summary>
    [HttpGet("inventory")]
    public async Task<ActionResult<ApiResponse<InventorySettings>>> GetInventory()
    {
        var canViewInventorySettings = User.HasPermission("settings.view")
            || User.HasPermission("inventory.view")
            || User.HasPermission("stock_adjustments.view")
            || User.HasPermission("stock_adjustments.create");

        if (!canViewInventorySettings)
        {
            return Forbid();
        }

        var settings = await _settingsService.GetInventorySettingsAsync();
        return Ok(ApiResponse<InventorySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates inventory settings.</summary>
    [HttpPut("inventory")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateInventory([FromBody] InventorySettings settings)
    {
        var result = await _settingsService.UpdateInventorySettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Inventory settings updated"));
    }
}
