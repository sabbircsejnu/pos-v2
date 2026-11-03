import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../services/auth.service';
import { BUSINESS_CONTEXT_HEADER, BusinessContextStore, SKIP_BUSINESS_CONTEXT, normalizeUserId } from '../services/business-context.store';

export function isApiRequest(url: string): boolean {
  try {
    const base = typeof location !== 'undefined' && location.origin && location.origin !== 'null'
      ? location.origin
      : 'http://localhost';
    const api = new URL(environment.apiUrl, base);
    const target = new URL(url, base);
    const apiPath = api.pathname.replace(/\/+$/, '');
    return target.origin === api.origin
      && (apiPath === '' || target.pathname === apiPath || target.pathname.startsWith(`${apiPath}/`));
  } catch {
    return false;
  }
}

/** Auth endpoints that never carry Business scope and must not wait for context restoration. */
const UNSCOPED_AUTH_PATHS = ['/auth/login', '/auth/register', '/auth/logout', '/auth/refresh', '/auth/switch-role', '/auth/return-owner', '/auth/business-context'];

/**
 * The profile refresh issued by restoration itself; it carries the verified scope but must not
 * wait for restoration to settle, since restoration settles only after it completes.
 */
function isProfileRequest(url: string): boolean {
  try {
    return new URL(url, 'http://localhost').pathname.toLowerCase().replace(/\/+$/, '').endsWith('/auth/me');
  } catch {
    return false;
  }
}

function isUnscopedAuthRequest(url: string): boolean {
  try {
    const path = new URL(url, 'http://localhost').pathname.toLowerCase().replace(/\/+$/, '');
    return UNSCOPED_AUTH_PATHS.some(p => path.endsWith(p) || path.endsWith(p + '-token'));
  } catch {
    return false;
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const contextStore = inject(BusinessContextStore);
  const token = authService.getToken();

  // Business scope is only ever attached by this interceptor; never trust a caller-supplied value.
  if (req.headers.has(BUSINESS_CONTEXT_HEADER)) {
    req = req.clone({ headers: req.headers.delete(BUSINESS_CONTEXT_HEADER) });
  }

  const setHeaders: Record<string, string> = {};
  if (token) {
    setHeaders['Authorization'] = `Bearer ${token}`;
  }

  const scopeEligible = !req.context.get(SKIP_BUSINESS_CONTEXT) && isApiRequest(req.url) && !isUnscopedAuthRequest(req.url);

  const send = () => {
    const headers = { ...setHeaders };
    if (scopeEligible && authService.isSuperAdmin()) {
      const businessId = contextStore.getVerifiedBusinessId(authService.getUserValue()?.id);
      if (businessId !== null) {
        headers[BUSINESS_CONTEXT_HEADER] = String(businessId);
      }
    }
    return next(Object.keys(headers).length > 0 ? req.clone({ setHeaders: headers }) : req);
  };

  // While a persisted Super Admin selection is being re-validated, hold Business-scoped requests
  // until it settles so they neither run unscoped nor carry an unverified saved id.
  const userId = normalizeUserId(authService.getUserValue()?.id);
  if (scopeEligible && !isProfileRequest(req.url) && authService.isSuperAdmin() && contextStore.isRestoring(userId)) {
    return contextStore.whenSettled(userId).pipe(switchMap(() => send()));
  }

  return send();
};
