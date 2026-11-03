using System.Text.Json;
using Moq;
using RabbitMQ.Client;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Integration;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class RabbitMqDeliveryContractTests
{
    [Fact]
    public async Task PublishIsMandatoryPersistentAndCarriesOriginalIdentity()
    {
        var payload = "{\"businessId\":1}";
        var envelope = new IntegrationEnvelope(Guid.NewGuid(), 1, "test", "test.changed", 1,
            "test", "42", Guid.NewGuid(), null, DateTime.UtcNow, payload, IntegrationEnvelope.Hash(payload));
        var channel = new Mock<IChannel>();
        channel.Setup(c => c.BasicPublishAsync("approved.exchange", envelope.MessageType, true,
                It.Is<BasicProperties>(p => p.Persistent && p.MessageId == envelope.MessageId.ToString()),
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        await RabbitMqConfirmedPublisher.PublishOnConfirmedChannelAsync(channel.Object, "approved.exchange", envelope, default);
        channel.Verify(c => c.BasicPublishAsync("approved.exchange", envelope.MessageType, true,
            It.Is<BasicProperties>(p => p.Persistent && p.MessageId == envelope.MessageId.ToString()),
            It.Is<ReadOnlyMemory<byte>>(bytes => BodyMatches(bytes, envelope)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NegativeConfirmIsNotSuccess()
    {
        var payload = "{\"businessId\":1}";
        var envelope = new IntegrationEnvelope(Guid.NewGuid(), 1, "test", "test.changed", 1,
            "test", "42", Guid.NewGuid(), null, DateTime.UtcNow, payload, IntegrationEnvelope.Hash(payload));
        var channel = new Mock<IChannel>();
        channel.Setup(c => c.BasicPublishAsync(It.IsAny<string>(), It.IsAny<string>(), true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(new IOException("negative confirm"))));
        await Assert.ThrowsAsync<IOException>(async () =>
            await RabbitMqConfirmedPublisher.PublishOnConfirmedChannelAsync(channel.Object, "approved", envelope, default));
    }

    [Fact]
    public void TransportConfigurationIsDisabledAndFailsClosedWithoutApprovedTlsSettings()
    {
        Assert.False(new RabbitMqDeliveryOptions().Enabled);
        Assert.Throws<InvalidOperationException>(() => new RabbitMqDeliveryOptions().Validate());
        Assert.Throws<InvalidOperationException>(() => new RabbitMqDeliveryOptions
            { Enabled = true, ConnectionUri = "amqp://localhost", Exchange = "test" }.Validate());
    }

    private static bool BodyMatches(ReadOnlyMemory<byte> bytes, IntegrationEnvelope expected) =>
        JsonSerializer.Deserialize<IntegrationEnvelope>(bytes.Span)?.EnvelopeHash == expected.EnvelopeHash;
}
