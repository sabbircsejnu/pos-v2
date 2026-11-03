using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingConfiguration.Queries;

namespace RetailPOS.API.Services;

public sealed class AccountingConfigurationService(ISender sender)
    : IAccountingConfigurationService
{
    public async Task<AccountingConfigurationDto> GetBusinessConfigurationAsync()
    {
        var configuration = await sender.Send(
            new GetBusinessConfigurationQuery(),
            CancellationToken.None);
        return AccountingConfigurationDtoMapper.ToDto(configuration);
    }

    public async Task<PostingAccountResolutionDto> ResolvePostingAccountAsync(
        string slot,
        long? outletId = null)
    {
        var resolution = await sender.Send(
            new ResolvePostingAccountQuery(slot, outletId),
            CancellationToken.None);
        return new PostingAccountResolutionDto
        {
            Slot = resolution.Slot,
            AccountId = resolution.AccountId,
            OutletId = resolution.OutletId,
            Source = resolution.Source
        };
    }
}
