using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Pos;

namespace RetailPOS.Tests.Sales;

public sealed class CashSessionOpenAuditWriterTests
{
    [Fact]
    public async Task AuditFailureIsBestEffortAfterSessionPersistence()
    {
        var auditService = new Mock<IAuditService>(MockBehavior.Strict);
        auditService
            .Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));

        var writer = new CashSessionOpenAuditWriter(
            auditService.Object,
            NullLogger<CashSessionOpenAuditWriter>.Instance);

        await writer.RecordOpenedAsync(
            new CashSessionSnapshot(
                8101,
                1,
                5001,
                8001,
                DateTime.UtcNow.Date,
                DateTime.UtcNow,
                null,
                100m,
                null,
                null,
                null,
                "open",
                7001,
                null,
                null,
                null),
            100m,
            CancellationToken.None);

        auditService.VerifyAll();
    }
}
