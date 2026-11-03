using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.Application.Barcodes.Queries;

public sealed record GetBarcodeTemplateByIdQuery(long Id) : IQuery<BarcodeTemplateReadModel>;

public sealed class GetBarcodeTemplateByIdQueryHandler(
    IBarcodeTemplateReadStore store,
    IBarcodeTemplateReadScope readScope)
    : IRequestHandler<GetBarcodeTemplateByIdQuery, BarcodeTemplateReadModel>
{
    public async Task<BarcodeTemplateReadModel> Handle(
        GetBarcodeTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var accessScope = await readScope.GetAsync(cancellationToken);
        var result = await store.GetByIdAsync(request.Id, accessScope, cancellationToken);
        return result.Status switch
        {
            BarcodeTemplateLookupStatus.Found => result.Template
                ?? throw new InvalidOperationException("Found barcode template must include its read model."),
            BarcodeTemplateLookupStatus.NotFound => throw new KeyNotFoundException(
                $"Barcode template with ID {request.Id} not found"),
            BarcodeTemplateLookupStatus.BusinessScopeRequired =>
                throw new RetailPOS.Application.Authorization.BusinessContextRequiredException(),
            BarcodeTemplateLookupStatus.Forbidden => throw new UnauthorizedAccessException(
                "You are not authorized to access this barcode template."),
            _ => throw new InvalidOperationException("Unknown barcode template lookup status.")
        };
    }
}
