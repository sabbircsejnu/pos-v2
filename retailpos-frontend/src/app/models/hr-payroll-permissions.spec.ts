import { HR_PAYROLL_PERMISSION_CATEGORIES } from './hr-payroll-permissions';

describe('HR/Payroll role editor categories', () => {
  it('contains unique action codes including sensitive and exceptional actions', () => {
    const codes = HR_PAYROLL_PERMISSION_CATEGORIES.flatMap(category => category.permissions);
    expect(new Set(codes).size).toBe(codes.length);
    expect(codes).toContain('hr.employee.view_identity');
    expect(codes).toContain('payroll.payment.view_sensitive');
    expect(codes).toContain('payroll.period.close_with_exception');
    expect(codes).toContain('payroll.run.approve');
    expect(codes.every(code => code.startsWith('hr.') || code.startsWith('payroll.'))).toBe(true);
  });
});
