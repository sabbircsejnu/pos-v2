import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  // Store the attempted URL for redirecting
  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
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

    if (!authService.isAuthenticated()) {
      router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
      return false;
    }

    if (authService.hasAnyPermission(permissions)) {
      return true;
    }

    router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
    return false;
  };
};

export const routePermissionGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const permissions = (route.data?.['permissions'] as string[] | undefined) ?? [];
  const mode = ((route.data?.['permissionMode'] as 'any' | 'all' | undefined) ?? 'any');

  if (permissions.length === 0) {
    return true;
  }

  const hasAccess = mode === 'all'
    ? authService.hasAllPermissions(permissions)
    : authService.hasAnyPermission(permissions);

  if (hasAccess) {
    return true;
  }

  router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
  return false;
};

export const roleGuard = (roles: string[]): CanActivateFn => {
  return (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
      return false;
    }

    const user = authService.getUserValue();
    if (user?.roleName && roles.includes(user.roleName)) {
      return true;
    }

    router.navigate(['/dashboard'], { queryParams: { unauthorized: '1' } });
    return false;
  };
};
