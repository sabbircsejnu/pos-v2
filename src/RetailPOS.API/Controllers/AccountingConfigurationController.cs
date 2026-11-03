using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.AccountingConfiguration.Commands;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/configuration")]
public class AccountingConfigurationController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public AccountingConfigurationController(
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _sender = sender;
    }

    [HttpGet]
    [Authorize(Policy = "accounting.configuration.view")]
    public async Task<ActionResult<ApiResponse<AccountingConfigurationDto>>> GetBusinessConfiguration(
        CancellationToken cancellationToken)
    {
        var configuration = await _sender.Send(new GetBusinessConfigurationQuery(), cancellationToken);
        var result = AccountingConfigurationDtoMapper.ToDto(configuration);
        return Ok(ApiResponse<AccountingConfigurationDto>.SuccessResponse(result));
    }

    [HttpPut]
    [Authorize(Policy = "accounting.configuration.edit")]
    public async Task<ActionResult<ApiResponse<AccountingConfigurationDto>>> UpdateBusinessConfiguration(
        [FromBody] AccountingConfigurationDto dto,
        CancellationToken cancellationToken)
    {
        var actorUserId = TryGetCurrentUserId();
        var configuration = await _sender.Send(
            new UpdateBusinessAccountingConfigurationCommand(
                AccountingConfigurationDtoMapper.ToEntity(dto),
                actorUserId),
            cancellationToken);
        var result = AccountingConfigurationDtoMapper.ToDto(configuration);
        return Ok(ApiResponse<AccountingConfigurationDto>.SuccessResponse(result, "Accounting configuration updated successfully"));
    }

    [HttpGet("outlets")]
    [Authorize(Policy = "accounting.configuration.view")]
    public async Task<ActionResult<ApiResponse<List<AccountingOutletConfigurationDto>>>> GetOutletConfigurations(
        CancellationToken cancellationToken)
    {
        var configurations = await _sender.Send(new GetAccountingOutletConfigurationsQuery(), cancellationToken);
        var result = configurations
            .Select(AccountingConfigurationDtoMapper.ToDto)
            .ToList();
        return Ok(ApiResponse<List<AccountingOutletConfigurationDto>>.SuccessResponse(result));
    }

    [HttpGet("outlets/{outletId:long}")]
    [Authorize(Policy = "accounting.configuration.view")]
    public async Task<ActionResult<ApiResponse<AccountingOutletConfigurationDto>>> GetOutletConfiguration(
        long outletId,
        CancellationToken cancellationToken)
    {
        await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var configuration = await _sender.Send(
            new GetAccountingOutletConfigurationQuery(outletId),
            cancellationToken);
        if (configuration is null)
        {
            return NotFound(ApiResponse<AccountingOutletConfigurationDto>.ErrorResponse("Outlet configuration not found"));
        }

        var result = AccountingConfigurationDtoMapper.ToDto(configuration);
        return Ok(ApiResponse<AccountingOutletConfigurationDto>.SuccessResponse(result));
    }

    [HttpPut("outlets/{outletId:long}")]
    [Authorize(Policy = "accounting.configuration.edit")]
    public async Task<ActionResult<ApiResponse<AccountingOutletConfigurationDto>>> UpsertOutletConfiguration(
        long outletId,
        [FromBody] AccountingOutletConfigurationDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.OutletId != outletId)
        {
            return BadRequest(ApiResponse<AccountingOutletConfigurationDto>.ErrorResponse("Outlet ID in route must match request body."));
        }

        var enforcedOutletId = await _outletAccess.EnforceWriteOutletAsync(outletId);
        dto.OutletId = enforcedOutletId;

        var actorUserId = TryGetCurrentUserId();
        var configuration = await _sender.Send(
            new UpsertAccountingOutletConfigurationCommand(
                AccountingConfigurationDtoMapper.ToOutletEntity(dto),
                actorUserId),
            cancellationToken);
        var result = AccountingConfigurationDtoMapper.ToDto(configuration);
        return Ok(ApiResponse<AccountingOutletConfigurationDto>.SuccessResponse(result, "Outlet accounting configuration updated successfully"));
    }

    [HttpGet("resolve")]
    [Authorize(Policy = "accounting.configuration.view")]
    public async Task<ActionResult<ApiResponse<PostingAccountResolutionDto>>> ResolvePostingAccount(
        [FromQuery] string slot,
        [FromQuery] long? outletId = null,
        CancellationToken cancellationToken = default)
    {
        if (outletId.HasValue)
        {
            await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId.Value);
        }

        var resolution = await _sender.Send(
            new ResolvePostingAccountQuery(slot, outletId),
            cancellationToken);
        var result = new PostingAccountResolutionDto
        {
            Slot = resolution.Slot,
            AccountId = resolution.AccountId,
            OutletId = resolution.OutletId,
            Source = resolution.Source
        };
        return Ok(ApiResponse<PostingAccountResolutionDto>.SuccessResponse(result));
    }

    private long? TryGetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out var userId) ? userId : null;
    }
}
