using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.API.Settings;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Gets all system settings.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> GetAll()
    {
        var settings = await _settingsService.GetAllSettingsAsync();
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(settings));
    }

    /// <summary>Gets company settings.</summary>
    [HttpGet("company")]
    public async Task<ActionResult<ApiResponse<CompanySettings>>> GetCompany()
    {
        var settings = await _settingsService.GetCompanySettingsAsync();
        return Ok(ApiResponse<CompanySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates company settings.</summary>
    [HttpPut("company")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateCompany([FromBody] CompanySettings settings)
    {
        var result = await _settingsService.UpdateCompanySettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Company settings updated"));
    }

    /// <summary>Gets tax settings.</summary>
    [HttpGet("tax")]
    public async Task<ActionResult<ApiResponse<TaxSettings>>> GetTax()
    {
        var settings = await _settingsService.GetTaxSettingsAsync();
        return Ok(ApiResponse<TaxSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates tax settings.</summary>
    [HttpPut("tax")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateTax([FromBody] TaxSettings settings)
    {
        var result = await _settingsService.UpdateTaxSettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Tax settings updated"));
    }

    /// <summary>Gets receipt settings.</summary>
    [HttpGet("receipt")]
    public async Task<ActionResult<ApiResponse<ReceiptSettings>>> GetReceipt()
    {
        var settings = await _settingsService.GetReceiptSettingsAsync();
        return Ok(ApiResponse<ReceiptSettings>.SuccessResponse(settings));
    }

    /// <summary>Updates receipt settings.</summary>
    [HttpPut("receipt")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateReceipt([FromBody] ReceiptSettings settings)
    {
        var result = await _settingsService.UpdateReceiptSettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Receipt settings updated"));
    }

    /// <summary>Gets inventory settings.</summary>
    [HttpGet("inventory")]
    public async Task<ActionResult<ApiResponse<InventorySettings>>> GetInventory()
    {
        var settings = await _settingsService.GetInventorySettingsAsync();
        return Ok(ApiResponse<InventorySettings>.SuccessResponse(settings));
    }

    /// <summary>Updates inventory settings.</summary>
    [HttpPut("inventory")]
    public async Task<ActionResult<ApiResponse<SystemSettings>>> UpdateInventory([FromBody] InventorySettings settings)
    {
        var result = await _settingsService.UpdateInventorySettingsAsync(settings);
        return Ok(ApiResponse<SystemSettings>.SuccessResponse(result, "Inventory settings updated"));
    }
}
