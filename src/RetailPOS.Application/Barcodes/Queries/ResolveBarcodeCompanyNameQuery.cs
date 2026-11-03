using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;

namespace RetailPOS.Application.Barcodes.Queries;

public sealed record ResolveBarcodeCompanyNameQuery(
    string? RequestedCompanyName,
    long? OutletId,
    long? UserId)
    : IQuery<string?>;

public sealed class ResolveBarcodeCompanyNameQueryHandler(
    IBarcodeCompanyNameReadStore readStore,
    IBarcodeTenantScope tenantScope)
    : IRequestHandler<ResolveBarcodeCompanyNameQuery, string?>
{
    public async Task<string?> Handle(
        ResolveBarcodeCompanyNameQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedCompanyName))
            return request.RequestedCompanyName.Trim();

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (businessId.HasValue)
        {
            var businessName = await readStore.FindBusinessNameAsync(
                businessId.Value,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(businessName))
                return businessName.Trim();
        }

        var outletId = request.OutletId;
        if (!outletId.HasValue && request.UserId.HasValue)
        {
            outletId = await readStore.FindUserOutletIdAsync(
                request.UserId.Value,
                cancellationToken);
        }

        if (outletId.HasValue)
        {
            var outletName = await readStore.FindOutletNameAsync(
                outletId.Value,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(outletName))
                return outletName.Trim();
        }

        return null;
    }
}
