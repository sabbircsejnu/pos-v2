import { HrPayrollCapabilityStore } from './hr-payroll-capability.store';

describe('HR/Payroll capability state', () => {
  it('defaults OFF and enforces dependencies even for inconsistent responses', () => {
    const store = new HrPayrollCapabilityStore();
    expect(store.isEnabled('payroll.enabled')).toBe(false);
    store.setVerified({ businessId: 1, hrEnabled: false, payrollEnabled: true,
      attendanceEnabled: true, deviceIntegrationEnabled: true });
    expect(store.isEnabled('payroll.enabled')).toBe(false);
    expect(store.isEnabled('deviceintegration.enabled')).toBe(false);
  });

  it('clears on scope invalidation and rejects an unscoped response', () => {
    const store = new HrPayrollCapabilityStore();
    store.setVerified({ businessId: 1, hrEnabled: true, payrollEnabled: true,
      attendanceEnabled: false, deviceIntegrationEnabled: true });
    expect(store.isEnabled('payroll.enabled')).toBe(true);
    expect(store.isEnabled('deviceintegration.enabled')).toBe(false);
    store.reset();
    expect(store.isEnabled('payroll.enabled')).toBe(false);
    expect(() => store.setVerified({ businessId: 0, hrEnabled: true, payrollEnabled: true,
      attendanceEnabled: true, deviceIntegrationEnabled: true })).toThrow();
    expect(store.isEnabled('hr.enabled')).toBe(false);
  });
});
