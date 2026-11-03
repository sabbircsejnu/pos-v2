using System.Globalization;
using System.Security.Cryptography;
using MediatR;
using RetailPOS.Application.Products.Abstractions;

namespace RetailPOS.Application.Products.Commands;

public sealed record GenerateProductBarcodeCommand(long? CategoryId) : IRequest<string>;

public sealed class GenerateProductBarcodeCommandHandler(IProductCreateStore store)
    : IRequestHandler<GenerateProductBarcodeCommand, string>
{
    private const int MaxAttempts = 100;

    public async Task<string> Handle(
        GenerateProductBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        var categoryCode = ((int)Math.Abs(request.CategoryId.GetValueOrDefault() % 1000))
            .ToString("D3", CultureInfo.InvariantCulture);
        var prefix = $"880{categoryCode}";

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var base12 = $"{prefix}{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
            var barcode = base12 + CalculateEan13CheckDigit(base12);
            if (!await store.BarcodeExistsAsync(barcode, cancellationToken))
            {
                return barcode;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique barcode. Please try again.");
    }

    private static int CalculateEan13CheckDigit(string base12)
    {
        var sum = 0;
        for (var index = 0; index < base12.Length; index++)
        {
            var digit = base12[index] - '0';
            sum += index % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }
}
