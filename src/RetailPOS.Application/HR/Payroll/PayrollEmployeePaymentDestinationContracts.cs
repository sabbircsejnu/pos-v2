using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollEmployeePaymentDestinationData(
    string Method,
    DateOnly EffectiveFrom,
    string? BankName,
    string? AccountName,
    string? AccountNumber,
    string? MobileProvider,
    string? MobileAccountName,
    string? MobileAccountNumber);

public sealed record CreatePayrollEmployeePaymentDestinationRequest(
    PayrollEmployeePaymentDestinationData Data);

public sealed record VersionPayrollEmployeePaymentDestinationRequest(
    long ExpectedVersion,
    PayrollEmployeePaymentDestinationData Data);

public sealed record DeactivatePayrollEmployeePaymentDestinationRequest(
    long ExpectedVersion,
    DateOnly EffectiveFrom);

public sealed record PayrollEmployeePaymentDestinationResponse(
    long Id,
    long BusinessId,
    long EmployeeId,
    int VersionNo,
    long Version,
    long OperationalLocationProjectionId,
    string Method,
    bool IsPrimary,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? BankName,
    string? AccountName,
    string? AccountNumber,
    string? MobileProvider,
    string? MobileAccountName,
    string? MobileAccountNumber,
    DateTime CreatedAtUtc,
    long CreatedByUserId);

public interface IPayrollEmployeePaymentDestinationStore
{
    Task<PayrollEmployeePaymentDestinationResponse> CreateAsync(
        long businessId, long actorUserId, long employeeId, long locationId,
        PayrollEmployeePaymentDestinationData data, CancellationToken cancellationToken);
    Task<PayrollEmployeePaymentDestinationResponse> VersionAsync(
        long businessId, long actorUserId, long employeeId, long currentDestinationId,
        long expectedVersion, long locationId, PayrollEmployeePaymentDestinationData data,
        CancellationToken cancellationToken);
    Task<PayrollEmployeePaymentDestinationResponse> DeactivateAsync(
        long businessId, long actorUserId, long employeeId, long currentDestinationId,
        long expectedVersion, DateOnly effectiveFrom, long locationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>> HistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<PayrollEmployeePaymentDestinationResponse> EffectiveAsync(
        long businessId, long employeeId, DateOnly effectiveOn,
        CancellationToken cancellationToken);
}

public interface IPayrollEmployeePaymentDestinationAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken);
    Task EnsureAsync(
        long businessId, long locationId, string permission, CancellationToken cancellationToken);
    bool HasBusinessPermission(string permission);
}

public sealed record CreatePayrollEmployeePaymentDestinationCommand(
    long EmployeeId, CreatePayrollEmployeePaymentDestinationRequest Data)
    : IRequest<PayrollEmployeePaymentDestinationResponse>;

public sealed record VersionPayrollEmployeePaymentDestinationCommand(
    long EmployeeId, long DestinationId, VersionPayrollEmployeePaymentDestinationRequest Data)
    : IRequest<PayrollEmployeePaymentDestinationResponse>;

public sealed record DeactivatePayrollEmployeePaymentDestinationCommand(
    long EmployeeId, long DestinationId, DeactivatePayrollEmployeePaymentDestinationRequest Data)
    : IRequest<PayrollEmployeePaymentDestinationResponse>;

public sealed record GetPayrollEmployeePaymentDestinationHistoryQuery(long EmployeeId)
    : IRequest<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>>;

public sealed record GetEffectivePayrollEmployeePaymentDestinationQuery(
    long EmployeeId, DateOnly? EffectiveOn)
    : IRequest<PayrollEmployeePaymentDestinationResponse>;

public sealed class PayrollEmployeePaymentDestinationHandlers(
    IPayrollEmployeePaymentDestinationAccess access,
    IPayrollSalarySetupStore salaries,
    IPayrollEmployeePaymentDestinationStore store,
    IRequiredAuditTransaction audit,
    TimeProvider clock) :
    IRequestHandler<CreatePayrollEmployeePaymentDestinationCommand,
        PayrollEmployeePaymentDestinationResponse>,
    IRequestHandler<VersionPayrollEmployeePaymentDestinationCommand,
        PayrollEmployeePaymentDestinationResponse>,
    IRequestHandler<DeactivatePayrollEmployeePaymentDestinationCommand,
        PayrollEmployeePaymentDestinationResponse>,
    IRequestHandler<GetPayrollEmployeePaymentDestinationHistoryQuery,
        IReadOnlyList<PayrollEmployeePaymentDestinationResponse>>,
    IRequestHandler<GetEffectivePayrollEmployeePaymentDestinationQuery,
        PayrollEmployeePaymentDestinationResponse>
{
    public async Task<PayrollEmployeePaymentDestinationResponse> Handle(
        CreatePayrollEmployeePaymentDestinationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        var data = NormalizeAndValidate(command.Data.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.EmployeeId, data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(businessId, locationId,
            "payroll.payment_destination.manage", cancellationToken);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment_destination.created", "Payroll",
                "Created a Payroll payment destination version.", "HrEmployee",
                command.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CreateAsync(businessId, actorUserId, command.EmployeeId, locationId,
                data, token),
            cancellationToken);
        return MaskIfNeeded(response);
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> Handle(
        VersionPayrollEmployeePaymentDestinationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        var data = NormalizeAndValidate(command.Data.Data);
        if (command.Data.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.Data.ExpectedVersion));
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.EmployeeId, data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(businessId, locationId,
            "payroll.payment_destination.manage", cancellationToken);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment_destination.versioned", "Payroll",
                "Created a successor Payroll payment destination version.", "HrEmployee",
                command.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.VersionAsync(businessId, actorUserId, command.EmployeeId,
                command.DestinationId, command.Data.ExpectedVersion, locationId, data, token),
            cancellationToken);
        return MaskIfNeeded(response);
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> Handle(
        DeactivatePayrollEmployeePaymentDestinationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Data);
        if (command.Data.ExpectedVersion <= 0 || command.Data.EffectiveFrom == default)
            throw new ArgumentException("Expected version and a valid effective date are required.");
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, command.EmployeeId, command.Data.EffectiveFrom, cancellationToken);
        await access.EnsureAsync(businessId, locationId,
            "payroll.payment_destination.manage", cancellationToken);
        var response = await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.payment_destination.deactivated", "Payroll",
                "Ended a Payroll payment destination by appending an inactive version.", "HrEmployee",
                command.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.DeactivateAsync(businessId, actorUserId, command.EmployeeId,
                command.DestinationId, command.Data.ExpectedVersion, command.Data.EffectiveFrom,
                locationId, token),
            cancellationToken);
        return MaskIfNeeded(response);
    }

    public async Task<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>> Handle(
        GetPayrollEmployeePaymentDestinationHistoryQuery query, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var permission = "payroll.payment_destination.view";
        await access.EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId,
                "payroll.payment_destination.history_accessed", "Payroll",
                "Accessed Payroll payment destination history.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var rows = await store.HistoryAsync(businessId, query.EmployeeId, token);
                foreach (var row in rows)
                {
                    var employeeLocation = await salaries.EmployeePrimaryLocationAsync(
                        businessId, query.EmployeeId, row.EffectiveFrom, token);
                    if (employeeLocation != row.OperationalLocationProjectionId)
                        throw new UnauthorizedAccessException(
                            "Payment destination history is outside the employee's recorded primary location.");
                    await access.EnsureAsync(businessId,
                        row.OperationalLocationProjectionId, permission, token);
                }
                return rows.Select(MaskIfNeeded).ToArray();
            },
            cancellationToken);
    }

    public async Task<PayrollEmployeePaymentDestinationResponse> Handle(
        GetEffectivePayrollEmployeePaymentDestinationQuery query,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var effectiveOn = query.EffectiveOn ?? Today();
        var permission = "payroll.payment_destination.view";
        await access.EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);
        var employeeLocation = await salaries.EmployeePrimaryLocationAsync(
            businessId, query.EmployeeId, effectiveOn, cancellationToken);
        await access.EnsureAsync(businessId, employeeLocation, permission, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId,
                "payroll.payment_destination.accessed", "Payroll",
                "Accessed an effective Payroll payment destination.", "HrEmployee",
                query.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var row = await store.EffectiveAsync(businessId, query.EmployeeId, effectiveOn, token);
                if (row.OperationalLocationProjectionId != employeeLocation)
                    throw new UnauthorizedAccessException(
                        "Payment destination is outside the employee's primary location scope.");
                await access.EnsureAsync(businessId, row.OperationalLocationProjectionId,
                    permission, token);
                return MaskIfNeeded(row);
            },
            cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private PayrollEmployeePaymentDestinationResponse MaskIfNeeded(
        PayrollEmployeePaymentDestinationResponse row)
    {
        if (access.HasBusinessPermission("payroll.payment_destination.view_sensitive"))
            return row;
        return row with
        {
            AccountName = MaskName(row.AccountName),
            AccountNumber = MaskIdentifier(row.AccountNumber),
            MobileAccountName = MaskName(row.MobileAccountName),
            MobileAccountNumber = MaskIdentifier(row.MobileAccountNumber)
        };
    }

    private static string? MaskName(string? value) => string.IsNullOrWhiteSpace(value)
        ? value
        : "***";

    private static string? MaskIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        var visible = value.Length <= 4 ? value : value[^4..];
        return $"****{visible}";
    }

    private static PayrollEmployeePaymentDestinationData NormalizeAndValidate(
        PayrollEmployeePaymentDestinationData? data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.EffectiveFrom == default || data.EffectiveFrom == DateOnly.MaxValue)
            throw new ArgumentException("A valid effective-from date is required.");
        if (string.IsNullOrWhiteSpace(data.Method))
            throw new ArgumentException("A payment destination method is required.");
        var method = data.Method.Trim();
        if (method is not (PayrollPaymentDestinationMethods.Cash
            or PayrollPaymentDestinationMethods.Bank or PayrollPaymentDestinationMethods.Mobile))
            throw new ArgumentException("Payment destination method must be Cash, Bank, or Mobile.");

        var bankName = NormalizeOptional(data.BankName, 150, nameof(data.BankName));
        var accountName = NormalizeOptional(data.AccountName, 150, nameof(data.AccountName));
        var accountNumber = NormalizeOptional(data.AccountNumber, 200, nameof(data.AccountNumber));
        var provider = NormalizeOptional(data.MobileProvider, 100, nameof(data.MobileProvider));
        var mobileName = NormalizeOptional(data.MobileAccountName, 150, nameof(data.MobileAccountName));
        var mobileNumber = NormalizeOptional(data.MobileAccountNumber, 100,
            nameof(data.MobileAccountNumber));
        switch (method)
        {
            case PayrollPaymentDestinationMethods.Cash:
                return new(method, data.EffectiveFrom, null, null, null, null, null, null);
            case PayrollPaymentDestinationMethods.Bank when bankName is not null
                && accountName is not null && accountNumber is not null:
                return new(method, data.EffectiveFrom, bankName, accountName, accountNumber,
                    null, null, null);
            case PayrollPaymentDestinationMethods.Mobile when provider is not null
                && mobileName is not null && mobileNumber is not null:
                return new(method, data.EffectiveFrom, null, null, null,
                    provider, mobileName, mobileNumber);
            case PayrollPaymentDestinationMethods.Bank:
                throw new ArgumentException("Bank name, account name, and account number are required.");
            default:
                throw new ArgumentException(
                    "Mobile provider, account name, and account number are required.");
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}

public sealed class PayrollEmployeePaymentDestinationConflictException(
    string message, Exception? innerException = null)
    : Exception(message, innerException);
