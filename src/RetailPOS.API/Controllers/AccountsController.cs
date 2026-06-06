using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Account;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "accounts.view")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(IAccountService accountService, ILogger<AccountsController> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    /// <summary>Get all accounts, optionally filtered by type</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AccountListDto>>> GetAll([FromQuery] string? type = null)
    {
        var result = await _accountService.GetAllAsync(type);
        return Ok(ApiResponse<AccountListDto>.SuccessResponse(result));
    }

    /// <summary>Get account by ID with computed balance</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> GetById(long id)
    {
        var account = await _accountService.GetByIdAsync(id);
        return Ok(ApiResponse<AccountDto>.SuccessResponse(account));
    }

    /// <summary>Create a new account</summary>
    [HttpPost]
    [Authorize(Policy = "accounts.create")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Create([FromBody] CreateAccountDto dto)
    {
        var account = await _accountService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = account.Id },
            ApiResponse<AccountDto>.SuccessResponse(account, "Account created successfully"));
    }

    /// <summary>Update an account</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "accounts.edit")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Update(long id, [FromBody] UpdateAccountDto dto)
    {
        var account = await _accountService.UpdateAsync(id, dto);
        return Ok(ApiResponse<AccountDto>.SuccessResponse(account, "Account updated successfully"));
    }

    /// <summary>Delete an account (only if no transactions exist)</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "accounts.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _accountService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Account deleted successfully"));
    }
}
