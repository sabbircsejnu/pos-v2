namespace RetailPOS.Application.Sales.ReadModels;

public sealed record SaleReceiptPdfReadModel(string SaleNumber, byte[] Content);
