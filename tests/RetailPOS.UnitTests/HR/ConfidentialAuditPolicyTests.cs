using RetailPOS.Core.Audit;

namespace RetailPOS.UnitTests.HR;

public sealed class ConfidentialAuditPolicyTests
{
    [Theory]
    [InlineData("EmployeeDocument", true, false)]
    [InlineData("EmployeeIdentity", true, false)]
    [InlineData("EmployeeSalary", true, true)]
    [InlineData("SalaryRevision", false, true)]
    [InlineData("PayrollExport", false, true)]
    [InlineData("PayslipArtifact", false, true)]
    [InlineData("ApprovalPolicy", true, true)]
    [InlineData("ApprovalPolicyStep", true, true)]
    [InlineData("ApprovalInstance", true, true)]
    [InlineData("ApprovalInstanceStep", true, true)]
    [InlineData("ApprovalDecision", true, true)]
    [InlineData("Product", false, false)]
    public void ClassifiesMixedAndLegacyEvents(string entity, bool hr, bool payroll)
    {
        Assert.Equal(hr, ConfidentialAuditPolicy.IsHr("system", entity));
        Assert.Equal(payroll, ConfidentialAuditPolicy.IsPayroll("system", entity));
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("0123", "0123")]
    [InlineData("identity-number-not-a-reference", ConfidentialAuditPolicy.RedactedToken)]
    public void ReferencesCannotExposeArbitraryConfidentialText(string input, string expected) =>
        Assert.Equal(expected, ConfidentialAuditPolicy.SafeReference(input));

    [Theory]
    [InlineData("Salary", false)]
    [InlineData("BankAccountNumber", false)]
    [InlineData("MobilePaymentNumber", false)]
    [InlineData("NationalId", false)]
    [InlineData("FileName", false)]
    [InlineData("StorageKey", false)]
    [InlineData("Status", true)]
    [InlineData("RecordCount", true)]
    public void OnlySafeAuditEvidenceIsPreserved(string field, bool expected) =>
        Assert.Equal(expected, ConfidentialAuditPolicy.IsSafeField(field));
}
