using MediatR;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Commands;

public sealed record CreateFinancialYearCommand(
    string YearCode,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    bool IsCurrent,
    long ActorUserId) : IRequest<FinancialYearReadModel>;

public sealed class CreateFinancialYearCommandHandler(
    IFinancialYearCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<CreateFinancialYearCommand, FinancialYearReadModel>
{
    public async Task<FinancialYearReadModel> Handle(
        CreateFinancialYearCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.CreateAsync(businessId, request, cancellationToken);
    }
}

public sealed record UpdateFinancialYearCommand(
    long Id,
    string YearCode,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    long ActorUserId) : IRequest<FinancialYearReadModel>;

public sealed class UpdateFinancialYearCommandHandler(
    IFinancialYearCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<UpdateFinancialYearCommand, FinancialYearReadModel>
{
    public async Task<FinancialYearReadModel> Handle(
        UpdateFinancialYearCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.UpdateAsync(businessId, request, cancellationToken);
    }
}

public sealed record ActivateFinancialYearCommand(long Id, long ActorUserId)
    : IRequest<FinancialYearReadModel>;

public sealed class ActivateFinancialYearCommandHandler(
    IFinancialYearCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<ActivateFinancialYearCommand, FinancialYearReadModel>
{
    public async Task<FinancialYearReadModel> Handle(
        ActivateFinancialYearCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ActivateAsync(businessId, request, cancellationToken);
    }
}
