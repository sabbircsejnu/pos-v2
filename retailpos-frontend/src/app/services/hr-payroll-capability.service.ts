import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SUPPRESS_ERROR_NOTICE } from '../interceptors/error.interceptor';
import { HrPayrollCapabilities } from './hr-payroll-capability.store';

/** Reads the backend's authoritative HR/Payroll feature entitlements for the validated Business. */
@Injectable({ providedIn: 'root' })
export class HrPayrollCapabilityService {
  constructor(private readonly http: HttpClient) {}

  /** Background probe: failures must not raise a global notification (menus simply stay hidden). */
  load(): Observable<HrPayrollCapabilities> {
    return this.http.get<HrPayrollCapabilities>(`${environment.apiUrl}/hr/v1/capabilities`, {
      context: new HttpContext().set(SUPPRESS_ERROR_NOTICE, true)
    });
  }
}
