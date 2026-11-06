using MediatR;
using System.Security.Cryptography;
using System.Text;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Application.BusinessAdministration.Queries;
using RetailPOS.Application.BusinessAdministration.ReadModels;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.Application.BusinessAdministration.Commands;

public sealed record SetBusinessActiveCommand(long BusinessId, bool IsActive)
    : ICommand<BusinessSummaryReadModel>;

public sealed class SetBusinessActiveCommandHandler(
    IBusinessAdministrationWriteStore store,
    ISender sender)
    : IRequestHandler<SetBusinessActiveCommand, BusinessSummaryReadModel>
{
    public async Task<BusinessSummaryReadModel> Handle(
        SetBusinessActiveCommand request,
        CancellationToken cancellationToken)
    {
        if (!await store.SetBusinessActiveAsync(
                request.BusinessId,
                request.IsActive,
                cancellationToken))
            throw new KeyNotFoundException("Business not found.");

        return await sender.Send(
            new GetBusinessByIdQuery(request.BusinessId),
            cancellationToken);
    }
}

public sealed record UpdateBusinessSubscriptionCommand(
    long BusinessId,
    string? SubscriptionPlan,
    DateTime? TrialEndsAt,
    DateTime? SubscriptionEndsAt,
    int? MaxOutlets,
    int? MaxUsers) : ICommand<BusinessSummaryReadModel>;

public sealed class UpdateBusinessSubscriptionCommandHandler(
    IBusinessAdministrationWriteStore store,
    ISender sender)
    : IRequestHandler<UpdateBusinessSubscriptionCommand, BusinessSummaryReadModel>
{
    public async Task<BusinessSummaryReadModel> Handle(
        UpdateBusinessSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.MaxOutlets is < 0)
            throw new InvalidOperationException("MaxOutlets cannot be negative.");

        if (request.MaxUsers is < 0)
            throw new InvalidOperationException("MaxUsers cannot be negative.");

        var subscriptionPlan = string.IsNullOrWhiteSpace(request.SubscriptionPlan)
            ? null
            : request.SubscriptionPlan.Trim();
        if (!await store.UpdateSubscriptionAsync(
                request.BusinessId,
                subscriptionPlan,
                request.TrialEndsAt,
                request.SubscriptionEndsAt,
                request.MaxOutlets,
                request.MaxUsers,
                cancellationToken))
            throw new KeyNotFoundException("Business not found.");

        return await sender.Send(
            new GetBusinessByIdQuery(request.BusinessId),
            cancellationToken);
    }
}

public sealed record UpdateBusinessReceiptFooterCommand(
    long BusinessId,
    bool ShowPoweredByOnReceipt,
    string? PoweredByText) : ICommand<BusinessSummaryReadModel>;

public sealed class UpdateBusinessReceiptFooterCommandHandler(
    IBusinessAdministrationWriteStore store,
    IBusinessTargetAccess targetAccess,
    ISender sender)
    : IRequestHandler<UpdateBusinessReceiptFooterCommand, BusinessSummaryReadModel>
{
    public async Task<BusinessSummaryReadModel> Handle(
        UpdateBusinessReceiptFooterCommand request,
        CancellationToken cancellationToken)
    {
        targetAccess.RequirePlatformSuperAdmin();
        if (!await store.UpdateReceiptFooterAsync(
                request.BusinessId,
                request.ShowPoweredByOnReceipt,
                BusinessReceiptFooterRules.NormalizePoweredByText(request.PoweredByText),
                cancellationToken))
            throw new KeyNotFoundException("Business not found.");

        return await sender.Send(
            new GetBusinessByIdQuery(request.BusinessId),
            cancellationToken);
    }
}

public sealed record UpsertBusinessFeatureSettingsCommand(
    long BusinessId,
    IReadOnlyList<BusinessFeatureSettingChange> Features)
    : ICommand<IReadOnlyList<BusinessFeatureSettingReadModel>>;

public sealed class UpsertBusinessFeatureSettingsCommandHandler(
    IBusinessAdministrationWriteStore store,
    IHrPayrollCapabilityReadStore capabilityReadStore,
    IBusinessTargetAccess targetAccess,
    ISender sender)
    : IRequestHandler<UpsertBusinessFeatureSettingsCommand, IReadOnlyList<BusinessFeatureSettingReadModel>>
{
    public async Task<IReadOnlyList<BusinessFeatureSettingReadModel>> Handle(
        UpsertBusinessFeatureSettingsCommand request,
        CancellationToken cancellationToken)
    {
        // Feature changes act on an explicit target Business; they never depend on (or require)
        // the caller's selected Business context.
        var targetBusinessId = targetAccess.RequireTargetBusinessId(request.BusinessId);

        var normalizedFeatures = request.Features
            .Select(feature => feature with
            {
                FeatureKey = feature.FeatureKey.Trim().ToLowerInvariant()
            })
            .ToList();

        if (normalizedFeatures.Any(feature =>
                feature.FeatureKey is HrPayrollCapabilityKeys.Hr or HrPayrollCapabilityKeys.Payroll))
        {
            var current = await capabilityReadStore.GetExplicitSettingsAsync(
                targetBusinessId, cancellationToken);
            var hrEnabled = current.TryGetValue(HrPayrollCapabilityKeys.Hr, out var currentHr) && currentHr;
            var payrollEnabled = current.TryGetValue(HrPayrollCapabilityKeys.Payroll, out var currentPayroll)
                && currentPayroll;

            foreach (var feature in normalizedFeatures)
            {
                if (feature.FeatureKey == HrPayrollCapabilityKeys.Hr)
                    hrEnabled = feature.IsEnabled;
                else if (feature.FeatureKey == HrPayrollCapabilityKeys.Payroll)
                    payrollEnabled = feature.IsEnabled;
            }

            if (payrollEnabled && !hrEnabled)
                throw new ArgumentException("Payroll cannot be enabled unless HR is enabled.");
        }

        if (!await store.UpsertFeatureSettingsAsync(
                request.BusinessId,
                normalizedFeatures,
                cancellationToken))
            throw new KeyNotFoundException("Business not found.");

        return await sender.Send(
            new GetBusinessFeatureSettingsQuery(request.BusinessId),
            cancellationToken);
    }
}

public sealed record ResetBusinessOwnerAccessCommand(long BusinessId)
    : ICommand<ResetBusinessOwnerAccessReadModel>;

public sealed class ResetBusinessOwnerAccessCommandHandler(IBusinessAdministrationWriteStore store)
    : IRequestHandler<ResetBusinessOwnerAccessCommand, ResetBusinessOwnerAccessReadModel>
{
    private static readonly TimeSpan InvitationTtl = TimeSpan.FromDays(7);

    public async Task<ResetBusinessOwnerAccessReadModel> Handle(
        ResetBusinessOwnerAccessCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var expiresAt = now.Add(InvitationTtl);
        var result = await store.ResetBusinessOwnerAccessAsync(
            request.BusinessId,
            tokenHash,
            now,
            expiresAt,
            cancellationToken);

        return result.Status switch
        {
            BusinessOwnerAccessResetStatus.Reset => new ResetBusinessOwnerAccessReadModel(
                result.UserId!.Value,
                result.OwnerEmail!,
                rawToken,
                expiresAt),
            BusinessOwnerAccessResetStatus.BusinessNotFound =>
                throw new KeyNotFoundException("Business not found."),
            BusinessOwnerAccessResetStatus.OwnerNotFound =>
                throw new InvalidOperationException("No BusinessOwner user found for this business."),
            _ => throw new InvalidOperationException(
                $"Unsupported business owner access reset result: {result.Status}.")
        };
    }
}
