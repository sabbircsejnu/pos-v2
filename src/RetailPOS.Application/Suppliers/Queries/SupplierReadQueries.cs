using MediatR;
using RetailPOS.Application.Suppliers.Abstractions;

namespace RetailPOS.Application.Suppliers.Queries;

public sealed record GetSuppliersQuery : IRequest<IReadOnlyList<SupplierReadModel>>;

public sealed class GetSuppliersQueryHandler(
    ISupplierReadStore store,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<GetSuppliersQuery, IReadOnlyList<SupplierReadModel>>
{
    public async Task<IReadOnlyList<SupplierReadModel>> Handle(
        GetSuppliersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetAllAsync(businessId, cancellationToken);
    }
}

public sealed record SearchSuppliersQuery(
    string? SearchQuery,
    decimal? MinCreditLimit,
    decimal? MaxCreditLimit,
    int PageNumber,
    int PageSize,
    string SortBy,
    string SortOrder) : IRequest<SupplierSearchReadModel>;

public sealed class SearchSuppliersQueryHandler(
    ISupplierReadStore store,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<SearchSuppliersQuery, SupplierSearchReadModel>
{
    public async Task<SupplierSearchReadModel> Handle(
        SearchSuppliersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.SearchAsync(
            request.SearchQuery,
            request.MinCreditLimit,
            request.MaxCreditLimit,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            businessId,
            cancellationToken);
    }
}

public sealed record GetSupplierByIdQuery(long SupplierId) : IRequest<SupplierReadModel>;

public sealed class GetSupplierByIdQueryHandler(
    ISupplierReadStore store,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<GetSupplierByIdQuery, SupplierReadModel>
{
    public async Task<SupplierReadModel> Handle(
        GetSupplierByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetByIdAsync(request.SupplierId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");
    }
}

public sealed record GetSupplierPerformanceQuery(long SupplierId)
    : IRequest<SupplierPerformanceReadModel>;

public sealed class GetSupplierPerformanceQueryHandler(
    ISupplierReadStore store,
    ISupplierReadScopeProvider scopeProvider)
    : IRequestHandler<GetSupplierPerformanceQuery, SupplierPerformanceReadModel>
{
    public async Task<SupplierPerformanceReadModel> Handle(
        GetSupplierPerformanceQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var data = await store.GetPerformanceDataAsync(
            request.SupplierId,
            businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Supplier with ID {request.SupplierId} not found");

        var creditUtilization = data.CreditLimit > 0
            ? (data.OutstandingBalance / data.CreditLimit) * 100
            : 0;
        var healthStatus = creditUtilization switch
        {
            >= 90 => "Critical",
            >= 70 => "Warning",
            _ => "Good"
        };

        return new SupplierPerformanceReadModel(
            data.Id,
            data.Name,
            data.TotalPurchaseOrders,
            data.TotalBills,
            data.TotalPurchaseAmount,
            data.OutstandingBalance,
            data.CreditLimit,
            creditUtilization,
            healthStatus);
    }
}
