using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Employees;

public sealed record GetHrEmploymentTypesQuery : IRequest<IReadOnlyList<HrEmploymentTypeReadModel>>;
public sealed record SearchHrEmployeesQuery(string? Search, int Page = 1, int PageSize = 25)
    : IRequest<HrEmployeeSearchPage>;
public sealed record GetHrEmployeeProfileQuery(long EmployeeId) : IRequest<HrEmployeeProfile?>;

public sealed class GetHrEmploymentTypesQueryHandler(IHrEmployeeAccess access, IHrEmployeeStore store)
    : IRequestHandler<GetHrEmploymentTypesQuery, IReadOnlyList<HrEmploymentTypeReadModel>>
{
    public async Task<IReadOnlyList<HrEmploymentTypeReadModel>> Handle(
        GetHrEmploymentTypesQuery request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        return await store.GetEmploymentTypesAsync(businessId, cancellationToken);
    }
}

public sealed class SearchHrEmployeesQueryHandler(IHrEmployeeAccess access, IHrEmployeeStore store)
    : IRequestHandler<SearchHrEmployeesQuery, HrEmployeeSearchPage>
{
    public async Task<HrEmployeeSearchPage> Handle(
        SearchHrEmployeesQuery request, CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "Page must be positive and page size must be between 1 and 100.");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        var allowedLocations = await access.GetEmployeeSearchLocationScopeAsync(businessId, cancellationToken);
        return await store.SearchAsync(businessId, request.Search, request.Page, request.PageSize,
            allowedLocations, cancellationToken);
    }
}

public sealed class GetHrEmployeeProfileQueryHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<GetHrEmployeeProfileQuery, HrEmployeeProfile?>
{
    public async Task<HrEmployeeProfile?> Handle(
        GetHrEmployeeProfileQuery request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.EmployeeId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var fieldAccess = access.GetFieldAccess();
        if (!fieldAccess.CanViewSensitive && !fieldAccess.CanViewIdentity)
            return await store.GetProfileAsync(businessId, request.EmployeeId, fieldAccess, cancellationToken);

        access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "view_sensitive", "hr",
                "Read confidential employee profile", "HrEmployee", request.EmployeeId.ToString()),
            token => store.GetProfileAsync(businessId, request.EmployeeId, fieldAccess, token),
            result => result is not null,
            cancellationToken);
    }
}
