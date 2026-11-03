import { inject } from '@angular/core';
import { GuardResult, Router, RouterStateSnapshot, type CanActivateFn } from '@angular/router';
import { Observable, isObservable, map, of, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { BusinessContextStore } from '../services/business-context.store';
import { BusinessContextService } from '../services/business-context.service';

/**
 * Runs `activate` once the session is valid. An expired access token is silently restored through
 * the single-flight refresh first; the user is sent to login only when that is not possible.
 */
function activateWhenAuthenticated(
  authService: AuthService,
  router: Router,
  state: RouterStateSnapshot,
  activate: () => GuardResult | Observable<GuardResult>
): GuardResult | Observable<GuardResult> {
  const toLogin = (): false => {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  };

  if (authService.isAuthenticated()) {
    return activate();
  }
  if (!authService.hasRecoverableSession()) {
    return toLogin();
  }

  return authService.ensureAuthenticated().pipe(
    switchMap(restored => {
      if (!restored) {
        return of(toLogin());
      }
      const result = activate();
      return isObservable(result) ? result : of(result);
    })
  );
}

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return activateWhenAuthenticated(authService, router, state, () => true);
};

export const loginGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return true;
  }

  // Already logged in, redirect to dashboard
  router.navigate(['/dashboard']);
  return false;
};

export const permissionGuard = (permissions: string[]): CanActivateFn => {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    return activateWhenAuthenticated(authService, router, state, () => {
      if (authService.hasAnyPermission(permissions)) {
        return true;
      }

      router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
      return false;
    });
  };
};

export const routePermissionGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const contextStore = inject(BusinessContextStore);
  const contextService = inject(BusinessContextService);

  return activateWhenAuthenticated(authService, router, state, () => {
    const permissions = (route.data?.['permissions'] as string[] | undefined) ?? [];
    const mode = ((route.data?.['permissionMode'] as 'any' | 'all' | undefined) ?? 'any');

    if (permissions.length === 0) {
      return true;
    }

    const hasAccess = mode === 'all'
      ? authService.hasAllPermissions(permissions)
      : authService.hasAnyPermission(permissions);

    if (!hasAccess) {
      router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
      return false;
    }

    if (!authService.isSuperAdmin()) {
      return true;
    }

    // Permission-gated workflows are Business-scoped. For a Super Admin, make sure restoration of
    // the saved context has run (starting or retrying it if needed) and finished, and only activate
    // once a server-verified one exists, so the page never initialises unscoped (which the API rejects).
    return contextService.ensureRestored().pipe(
      map(() => {
        if (contextStore.getVerifiedBusinessId(authService.getUserValue()?.id) !== null) {
          return true;
        }
        return router.createUrlTree(['/dashboard'], { queryParams: { businessContextRequired: '1' } });
      })
    );
  });
};

export const roleGuard = (roles: string[]): CanActivateFn => {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    return activateWhenAuthenticated(authService, router, state, () => {
      const user = authService.getUserValue();
      if (user?.roleName && roles.includes(user.roleName)) {
        return true;
      }

      router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
      return false;
    });
  };
};
