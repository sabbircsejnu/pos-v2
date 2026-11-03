using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Account;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Accounts.Queries;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "accounts.view")]
public class AccountsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(ISender sender, ILogger<AccountsController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>Get all accounts, optionally filtered by type</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AccountListDto>>> GetAll(
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAccountsQuery(type), cancellationToken);
        var response = new AccountListDto
        {
            Accounts = result.Accounts.Select(account => new AccountDto
            {
                Id = account.Id,
                Name = account.Name,
                Type = account.Type,
                Balance = account.Balance
            }).ToList(),
            TotalCount = result.TotalCount
        };
        return Ok(ApiResponse<AccountListDto>.SuccessResponse(response));
    }

    /// <summary>Get account by ID with computed balance</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> GetById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var account = await _sender.Send(new GetAccountByIdQuery(id), cancellationToken);
        var response = new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Balance = account.Balance
        };
        return Ok(ApiResponse<AccountDto>.SuccessResponse(response));
    }

    /// <summary>Create a new account</summary>
    [HttpPost]
    [Authorize(Policy = "accounts.create")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Create([FromBody] CreateAccountDto dto)
    {
        _logger.LogWarning("Blocked legacy Account create request. ChartAccount/Journal Engine is authoritative.");
        return Conflict(ApiResponse<AccountDto>.ErrorResponse("Legacy Account writes are disabled. Use Accounting Chart Accounts and the Journal Engine."));
    }

    /// <summary>Update an account</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "accounts.edit")]
    public async Task<ActionResult<ApiResponse<AccountDto>>> Update(long id, [FromBody] UpdateAccountDto dto)
    {
        _logger.LogWarning("Blocked legacy Account update request for account {AccountId}. ChartAccount/Journal Engine is authoritative.", id);
        return Conflict(ApiResponse<AccountDto>.ErrorResponse("Legacy Account writes are disabled. Use Accounting Chart Accounts and the Journal Engine."));
    }

    /// <summary>Delete an account (only if no transactions exist)</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "accounts.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        _logger.LogWarning("Blocked legacy Account delete request for account {AccountId}. ChartAccount/Journal Engine is authoritative.", id);
        return Conflict(ApiResponse.ErrorResponse("Legacy Account writes are disabled. Use Accounting Chart Accounts and the Journal Engine."));
    }
}
