using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollMonetaryConfigurationRequest(
    DateOnly EffectiveFrom,
    string CurrencyCode,
    int DecimalPrecision,
    string RoundingMode);

public sealed record PayrollMonetaryConfigurationResponse(
    long Id,
    long BusinessId,
    int VersionNo,
    DateOnly EffectiveFrom,
    string CurrencyCode,
    int DecimalPrecision,
    string RoundingMode,
    DateTime CreatedAtUtc,
    long CreatedByUserId);

public interface IPayrollMonetaryConfigurationStore
{
    Task<PayrollMonetaryConfigurationResponse> CreateAsync(
        long businessId,
        long actorUserId,
        CreatePayrollMonetaryConfigurationRequest request,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollMonetaryConfigurationResponse>> ListAsync(
        long businessId, CancellationToken cancellationToken);
}

public sealed record CreatePayrollMonetaryConfigurationCommand(
    CreatePayrollMonetaryConfigurationRequest Data)
    : IRequest<PayrollMonetaryConfigurationResponse>;

public sealed record GetPayrollMonetaryConfigurationsQuery
    : IRequest<IReadOnlyList<PayrollMonetaryConfigurationResponse>>;

public sealed class PayrollMonetaryConfigurationHandlers(
    IHrPayrollCapabilityService capabilities,
    IPayrollRunAccess access,
    IPayrollMonetaryConfigurationStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollMonetaryConfigurationCommand, PayrollMonetaryConfigurationResponse>,
    IRequestHandler<GetPayrollMonetaryConfigurationsQuery,
        IReadOnlyList<PayrollMonetaryConfigurationResponse>>
{
    public async Task<PayrollMonetaryConfigurationResponse> Handle(
        CreatePayrollMonetaryConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        var request = NormalizeAndValidate(command.Data);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(
            businessId, "payroll.calculation_policy.manage", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.monetary_configuration.created",
                "Payroll", "Created an immutable Business-scoped Payroll monetary configuration.",
                "PayrollMonetaryConfigurationVersion",
                request.EffectiveFrom.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CreateAsync(businessId, actorUserId, request, token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollMonetaryConfigurationResponse>> Handle(
        GetPayrollMonetaryConfigurationsQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(
            businessId, "payroll.calculation_policy.view", cancellationToken);
        return await store.ListAsync(businessId, cancellationToken);
    }

    private async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private static CreatePayrollMonetaryConfigurationRequest NormalizeAndValidate(
        CreatePayrollMonetaryConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EffectiveFrom == default || request.EffectiveFrom == DateOnly.MaxValue)
            throw new ArgumentException("Monetary configuration effective-from must be a valid date.");
        if (string.IsNullOrWhiteSpace(request.CurrencyCode))
            throw new ArgumentException("A three-letter ISO currency code is required.");
        var currencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currencyCode.Length != 3 || currencyCode.Any(character => !char.IsAsciiLetter(character)))
            throw new ArgumentException("Currency code must contain exactly three ASCII letters.");
        if (request.DecimalPrecision is < 0 or > 6)
            throw new ArgumentException("Currency decimal precision must be between zero and six.");
        if (!Enum.TryParse<MidpointRounding>(request.RoundingMode, ignoreCase: true, out var roundingMode)
            || !Enum.IsDefined(roundingMode))
            throw new ArgumentException("Payroll rounding mode is not supported.");
        return request with
        {
            CurrencyCode = currencyCode,
            RoundingMode = roundingMode.ToString()
        };
    }
}

public sealed class PayrollMonetaryConfigurationConflictException(
    string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
