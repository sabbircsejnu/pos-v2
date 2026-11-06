using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.SupplierPayments;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingJournals.Queries;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

internal static class SupplierPaymentCreationTestHarness
{
    public static Task<SupplierAdvanceReadModel> SubmitAdvanceAsync(
        RetailPOSDbContext dbContext,
        long advanceId,
        long actorUserId,
        IAuditService auditService) =>
        new SubmitSupplierAdvanceCommandHandler(
            new SupplierAdvanceSubmissionStore(dbContext),
            new TestAuthorizer(),
            new SupplierPaymentCreationAuditWriter(auditService))
            .Handle(
                new SubmitSupplierAdvanceCommand(advanceId, actorUserId),
                CancellationToken.None);

    public static Task<SupplierAdvanceReadModel> CreateAdvanceAsync(
        RetailPOSDbContext dbContext,
        CreateSupplierAdvanceDto dto,
        long actorUserId,
        IAuditService auditService) =>
        new CreateSupplierAdvanceCommandHandler(
            new SupplierAdvanceCreationStore(dbContext),
            new TestAuthorizer(),
            new SupplierPaymentCreationAuditWriter(auditService))
            .Handle(
                new CreateSupplierAdvanceCommand(
                    dto.SupplierId,
                    dto.OutletId,
                    dto.PaymentDate,
                    dto.PaymentMethod,
                    dto.PaymentAccountId,
                    dto.Amount,
                    dto.ReferenceNumber,
                    dto.Notes,
                    actorUserId),
                CancellationToken.None);

    public static Task<SupplierPaymentReadModel> CreateAsync(
        RetailPOSDbContext dbContext,
        CreateSupplierPaymentDto dto,
        long actorUserId,
        IAuditService auditService) =>
        new CreateSupplierPaymentCommandHandler(
            new SupplierPaymentCreationStore(dbContext),
            new TestAuthorizer(),
            new SupplierPaymentCreationAuditWriter(auditService))
            .Handle(
                new CreateSupplierPaymentCommand(
                    dto.SupplierId,
                    dto.OutletId,
                    dto.PaymentDate,
                    dto.PaymentMethod,
                    dto.PaymentAccountId,
                    dto.Amount,
                    dto.ReferenceNumber,
                    dto.Notes,
                    dto.Allocations?.Select(line => new CreateSupplierPaymentAllocationCommand(
                        line.BillId,
                        line.SupplierPayableDocumentId,
                        line.Amount)).ToArray(),
                    actorUserId),
                CancellationToken.None);

    public static Task<SupplierPaymentReadModel> SubmitAsync(
        RetailPOSDbContext dbContext,
        long paymentId,
        long actorUserId,
        IAuditService auditService) =>
        new SubmitSupplierPaymentCommandHandler(
            new SupplierPaymentCreationStore(dbContext),
            new TestAuthorizer(),
            new SupplierPaymentCreationAuditWriter(auditService))
            .Handle(
                new SubmitSupplierPaymentCommand(paymentId, actorUserId),
                CancellationToken.None);

    public static Task<SupplierPaymentReadModel> ApproveAsync(
        RetailPOSDbContext dbContext,
        long paymentId,
        long actorUserId,
        IAuditService auditService,
        IAccountingConfigurationService configurationService,
        IJournalNumberAllocator journalNumberAllocator) =>
        new ApproveSupplierPaymentCommandHandler(
            new SupplierPaymentApprovalStore(
                dbContext,
                new SupplierPaymentApprovalJournalPoster(
                    CreateJournalCommandSender(
                        new ManualJournalDraftStore(
                            dbContext,
                            auditService,
                            journalNumberAllocator,
                            NullLogger<ManualJournalDraftStore>.Instance)),
                    configurationService),
                new SupplierPaymentCreationAuditWriter(auditService)),
            new TestAuthorizer())
            .Handle(
                new ApproveSupplierPaymentCommand(paymentId, actorUserId),
                CancellationToken.None);

    public static Task<SupplierAdvanceReadModel> ApproveAdvanceAsync(
        RetailPOSDbContext dbContext,
        long advanceId,
        long actorUserId,
        IAuditService auditService,
        IAccountingConfigurationService configurationService,
        IJournalNumberAllocator journalNumberAllocator) =>
        new ApproveSupplierAdvanceCommandHandler(
            new SupplierAdvanceApprovalStore(
                dbContext,
                new SupplierAdvanceApprovalJournalPoster(
                    CreateJournalCommandSender(
                        new ManualJournalDraftStore(
                            dbContext,
                            auditService,
                            journalNumberAllocator,
                            NullLogger<ManualJournalDraftStore>.Instance)),
                    configurationService),
                new SupplierPaymentCreationAuditWriter(auditService)),
            new TestAuthorizer())
            .Handle(
                new ApproveSupplierAdvanceCommand(advanceId, actorUserId),
                CancellationToken.None);

    public static Task ApplyAdvanceAsync(
        RetailPOSDbContext dbContext,
        long advanceId,
        ApplySupplierAdvanceDto dto,
        long actorUserId,
        IAuditService auditService,
        IAccountingConfigurationService configurationService,
        IJournalNumberAllocator journalNumberAllocator) =>
        ApplyAdvanceAsync(
            dbContext,
            advanceId,
            dto,
            actorUserId,
            auditService,
            configurationService,
            journalNumberAllocator,
            CancellationToken.None);

    private static Task ApplyAdvanceAsync(
        RetailPOSDbContext dbContext,
        long advanceId,
        ApplySupplierAdvanceDto dto,
        long actorUserId,
        IAuditService auditService,
        IAccountingConfigurationService configurationService,
        IJournalNumberAllocator journalNumberAllocator,
        CancellationToken cancellationToken)
    {
        var journalStore = new ManualJournalDraftStore(
            dbContext,
            auditService,
            journalNumberAllocator,
            NullLogger<ManualJournalDraftStore>.Instance);
        return new ApplySupplierAdvanceCommandHandler(
            new SupplierAdvanceUtilizationStore(
                dbContext,
                new SupplierAdvanceUtilizationJournalPoster(
                    CreateJournalCommandSender(journalStore),
                    configurationService),
                new SupplierPaymentReversalJournalPoster(journalStore),
                new SupplierPaymentCreationAuditWriter(auditService)),
            new TestAuthorizer())
            .Handle(
                new ApplySupplierAdvanceCommand(
                    advanceId,
                    dto.SupplierPayableDocumentId,
                    dto.Amount,
                    dto.EffectiveDate,
                    dto.ReferenceNumber,
                    dto.Notes,
                    actorUserId),
                cancellationToken);
    }

    public static ISender CreateJournalCommandSender(
        IManualJournalDraftStore journalStore,
        IJournalEntryReadStore? journalReadStore = null)
    {
        var createHandler = new CreateOrGetSystemJournalDraftCommandHandler(journalStore);
        var postHandler = new PostJournalEntryCommandHandler(journalStore);
        var reverseHandler = new ReverseJournalEntryCommandHandler(journalStore, TestCompanySettings.Provider());
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(
                It.IsAny<CreateOrGetSystemJournalDraftCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns((CreateOrGetSystemJournalDraftCommand command, CancellationToken ct) =>
                createHandler.Handle(command, ct));
        sender.Setup(value => value.Send(
                It.IsAny<PostJournalEntryCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns((PostJournalEntryCommand command, CancellationToken ct) =>
                postHandler.Handle(command, ct));
        sender.Setup(value => value.Send(
                It.IsAny<ReverseJournalEntryCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns((ReverseJournalEntryCommand command, CancellationToken ct) =>
                reverseHandler.Handle(command, ct));
        if (journalReadStore is not null)
        {
            var getHandler = new GetJournalEntryByIdQueryHandler(journalReadStore);
            sender.Setup(value => value.Send(
                    It.IsAny<GetJournalEntryByIdQuery>(),
                    It.IsAny<CancellationToken>()))
                .Returns((GetJournalEntryByIdQuery query, CancellationToken ct) =>
                    getHandler.Handle(query, ct));
        }
        return sender.Object;
    }

    public static Task<SupplierPaymentReadModel> ReverseAsync(
        RetailPOSDbContext dbContext,
        long paymentId,
        ReverseSupplierPaymentDto dto,
        long actorUserId,
        IAuditService auditService,
        IJournalNumberAllocator journalNumberAllocator) =>
        new ReverseSupplierPaymentCommandHandler(
            new SupplierPaymentReversalStore(
                dbContext,
                new SupplierPaymentReversalJournalPoster(
                    new ManualJournalDraftStore(
                        dbContext,
                        auditService,
                        journalNumberAllocator,
                        NullLogger<ManualJournalDraftStore>.Instance)),
                new SupplierPaymentCreationAuditWriter(auditService)),
            new TestAuthorizer())
            .Handle(
                new ReverseSupplierPaymentCommand(
                    paymentId,
                    dto.PostingDate,
                    dto.Reason,
                    actorUserId),
                CancellationToken.None);

    private sealed class TestAuthorizer :
        ISupplierPaymentCreationAuthorizer,
        ISupplierPaymentWorkflowAuthorizer
    {
        public Task<SupplierPaymentCreationScope> AuthorizeAsync(
            long? outletId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SupplierPaymentCreationScope(1, outletId));

        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(1L);

        public Task<long?> EnforceOutletAsync(
            long? outletId,
            CancellationToken cancellationToken) =>
            Task.FromResult(outletId);
    }
}
