import { APP_INITIALIZER, ApplicationInitStatus, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { initBusinessContext } from './app.config';
import { authGuard, routePermissionGuard } from './guards/auth.guard';
import { authInterceptor } from './interceptors/auth.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';
import { environment } from '../environments/environment';
import { User } from './models/auth.models';
import { BusinessContextService } from './services/business-context.service';
import { BUSINESS_CONTEXT_HEADER } from './services/business-context.store';

@Component({ template: '' })
class StubPage {}

const API = environment.apiUrl;
const CONTEXT_URL = `${API}/auth/business-context`;
const ME_URL = `${API}/auth/me`;
const validJwt = `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }))}.s`;
// Shape the browser keeps in localStorage after Apply refreshed /auth/me with the verified scope.
const superAdmin: User = {
  id: 1, businessId: 1, name: 'Root', email: 'root@x.test',
  roleName: 'Super Admin', realRoleName: 'Super Admin', permissions: ['*'], isRoleSwitched: false,
};

/**
 * Cold start of a browser reload on a Business-scoped report, in real startup order: persisted
 * auth + saved selection -> APP_INITIALIZER -> initial navigation through the real guards.
 */
describe('Super Admin cold start on a Business-scoped page', () => {
  let http: HttpTestingController;
  let initDone: boolean;

  function coldStart(): Promise<void> {
    localStorage.clear();
    localStorage.setItem('auth_token', validJwt);
    localStorage.setItem('refresh_token', 'refresh');
    localStorage.setItem('current_user', JSON.stringify(superAdmin));
    localStorage.setItem('business_context:1', '1');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: StubPage },
          {
            path: '',
            canActivate: [authGuard],
            children: [
              { path: 'dashboard', component: StubPage },
              { path: 'reports/profitability', component: StubPage, canActivate: [routePermissionGuard], data: { permissions: ['accounting.view'] } },
            ],
          },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_INITIALIZER, useFactory: initBusinessContext, deps: [BusinessContextService], multi: true },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    initDone = false;
    const init = TestBed.inject(ApplicationInitStatus).donePromise.then(() => { initDone = true; });
    return init;
  }

  const settle = () => new Promise<void>(resolve => setTimeout(resolve));

  afterEach(() => {
    http.verify();
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('waits for server validation of the saved Business before deciding the reloaded route', async () => {
    const init = coldStart();
    const router = TestBed.inject(Router);
    const nav = router.navigateByUrl('/reports/profitability');
    await settle();

    const validation = http.expectOne(CONTEXT_URL);
    expect(validation.request.body).toEqual({ businessId: 1 });
    expect(validation.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
    expect(initDone).toBe(false);
    expect(router.url).not.toBe('/dashboard?businessContextRequired=1');

    validation.flush({ businessId: 1, businessName: 'RetailPOS Demo Business' });
    const me = http.expectOne(ME_URL);
    expect(me.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('1');
    me.flush(superAdmin);
    await init;

    expect(await nav).toBe(true);
    expect(router.url).toBe('/reports/profitability');
    expect(localStorage.getItem('business_context:1')).toBe('1');

    TestBed.inject(HttpClient).get(`${API}/accounting/reports/profitability`).subscribe();
    expect(http.expectOne(`${API}/accounting/reports/profitability`).request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('1');
  });

  it('keeps the saved Business across transient failures and retries validation on each navigation', async () => {
    const init = coldStart();
    const router = TestBed.inject(Router);
    http.expectOne(CONTEXT_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await init;
    expect(localStorage.getItem('business_context:1')).toBe('1');

    const failed = router.navigateByUrl('/reports/profitability');
    await settle();
    const firstRetry = http.expectOne(CONTEXT_URL);
    expect(firstRetry.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
    firstRetry.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await failed;
    expect(router.url).toBe('/dashboard?businessContextRequired=1');
    expect(localStorage.getItem('business_context:1')).toBe('1');
    expect(localStorage.getItem('auth_token')).toBe(validJwt);

    const nav = router.navigateByUrl('/reports/profitability');
    await settle();
    http.expectOne(CONTEXT_URL).flush({ businessId: 1, businessName: 'RetailPOS Demo Business' });
    http.expectOne(ME_URL).flush(superAdmin);

    expect(await nav).toBe(true);
    expect(router.url).toBe('/reports/profitability');
  });
  it('holds the reloaded report request until the /auth/me restore completes, then sends the verified scope', async () => {
    const init = coldStart();
    const router = TestBed.inject(Router);
    const nav = router.navigateByUrl('/reports/profitability');
    await settle();
    http.expectOne(CONTEXT_URL).flush({ businessId: 1, businessName: 'RetailPOS Demo Business' });

    // Validation succeeded but the profile restore is still in flight.
    const me = http.expectOne(ME_URL);
    expect(me.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('1');
    await settle();
    expect(initDone).toBe(false);
    expect(router.url).not.toBe('/reports/profitability');

    // A Business-scoped child request issued now must wait, never go out unscoped (backend 401 -> logout).
    const reportUrl = `${API}/accounting/reports/business-profitability`;
    TestBed.inject(HttpClient).get(reportUrl).subscribe();
    http.expectNone(reportUrl);

    me.flush(superAdmin);
    await init;
    expect(await nav).toBe(true);
    expect(router.url).toBe('/reports/profitability');

    const report = http.expectOne(reportUrl);
    expect(report.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('1');
    report.flush({});
    expect(localStorage.getItem('business_context:1')).toBe('1');
    expect(localStorage.getItem('auth_token')).toBe(validJwt);
    http.expectNone(`${API}/auth/logout`);
  });

  it('keeps the restored selection when /auth/me reports the same user id as a string', async () => {
    const init = coldStart();
    const router = TestBed.inject(Router);
    http.expectOne(CONTEXT_URL).flush({ businessId: 1, businessName: 'RetailPOS Demo Business' });
    http.expectOne(ME_URL).flush({ ...superAdmin, id: '1' });
    await init;

    expect(await router.navigateByUrl('/reports/profitability')).toBe(true);
    expect(router.url).toBe('/reports/profitability');
    expect(localStorage.getItem('business_context:1')).toBe('1');
    http.expectNone(CONTEXT_URL);

    TestBed.inject(HttpClient).get(`${API}/accounting/reports/business-profitability`).subscribe();
    const report = http.expectOne(`${API}/accounting/reports/business-profitability`);
    expect(report.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('1');
    report.flush({});
  });

  it('forgets a saved Business the server rejects and never sends it', async () => {
    const init = coldStart();
    const router = TestBed.inject(Router);
    http.expectOne(CONTEXT_URL).flush(null, { status: 403, statusText: 'Forbidden' });
    await init;

    await router.navigateByUrl('/reports/profitability');
    expect(router.url).toBe('/dashboard?businessContextRequired=1');
    expect(localStorage.getItem('business_context:1')).toBeNull();
    expect(localStorage.getItem('auth_token')).toBe(validJwt);
    http.expectNone(CONTEXT_URL);
  });
});