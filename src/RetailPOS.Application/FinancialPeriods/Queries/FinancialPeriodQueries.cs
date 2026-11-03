using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Queries;

public sealed record GetFinancialYearsQuery : IQuery<IReadOnlyList<FinancialYearReadModel>>;

public sealed class GetFinancialYearsQueryHandler(
    IFinancialPeriodTenantScope tenantScope,
    IFinancialPeriodReadStore readStore)
    : IRequestHandler<GetFinancialYearsQuery, IReadOnlyList<FinancialYearReadModel>>
{
    public async Task<IReadOnlyList<FinancialYearReadModel>> Handle(
        GetFinancialYearsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetFinancialYearsAsync(businessId, cancellationToken);
    }
}

public sealed record GetAccountingPeriodsQuery(long FinancialYearId)
    : IQuery<IReadOnlyList<AccountingPeriodReadModel>>;

public sealed class GetAccountingPeriodsQueryHandler(
    IFinancialPeriodTenantScope tenantScope,
    IFinancialPeriodReadStore readStore)
    : IRequestHandler<GetAccountingPeriodsQuery, IReadOnlyList<AccountingPeriodReadModel>>
{
    public async Task<IReadOnlyList<AccountingPeriodReadModel>> Handle(
        GetAccountingPeriodsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (!await readStore.FinancialYearExistsAsync(
                businessId,
                request.FinancialYearId,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Financial year with ID {request.FinancialYearId} was not found.");
        }

        return await readStore.GetPeriodsAsync(
            businessId,
            request.FinancialYearId,
            cancellationToken);
    }
}
