using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollPayslipServiceTests
{
    [Fact]
    public async Task GenerateCreatesAuditedPrivateArtifactFromPersistedSource()
    {
        var fixture = new PayslipFixture();

        var result = await fixture.Service.GenerateAsync(17, 42, CancellationToken.None);

        Assert.Equal(fixture.Artifact.Id, result.Id);
        Assert.False(result.IsReplay);
        Assert.Equal("payroll.payslip.generated", fixture.Audit.LastOperation?.ActionType);
        fixture.Access.Verify(access => access.EnsureScopeAsync(
            1, PayrollRunScopeTypes.Location, 10, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Store.Verify(store => store.CreateOrReuseAsync(
            It.Is<PayrollPayslipArtifactCandidate>(candidate =>
                candidate.BusinessId == 1
                && candidate.RunId == 17
                && candidate.EmployeeResultId == 91
                && candidate.StorageKey == "private-storage-reference"
                && candidate.TemplateVersion == "payslip-v2-dd-MM-yyyy"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateCleansStagedPrivateBlobWhenRequiredAuditFails()
    {
        var fixture = new PayslipFixture(failAuditAfterAction: true);

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.GenerateAsync(17, 42, CancellationToken.None));

        fixture.Blobs.Verify(blobs => blobs.DeleteOwnedStageAsync(
            1, "private-storage-reference", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateReplayDeletesTheLosingStagedPrivateBlob()
    {
        var fixture = new PayslipFixture();
        fixture.Store.Setup(store => store.CreateOrReuseAsync(
                It.IsAny<PayrollPayslipArtifactCandidate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayrollPayslipArtifactWriteResult(fixture.Artifact, true));

        var response = await fixture.Service.GenerateAsync(17, 42, CancellationToken.None);

        Assert.True(response.IsReplay);
        fixture.Blobs.Verify(blobs => blobs.DeleteOwnedStageAsync(
            1, "private-storage-reference", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadingPayslipMetadataRequiresAudit()
    {
        var fixture = new PayslipFixture();
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);

        var response = await fixture.Service.GetAsync(17, 42, CancellationToken.None);

        Assert.Equal(fixture.Artifact.Id, response.Id);
        Assert.Equal("payroll.payslip.viewed", fixture.Audit.LastOperation?.ActionType);
    }

    [Fact]
    public async Task PayslipMetadataIsNotReturnedWhenRequiredAuditFails()
    {
        var fixture = new PayslipFixture(failAuditAfterAction: true);
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.GetAsync(17, 42, CancellationToken.None));

        Assert.Equal("payroll.payslip.viewed", fixture.Audit.LastOperation?.ActionType);
    }

    [Fact]
    public async Task PrivateStorageStageFailureDoesNotCreateArtifactMetadata()
    {
        var fixture = new PayslipFixture();
        fixture.Blobs.Setup(blobs => blobs.StageAsync(
            1, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Injected private storage failure."));

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.GenerateAsync(17, 42, CancellationToken.None));

        fixture.Store.Verify(store => store.CreateOrReuseAsync(
            It.IsAny<PayrollPayslipArtifactCandidate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadDoesNotReleaseContentWhenRequiredAccessAuditFails()
    {
        var fixture = new PayslipFixture(failAuditAfterAction: true);
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);
        fixture.Blobs.Setup(blobs => blobs.OpenAsync(
            1, "private-storage-reference", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("%PDF-test"u8.ToArray()));
        fixture.Encryption.Setup(encryption => encryption.DecryptAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), "key-reference", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<byte> encrypted, string _, CancellationToken _) =>
                encrypted.ToArray());

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.DownloadAsync(17, 42, false, CancellationToken.None));

        Assert.Equal("payroll.payslip.downloaded", fixture.Audit.LastOperation?.ActionType);
    }

    [Fact]
    public async Task EmailDoesNotInvokeTransportWhenRequestAuditFails()
    {
        var fixture = new PayslipFixture(failAuditAfterAction: true);
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);
        fixture.Store.Setup(store => store.AddDeliveryAsync(
            It.IsAny<PayrollPayslipDeliveryWrite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayrollPayslipDeliveryWriteResult(
                new PayrollPayslipDelivery { Id = 7, BusinessId = 1, ArtifactVersionId = fixture.Artifact.Id }, false));

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.EmailAsync(17, 42, "email-request-1", CancellationToken.None));

        Assert.Equal("payroll.payslip.email_requested", fixture.Audit.LastOperation?.ActionType);
        fixture.EmailTransport.Verify(transport => transport.DeliverAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateEmailRequestReplayDoesNotInvokeTransport()
    {
        var fixture = new PayslipFixture();
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);
        var delivery = new PayrollPayslipDelivery
        {
            Id = 7, BusinessId = 1, ArtifactVersionId = fixture.Artifact.Id,
            RequestedByUserId = 101, IdempotencyKey = "email-request-1",
            PayloadHash = new string('C', 64), Status = "Pending"
        };
        fixture.Store.Setup(store => store.AddDeliveryAsync(
            It.IsAny<PayrollPayslipDeliveryWrite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayrollPayslipDeliveryWriteResult(delivery, true));
        fixture.Store.Setup(store => store.FindDeliveryAsync(
            1, 101, "email-request-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(delivery);

        var result = await fixture.Service.EmailAsync(
            17, 42, "email-request-1", CancellationToken.None);

        Assert.Equal("Pending", result.Status);
        fixture.EmailTransport.Verify(transport => transport.DeliverAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, "Sent", null)]
    [InlineData(false, "Failed", "ProviderUnavailable")]
    public async Task ConcurrentEmailReplayWaitsForAndReturnsTheRecordedTransportOutcome(
        bool succeeds, string expectedStatus, string? expectedFailureCode)
    {
        var fixture = new PayslipFixture();
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);
        var delivery = new PayrollPayslipDelivery
        {
            Id = 7, BusinessId = 1, ArtifactVersionId = fixture.Artifact.Id,
            RequestedByUserId = 101, IdempotencyKey = "email-request-race",
            PayloadHash = new string('C', 64), Status = "Pending"
        };
        var addGate = new object();
        var added = false;
        var replayObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.Setup(store => store.AddDeliveryAsync(
                It.IsAny<PayrollPayslipDeliveryWrite>(), It.IsAny<CancellationToken>()))
            .Returns((PayrollPayslipDeliveryWrite _, CancellationToken _) =>
            {
                lock (addGate)
                {
                    var replay = added;
                    added = true;
                    if (replay)
                        replayObserved.TrySetResult();
                    return Task.FromResult(new PayrollPayslipDeliveryWriteResult(delivery, replay));
                }
            });
        fixture.Store.Setup(store => store.FindDeliveryAsync(
                1, 101, "email-request-race", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => delivery);
        fixture.Store.Setup(store => store.UpdateDeliveryAsync(
                7, 1, expectedStatus, It.IsAny<string?>(), expectedFailureCode, It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns((long _, long _, string status, string? provider, string? failure,
                DateTime? sentAt, CancellationToken _) =>
            {
                delivery.Status = status;
                delivery.ProviderReference = provider;
                delivery.FailureCode = failure;
                delivery.SentAtUtc = sentAt;
                return Task.FromResult(delivery);
            });
        var transportEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransport = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transportCalls = 0;
        fixture.EmailTransport.Setup(transport => transport.DeliverAsync(
                1, 42, "employee@example.invalid", fixture.Artifact.Id,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref transportCalls);
                transportEntered.TrySetResult();
                await releaseTransport.Task;
                if (!succeeds)
                    throw new PayrollPayslipEmailTransportException("ProviderUnavailable");
                return new PayrollPayslipEmailResult(true, "provider-1");
            });

        var first = fixture.Service.EmailAsync(
            17, 42, "email-request-race", CancellationToken.None);
        await transportEntered.Task;
        var second = fixture.Service.EmailAsync(
            17, 42, "email-request-race", CancellationToken.None);
        await replayObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseTransport.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, transportCalls);
        Assert.All(results, result => Assert.Equal(7, result.Id));
        Assert.All(results, result => Assert.Equal(expectedStatus, result.Status));
        Assert.All(results, result => Assert.Equal(expectedFailureCode, result.FailureCode));
        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public async Task EmailIdempotencyPayloadConflictDoesNotInvokeTransport()
    {
        var fixture = new PayslipFixture();
        fixture.Store.Setup(store => store.FindLatestAsync(
            1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Artifact);
        fixture.Store.Setup(store => store.AddDeliveryAsync(
            It.IsAny<PayrollPayslipDeliveryWrite>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PayrollPayslipIdempotencyConflictException());

        await Assert.ThrowsAsync<PayrollPayslipIdempotencyConflictException>(
            () => fixture.Service.EmailAsync(17, 42, "email-request-1", CancellationToken.None));

        fixture.EmailTransport.Verify(transport => transport.DeliverAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateDoesNotRenderOrStageForUnauthorizedEmployeeLocation()
    {
        var fixture = new PayslipFixture();
        fixture.Access
            .Setup(access => access.EnsureScopeAsync(
                1, PayrollRunScopeTypes.Location, 10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => fixture.Service.GenerateAsync(17, 42, CancellationToken.None));

        fixture.Renderer.Verify(renderer => renderer.Render(
            It.IsAny<PayrollPayslipSource>(), It.IsAny<string>()), Times.Never);
        fixture.Blobs.Verify(blobs => blobs.StageAsync(
            It.IsAny<long>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class PayslipFixture
    {
        private static readonly byte[] PdfBytes = "%PDF-test"u8.ToArray();

        public Mock<IPayrollRunAccess> Access { get; } = new();
        public Mock<IPayrollPayslipStore> Store { get; } = new();
        public Mock<IPayrollPayslipRenderer> Renderer { get; } = new();
        public Mock<IPrivateBlobStore> Blobs { get; } = new();
        public Mock<IConfidentialFileEncryption> Encryption { get; } = new();
        public Mock<IConfidentialContentInspector> Inspector { get; } = new();
        public Mock<IPayrollPayslipEmailTransport> EmailTransport { get; } = new();
        public RecordingAuditTransaction Audit { get; }
        public PayrollArtifactVersion Artifact { get; } = new()
        {
            Id = Guid.NewGuid(),
            BusinessId = 1,
            PayrollRunId = 17,
            RunRevision = 2,
            PayrollEmployeeResultId = 91,
            VersionNo = 1,
            PayrollSnapshotHash = new string('A', 64),
            PaymentInfoSnapshotHash = new string('B', 64),
            TemplateVersion = "payslip-v1",
            StorageKey = "private-storage-reference",
            EncryptionKeyReference = "key-reference",
            ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(PdfBytes)),
            ByteLength = PdfBytes.Length,
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow,
            FileName = "payslip-EMP-42-17-r2.pdf"
        };
        public PayrollPayslipService Service { get; }

        public PayslipFixture(bool failAuditAfterAction = false)
        {
            Audit = new RecordingAuditTransaction(failAuditAfterAction);
            Access.Setup(access => access.RequireBusinessId()).Returns(1);
            Access.Setup(access => access.RequireRealUserId()).Returns(101);
            Access.Setup(access => access.EnsurePermissionAsync(
                1, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Access.Setup(access => access.EnsureScopeAsync(
                1, PayrollRunScopeTypes.Location, 10, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(store => store.GetSourceAsync(
                1, 17, 42, It.IsAny<CancellationToken>())).ReturnsAsync(Source());
            Store.Setup(store => store.CreateOrReuseAsync(
                It.IsAny<PayrollPayslipArtifactCandidate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PayrollPayslipArtifactWriteResult(Artifact, false));
            Renderer.Setup(renderer => renderer.Render(
                It.Is<PayrollPayslipSource>(source => source.DateFormat == "dd/MM/yyyy"), "payslip-v2-dd-MM-yyyy")).Returns(() => PdfBytes.ToArray());
            Inspector.Setup(inspector => inspector.ValidateAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConfidentialContentValidation("application/pdf", ".pdf"));
            Inspector.Setup(inspector => inspector.ScanAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ConfidentialFileScanStatus.Clean);
            Encryption.Setup(encryption => encryption.EncryptAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ReadOnlyMemory<byte> content, CancellationToken _) =>
                    new EncryptedConfidentialContent(content.ToArray(), "key-reference"));
            Blobs.Setup(blobs => blobs.StageAsync(
                1, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("private-storage-reference");
            Blobs.Setup(blobs => blobs.DeleteOwnedStageAsync(
                1, "private-storage-reference", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Service = new PayrollPayslipService(
                Access.Object, Store.Object, Renderer.Object, Blobs.Object,
                Encryption.Object, Inspector.Object, EmailTransport.Object,
                Audit, TimeProvider.System,
                Mock.Of<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(settings =>
                    settings.GetAsync(It.IsAny<CancellationToken>()) ==
                    Task.FromResult(new RetailPOS.Application.Settings.SystemSettings {
                        Company = new RetailPOS.Application.Settings.CompanySettings { DateFormat = "dd/MM/yyyy" }
                    })));
        }

        private static PayrollPayslipSource Source() => new(
            1, "Business", 17, 2, "Regular", "RUN-17", "BDT", 2,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            new DateOnly(2026, 11, 5), 42, "EMP-42", "Test Employee", 10, null,
            "employee@example.invalid", 91, 2, new string('A', 64),
            1000m, 100m, 900m,
            [new PayrollPayslipLine("BASE", "Base salary", "Earning", 1000m, 1)], null);
    }

    private sealed class RecordingAuditTransaction(bool failAfterAction) : IRequiredAuditTransaction
    {
        public RequiredAuditOperation? LastOperation { get; private set; }

        public Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            ExecuteAsync(operation, action, _ => true, cancellationToken);

        public async Task<T> ExecuteAsync<T>(
            RequiredAuditOperation operation,
            Func<CancellationToken, Task<T>> action,
            Func<T, bool> requiresAudit,
            CancellationToken cancellationToken)
        {
            LastOperation = operation;
            var result = await action(cancellationToken);
            if (requiresAudit(result) && failAfterAction)
                throw new IOException("Injected required-audit persistence failure.");
            return result;
        }
    }
}
