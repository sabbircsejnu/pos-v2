namespace RetailPOS.Application.Suppliers;

public sealed record SupplierWriteModel(
    string Name,
    string? Contact,
    string? Address,
    decimal CreditLimit);
