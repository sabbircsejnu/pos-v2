using MediatR;
using RetailPOS.Application.BankStatements.Abstractions;
using RetailPOS.Application.BankStatements.ReadModels;

namespace RetailPOS.Application.BankStatements.Commands;

public sealed record ImportBankStatementCsvCommand(
    long ActorUserId,
    long BankAccountId,
    string FileName,
    Stream Content) : IRequest<BankStatementImportReadModel>;

public sealed class ImportBankStatementCsvCommandHandler(
    IBankStatementImportCommandStore commandStore,
    IBankStatementImportTenantScope tenantScope)
    : IRequestHandler<ImportBankStatementCsvCommand, BankStatementImportReadModel>
{
    public async Task<BankStatementImportReadModel> Handle(
        ImportBankStatementCsvCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ImportCsvAsync(
            businessId,
            request.ActorUserId,
            request.BankAccountId,
            request.FileName,
            request.Content,
            cancellationToken);
    }
}
