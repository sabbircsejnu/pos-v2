using RetailPOS.Application.SalesReports;
using RetailPOS.Application.SalesReports.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SalesReportReadScopeProvider(IUserOutletAccessService outletAccess)
    : ISalesReportReadScopeProvider
{
    public async Task<SalesReportReadScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorizedOutlets = await outletAccess.GetAuthorizedOutletsAsync();
        return new SalesReportReadScope(authorizedOutlets.Outlets.Select(outlet => outlet.Id).ToArray());
    }
}
