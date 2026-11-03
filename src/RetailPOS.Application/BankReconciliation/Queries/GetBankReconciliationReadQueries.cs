using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;

namespace RetailPOS.Application.BankReconciliation.Queries;

public sealed record GetStatementCandidatesQuery(long SessionId)
    : IQuery<IReadOnlyList<ReconciliationCandidateReadModel>>;

public sealed class GetStatementCandidatesQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetStatementCandidatesQuery,
        IReadOnlyList<ReconciliationCandidateReadModel>>
{
    public async Task<IReadOnlyList<ReconciliationCandidateReadModel>> Handle(
        GetStatementCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindStatementCandidatesAsync(
            businessId,
            request.SessionId,
            cancellationToken);
    }
}

public sealed record GetGlCandidatesQuery(long SessionId)
    : IQuery<IReadOnlyList<ReconciliationCandidateReadModel>>;

public sealed class GetGlCandidatesQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetGlCandidatesQuery,
        IReadOnlyList<ReconciliationCandidateReadModel>>
{
    public async Task<IReadOnlyList<ReconciliationCandidateReadModel>> Handle(
        GetGlCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindGlCandidatesAsync(
            businessId,
            request.SessionId,
            cancellationToken);
    }
}

public sealed record GetBankReconciliationCalculationQuery(long SessionId)
    : IQuery<BankReconciliationCalculationReadModel>;

public sealed class GetBankReconciliationCalculationQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetBankReconciliationCalculationQuery,
        BankReconciliationCalculationReadModel>
{
    public async Task<BankReconciliationCalculationReadModel> Handle(
        GetBankReconciliationCalculationQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetCalculationAsync(
            businessId,
            request.SessionId,
            cancellationToken);
    }
}

public sealed record GetBankReconciliationSessionsQuery(long? BankAccountId)
    : IQuery<IReadOnlyList<BankReconciliationSessionReadModel>>;

public sealed class GetBankReconciliationSessionsQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetBankReconciliationSessionsQuery,
        IReadOnlyList<BankReconciliationSessionReadModel>>
{
    public async Task<IReadOnlyList<BankReconciliationSessionReadModel>> Handle(
        GetBankReconciliationSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListSessionsAsync(
            businessId,
            request.BankAccountId,
            cancellationToken);
    }
}

public sealed record GetActiveBankAccountsQuery
    : IQuery<IReadOnlyList<BankAccountReadModel>>;

public sealed class GetActiveBankAccountsQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<GetActiveBankAccountsQuery, IReadOnlyList<BankAccountReadModel>>
{
    public async Task<IReadOnlyList<BankAccountReadModel>> Handle(
        GetActiveBankAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindActiveBankAccountsAsync(businessId, cancellationToken);
    }
}

public sealed record GetReconciliationMatchesQuery(long SessionId)
    : IQuery<IReadOnlyList<ReconciliationMatchReadModel>>;

public sealed class GetReconciliationMatchesQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetReconciliationMatchesQuery,
        IReadOnlyList<ReconciliationMatchReadModel>>
{
    public async Task<IReadOnlyList<ReconciliationMatchReadModel>> Handle(
        GetReconciliationMatchesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindMatchesAsync(
            request.SessionId,
            businessId,
            cancellationToken);
    }
}

public sealed record GetStatementAdjustmentsQuery(long BankAccountId)
    : IQuery<IReadOnlyList<StatementAdjustmentReadModel>>;

public sealed class GetStatementAdjustmentsQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetStatementAdjustmentsQuery,
        IReadOnlyList<StatementAdjustmentReadModel>>
{
    public async Task<IReadOnlyList<StatementAdjustmentReadModel>> Handle(
        GetStatementAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindAdjustmentsAsync(
            request.BankAccountId,
            businessId,
            cancellationToken);
    }
}

public sealed record GetStatementExceptionsQuery(long BankAccountId)
    : IQuery<IReadOnlyList<StatementExceptionReadModel>>;

public sealed class GetStatementExceptionsQueryHandler(
    IBankReconciliationReadStore readStore,
    IBankReconciliationTenantScope tenantScope)
    : IRequestHandler<
        GetStatementExceptionsQuery,
        IReadOnlyList<StatementExceptionReadModel>>
{
    public async Task<IReadOnlyList<StatementExceptionReadModel>> Handle(
        GetStatementExceptionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindExceptionsAsync(
            request.BankAccountId,
            businessId,
            cancellationToken);
    }
}
