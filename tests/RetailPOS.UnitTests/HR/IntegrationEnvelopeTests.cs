using RetailPOS.Application.Integration;

namespace RetailPOS.UnitTests.HR;

public sealed class IntegrationEnvelopeTests
{
    [Fact]
    public void FrozenEnvelopeHashChangesForPayloadScopeTypeOrIdentity()
    {
        var envelope = Envelope();
        envelope.Validate();
        Assert.Equal(envelope.EnvelopeHash, (envelope with { }).EnvelopeHash);
        Assert.NotEqual(envelope.EnvelopeHash, (envelope with { MessageType = "changed" }).EnvelopeHash);
        Assert.NotEqual(envelope.EnvelopeHash, (envelope with { BusinessId = 2 }).EnvelopeHash);
        Assert.NotEqual(envelope.EnvelopeHash, (envelope with { MessageId = Guid.NewGuid() }).EnvelopeHash);
    }

    [Theory]
    [InlineData("{\"businessId\":2}")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void PayloadTenantMismatchOrAbsentTenantIsRejected(string payload)
    {
        var envelope = Envelope() with { PayloadJson = payload, PayloadHash = IntegrationEnvelope.Hash(payload) };
        Assert.Throws<InvalidDataException>(envelope.Validate);
    }

    [Fact]
    public void ChangedPayloadWithoutMatchingHashIsRejected() =>
        Assert.Throws<InvalidDataException>(() => (Envelope() with { PayloadJson = "{\"businessId\":1,\"v\":2}" }).Validate());

    private static IntegrationEnvelope Envelope() => new(Guid.NewGuid(), 1, "test", "test.changed", 1,
        "test", "42", Guid.NewGuid(), null, DateTime.UtcNow, "{\"businessId\":1}",
        IntegrationEnvelope.Hash("{\"businessId\":1}"));
}
