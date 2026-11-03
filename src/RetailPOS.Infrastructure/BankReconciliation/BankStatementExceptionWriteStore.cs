using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankStatementExceptionWriteStore(RetailPOSDbContext dbContext)
    : IBankStatementExceptionWriteStore
{
    public async Task CreateAsync(
        long statementLineId,
        long businessId,
        string classification,
        string reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var line = await dbContext.BankStatementLines.SingleOrDefaultAsync(
            statementLine => statementLine.Id == statementLineId
                && statementLine.BusinessId == businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Statement line not found.");

        dbContext.BankStatementExceptions.Add(new BankStatementException
        {
            BusinessId = line.BusinessId,
            BankAccountId = line.BankAccountId,
            BankStatementLineId = line.Id,
            Classification = classification,
            Reason = reason.Trim(),
            CreatedBy = actorUserId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResolveAsync(
        long exceptionId,
        long businessId,
        string resolutionNotes,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var exception = await dbContext.BankStatementExceptions.SingleOrDefaultAsync(
            row => row.Id == exceptionId && row.BusinessId == businessId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Statement exception not found.");
        if (exception.Status == BankStatementExceptionStatuses.Resolved)
            return;

        exception.Status = BankStatementExceptionStatuses.Resolved;
        exception.ResolutionNotes = resolutionNotes.Trim();
        exception.ResolvedBy = actorUserId;
        exception.ResolvedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
