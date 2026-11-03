import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Observable } from 'rxjs';
import { hrPayrollCapabilityGuard } from './hr-payroll-capability.guard';
import { HrPayrollCapabilityStore } from '../services/hr-payroll-capability.store';
import { environment } from '../../environments/environment';

describe('HR/Payroll route capability guard', () => {
  const response = { businessId: 1, hrEnabled: true, payrollEnabled: true,
    attendanceEnabled: false, deviceIntegrationEnabled: false };
  const denied = new UrlTree();
  let http: HttpTestingController;
  let store: HrPayrollCapabilityStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: Router, useValue: { createUrlTree: () => denied } }
    ] });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(HrPayrollCapabilityStore);
  });
  afterEach(() => http.verify());

  function run(): Observable<boolean | UrlTree> {
    return TestBed.runInInjectionContext(() => hrPayrollCapabilityGuard('payroll.enabled')(
      new ActivatedRouteSnapshot(), {} as RouterStateSnapshot)) as Observable<boolean | UrlTree>;
  }

  it('refetches on each route entry rather than trusting a cached grant', () => {
    let result: boolean | UrlTree | undefined;
    run().subscribe(value => result = value);
    http.expectOne(`${environment.apiUrl}/hr/v1/capabilities`).flush(response);
    expect(result).toBe(true);
    run().subscribe(value => result = value);
    expect(store.isEnabled('payroll.enabled')).toBe(false);
    http.expectOne(`${environment.apiUrl}/hr/v1/capabilities`).flush({ ...response, payrollEnabled: false });
    expect(result).toBe(denied);
  });

  it('does not restore an old response after Business/user context invalidation', () => {
    let result: boolean | UrlTree | undefined;
    run().subscribe(value => result = value);
    store.reset();
    http.expectOne(`${environment.apiUrl}/hr/v1/capabilities`).flush(response);
    expect(result).toBe(denied);
    expect(store.isEnabled('payroll.enabled')).toBe(false);
  });
});
