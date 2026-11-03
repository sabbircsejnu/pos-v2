using MediatR;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record GenerateProductSkuCommand(string ProductName) : IRequest<string>;

public sealed class GenerateProductSkuCommandHandler(IProductSkuStore store)
    : IRequestHandler<GenerateProductSkuCommand, string>
{
    public async Task<string> Handle(
        GenerateProductSkuCommand request,
        CancellationToken cancellationToken)
    {
        var prefix = new string(request.ProductName
            .ToUpper()
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .ToArray());
        if (string.IsNullOrEmpty(prefix))
        {
            prefix = "PRD";
        }

        prefix = prefix.PadRight(3, 'X');
        var originalSku = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sku = originalSku;
        var counter = 1;
        while (await store.SkuExistsAsync(sku, cancellationToken))
        {
            sku = $"{originalSku}-{counter}";
            counter++;
        }

        return sku;
    }
}
