using Moq;
using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.Services;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingJournals.Queries;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Tests.Purchase;

public class PurchaseAndGrnAccountingTests
{
    [Fact]
    public async Task GrnCompletion_PostsPurchaseReceiptJournal_UsingGrnUnitCost()
    {
        var po = new PurchaseOrder
        {
            Id = 300,
            SupplierId = 20,
            WarehouseId = 10,
            Supplier = new Supplier { Id = 20, BusinessId = 1, Name = "Supplier One" },
            Warehouse = new Warehouse { Id = 10, BusinessId = 1, Name = "Main WH" },
            CreatedBy = 77
        };
        var grn = new Grn
        {
            Id = 400,
            PoId = 300,
            Status = "completed",
            ReceivedDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            PurchaseOrder = po,
            Items =
            [
                new GrnItem
                {
                    Id = 1,
                    PoItemId = 21,
                    ReceivedQty = 5,
                    UnitCost = 8m,
                    TaxableAmount = 40m,
                    VatAmount = 8m
                }
            ]
        };

        var sender = new Mock<ISender>();
        sender
            .Setup(service => service.Send(
                It.IsAny<CreateOrGetSystemJournalDraftCommand>(),
                CancellationToken.None))
            .ReturnsAsync(500L);
        sender
            .Setup(service => service.Send(
                It.IsAny<PostJournalEntryCommand>(),
                CancellationToken.None))
            .ReturnsAsync(500L);
        sender
            .Setup(service => service.Send(
                It.IsAny<GetJournalEntryByIdQuery>(),
                CancellationToken.None))
            .ReturnsAsync(CreatePostedJournalReadModel());

        var configurationService = new Mock<IAccountingConfigurationService>();
        configurationService
            .Setup(service => service.ResolvePostingAccountAsync(
                It.IsAny<string>(),
                It.IsAny<long?>()))
            .ReturnsAsync((string slot, long? _) => new PostingAccountResolutionDto
            {
                Slot = slot,
                AccountId = slot switch
                {
                    PostingAccountSlots.Inventory => 1,
                    PostingAccountSlots.SupplierPayable => 2,
                    PostingAccountSlots.VatReceivable => 3,
                    _ => throw new InvalidOperationException($"Unexpected posting account slot: {slot}")
                },
                Source = "GlobalConfiguration"
            });
        configurationService
            .Setup(service => service.GetBusinessConfigurationAsync())
            .ReturnsAsync(new AccountingConfigurationDto
            {
                SupplierPayableAccountId = 2,
                DefaultSupplierPaymentTermsDays = 30
            });
        var payableWriter = new Mock<ISupplierPayableRecognitionWriter>();
        payableWriter
            .Setup(service => service.RecordFromPostedGrnAsync(
                grn,
                po,
                It.Is<SupplierPayableRecognitionJournal>(journal =>
                    journal.Id == 500
                    && journal.BusinessId == 1
                    && journal.Status == JournalEntryStatuses.Posted
                    && journal.Lines.Count == 3
                    && journal.Lines.Single(line => line.AccountId == 1).DebitAmount == 40m
                    && journal.Lines.Single(line => line.AccountId == 3).DebitAmount == 8m
                    && journal.Lines.Single(line => line.AccountId == 2).CreditAmount == 48m),
                2,
                30,
                77,
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var poster = new GrnCompletionAccountingPosterAdapter(
            sender.Object,
            configurationService.Object,
            payableWriter.Object);

        var journalEntryId = await poster.PostReceiptAndCreatePayableAsync(
            grn,
            po,
            1,
            77,
            CancellationToken.None);

        Assert.Equal(500, journalEntryId);
        sender.Verify(
            service => service.Send(
                It.Is<CreateOrGetSystemJournalDraftCommand>(command =>
                    command.BusinessId == 1
                    && command.ActorUserId == 77
                    && command.IdempotencyDiscriminator == "grn:400"
                    && command.Journal.JournalType == "PurchaseReceiptPosting"
                    && command.Journal.ReferenceType == "Grn"
                    && command.Journal.ReferenceId == "400"
                    && command.Journal.Lines.Sum(line => line.DebitAmount) == 48m
                    && command.Journal.Lines.Sum(line => line.CreditAmount) == 48m
                    && command.Journal.Lines.Any(line =>
                        line.AccountId == 3
                        && line.Description == "Input VAT"
                        && line.DebitAmount == 8m
                        && line.WarehouseId == 10)),
                CancellationToken.None),
            Times.Once);
        sender.Verify(
            service => service.Send(
                It.Is<PostJournalEntryCommand>(command =>
                    command.BusinessId == 1
                    && command.ActorUserId == 77
                    && command.JournalId == 500),
                CancellationToken.None),
            Times.Once);
        sender.Verify(
            service => service.Send(
                It.Is<GetJournalEntryByIdQuery>(query =>
                    query.BusinessId == 1
                    && query.JournalEntryId == 500),
                CancellationToken.None),
            Times.Once);
        payableWriter.Verify(
            service => service.RecordFromPostedGrnAsync(
                grn,
                po,
                It.IsAny<SupplierPayableRecognitionJournal>(),
                2,
                30,
                77,
                CancellationToken.None),
            Times.Once);

        var auditEvents = new List<AuditEventInput>();
        var auditService = new Mock<IAuditService>();
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        var auditWriter = new GrnCompletionAuditWriterAdapter(auditService.Object);
        await auditWriter.RecordCompletedAsync(
            grn,
            po,
            77,
            1,
            journalEntryId,
            CancellationToken.None);

        var completionAudit = Assert.Single(
            auditEvents,
            input => input.PrimaryEntity?.Type == "Grn"
                && input.PrimaryEntity?.Id == "400"
                && input.ActionType == AuditActionType.Approve);
        Assert.Equal(77L, completionAudit.Metadata?["actorUserId"]);
        Assert.Equal(1L, completionAudit.Metadata?["businessId"]);
        Assert.Equal(10L, completionAudit.Metadata?["warehouseId"]);
        Assert.Equal(20L, completionAudit.Metadata?["supplierId"]);
        Assert.Equal(300L, completionAudit.Metadata?["poId"]);
        Assert.Equal(500L, completionAudit.Metadata?["journalEntryId"]);
        Assert.Contains(completionAudit.Entities.Single().Fields, x => x.FieldName == "JournalEntryId" && Equals(x.NewValue, 500L));
    }

    private static JournalEntryReadModel CreatePostedJournalReadModel() =>
        new(
            500,
            1,
            "JR-500",
            "PurchaseReceiptPosting",
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 10),
            new DateTime(2026, 3, 10),
            null,
            null,
            "Grn",
            "400",
            "GRN-000400",
            "Purchase receipt posting for GRN-000400",
            null,
            10,
            null,
            null,
            JournalEntryStatuses.Posted,
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow,
            77,
            DateTime.UtcNow,
            77,
            DateTime.UtcNow,
            77,
            null,
            null,
            null,
            null,
            null,
            [
                new JournalEntryLineReadModel(
                    1,
                    1,
                    "Inventory receipt",
                    40m,
                    0,
                    null,
                    10,
                    null,
                    null,
                    null,
                    null),
                new JournalEntryLineReadModel(
                    2,
                    2,
                    "Supplier payable",
                    0,
                    48m,
                    null,
                    10,
                    null,
                    null,
                    null,
                    null),
                new JournalEntryLineReadModel(
                    3,
                    3,
                    "Input VAT",
                    8m,
                    0,
                    null,
                    10,
                    null,
                    null,
                    null,
                    null)
            ],
            []);

    [Fact]
    public async Task PurchaseReceiptJournalPoster_PostsPurchaseReceiptJournal_ForImmediateReceive()
    {
        var purchaseOrder = new PurchaseOrder
        {
            Id = 300,
            SupplierId = 20,
            WarehouseId = 10,
            CreatedBy = 77
        };
        var grn = new Grn
        {
            Id = 400,
            PoId = purchaseOrder.Id,
            ReceivedDate = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = 77,
            Items =
            [
                new GrnItem { ReceivedQty = 5, UnitCost = 7.5m }
            ]
        };
        var journalCommands = new Mock<ISender>();
        journalCommands.Setup(x => x.Send(
                It.IsAny<CreateOrGetSystemJournalDraftCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(600L);
        journalCommands.Setup(x => x.Send(
                It.IsAny<PostJournalEntryCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(600L);
        var configurationService = new Mock<IAccountingConfigurationService>();
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, null)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Inventory, AccountId = 1, Source = "GlobalConfiguration" });
        configurationService.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.SupplierPayable, null)).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.SupplierPayable, AccountId = 2, Source = "GlobalConfiguration" });
        var poster = new PurchaseReceiptJournalPoster(
            journalCommands.Object,
            configurationService.Object);

        await poster.PostAsync(grn, purchaseOrder, 1, CancellationToken.None);

        var create = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(
            journalCommands.Invocations.Single(invocation =>
                invocation.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]);
        Assert.Equal(1, create.BusinessId);
        Assert.Equal(77, create.ActorUserId);
        Assert.Equal("PurchaseReceiptPosting", create.Journal.JournalType);
        Assert.Equal(grn.ReceivedDate, create.Journal.TransactionDate);
        Assert.Equal("Grn", create.Journal.ReferenceType);
        Assert.Equal("400", create.Journal.ReferenceId);
        Assert.Equal("GRN-000400", create.Journal.ReferenceNumber);
        Assert.Equal("Purchase receipt posting for GRN-000400", create.Journal.Description);
        Assert.Equal(10, create.Journal.WarehouseId);
        Assert.Equal("grn:400", create.IdempotencyDiscriminator);
        Assert.Equal(37.5m, create.Journal.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(37.5m, create.Journal.Lines.Sum(line => line.CreditAmount));
        Assert.All(create.Journal.Lines, line => Assert.Equal(10, line.WarehouseId));
        journalCommands.Verify(
            sender => sender.Send(
                It.Is<PostJournalEntryCommand>(command =>
                    command.BusinessId == 1
                    && command.ActorUserId == 77
                    && command.JournalId == 600),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
