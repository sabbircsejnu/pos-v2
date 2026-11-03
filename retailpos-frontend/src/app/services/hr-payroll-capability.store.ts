import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type HrPayrollCapability = 'hr.enabled' | 'payroll.enabled' | 'attendance.enabled' | 'deviceintegration.enabled';

export interface HrPayrollCapabilities {
  businessId: number;
  hrEnabled: boolean;
  payrollEnabled: boolean;
  attendanceEnabled: boolean;
  deviceIntegrationEnabled: boolean;
}

@Injectable({ providedIn: 'root' })
export class HrPayrollCapabilityStore {
  private readonly state = new BehaviorSubject<HrPayrollCapabilities | null>(null);
  readonly changed$ = this.state.asObservable();
  private revision = 0;

  get generation(): number {
    return this.revision;
  }

  reset(): void {
    this.revision++;
    this.state.next(null);
  }

  setVerified(value: HrPayrollCapabilities): void {
    if (!Number.isSafeInteger(value.businessId) || value.businessId <= 0) {
      this.reset();
      throw new Error('Capability response has no valid Business scope.');
    }
    this.state.next(value);
  }

  isEnabled(capability: HrPayrollCapability): boolean {
    const value = this.state.value;
    if (!value || value.hrEnabled !== true) return false;
    switch (capability) {
      case 'hr.enabled': return true;
      case 'payroll.enabled': return value.payrollEnabled === true;
      case 'attendance.enabled': return value.attendanceEnabled === true;
      case 'deviceintegration.enabled':
        return value.attendanceEnabled === true && value.deviceIntegrationEnabled === true;
    }
  }
}
