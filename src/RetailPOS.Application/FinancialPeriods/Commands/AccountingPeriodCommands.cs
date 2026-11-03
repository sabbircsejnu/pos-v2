using MediatR;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.ReadModels;

namespace RetailPOS.Application.FinancialPeriods.Commands;

public sealed record CreateAccountingPeriodCommand(
    long FinancialYearId,
    int PeriodNumber,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    long ActorUserId) : IRequest<AccountingPeriodReadModel>;

public sealed class CreateAccountingPeriodCommandHandler(
    IAccountingPeriodCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<CreateAccountingPeriodCommand, AccountingPeriodReadModel>
{
    public async Task<AccountingPeriodReadModel> Handle(
        CreateAccountingPeriodCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.CreateAsync(businessId, request, cancellationToken);
    }
}

public sealed record UpdateAccountingPeriodCommand(
    long Id,
    int PeriodNumber,
    string Name,
    DateTime StartDate,
    DateTime EndDate,
    long ActorUserId) : IRequest<AccountingPeriodReadModel>;

public sealed class UpdateAccountingPeriodCommandHandler(
    IAccountingPeriodCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<UpdateAccountingPeriodCommand, AccountingPeriodReadModel>
{
    public async Task<AccountingPeriodReadModel> Handle(
        UpdateAccountingPeriodCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.UpdateAsync(businessId, request, cancellationToken);
    }
}

public sealed record UpdateAccountingPeriodStatusCommand(
    long Id,
    string Status,
    string? Reason,
    long ActorUserId) : IRequest<AccountingPeriodReadModel>;

public sealed class UpdateAccountingPeriodStatusCommandHandler(
    IAccountingPeriodCommandStore commandStore,
    IFinancialPeriodTenantScope tenantScope)
    : IRequestHandler<UpdateAccountingPeriodStatusCommand, AccountingPeriodReadModel>
{
    public async Task<AccountingPeriodReadModel> Handle(
        UpdateAccountingPeriodStatusCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.UpdateStatusAsync(businessId, request, cancellationToken);
    }
}
