namespace RetailPOS.Application.ChartAccounts;

public sealed record ChartAccountReadModel(
    long Id,
    string AccountCode,
    string AccountName,
    string AccountType,
    string NormalBalanceType,
    long? ParentAccountId,
    bool IsSystemAccount,
    bool IsControlAccount,
    bool AllowManualPosting,
    bool IsActive,
    int DisplayOrder);
