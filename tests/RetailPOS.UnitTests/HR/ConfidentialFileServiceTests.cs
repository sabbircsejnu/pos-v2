using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;

namespace RetailPOS.UnitTests.HR;

public sealed class ConfidentialFileServiceTests
{
    [Theory]
    [InlineData(ConfidentialFileScanStatus.Pending)]
    [InlineData(ConfidentialFileScanStatus.Rejected)]
    [InlineData(ConfidentialFileScanStatus.Error)]
    public async Task NonCleanScanNeverStagesOrPublishes(ConfidentialFileScanStatus status)
    {
        var inspector = new Mock<IConfidentialContentInspector>();
        inspector.Setup(reader => reader.ValidateAsync(It.IsAny<ReadOnlyMemory<byte>>(), "test.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfidentialContentValidation("application/pdf", ".pdf"));
        inspector.Setup(reader => reader.ScanAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var blobs = new Mock<IPrivateBlobStore>(MockBehavior.Strict);
        var versions = new Mock<IConfidentialFileVersionStore>(MockBehavior.Strict);
        versions.Setup(writer => writer.AddRejectedAsync(It.IsAny<ConfidentialFileVersion>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IRequiredAuditTransaction>();
        audit.Setup(writer => writer.ExecuteAsync(It.IsAny<RequiredAuditOperation>(),
            It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<RequiredAuditOperation, Func<CancellationToken, Task<bool>>, CancellationToken>(
                (_, action, token) => action(token));
        var service = new ConfidentialFileService(Mock.Of<IConfidentialFileAccess>(), inspector.Object,
            Mock.Of<IConfidentialFileEncryption>(), blobs.Object, versions.Object, audit.Object,
            new ConfidentialFileLimits(100), TimeProvider.System);
        var failure = await Assert.ThrowsAsync<ConfidentialFileScanException>(() =>
            service.StageAndCommitVersionAsync(
            new ConfidentialFileUpload(1, 2, 1, ConfidentialFileClassification.EmployeeDocument,
                "test.pdf", new MemoryStream([1, 2]), null, null,
                "application/pdf", 9, ExpectedSubjectVersion: 1), CancellationToken.None));
        Assert.Equal(status, failure.Status);
        blobs.VerifyNoOtherCalls();
        versions.Verify(writer => writer.AddRejectedAsync(
            It.Is<ConfidentialFileVersion>(version => version.ScanStatus == status),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ExpiredOrRevokedVersionNeverOpensBlob(bool expired, bool revoked)
    {
        var version = Version() with { ExpiresAt = expired ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            RevokedAt = revoked ? DateTimeOffset.UtcNow : null };
        var versions = new Mock<IConfidentialFileVersionStore>();
        versions.Setup(reader => reader.FindAsync(1, version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var blobs = new Mock<IPrivateBlobStore>(MockBehavior.Strict);
        var service = new ConfidentialFileService(Mock.Of<IConfidentialFileAccess>(), Mock.Of<IConfidentialContentInspector>(),
            Mock.Of<IConfidentialFileEncryption>(), blobs.Object, versions.Object, Mock.Of<IRequiredAuditTransaction>(),
            new ConfidentialFileLimits(100), TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.OpenAuthorizedReadAsync(1, version.Id, CancellationToken.None));
        blobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuccessfulAuditedReadClearsOwnedPlaintextOnDisposal()
    {
        var version = Version();
        var plaintext = new byte[] { 1, 2 };
        var versions = new Mock<IConfidentialFileVersionStore>();
        versions.Setup(store => store.FindAsync(1, version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var blobs = new Mock<IPrivateBlobStore>();
        blobs.Setup(store => store.OpenAsync(1, version.StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2]));
        var encryption = new Mock<IConfidentialFileEncryption>();
        encryption.Setup(store => store.DecryptAsync(It.IsAny<ReadOnlyMemory<byte>>(), "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(plaintext);
        var audit = new Mock<IRequiredAuditTransaction>();
        audit.Setup(store => store.ExecuteAsync(It.IsAny<RequiredAuditOperation>(),
            It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new ConfidentialFileService(Mock.Of<IConfidentialFileAccess>(), Mock.Of<IConfidentialContentInspector>(),
            encryption.Object, blobs.Object, versions.Object, audit.Object, new(100, 200), TimeProvider.System);
        var response = await service.OpenAuthorizedReadAsync(1, version.Id, default);
        Assert.Equal(1, response.Content.ReadByte());
        response.Content.Dispose();
        Assert.All(plaintext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task AuditFailureDoesNotReturnPrivateContent()
    {
        var version = Version();
        var versions = new Mock<IConfidentialFileVersionStore>();
        versions.Setup(reader => reader.FindAsync(1, version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        var blobs = new Mock<IPrivateBlobStore>();
        blobs.Setup(reader => reader.OpenAsync(1, version.StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2]));
        var encryption = new Mock<IConfidentialFileEncryption>();
        encryption.Setup(reader => reader.DecryptAsync(It.IsAny<ReadOnlyMemory<byte>>(), "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2 });
        var audit = new Mock<IRequiredAuditTransaction>();
        audit.Setup(writer => writer.ExecuteAsync(It.IsAny<RequiredAuditOperation>(),
            It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Audit unavailable"));
        var service = new ConfidentialFileService(Mock.Of<IConfidentialFileAccess>(), Mock.Of<IConfidentialContentInspector>(),
            encryption.Object, blobs.Object, versions.Object, audit.Object, new ConfidentialFileLimits(100, 200), TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(() => service.OpenAuthorizedReadAsync(1, version.Id, CancellationToken.None));
    }

    [Fact]
    public async Task MetadataAuditFailureCleansOwnedStagedBlob()
    {
        var inspector = new Mock<IConfidentialContentInspector>();
        inspector.Setup(reader => reader.ValidateAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), "test.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfidentialContentValidation("application/pdf", ".pdf"));
        inspector.Setup(reader => reader.ScanAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfidentialFileScanStatus.Clean);
        var encryption = new Mock<IConfidentialFileEncryption>();
        encryption.Setup(service => service.EncryptAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncryptedConfidentialContent([3, 4], "test-key"));
        var blobs = new Mock<IPrivateBlobStore>();
        blobs.Setup(store => store.StageAsync(1, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("owned-stage-key");
        blobs.Setup(store => store.DeleteOwnedStageAsync(1, "owned-stage-key", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var versions = new Mock<IConfidentialFileVersionStore>();
        versions.Setup(store => store.AddAsync(It.IsAny<ConfidentialFileVersion>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IRequiredAuditTransaction>();
        audit.Setup(writer => writer.ExecuteAsync(It.IsAny<RequiredAuditOperation>(),
            It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Audit transaction failed."));
        var service = new ConfidentialFileService(Mock.Of<IConfidentialFileAccess>(), inspector.Object,
            encryption.Object, blobs.Object, versions.Object, audit.Object, new(100, 200), TimeProvider.System);

        await Assert.ThrowsAsync<IOException>(() => service.StageAndCommitVersionAsync(
            new ConfidentialFileUpload(1, 2, 1, ConfidentialFileClassification.EmployeeDocument,
                "test.pdf", new MemoryStream([1, 2]), null, null,
                "application/pdf", 9, ExpectedSubjectVersion: 1), CancellationToken.None));

        blobs.Verify(store => store.DeleteOwnedStageAsync(1, "owned-stage-key", CancellationToken.None), Times.Once);
        versions.Verify(store => store.AddAsync(It.IsAny<ConfidentialFileVersion>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ConfidentialFileVersion Version() => new(Guid.NewGuid(), 1, 2, 1,
        ConfidentialFileClassification.EmployeeDocument, "key", "test.pdf", "application/pdf", 2,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2 })),
        "key", ConfidentialFileScanStatus.Clean, null, null, null);
}
