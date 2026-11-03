using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.UnitTests.HR;

public sealed class PayrollSalaryFormulaRulesTests
{
    [Fact]
    public void RestrictedFormulaGrammarExtractsComponentReferences()
    {
        var references = PayrollSalaryFormulaRules.ParseReferences("(basic + 1250.50) * rate / 2");

        Assert.Equal(new[] { "BASIC", "RATE" }, references.OrderBy(value => value));
    }

    [Fact]
    public void FormulaEvaluationUsesDecimalPrecedenceAndComponentReferences()
    {
        var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["base"] = 100m
        };

        Assert.Equal(20m, PayrollSalaryFormulaEvaluator.Evaluate("(BASE + 100) * 0.1", values));
        Assert.Equal(23m, PayrollSalaryFormulaEvaluator.Evaluate("BASE / 4 - 2", values));
        Assert.Equal(-98m, PayrollSalaryFormulaEvaluator.Evaluate("-BASE + 2", values));
    }

    [Fact]
    public void FormulaEvaluationRejectsMissingReferencesAndDivisionByZero()
    {
        var values = new Dictionary<string, decimal>();

        Assert.Throws<ArgumentException>(() =>
            PayrollSalaryFormulaEvaluator.Evaluate("MISSING + 1", values));
        Assert.Throws<ArgumentException>(() =>
            PayrollSalaryFormulaEvaluator.Evaluate("10 / (2 - 2)", values));
    }

    [Theory]
    [InlineData("SUM(BASIC)")]
    [InlineData("BASIC + External.Rate")]
    [InlineData("BASIC ** 2")]
    [InlineData("1e5 + BASIC")]
    [InlineData("1 +")]
    [InlineData("1..5")]
    public void FormulaGrammarRejectsFunctionsAndUnsupportedTokens(string expression) =>
        Assert.Throws<ArgumentException>(() => PayrollSalaryFormulaRules.ParseReferences(expression));

    [Fact]
    public void ComponentGraphRequiresActiveReferencesAndRejectsCycles()
    {
        var basePay = Component(1, "BASE", "Fixed");
        var allowance = Component(2, "ALLOWANCE", "Formula", "BASE * 0.1");
        PayrollSalaryFormulaRules.ValidateComponentGraph([basePay, allowance]);

        Assert.Throws<ArgumentException>(() => PayrollSalaryFormulaRules.ValidateComponentGraph(
            [basePay, Component(3, "INVALID", "Formula", "MISSING + 1")]));
        basePay.IsActive = false;
        Assert.Throws<ArgumentException>(() =>
            PayrollSalaryFormulaRules.ValidateComponentGraph([basePay, allowance]));

        Assert.Throws<ArgumentException>(() => PayrollSalaryFormulaRules.ValidateComponentGraph(
        [
            Component(4, "FIRST", "Formula", "SECOND + 1"),
            Component(5, "SECOND", "Formula", "FIRST + 1")
        ]));
    }

    [Fact]
    public void StructureFormulaMustReferenceComponentsInTheSameTemplateAndMustBeAcyclic()
    {
        var basePay = Component(10, "BASE", "Fixed");
        var allowance = Component(11, "ALLOWANCE", "Formula", "BASE * 0.1");
        var lines = new[]
        {
            Line(10, 1),
            Line(11, 2)
        };

        PayrollSalaryFormulaRules.ValidateStructureFormulas(lines, [basePay, allowance]);
        Assert.Throws<ArgumentException>(() => PayrollSalaryFormulaRules.ValidateStructureFormulas(
            [Line(11, 1)], [basePay, allowance]));
        Assert.Throws<ArgumentException>(() => PayrollSalaryFormulaRules.ValidateStructureFormulas(
            [Line(10, 1, "ALLOWANCE"), Line(11, 2, "BASE")], [basePay, allowance]));
    }

    private static PayrollSalaryComponent Component(
        long id, string code, string valueType, string? formula = null) =>
        new()
        {
            Id = id,
            BusinessId = 1,
            Code = code,
            Name = code,
            ComponentType = "Earning",
            ValueType = valueType,
            FormulaExpression = formula
        };

    private static PayrollSalaryStructureLine Line(
        long componentId, int order, string? formulaOverride = null) =>
        new()
        {
            BusinessId = 1,
            StructureId = 1,
            SalaryComponentId = componentId,
            CalculationOrder = order,
            FormulaOverride = formulaOverride
        };
}
