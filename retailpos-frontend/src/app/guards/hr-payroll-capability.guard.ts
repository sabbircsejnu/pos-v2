import { inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { environment } from '../../environments/environment';
import { HrPayrollCapabilities, HrPayrollCapability, HrPayrollCapabilityStore } from '../services/hr-payroll-capability.store';

// Pair with routePermissionGuard; a permission or wildcard never replaces a capability.
export function hrPayrollCapabilityGuard(capability: HrPayrollCapability): CanActivateFn {
  return () => {
    const store = inject(HrPayrollCapabilityStore);
    const http = inject(HttpClient);
    const router = inject(Router);
    store.reset();
    const generation = store.generation;
    return http.get<HrPayrollCapabilities>(`${environment.apiUrl}/hr/v1/capabilities`).pipe(
      map(value => {
        if (store.generation !== generation) {
          return router.createUrlTree(['/dashboard'], { queryParams: { hrPayrollContextChanged: '1' } });
        }
        store.setVerified(value);
        return store.isEnabled(capability) || router.createUrlTree(['/dashboard'],
          { queryParams: { hrPayrollDisabled: '1' } });
      }),
      catchError((error: unknown) => {
        store.reset();
        console.error('HR/Payroll capability verification failed.', error);
        return of(router.createUrlTree(['/dashboard'], { queryParams: { hrPayrollVerificationFailed: '1' } }));
      })
    );
  };
}
