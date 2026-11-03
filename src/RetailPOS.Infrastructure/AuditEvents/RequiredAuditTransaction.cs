using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AuditEvents;

public sealed class RequiredAuditTransaction(
    RetailPOSDbContext db,
    IAuditContext context,
    IAuditService audit,
    ILogger<RequiredAuditTransaction> logger) : IRequiredAuditTransaction
{
    public async Task<T> ExecuteAsync<T>(
        RequiredAuditOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
        => await ExecuteAsync(operation, action, _ => true, cancellationToken);

    public async Task<T> ExecuteAsync<T>(
        RequiredAuditOperation operation,
        Func<CancellationToken, Task<T>> action,
        Func<T, bool> requiresAudit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(requiresAudit);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.ActionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Summary);
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.BusinessId <= 0 || !context.IsInitialized
            || context.BusinessId != operation.BusinessId || context.CorrelationId == Guid.Empty)
            throw new UnauthorizedAccessException("Required audit needs a validated matching Business and correlation.");
        if (context.Source != AuditSource.SystemJob && context.RealUserId is not > 0)
            throw new UnauthorizedAccessException("Required audit needs an authenticated real actor.");
        if (context.Source == AuditSource.SystemJob && string.IsNullOrWhiteSpace(context.RequestPath))
            throw new UnauthorizedAccessException("Required audit needs an identified Business-scoped job.");
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("Required audit needs a relational database transaction.");
        if (db.Database.CurrentTransaction is not null || context.HasActiveScope)
            throw new InvalidOperationException("Required audit owns its transaction and scope; nested boundaries are not supported.");
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Begin the required audit boundary before staging business changes.");

        var previouslyTracked = db.ChangeTracker.Entries().Select(entry => entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        context.BeginScope(operation.Summary, operation.ActionType, operation.Module,
            operation.PrimaryEntityType, operation.PrimaryEntityId, requiresAtomicPersistence: true);
        try
        {
            var result = await action(cancellationToken);
            if (!context.HasActiveScope || !context.RequiresAtomicPersistence || context.ScopeFailed
                || context.BusinessId != operation.BusinessId)
                throw new InvalidOperationException("The required audit scope was changed or ended before commit.");
            if (!requiresAudit(result))
            {
                if (db.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("An idempotent audit replay cannot stage database changes.");
                context.EndScope();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (operation.Evidence is { Count: > 0 })
                await new AuditEventCommandStore(audit).RecordAsync(new AuditEventWriteRequest(
                    operation.ActionType, operation.Module, operation.Summary,
                    operation.PrimaryEntityType, operation.PrimaryEntityId, operation.Evidence), cancellationToken);
            await audit.FlushScopeAsync(cancellationToken);
            if (context.HasActiveScope)
                throw new InvalidOperationException("Required audit was not flushed.");
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception operationException)
        {
            context.EndScope();
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // SaveChanges accepts tracked state before transaction commit; never leave rolled-back values reusable.
                foreach (var entry in db.ChangeTracker.Entries().ToArray())
                {
                    if (!previouslyTracked.Contains(entry.Entity))
                        entry.State = EntityState.Detached;
                    else
                        await entry.ReloadAsync(CancellationToken.None);
                }
            }
            catch (Exception cleanupException)
            {
                logger.LogCritical("Required audit rollback cleanup failed for Business {BusinessId}, correlation {CorrelationId}.",
                    operation.BusinessId, context.CorrelationId);
                throw new AggregateException("Required audit operation and rollback cleanup failed.",
                    operationException, cleanupException);
            }
            logger.LogError("Required Audit v2 transaction rolled back for Business {BusinessId}, correlation {CorrelationId}.",
                operation.BusinessId, context.CorrelationId);
            throw;
        }
    }
}
