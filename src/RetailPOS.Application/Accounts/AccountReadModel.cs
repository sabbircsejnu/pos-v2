namespace RetailPOS.Application.Accounts;

public sealed record AccountReadModel(
    long Id,
    string Name,
    string Type,
    decimal Balance);

public sealed record AccountListReadModel(
    IReadOnlyList<AccountReadModel> Accounts,
    int TotalCount);
