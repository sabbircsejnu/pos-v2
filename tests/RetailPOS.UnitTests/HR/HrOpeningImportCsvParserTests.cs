using System.Text;
using RetailPOS.Application.HR.Employees;

namespace RetailPOS.UnitTests.HR;

public sealed class HrOpeningImportCsvParserTests
{
    private readonly HrOpeningImportCsvParser parser = new();

    [Theory]
    [InlineData("LeaveOpening")]
    [InlineData("Payroll")]
    [InlineData("Attendance")]
    public void LaterImportTypesAreRejected(string type) =>
        Assert.Throws<ArgumentException>(() => parser.Template(type, "1"));

    [Fact]
    public void TemplateVersionIsExplicit() =>
        Assert.Throws<ArgumentException>(() => parser.Template("Employee", "2"));

    [Fact]
    public void ValidQuotedCsvUsesExistingEmployeeValidation()
    {
        var row = Parse("ext,EMP,\"Alex, Employee\",\"Alex \"\"Employee\"\"\",1,Active,2026-10-01,,,,,,");
        Assert.Empty(row.Errors);
        Assert.Equal("Alex, Employee", row.Employee!.FirstName);
        Assert.Equal("Alex \"Employee\"", row.Employee.DisplayName);
    }

    [Theory]
    [InlineData("ext,EMP,,Name,1,Active,2026-10-01,,,,,,")]
    [InlineData("ext,EMP,Alex,,1,Active,2026-10-01,,,,,,")]
    [InlineData("ext,EMP,Alex,Name,0,Active,2026-10-01,,,,,,")]
    [InlineData("ext,EMP,Alex,Name,1,Exited,2026-10-01,,,,,,")]
    [InlineData("ext,EMP,Alex,Name,1,Active,not-a-date,,,,,,")]
    [InlineData("ext,EMP,Alex,Name,1,Active,2026-10-01,7,,,,,")]
    [InlineData("ext,EMP,Alex,Name,1,Active,2026-10-01,,,1,Temporary,,")]
    public void InvalidRowsRetainValidationEvidence(string csv) => Assert.NotEmpty(Parse(csv).Errors);

    [Fact]
    public void DuplicateCodesAndKeysAreRejectedOnEveryAffectedRow()
    {
        var rows = parser.Parse(Encoding.UTF8.GetBytes(parser.Template("Employee", "1")
            + "ext,EMP,Alex,Name,1,Active,2026-10-01,,,,,,\n"
            + "ext,emp,Other,Name,1,Active,2026-10-01,,,,,,"), "Employee", "1", 10);
        Assert.All(rows, row =>
        {
            Assert.Contains("Duplicate EmployeeCode in file.", row.Errors);
            Assert.Contains("Duplicate ExternalKey in file.", row.Errors);
        });
    }

    [Fact]
    public void AssignmentUsesProjectionNotDirectOutletReference()
    {
        var row = Assert.Single(parser.Parse(Encoding.UTF8.GetBytes(parser.Template("Assignment", "1")
            + "ext,EMP,7,Additional,2026-10-01"), "Assignment", "1", 10));
        Assert.Null(row.Employee);
        Assert.Equal(7, row.LocationId);
        Assert.Equal("Additional", row.AssignmentType);
        Assert.Empty(row.Errors);
    }

    [Fact]
    public void SalaryTemplateParsesImmutableSetupProposal()
    {
        var csv = parser.Template("Salary", "1")
            + "salary-1,EMP,42,2026-10-01,\"[{\"\"salaryComponentId\"\":5,\"\"value\"\":1250.5000}]\",Opening salary,REF-1";
        var row = Assert.Single(parser.Parse(Encoding.UTF8.GetBytes(csv), "Salary", "1", 10));
        Assert.Empty(row.Errors);
        Assert.Equal(42, row.Salary!.SalaryStructureId);
        Assert.Equal(new DateOnly(2026, 10, 1), row.Salary.EffectiveFrom);
        Assert.Equal(5, Assert.Single(row.Salary.Components).SalaryComponentId);
        Assert.Equal(1250.5m, row.Salary.Components[0].Value);
    }

    [Fact]
    public void LoanOpeningTemplateParsesHistoricalBalanceWithoutGuessingDisbursement()
    {
        var csv = parser.Template("LoanOpening", "1")
            + "loan-1,EMP,Advance,1000,0,2025-04-01,2026-10-01,CustomSchedule,,[],Opening balance,REF-1";
        var row = Assert.Single(parser.Parse(Encoding.UTF8.GetBytes(csv), "LoanOpening", "1", 10));
        Assert.Empty(row.Errors);
        Assert.Equal(0, row.Loan!.OutstandingAmount);
        Assert.Equal(new DateOnly(2025, 4, 1), row.Loan.DisbursedDate);
        Assert.Equal("CustomSchedule", row.Loan.Request.RecoveryMode);
        Assert.Empty(row.Loan.Request.Schedule);
    }

    [Fact]
    public void SizeOfRowSetIsBounded() =>
        Assert.Throws<ArgumentException>(() => parser.Parse(Encoding.UTF8.GetBytes(parser.Template("Assignment", "1")
            + "a,E1,1,Primary,2026-10-01\nb,E2,1,Primary,2026-10-01"), "Assignment", "1", 1));

    [Fact]
    public void ActualBinaryContentIsRejected() =>
        Assert.Throws<ArgumentException>(() => parser.Parse(new byte[] { 0xff, 0xfe, 0x00 }, "Employee", "1", 10));

    [Fact]
    public void UnapprovedColumnsAreRejected() =>
        Assert.Throws<ArgumentException>(() => parser.Parse(Encoding.UTF8.GetBytes(
            "EmployeeCode,Salary\nEMP,100"), "Employee", "1", 10));

    [Fact]
    public void InvalidQuoteIsRejected() =>
        Assert.Throws<ArgumentException>(() => Parse("ext,EMP,\"unterminated"));

    private HrOpeningImportRow Parse(string csv) =>
        Assert.Single(parser.Parse(Encoding.UTF8.GetBytes(parser.Template("Employee", "1") + csv),
            "Employee", "1", 10));
}
