namespace RetailPOS.Application.Grns.ReadModels;

public sealed record GrnSearchReadModel(
    IReadOnlyList<GrnReadModel> Grns,
    int TotalCount,
    int PageNumber,
    int PageSize);
