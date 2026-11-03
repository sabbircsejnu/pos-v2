namespace RetailPOS.Application.HR.Payroll;

public sealed class PayrollPayslipInfrastructureUnavailableException(string message)
    : InvalidOperationException(message);
