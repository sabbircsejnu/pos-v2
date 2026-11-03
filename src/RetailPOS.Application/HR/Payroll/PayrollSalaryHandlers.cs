using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record GetPayrollSalaryComponentsQuery(string? Search = null)
    : IRequest<IReadOnlyList<PayrollSalaryComponentResponse>>;
public sealed record GetPayrollSalaryComponentQuery(long Id) : IRequest<PayrollSalaryComponentResponse>;
public sealed record SavePayrollSalaryComponentCommand(
    long? Id, long? ExpectedVersion, PayrollSalaryComponentData? Data)
    : IRequest<PayrollSalaryComponentResponse>;
public sealed record GetPayrollSalaryStructuresQuery(string? Search = null)
    : IRequest<IReadOnlyList<PayrollSalaryStructureResponse>>;
public sealed record GetPayrollSalaryStructureQuery(long Id) : IRequest<PayrollSalaryStructureResponse>;
public sealed record SavePayrollSalaryStructureCommand(
    long? Id, long? ExpectedVersion, PayrollSalaryStructureData? Data)
    : IRequest<PayrollSalaryStructureResponse>;

public sealed class PayrollSalaryHandlers(
    IHrPayrollCapabilityScope scope,
    IHrPayrollCapabilityService capabilities,
    IPayrollSalarySetupStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<GetPayrollSalaryComponentsQuery, IReadOnlyList<PayrollSalaryComponentResponse>>,
    IRequestHandler<GetPayrollSalaryComponentQuery, PayrollSalaryComponentResponse>,
    IRequestHandler<SavePayrollSalaryComponentCommand, PayrollSalaryComponentResponse>,
    IRequestHandler<GetPayrollSalaryStructuresQuery, IReadOnlyList<PayrollSalaryStructureResponse>>,
    IRequestHandler<GetPayrollSalaryStructureQuery, PayrollSalaryStructureResponse>,
    IRequestHandler<SavePayrollSalaryStructureCommand, PayrollSalaryStructureResponse>
{
    public async Task<IReadOnlyList<PayrollSalaryComponentResponse>> Handle(
        GetPayrollSalaryComponentsQuery request, CancellationToken cancellationToken)
    {
        var businessId = await EnsurePayroll(cancellationToken);
        return await store.ComponentsAsync(businessId, request.Search, cancellationToken);
    }

    public async Task<PayrollSalaryComponentResponse> Handle(
        GetPayrollSalaryComponentQuery request, CancellationToken cancellationToken)
    {
        var businessId = await EnsurePayroll(cancellationToken);
        return await store.ComponentAsync(businessId, request.Id, cancellationToken);
    }

    public async Task<PayrollSalaryComponentResponse> Handle(
        SavePayrollSalaryComponentCommand request, CancellationToken cancellationToken)
    {
        ValidateIdentity(request.Id, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
            ValidateComponent(data);

        var businessId = await EnsurePayroll(cancellationToken);
        return await Write(businessId, "PayrollSalaryComponent", request.Id,
            request.Data is null ? "deactivate" : request.Id.HasValue ? "update" : "create",
            token => store.SaveComponentAsync(
                businessId, request.Id, request.ExpectedVersion, request.Data, token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollSalaryStructureResponse>> Handle(
        GetPayrollSalaryStructuresQuery request, CancellationToken cancellationToken)
    {
        var businessId = await EnsurePayroll(cancellationToken);
        return await store.StructuresAsync(businessId, request.Search, cancellationToken);
    }

    public async Task<PayrollSalaryStructureResponse> Handle(
        GetPayrollSalaryStructureQuery request, CancellationToken cancellationToken)
    {
        var businessId = await EnsurePayroll(cancellationToken);
        return await store.StructureAsync(businessId, request.Id, cancellationToken);
    }

    public async Task<PayrollSalaryStructureResponse> Handle(
        SavePayrollSalaryStructureCommand request, CancellationToken cancellationToken)
    {
        ValidateIdentity(request.Id, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
            ValidateStructure(data);

        var businessId = await EnsurePayroll(cancellationToken);
        return await Write(businessId, "PayrollSalaryStructure", request.Id,
            request.Data is null ? "deactivate" : request.Id.HasValue ? "update" : "create",
            token => store.SaveStructureAsync(
                businessId, request.Id, request.ExpectedVersion, request.Data, token),
            cancellationToken);
    }

    private async Task<long> EnsurePayroll(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
        return scope.RequireBusinessId();
    }

    private Task<T> Write<T>(
        long businessId,
        string entity,
        long? id,
        string action,
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken) =>
        audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, action, "payroll", $"{action} {entity}", entity, id?.ToString() ?? "new"),
            write,
            cancellationToken);

    private static void ValidateComponent(PayrollSalaryComponentData data)
    {
        Text(data.Code, 50, "Component code");
        var code = data.Code.Trim();
        if (!char.IsAsciiLetter(code[0]) && code[0] != '_'
            || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException(
                "Component codes must start with an ASCII letter or underscore and contain only ASCII letters, digits or underscores.");
        Text(data.Name, 100, "Component name");
        if (data.ComponentType is not ("Earning" or "Deduction"))
            throw new ArgumentException("Component type must be Earning or Deduction.");
        if (data.ValueType is not ("Fixed" or "Percentage" or "Formula" or "Calculated"))
            throw new ArgumentException("Value type must be Fixed, Percentage, Formula or Calculated.");
        if (data.CalculationOrder < 0)
            throw new ArgumentException("Calculation order cannot be negative.");
        if (data.TaxTreatmentCode?.Length > 50)
            throw new ArgumentException("Tax treatment code cannot exceed 50 characters.");

        if (data.ValueType == "Formula")
            PayrollSalaryFormulaRules.ParseReferences(data.FormulaExpression
                ?? throw new ArgumentException("Formula components require an expression."));
        else if (data.FormulaExpression is not null)
            throw new ArgumentException("Only Formula components may specify a formula expression.");
    }

    private static void ValidateStructure(PayrollSalaryStructureData data)
    {
        Text(data.Code, 50, "Structure code");
        Text(data.Name, 100, "Structure name");
        ArgumentNullException.ThrowIfNull(data.Lines);
        if (data.Lines.Count is < 1 or > 100
            || data.Lines.Any(line => line.SalaryComponentId <= 0 || line.CalculationOrder < 0)
            || data.Lines.Select(line => line.SalaryComponentId).Distinct().Count() != data.Lines.Count)
            throw new ArgumentException("A salary structure requires 1-100 distinct valid component lines.");
        if (data.Lines.Any(line => line.FormulaOverride?.Length > 1000))
            throw new ArgumentException("Formula overrides cannot exceed 1000 characters.");
        foreach (var line in data.Lines.Where(line => line.FormulaOverride is not null))
            PayrollSalaryFormulaRules.ParseReferences(line.FormulaOverride!);
    }

    private static void Text(string value, int limit, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit)
            throw new ArgumentException($"{field} is required and must not exceed {limit} characters.");
    }

    private static void ValidateIdentity(long? id, long? version, bool deactivating)
    {
        if (id is <= 0 || version is <= 0 || id.HasValue != version.HasValue
            || deactivating && !id.HasValue)
            throw new ArgumentException("Identity and expected concurrency version must be supplied together.");
    }
}
