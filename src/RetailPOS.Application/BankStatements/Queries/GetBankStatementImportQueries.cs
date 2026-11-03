using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BankStatements.Abstractions;
using RetailPOS.Application.BankStatements.ReadModels;

namespace RetailPOS.Application.BankStatements.Queries;

public sealed record GetBankStatementImportsQuery(long? BankAccountId)
    : IQuery<IReadOnlyList<BankStatementImportReadModel>>;

public sealed class GetBankStatementImportsQueryHandler(
    IBankStatementImportReadStore readStore,
    IBankStatementImportTenantScope tenantScope)
    : IRequestHandler<
        GetBankStatementImportsQuery,
        IReadOnlyList<BankStatementImportReadModel>>
{
    public async Task<IReadOnlyList<BankStatementImportReadModel>> Handle(
        GetBankStatementImportsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListImportsAsync(
            businessId,
            request.BankAccountId,
            cancellationToken);
    }
}

public sealed record GetBankStatementImportQuery(long Id)
    : IQuery<BankStatementImportReadModel?>;

public sealed class GetBankStatementImportQueryHandler(
    IBankStatementImportReadStore readStore,
    IBankStatementImportTenantScope tenantScope)
    : IRequestHandler<GetBankStatementImportQuery, BankStatementImportReadModel?>
{
    public async Task<BankStatementImportReadModel?> Handle(
        GetBankStatementImportQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetImportAsync(
            businessId,
            request.Id,
            cancellationToken);
    }
}

public sealed record GetBankStatementLinesQuery(
    long? ImportId,
    long? BankAccountId,
    DateTime? DateFrom,
    DateTime? DateTo) : IQuery<IReadOnlyList<BankStatementLineReadModel>>;

public sealed class GetBankStatementLinesQueryHandler(
    IBankStatementImportReadStore readStore,
    IBankStatementImportTenantScope tenantScope)
    : IRequestHandler<
        GetBankStatementLinesQuery,
        IReadOnlyList<BankStatementLineReadModel>>
{
    public async Task<IReadOnlyList<BankStatementLineReadModel>> Handle(
        GetBankStatementLinesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListLinesAsync(
            businessId,
            request.ImportId,
            request.BankAccountId,
            request.DateFrom,
            request.DateTo,
            cancellationToken);
    }
}
