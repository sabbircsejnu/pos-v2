using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.Application.BusinessAdministration.Abstractions;

public enum BusinessOnboardingStatus
{
    Created,
    DuplicateEmailsInRequest,
    ExistingEmails,
    BusinessNameExists
}

public sealed record BusinessOnboardingWriteResult(
    BusinessOnboardingStatus Status,
    BusinessOnboardingReadModel? Business = null,
    IReadOnlyList<string>? Emails = null);

public interface IBusinessOnboardingWriteStore
{
    Task<BusinessOnboardingWriteResult> CreateBusinessAsync(
        CreateBusinessCommand request,
        CancellationToken cancellationToken);
}
