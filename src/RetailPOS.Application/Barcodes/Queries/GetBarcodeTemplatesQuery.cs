using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.Application.Barcodes.Queries;

public sealed record GetBarcodeTemplatesQuery(bool IncludeInactive)
    : IQuery<IReadOnlyList<BarcodeTemplateReadModel>>;

public sealed class GetBarcodeTemplatesQueryHandler(
    IBarcodeTemplateReadStore store,
    IBarcodeTemplateDefaultsProvisioner defaultsProvisioner,
    IBarcodeTemplateReadScope readScope)
    : IRequestHandler<GetBarcodeTemplatesQuery, IReadOnlyList<BarcodeTemplateReadModel>>
{
    public async Task<IReadOnlyList<BarcodeTemplateReadModel>> Handle(
        GetBarcodeTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await readScope.GetAsync(cancellationToken);
        if (!scope.BusinessId.HasValue)
            throw new RetailPOS.Application.Authorization.BusinessContextRequiredException();

        var templates = await store.GetByBusinessAsync(
            scope.BusinessId.Value,
            request.IncludeInactive,
            cancellationToken);
        if (templates.Count == 0)
        {
            await defaultsProvisioner.EnsureAsync(scope.BusinessId.Value, cancellationToken);
            templates = await store.GetByBusinessAsync(
                scope.BusinessId.Value,
                request.IncludeInactive,
                cancellationToken);
        }

        return templates;
    }
}
