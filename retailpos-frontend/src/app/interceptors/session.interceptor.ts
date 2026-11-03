import { HttpContextToken, HttpEvent, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { isApiRequest } from './auth.interceptor';

/** Marks a request that was already replayed once after a silent token refresh. */
export const SESSION_RETRIED = new HttpContextToken<boolean>(() => false);

/** Endpoints that establish or end a session; a 401 from them is a credential outcome, not an expired token. */
const CREDENTIAL_PATHS = ['/auth/login', '/auth/complete-invitation', '/auth/refresh-token', '/auth/logout', '/auth/logout-all'];

function isCredentialRequest(url: string): boolean {
  try {
    const path = new URL(url, 'http://localhost').pathname.toLowerCase().replace(/\/+$/, '');
    return CREDENTIAL_PATHS.some(p => path.endsWith(p));
  } catch {
    return false;
  }
}

/**
 * Restores an expired session on HTTP 401 and nothing else.
 *
 * - 401 = unauthenticated: refresh the access token once (single flight in AuthService) and replay
 *   the request. Only when the session genuinely cannot be restored is the user signed out.
 * - 403 and every other status pass through untouched: a permission problem never signs the user
 *   out, drops tokens or resets Business/Outlet context.
 */
export const sessionInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!isApiRequest(req.url) || isCredentialRequest(req.url)) {
    return next(req);
  }

  const tokenUsed = authService.getToken();

  return next(req).pipe(
    catchError(error => {
      if (error?.status !== 401 || req.context.get(SESSION_RETRIED)) {
        return throwError(() => error);
      }

      const replay = (): Observable<HttpEvent<unknown>> =>
        next(req.clone({ context: req.context.set(SESSION_RETRIED, true) }));

      // Another request already refreshed the session after this one was sent: just replay it.
      if (tokenUsed !== authService.getToken() && authService.getToken()) {
        return replay();
      }

      if (!authService.hasRecoverableSession()) {
        endSession(authService, router);
        return throwError(() => error);
      }

      return authService.refreshSession().pipe(
        catchError(refreshError => {
          // Only a definitive rejection ends the session; offline or 5xx keeps it for a later retry.
          if (refreshError?.status === 400 || refreshError?.status === 401 || refreshError?.status === 403) {
            endSession(authService, router);
          }
          return throwError(() => error);
        }),
        switchMap(() => replay())
      );
    })
  );
};

function endSession(authService: AuthService, router: Router): void {
  const returnUrl = router.url && router.url !== '/' && !router.url.startsWith('/login') ? router.url : undefined;
  authService.logout();
  void router.navigate(['/login'], returnUrl ? { queryParams: { returnUrl } } : undefined);
}
