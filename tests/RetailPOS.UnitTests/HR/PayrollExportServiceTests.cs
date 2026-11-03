using System.Text;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollExportServiceTests
{
    [Fact]
    public async Task ExportRequiresBothPermissionsBeforeReadingPayrollEvidence()
    {
        var fixture = new ExportFixture();
        fixture.Access
            .Setup(access => access.EnsurePermissionAsync(
                1, "payroll.salary.view", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CsvAsync(12, CancellationToken.None));

        Assert.Equal(0, fixture.Store.FindRunCallCount);
        Assert.Equal(0, fixture.Store.EmployeesCallCount);
    }

    [Fact]
    public async Task ExportAuditFailureReturnsNoContentAndClearsRenderedBuffer()
    {
        var fixture = new ExportFixture(failAudit: true);

        await Assert.ThrowsAsync<IOException>(() =>
            fixture.Service.CsvAsync(12, CancellationToken.None));

        Assert.NotNull(fixture.RenderedContent);
        Assert.All(fixture.RenderedContent!, value => Assert.Equal((byte)0, value));
        Assert.Equal("payroll.export.csv", fixture.Audit.LastOperation?.ActionType);
    }

    [Fact]
    public async Task ExportUsesRunScopeAndIncludesPaymentEvidenceOnlyWithPaymentPermission()
    {
        var fixture = new ExportFixture(includePaymentPermission: false);

        var download = await fixture.Service.CsvAsync(12, CancellationToken.None);

        Assert.Equal("payroll-run-12-r3-register.csv", download.FileName);
        Assert.Equal("text/csv; charset=utf-8", download.ContentType);
        Assert.Equal(7, fixture.Store.LastLocationScopeId);
        Assert.False(fixture.Store.LastIncludesPaymentData);
        Assert.Contains("\"IncludesPaymentData\":false", fixture.Audit.LastOperation?.Evidence?.Single().Metadata);
        using (download.Content)
        {
            using var reader = new StreamReader(download.Content, Encoding.UTF8, leaveOpen: true);
            Assert.Contains("authoritative export", await reader.ReadToEndAsync());
        }

        Assert.All(fixture.RenderedContent!, value => Assert.Equal((byte)0, value));
    }

    private sealed class ExportFixture
    {
        public readonly Mock<IPayrollRunAccess> Access = new(MockBehavior.Strict);
        public readonly RecordingStore Store = new();
        public readonly Mock<IPayrollExportRenderer> Renderer = new(MockBehavior.Strict);
        public readonly Mock<IPayrollEmployeePaymentDestinationAccess> PaymentAccess = new();
        public readonly RecordingAudit Audit;
        public readonly PayrollExportService Service;
        public byte[]? RenderedContent;

        public ExportFixture(bool includePaymentPermission = true, bool failAudit = false)
        {
            Audit = new RecordingAudit(failAudit);
            Access.Setup(access => access.RequireBusinessId()).Returns(1);
            Access.Setup(access => access.EnsurePermissionAsync(
                1, "payroll.export", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Access.Setup(access => access.EnsurePermissionAsync(
                1, "payroll.salary.view", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Access.Setup(access => access.EnsureScopeAsync(
                1, "Location", 7, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            PaymentAccess.Setup(access => access.HasBusinessPermission("payroll.payment.view"))
                .Returns(includePaymentPermission);
            Renderer.Setup(renderer => renderer.RenderCsv(It.IsAny<PayrollExportSource>()))
                .Returns<PayrollExportSource>(_ =>
                {
                    RenderedContent = Encoding.UTF8.GetBytes("authoritative export");
                    return RenderedContent;
                });
            Service = new PayrollExportService(
                Access.Object, PaymentAccess.Object, Store, Renderer.Object, Audit,
                Mock.Of<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(settings =>
                    settings.GetAsync(It.IsAny<CancellationToken>()) ==
                    Task.FromResult(new RetailPOS.Application.Settings.SystemSettings())));
        }
    }

    private sealed class RecordingStore : IPayrollExportStore
    {
        public long? LastLocationScopeId { get; private set; }
        public bool LastIncludesPaymentData { get; private set; }
        public int FindRunCallCount { get; private set; }
        public int EmployeesCallCount { get; private set; }

        public Task<PayrollExportRun?> FindRunAsync(
            long businessId, long runId, CancellationToken cancellationToken)
        {
            FindRunCallCount++;
            return Task.FromResult<PayrollExportRun?>(new PayrollExportRun(
                businessId, "Business", runId, 4, 2, "Regular", "RUN-12", null,
                null, null, "Location", 7, "Approved", 3, "BDT", 2, "2026-10",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null));
        }

        public Task<IReadOnlyList<PayrollExportEmployee>> GetEmployeesAsync(
            long businessId, long runId, int revision, long? primaryLocationScopeId,
            bool includePaymentData, CancellationToken cancellationToken)
        {
            EmployeesCallCount++;
            LastLocationScopeId = primaryLocationScopeId;
            LastIncludesPaymentData = includePaymentData;
            return Task.FromResult<IReadOnlyList<PayrollExportEmployee>>([]);
        }
    }

    private sealed class RecordingAudit(bool fail) : IRequiredAuditTransaction
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
            if (fail && requiresAudit(result))
                throw new IOException("Injected audit persistence failure.");
            return result;
        }
    }
}
