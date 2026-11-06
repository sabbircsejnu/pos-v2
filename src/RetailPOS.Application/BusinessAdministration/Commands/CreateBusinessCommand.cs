using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.ReadModels;

namespace RetailPOS.Application.BusinessAdministration.Commands;

public sealed record CreateBusinessCommand(
    string BusinessName,
    string? BusinessEmail,
    string? BusinessPhone,
    string? BusinessAddress,
    string OwnerName,
    string OwnerEmail,
    string OutletManagerName,
    string OutletManagerEmail,
    string SalesPersonName,
    string SalesPersonEmail,
    string AccountsAdminName,
    string AccountsAdminEmail,
    string? DefaultOutletName,
    bool CreateDefaultWarehouse,
    string? DefaultWarehouseName,
    string? WarehouseManagerName,
    string? WarehouseManagerEmail,
    bool ShowPoweredByOnReceipt = false,
    string? PoweredByText = null) : ICommand<BusinessOnboardingReadModel>;

public sealed class CreateBusinessCommandHandler(
    IBusinessOnboardingWriteStore store,
    IBusinessTargetAccess targetAccess)
    : IRequestHandler<CreateBusinessCommand, BusinessOnboardingReadModel>
{
    public async Task<BusinessOnboardingReadModel> Handle(
        CreateBusinessCommand request,
        CancellationToken cancellationToken)
    {
        targetAccess.RequirePlatformSuperAdmin();
        ValidateRequest(request);
        var result = await store.CreateBusinessAsync(
            request with { PoweredByText = BusinessReceiptFooterRules.NormalizePoweredByText(request.PoweredByText) },
            cancellationToken);

        return result.Status switch
        {
            BusinessOnboardingStatus.Created when result.Business is not null => result.Business,
            BusinessOnboardingStatus.DuplicateEmailsInRequest =>
                throw new InvalidOperationException(
                    $"Duplicate emails in request: {string.Join(", ", RequireEmails(result))}"),
            BusinessOnboardingStatus.ExistingEmails =>
                throw new InvalidOperationException(
                    $"Email already exists: {string.Join(", ", RequireEmails(result))}"),
            BusinessOnboardingStatus.BusinessNameExists =>
                throw new InvalidOperationException("Business name already exists."),
            _ => throw new InvalidOperationException(
                $"Unsupported business onboarding result: {result.Status}.")
        };
    }

    private static IReadOnlyList<string> RequireEmails(BusinessOnboardingWriteResult result) =>
        result.Emails ?? throw new InvalidOperationException(
            $"Business onboarding result {result.Status} did not include the required email details.");

    private static void ValidateRequest(CreateBusinessCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName))
            throw new InvalidOperationException("BusinessName is required.");

        if (string.IsNullOrWhiteSpace(request.OwnerName) || string.IsNullOrWhiteSpace(request.OwnerEmail))
            throw new InvalidOperationException("Business owner name and email are required.");

        if (string.IsNullOrWhiteSpace(request.OutletManagerName) || string.IsNullOrWhiteSpace(request.OutletManagerEmail))
            throw new InvalidOperationException("Outlet manager name and email are required.");

        if (string.IsNullOrWhiteSpace(request.SalesPersonName) || string.IsNullOrWhiteSpace(request.SalesPersonEmail))
            throw new InvalidOperationException("Sales person name and email are required.");

        if (string.IsNullOrWhiteSpace(request.AccountsAdminName) || string.IsNullOrWhiteSpace(request.AccountsAdminEmail))
            throw new InvalidOperationException("Accounts admin name and email are required.");

        if (request.CreateDefaultWarehouse &&
            (string.IsNullOrWhiteSpace(request.WarehouseManagerName) ||
             string.IsNullOrWhiteSpace(request.WarehouseManagerEmail)))
        {
            throw new InvalidOperationException(
                "Warehouse manager name and email are required when CreateDefaultWarehouse is true.");
        }
    }
}
