using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Identity.LocationAccess;

namespace RetailPOS.API.Services;

public sealed class ExpenseLocationAuthorizer(IUserOutletAccessService locationAccess) : IExpenseLocationAuthorizer
{
    private ExpenseLocationScope? _scope;

    public async Task<ExpenseLocationScope> GetScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_scope is not null)
            return _scope;

        var authorized = await locationAccess.GetAuthorizedOutletsAsync(LocationAccessPolicy.ExpenseOperation);
        _scope = new ExpenseLocationScope(
            authorized.IsGlobalAccess,
            authorized.Outlets.Select(location => location.Id).ToList(),
            authorized.Warehouses.Select(location => location.Id).ToList());
        return _scope;
    }

    public async Task AuthorizeAsync(long? outletId, long? warehouseId, CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        if (!scope.Allows(outletId, warehouseId))
        {
            // The location can come from a stored expense, so the refusal must not name it.
            throw new UnauthorizedAccessException("You are not authorized for this expense location.");
        }
    }
}
