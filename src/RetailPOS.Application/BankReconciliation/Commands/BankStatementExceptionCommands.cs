using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.BankReconciliation.Commands;

public sealed record CreateBankStatementExceptionCommand(
    long StatementLineId,
    string Classification,
    string Reason,
    long ActorUserId) : ICommand;

public sealed class CreateBankStatementExceptionCommandHandler(
    IBankStatementExceptionWriteStore writeStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<CreateBankStatementExceptionCommand>
{
    public async Task Handle(
        CreateBankStatementExceptionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Classification is not (
            BankStatementExceptionClassifications.Chargeback
            or BankStatementExceptionClassifications.FailedSettlement))
        {
            throw new InvalidOperationException("Unsupported exception classification.");
        }

        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await writeStore.CreateAsync(
            request.StatementLineId,
            businessId,
            request.Classification,
            request.Reason,
            request.ActorUserId,
            cancellationToken);
    }
}

public sealed record ResolveBankStatementExceptionCommand(
    long ExceptionId,
    string ResolutionNotes,
    long ActorUserId) : ICommand;

public sealed class ResolveBankStatementExceptionCommandHandler(
    IBankStatementExceptionWriteStore writeStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<ResolveBankStatementExceptionCommand>
{
    public async Task Handle(
        ResolveBankStatementExceptionCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        await writeStore.ResolveAsync(
            request.ExceptionId,
            businessId,
            request.ResolutionNotes,
            request.ActorUserId,
            cancellationToken);
    }
}
