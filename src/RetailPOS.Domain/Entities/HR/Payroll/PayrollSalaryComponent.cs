namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollSalaryComponent
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ComponentType { get; set; } = "";
    public string ValueType { get; set; } = "";
    public string? TaxTreatmentCode { get; set; }
    public bool IsRecurring { get; set; }
    public int CalculationOrder { get; set; }
    public string? FormulaExpression { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class PayrollSalaryStructure
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public List<PayrollSalaryStructureLine> Lines { get; set; } = [];
}

public sealed class PayrollSalaryStructureLine
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long StructureId { get; set; }
    public long SalaryComponentId { get; set; }
    public decimal? DefaultValue { get; set; }
    public string? FormulaOverride { get; set; }
    public int CalculationOrder { get; set; }
}
