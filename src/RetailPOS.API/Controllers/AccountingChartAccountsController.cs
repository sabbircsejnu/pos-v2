using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.Application.ChartAccounts.Queries;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/chart-accounts")]
public class AccountingChartAccountsController : ControllerBase
{
    private readonly ISender _sender;

    public AccountingChartAccountsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [Authorize(Policy = "accounting.chart_accounts.view")]
    public async Task<ActionResult<ApiResponse<List<ChartAccountDto>>>> GetAll(
        [FromQuery] string? query,
        [FromQuery] string? accountType,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var rows = await _sender.Send(
            new GetChartAccountsQuery(query, accountType, isActive),
            cancellationToken);

        return Ok(ApiResponse<List<ChartAccountDto>>.SuccessResponse(rows.Select(Map).ToList()));
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "accounting.chart_accounts.view")]
    public async Task<ActionResult<ApiResponse<ChartAccountDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetChartAccountByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<ChartAccountDto>.SuccessResponse(Map(result)));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.chart_accounts.create")]
    public async Task<ActionResult<ApiResponse<ChartAccountDto>>> Create(
        [FromBody] CreateChartAccountDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateChartAccountCommand(
                dto.AccountCode,
                dto.AccountName,
                dto.AccountType,
                dto.NormalBalanceType,
                dto.ParentAccountId,
                dto.IsControlAccount,
                dto.AllowManualPosting,
                dto.IsActive,
                dto.DisplayOrder,
                TryGetCurrentUserId(),
                dto.IsExpensePaymentAccount),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            ApiResponse<ChartAccountDto>.SuccessResponse(Map(result), "Chart account created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "accounting.chart_accounts.edit")]
    public async Task<ActionResult<ApiResponse<ChartAccountDto>>> Update(
        long id,
        [FromBody] UpdateChartAccountDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdateChartAccountCommand(
                id,
                dto.AccountName,
                dto.ParentAccountId,
                dto.IsControlAccount,
                dto.AllowManualPosting,
                dto.IsActive,
                dto.DisplayOrder,
                TryGetCurrentUserId(),
                dto.IsExpensePaymentAccount),
            cancellationToken);
        return Ok(ApiResponse<ChartAccountDto>.SuccessResponse(Map(result), "Chart account updated successfully"));
    }

    [HttpPost("{id:long}/deactivate")]
    [Authorize(Policy = "accounting.chart_accounts.edit")]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate(
        long id,
        CancellationToken cancellationToken)
    {
        var changed = await _sender.Send(
            new DeactivateChartAccountCommand(id, TryGetCurrentUserId()),
            cancellationToken);
        if (!changed)
        {
            return Ok(ApiResponse<object>.SuccessResponse("Chart account is already inactive"));
        }

        return Ok(ApiResponse<object>.SuccessResponse("Chart account deactivated successfully"));
    }

    private long? TryGetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out var userId) ? userId : null;
    }

    private static ChartAccountDto Map(RetailPOS.Application.ChartAccounts.ChartAccountReadModel account) =>
        new()
        {
            Id = account.Id,
            AccountCode = account.AccountCode,
            AccountName = account.AccountName,
            AccountType = account.AccountType,
            NormalBalanceType = account.NormalBalanceType,
            ParentAccountId = account.ParentAccountId,
            IsSystemAccount = account.IsSystemAccount,
            IsControlAccount = account.IsControlAccount,
            AllowManualPosting = account.AllowManualPosting,
            IsActive = account.IsActive,
            IsExpensePaymentAccount = account.IsExpensePaymentAccount,
            DisplayOrder = account.DisplayOrder
        };
}
