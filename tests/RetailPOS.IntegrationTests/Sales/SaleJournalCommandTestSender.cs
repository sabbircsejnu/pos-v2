using MediatR;
using Moq;
using RetailPOS.Application.AccountingJournals.Commands;

namespace RetailPOS.Tests.Sales;

internal static class SaleJournalCommandTestSender
{
    public static Mock<ISender> Create(long draftJournalId, long postedJournalId)
    {
        var sender = new Mock<ISender>();
        sender.Setup(service => service.Send(
                It.IsAny<CreateOrGetSystemJournalDraftCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(draftJournalId);
        sender.Setup(service => service.Send(
                It.IsAny<PostJournalEntryCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(postedJournalId);
        return sender;
    }
}
