import { ApplicationConfig, APP_INITIALIZER, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { firstValueFrom, catchError, of, switchMap, timeout } from 'rxjs';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { sessionInterceptor } from './interceptors/session.interceptor';
import { CurrencyService } from './services/currency.service';
import { BusinessContextService } from './services/business-context.service';
import { AuthService } from './services/auth.service';
import { CompanyDateService } from './services/company-date.service';

import { routes } from './app.routes';

export function initCurrency(currency: CurrencyService): () => Promise<unknown> {
  return () => firstValueFrom(currency.load()).catch(() => undefined);
}

/**
 * After a browser refresh the cached profile may be stale (roles or permissions can change while
 * the tab is closed). Re-reads the authoritative profile; failures never block startup or sign out.
 * An expired access token is restored first through the single-flight refresh.
 */
export function initProfile(auth: AuthService): () => Promise<unknown> {
  return () => {
    if (!auth.getUserValue() || !(auth.isAuthenticated() || auth.hasRecoverableSession())) {
      return Promise.resolve();
    }
    return firstValueFrom(auth.ensureAuthenticated().pipe(
      switchMap(ok => (ok ? auth.getCurrentUser() : of(null))),
      timeout(8000),
      catchError(() => of(null))
    ), { defaultValue: null });
  };
}

/**
 * Restores (server-validates) the saved Super Admin Business context before the initial route is
 * activated, so a direct reload of a Business-scoped page is decided against the verified context.
 */
export function initBusinessContext(context: BusinessContextService): () => Promise<void> {
  return () => firstValueFrom(context.ensureRestored(), { defaultValue: undefined });
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(
      withInterceptors([sessionInterceptor, authInterceptor, errorInterceptor])
    ),
    {
      provide: APP_INITIALIZER,
      useFactory: (dates: CompanyDateService, auth: AuthService) => () => dates.connect(auth.currentUser$),
      deps: [CompanyDateService, AuthService],
      multi: true,
    },
    {
      provide: APP_INITIALIZER,
      useFactory: initProfile,
      deps: [AuthService],
      multi: true,
    },
    {
      provide: APP_INITIALIZER,
      useFactory: initBusinessContext,
      deps: [BusinessContextService],
      multi: true,
    },
    {
      provide: APP_INITIALIZER,
      useFactory: initCurrency,
      deps: [CurrencyService],
      multi: true,
    },
  ]
};
