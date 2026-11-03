using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Queries;

public sealed record GetProductBySkuQuery(string Sku) : IQuery<ProductReadModel?>;

public sealed class GetProductBySkuQueryHandler(IProductReadStore store)
    : IRequestHandler<GetProductBySkuQuery, ProductReadModel?>
{
    public Task<ProductReadModel?> Handle(
        GetProductBySkuQuery request,
        CancellationToken cancellationToken) =>
        store.FindBySkuAsync(request.Sku, cancellationToken);
}

public sealed record GetProductByBarcodeQuery(string Barcode) : IQuery<ProductReadModel?>;

public sealed class GetProductByBarcodeQueryHandler(IProductReadStore store)
    : IRequestHandler<GetProductByBarcodeQuery, ProductReadModel?>
{
    public Task<ProductReadModel?> Handle(
        GetProductByBarcodeQuery request,
        CancellationToken cancellationToken) =>
        store.FindByBarcodeAsync(request.Barcode, cancellationToken);
}
