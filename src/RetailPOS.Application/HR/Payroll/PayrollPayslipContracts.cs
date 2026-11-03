using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollPayslipLine(
    string Code, string Name, string Type, decimal Amount, int Order);

public sealed record PayrollPayslipPayment(
    string Method, decimal PaidAmount, decimal OutstandingAmount, string Status,
    string? ReferenceSummary, IReadOnlyList<string> ReconciliationStatuses);

/// <summary>Frozen persisted Payroll values used to render an official Payslip.</summary>
public sealed record PayrollPayslipSource(
    long BusinessId, string BusinessName, long RunId, int RunRevision, string RunType,
    string RunReference, string CurrencyCode, int DecimalPrecision,
    DateOnly PeriodStart, DateOnly PeriodEnd,
    DateOnly? PayDate, long EmployeeId, string EmployeeCode, string EmployeeName,
    long? HistoricalLocationId, string? PositionSnapshot, string WorkEmail,
    long EmployeeResultId, int CalculationVersion, string SnapshotHash,
    decimal GrossEarnings, decimal TotalDeductions, decimal NetPayable,
    IReadOnlyList<PayrollPayslipLine> Lines, PayrollPayslipPayment? Payment,
    string? HistoricalLocationName = null, string? HistoricalLocationReference = null,
    string? HistoricalLocationType = null)
{
    public string DateFormat { get; init; } = "yyyy-MM-dd";
}

/// <summary>Describes a private, immutable Payslip artifact without exposing storage details.</summary>
public sealed record PayrollPayslipArtifactResponse(
    Guid Id, long RunId, int RunRevision, long EmployeeResultId, int VersionNo,
    string ArtifactType, string Format, string ContentHash, string ContentType,
    long ByteLength, string TemplateVersion, string ScanStatus, DateTimeOffset CreatedAtUtc,
    bool IsReplay);

/// <summary>Describes an email delivery result without revealing the full recipient address.</summary>
public sealed record PayrollPayslipDeliveryResponse(
    long Id, Guid ArtifactVersionId, string RecipientMasked, string Status,
    string? FailureCode, DateTimeOffset RequestedAtUtc, DateTimeOffset? SentAtUtc);

public sealed record PayrollPayslipDownload(string FileName, Stream Content);

public interface IPayrollPayslipRenderer
{
    byte[] Render(PayrollPayslipSource source, string templateVersion);
}

public sealed record PayrollPayslipArtifactCandidate(
    long BusinessId, long RunId, int RunRevision, long EmployeeResultId,
    string PayrollSnapshotHash, string PaymentInfoSnapshotHash, string TemplateVersion,
    string StorageKey, string EncryptionKeyReference, string ContentHash, long ByteLength,
    string ScanStatus, long CreatedByUserId, DateTime CreatedAtUtc, string FileName);

public sealed record PayrollPayslipArtifactWriteResult(PayrollArtifactVersion Artifact, bool IsReplay);

public sealed record PayrollPayslipDeliveryWrite(
    long BusinessId, Guid ArtifactVersionId, string RecipientSnapshot, long RequestedByUserId,
    string IdempotencyKey, string PayloadHash,
    DateTime RequestedAtUtc, string Status, string? ProviderReference, string? FailureCode,
    DateTime? SentAtUtc);

public sealed record PayrollPayslipDeliveryWriteResult(
    PayrollPayslipDelivery Delivery, bool IsReplay);

public interface IPayrollPayslipStore
{
    Task<PayrollPayslipSource> GetSourceAsync(
        long businessId, long runId, long employeeId, CancellationToken cancellationToken);
    Task<PayrollArtifactVersion?> FindLatestAsync(
        long businessId, long runId, long employeeId, CancellationToken cancellationToken);
    Task<PayrollArtifactVersion?> FindAsync(
        long businessId, Guid artifactVersionId, CancellationToken cancellationToken);
    Task<PayrollPayslipArtifactWriteResult> CreateOrReuseAsync(
        PayrollPayslipArtifactCandidate candidate, CancellationToken cancellationToken);
    Task<PayrollPayslipDeliveryWriteResult> AddDeliveryAsync(
        PayrollPayslipDeliveryWrite delivery, CancellationToken cancellationToken);
    Task<PayrollPayslipDelivery?> FindDeliveryAsync(
        long businessId, long actorUserId, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<PayrollPayslipDelivery> UpdateDeliveryAsync(
        long deliveryId, long businessId, string status, string? providerReference,
        string? failureCode, DateTime? sentAtUtc, CancellationToken cancellationToken);
}

public sealed record GeneratePayrollPayslipCommand(long RunId, long EmployeeId)
    : IRequest<PayrollPayslipArtifactResponse>;
public sealed record GetPayrollPayslipQuery(long RunId, long EmployeeId)
    : IRequest<PayrollPayslipArtifactResponse>;
public sealed record DownloadPayrollPayslipQuery(
    long RunId, long EmployeeId, bool ForPrint = false)
    : IRequest<PayrollPayslipDownload>;
public sealed record EmailPayrollPayslipCommand(long RunId, long EmployeeId, string IdempotencyKey)
    : IRequest<PayrollPayslipDeliveryResponse>;

public sealed record PayrollPayslipEmailResult(bool ConfirmedSent, string? ProviderReference);

public interface IPayrollPayslipEmailTransport
{
    Task<PayrollPayslipEmailResult> DeliverAsync(
        long businessId, long employeeId, string recipient, Guid artifactVersionId,
        CancellationToken cancellationToken);
}

public sealed class PayrollPayslipEmailNotConfiguredException()
    : InvalidOperationException("Confidential Payslip email delivery is not configured.");

public sealed class PayrollPayslipEmailTransportException(string failureCode)
    : InvalidOperationException("Confidential Payslip delivery failed.")
{
    public string FailureCode { get; } = failureCode;
}

public sealed class PayrollPayslipIdempotencyConflictException()
    : InvalidOperationException("Payslip email idempotency key was already used with a different request.");

public sealed class PayrollPayslipService(
    IPayrollRunAccess access,
    IPayrollPayslipStore store,
    IPayrollPayslipRenderer renderer,
    IPrivateBlobStore blobs,
    IConfidentialFileEncryption encryption,
    IConfidentialContentInspector inspector,
    IPayrollPayslipEmailTransport emailTransport,
    IRequiredAuditTransaction audit,
    TimeProvider clock,
    RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore settings)
{
    private const string TemplateVersion = "payslip-v2";
    private const long MaxPdfBytes = 10 * 1024 * 1024;

    public async Task<PayrollPayslipArtifactResponse> GenerateAsync(
        long runId, long employeeId, CancellationToken cancellationToken)
    {
        ValidateIds(runId, employeeId);
        var businessId = access.RequireBusinessId();
        var source = await AuthorizeAndLoadAsync(
            businessId, runId, employeeId, "payroll.payslip.view", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        if (source.Payment is not null)
            await access.EnsurePermissionAsync(businessId, "payroll.payment.view", cancellationToken);

        var paymentHash = HashJson(source.Payment);
        var dateFormat = RetailPOS.Application.Common.Formatting.UiDateFormatting.Normalize(
            (await settings.GetAsync(cancellationToken)).Company.DateFormat);
        source = source with { DateFormat = dateFormat };
        var templateVersion = $"{TemplateVersion}-{dateFormat.Replace('/', '-')}";
        var bytes = renderer.Render(source, templateVersion);
        byte[]? encryptedBytes = null;
        var stagedStorageKey = (string?)null;
        try
        {
            if (bytes.Length == 0 || bytes.Length > MaxPdfBytes)
                throw new InvalidDataException("Payslip PDF is empty or exceeds its approved size.");
            var fileName =
                $"payslip-{SafeFileNameComponent(source.EmployeeCode)}-{source.RunId}-r{source.RunRevision}.pdf";
            var validation = await inspector.ValidateAsync(bytes, fileName, cancellationToken);
            if (!string.Equals(validation.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(validation.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payslip PDF validation failed.");
            var scanStatus = await inspector.ScanAsync(bytes, cancellationToken);
            if (scanStatus != ConfidentialFileScanStatus.Clean)
                throw new InvalidDataException("Payslip PDF did not pass the configured confidential-content scan.");

            var contentHash = Convert.ToHexString(SHA256.HashData(bytes));
            var encrypted = await encryption.EncryptAsync(bytes, cancellationToken);
            encryptedBytes = encrypted.Bytes;
            if (encrypted.Bytes.Length == 0 || string.IsNullOrWhiteSpace(encrypted.KeyReference))
                throw new InvalidOperationException(
                    "Configured confidential encryption did not produce a protected artifact.");

            stagedStorageKey = await blobs.StageAsync(businessId, encrypted.Bytes, cancellationToken);
            var candidate = new PayrollPayslipArtifactCandidate(
                businessId, runId, source.RunRevision, source.EmployeeResultId, source.SnapshotHash,
                paymentHash, templateVersion, stagedStorageKey, encrypted.KeyReference, contentHash,
                bytes.LongLength, scanStatus.ToString(), access.RequireRealUserId(),
                clock.GetUtcNow().UtcDateTime, fileName);
            var result = await audit.ExecuteAsync(
                new RequiredAuditOperation(businessId, "payroll.payslip.generated", "Payroll",
                    "Generated a confidential Payslip artifact.", "Payslip",
                    $"{runId}:{source.EmployeeResultId}"),
                token => store.CreateOrReuseAsync(candidate, token),
                result => !result.IsReplay,
                cancellationToken);
            if (result.IsReplay)
            {
                await blobs.DeleteOwnedStageAsync(businessId, stagedStorageKey, cancellationToken);
                stagedStorageKey = null;
            }
            else
            {
                stagedStorageKey = null;
            }
            return Map(result.Artifact, result.IsReplay);
        }
        catch (Exception failure) when (stagedStorageKey is not null)
        {
            try
            {
                await blobs.DeleteOwnedStageAsync(
                    businessId, stagedStorageKey, CancellationToken.None);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Payslip metadata commit failed and its owned staged blob could not be cleaned up.",
                    failure, cleanupFailure);
            }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (encryptedBytes is not null)
                CryptographicOperations.ZeroMemory(encryptedBytes);
        }
    }

    public async Task<PayrollPayslipArtifactResponse> GetAsync(
        long runId, long employeeId, CancellationToken cancellationToken)
    {
        ValidateIds(runId, employeeId);
        var businessId = access.RequireBusinessId();
        _ = await AuthorizeAndLoadAsync(
            businessId, runId, employeeId, "payroll.payslip.view", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var artifact = await store.FindLatestAsync(businessId, runId, employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Payslip artifact not found.");
        await RequirePaymentPermissionIfIncludedAsync(businessId, artifact, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payslip.viewed", "Payroll",
                "Read confidential Payslip artifact metadata.", "Payslip", artifact.Id.ToString()),
            _ => Task.FromResult(Map(artifact, false)), cancellationToken);
    }

    public async Task<PayrollPayslipDownload> DownloadAsync(
        long runId, long employeeId, bool forPrint, CancellationToken cancellationToken)
    {
        ValidateIds(runId, employeeId);
        var businessId = access.RequireBusinessId();
        _ = await AuthorizeAndLoadAsync(
            businessId, runId, employeeId,
            forPrint ? "payroll.payslip.print" : "payroll.payslip.download", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var artifact = await store.FindLatestAsync(businessId, runId, employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Payslip artifact not found.");
        await RequirePaymentPermissionIfIncludedAsync(businessId, artifact, cancellationToken);
        if (artifact.ScanStatus != ConfidentialFileScanStatus.Clean.ToString()
            || artifact.RevokedAtUtc.HasValue
            || artifact.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            throw new UnauthorizedAccessException("Payslip artifact is unavailable.");
        var content = await ReadAndVerifyAsync(artifact, cancellationToken);
        try
        {
            await audit.ExecuteAsync(
                new RequiredAuditOperation(businessId,
                    forPrint ? "payroll.payslip.printed" : "payroll.payslip.downloaded",
                    "Payroll", forPrint ? "Printed a confidential Payslip." : "Downloaded a confidential Payslip.",
                    "Payslip", artifact.Id.ToString()),
                _ => Task.FromResult(true), cancellationToken);
            return new PayrollPayslipDownload(
                Path.GetFileName(artifact.FileName), new SensitiveMemoryStream(content));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(content);
            throw;
        }
    }

    public async Task<PayrollPayslipDeliveryResponse> EmailAsync(
        long runId, long employeeId, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateIds(runId, employeeId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 150
            || idempotencyKey.Any(char.IsControl))
            throw new ArgumentException("A valid Payslip email idempotency key is required.");
        var businessId = access.RequireBusinessId();
        var source = await AuthorizeAndLoadAsync(
            businessId, runId, employeeId, "payroll.payslip.email", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var artifact = await store.FindLatestAsync(businessId, runId, employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Payslip artifact not found.");
        await RequirePaymentPermissionIfIncludedAsync(businessId, artifact, cancellationToken);
        if (string.IsNullOrWhiteSpace(source.WorkEmail)
            || !MailAddress.TryCreate(source.WorkEmail, out var recipient))
            throw new ArgumentException("The Employee has no valid recorded work email address.");

        var requestedAt = clock.GetUtcNow().UtcDateTime;
        var payloadHash = HashJson(new { artifact.Id, Recipient = recipient.Address });
        var writeResult = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payslip.email_requested", "Payroll",
                "Requested delivery of a confidential Payslip.", "Payslip", artifact.Id.ToString()),
            token => store.AddDeliveryAsync(new PayrollPayslipDeliveryWrite(
                businessId, artifact.Id, recipient.Address, access.RequireRealUserId(),
                idempotencyKey, payloadHash,
                requestedAt, "Pending", null, null, null), token),
            result => !result.IsReplay,
            cancellationToken);
        var delivery = writeResult.Delivery;
        if (writeResult.IsReplay)
        {
            if (delivery.Status == "Pending")
                delivery = await WaitForDeliveryOutcomeAsync(
                    businessId, access.RequireRealUserId(), idempotencyKey,
                    delivery, cancellationToken);
            return MapDelivery(delivery, artifact.Id);
        }

        PayrollPayslipEmailResult result;
        string? failureCode = null;
        try
        {
            result = await emailTransport.DeliverAsync(
                businessId, employeeId, recipient.Address, artifact.Id, cancellationToken);
            if (!result.ConfirmedSent)
                failureCode = "DeliveryNotConfirmed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PayrollPayslipEmailNotConfiguredException)
        {
            result = new PayrollPayslipEmailResult(false, null);
            failureCode = "DeliveryNotConfigured";
        }
        catch (PayrollPayslipEmailTransportException exception)
        {
            result = new PayrollPayslipEmailResult(false, null);
            failureCode = exception.FailureCode;
        }

        var status = result.ConfirmedSent ? "Sent" : "Failed";
        DateTime? sentAt = result.ConfirmedSent ? clock.GetUtcNow().UtcDateTime : null;
        delivery = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payslip.email_result", "Payroll",
                "Recorded confidential Payslip delivery outcome.", "PayslipDelivery",
                delivery.Id.ToString()),
            token => store.UpdateDeliveryAsync(
                delivery.Id, businessId, status, result.ProviderReference, failureCode, sentAt, token),
            cancellationToken);
        delivery = await store.FindDeliveryAsync(
            businessId, access.RequireRealUserId(), idempotencyKey, cancellationToken)
            ?? throw new InvalidOperationException("Recorded Payslip delivery outcome could not be reloaded.");
        return MapDelivery(delivery, artifact.Id);
    }

    private async Task<PayrollPayslipDelivery> WaitForDeliveryOutcomeAsync(
        long businessId, long actorUserId, string idempotencyKey,
        PayrollPayslipDelivery pending, CancellationToken cancellationToken)
    {
        var timeoutAt = clock.GetUtcNow().AddSeconds(30);
        while (clock.GetUtcNow() < timeoutAt)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), clock, cancellationToken);
            var current = await store.FindDeliveryAsync(
                businessId, actorUserId, idempotencyKey, cancellationToken);
            if (current is null)
                throw new InvalidOperationException("Payslip delivery disappeared during idempotent replay.");
            if (current.Status != "Pending")
                return current;
            pending = current;
        }
        return pending;
    }

    private async Task<PayrollPayslipSource> AuthorizeAndLoadAsync(
        long businessId, long runId, long employeeId, string permission,
        CancellationToken cancellationToken)
    {
        await access.EnsurePermissionAsync(businessId, permission, cancellationToken);
        var source = await store.GetSourceAsync(businessId, runId, employeeId, cancellationToken);
        if (source.BusinessId != businessId || source.EmployeeId != employeeId
            || source.RunId != runId || source.EmployeeResultId <= 0
            || string.IsNullOrWhiteSpace(source.SnapshotHash))
            throw new KeyNotFoundException("Approved Payroll result not found.");
        if (source.HistoricalLocationId is not > 0)
            throw new UnauthorizedAccessException("Payslip Employee location scope is unavailable.");
        await access.EnsureScopeAsync(
            businessId, PayrollRunScopeTypes.Location, source.HistoricalLocationId, cancellationToken);
        return source;
    }

    private async Task<byte[]> ReadAndVerifyAsync(
        PayrollArtifactVersion artifact, CancellationToken cancellationToken)
    {
        await using var encryptedStream = await blobs.OpenAsync(
            artifact.BusinessId, artifact.StorageKey, cancellationToken);
        using var encrypted = new MemoryStream();
        await encryptedStream.CopyToAsync(encrypted, cancellationToken);
        var bytes = await encryption.DecryptAsync(encrypted.ToArray(),
            artifact.EncryptionKeyReference, cancellationToken);
        if (bytes.LongLength != artifact.ByteLength
            || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(
                artifact.ContentHash, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException("Payslip artifact integrity validation failed.");
        }
        return bytes;
    }

    private static PayrollPayslipArtifactResponse Map(PayrollArtifactVersion row, bool replay) =>
        new(row.Id, row.PayrollRunId, row.RunRevision, row.PayrollEmployeeResultId, row.VersionNo,
            row.ArtifactType, row.Format, row.ContentHash, row.ContentType, row.ByteLength,
            row.TemplateVersion, row.ScanStatus, new DateTimeOffset(ToUtc(row.CreatedAtUtc)), replay);

    private async Task RequirePaymentPermissionIfIncludedAsync(
        long businessId, PayrollArtifactVersion artifact, CancellationToken cancellationToken)
    {
        if (!artifact.PaymentInfoSnapshotHash.Equals(HashJson<PayrollPayslipPayment?>(null),
                    StringComparison.Ordinal))
                await access.EnsurePermissionAsync(
                    businessId, "payroll.payment.view", cancellationToken);
    }

    private static string HashJson<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value))));

    private static string MaskRecipient(string address)
    {
        var at = address.IndexOf('@');
        if (at <= 0) return "***";
        var prefix = address[..at];
        return $"{prefix[0]}***{address[at..]}";
    }

    private static PayrollPayslipDeliveryResponse MapDelivery(
        PayrollPayslipDelivery delivery, Guid artifactId) =>
        new(delivery.Id, artifactId, MaskRecipient(delivery.RecipientSnapshot),
            delivery.Status, delivery.FailureCode,
            new DateTimeOffset(ToUtc(delivery.RequestedAtUtc)),
            delivery.SentAtUtc is { } sentAt
                ? new DateTimeOffset(ToUtc(sentAt))
                : null);

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string SafeFileNameComponent(string value)
    {
        var safe = new string(value.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "employee" : safe;
    }

    private static void ValidateIds(long runId, long employeeId)
    {
        if (runId <= 0 || employeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId));
    }

    private sealed class SensitiveMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private readonly byte[] ownedBytes = bytes;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                CryptographicOperations.ZeroMemory(ownedBytes);
            base.Dispose(disposing);
        }
    }

    public sealed class PayrollPayslipHandlers(PayrollPayslipService service) :
        IRequestHandler<GeneratePayrollPayslipCommand, PayrollPayslipArtifactResponse>,
        IRequestHandler<GetPayrollPayslipQuery, PayrollPayslipArtifactResponse>,
        IRequestHandler<DownloadPayrollPayslipQuery, PayrollPayslipDownload>,
        IRequestHandler<EmailPayrollPayslipCommand, PayrollPayslipDeliveryResponse>
    {
        public Task<PayrollPayslipArtifactResponse> Handle(
            GeneratePayrollPayslipCommand request, CancellationToken cancellationToken) =>
            service.GenerateAsync(request.RunId, request.EmployeeId, cancellationToken);

        public Task<PayrollPayslipArtifactResponse> Handle(
            GetPayrollPayslipQuery request, CancellationToken cancellationToken) =>
            service.GetAsync(request.RunId, request.EmployeeId, cancellationToken);

        public Task<PayrollPayslipDownload> Handle(
            DownloadPayrollPayslipQuery request, CancellationToken cancellationToken) =>
            service.DownloadAsync(request.RunId, request.EmployeeId, request.ForPrint, cancellationToken);

        public Task<PayrollPayslipDeliveryResponse> Handle(
            EmailPayrollPayslipCommand request, CancellationToken cancellationToken) =>
            service.EmailAsync(request.RunId, request.EmployeeId, request.IdempotencyKey, cancellationToken);
    }
}
