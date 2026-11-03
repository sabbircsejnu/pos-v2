using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.Infrastructure.ConfidentialFiles;

public sealed class UnconfiguredPrivateBlobStore : IPrivateBlobStore
{
    private static PayrollPayslipInfrastructureUnavailableException Unavailable() =>
        new("Confidential private storage is not configured.");

    public Task<string> StageAsync(
        long businessId, ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken) =>
        Task.FromException<string>(Unavailable());

    public Task<Stream> OpenAsync(
        long businessId, string key, CancellationToken cancellationToken) =>
        Task.FromException<Stream>(Unavailable());

    public Task DeleteOwnedStageAsync(
        long businessId, string key, CancellationToken cancellationToken) =>
        Task.FromException(Unavailable());
}

public sealed class UnconfiguredConfidentialFileEncryption : IConfidentialFileEncryption
{
    private static PayrollPayslipInfrastructureUnavailableException Unavailable() =>
        new("Confidential encryption and key management are not configured.");

    public Task<EncryptedConfidentialContent> EncryptAsync(
        ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken) =>
        Task.FromException<EncryptedConfidentialContent>(Unavailable());

    public Task<byte[]> DecryptAsync(
        ReadOnlyMemory<byte> ciphertext, string keyReference, CancellationToken cancellationToken) =>
        Task.FromException<byte[]>(Unavailable());
}

public sealed class UnconfiguredConfidentialContentInspector : IConfidentialContentInspector
{
    private static PayrollPayslipInfrastructureUnavailableException Unavailable() =>
        new("Confidential document validation and scanning are not configured.");

    public Task<ConfidentialContentValidation> ValidateAsync(
        ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken) =>
        Task.FromException<ConfidentialContentValidation>(Unavailable());

    public Task<ConfidentialFileScanStatus> ScanAsync(
        ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        Task.FromException<ConfidentialFileScanStatus>(Unavailable());
}

public sealed class UnconfiguredPayrollPayslipEmailTransport : IPayrollPayslipEmailTransport
{
    public Task<PayrollPayslipEmailResult> DeliverAsync(
        long businessId, long employeeId, string recipient, Guid artifactVersionId,
        CancellationToken cancellationToken) =>
        Task.FromException<PayrollPayslipEmailResult>(new PayrollPayslipEmailNotConfiguredException());
}
