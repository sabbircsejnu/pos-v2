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

    router.navigate(['/unauthorized']);
    return false;
  };
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

    router.navigate(['/unauthorized']);
    return false;
  };
};
