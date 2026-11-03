import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable, isObservable } from 'rxjs';
import { vi } from 'vitest';
import { routePermissionGuard } from '../guards/auth.guard';
import { authInterceptor, isApiRequest } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { environment } from '../../environments/environment';
import { User } from '../models/auth.models';
import { AuthService } from './auth.service';
import { AccountingService } from './accounting.service';
import { BusinessService } from './business.service';
import { BusinessContextService } from './business-context.service';
import { BUSINESS_CONTEXT_HEADER, BusinessContextStore, SKIP_BUSINESS_CONTEXT } from './business-context.store';
import { MenuService } from './menu.service';
import { PurchaseOrderService } from './purchase-order.service';

const API = environment.apiUrl;
type NavItem = { id: string; children?: NavItem[] };
const flatIds = (items: NavItem[]): string[] => items.flatMap(i => [i.id, ...flatIds(i.children ?? [])]);
const CONTEXT_URL = `${API}/auth/business-context`;
const ME_URL = `${API}/auth/me`;

const superAdmin: User = { id: 1, name: 'Root', email: 'root@x.test', roleName: 'Super Admin', realRoleName: 'Super Admin', permissions: ['purchases.view'] };
const otherSuperAdmin: User = { ...superAdmin, id: 2, email: 'root2@x.test' };
const owner: User = { id: 7, businessId: 3, name: 'Owner', email: 'owner@x.test', roleName: 'Business Owner / Admin', permissions: ['purchases.view'] };

describe('Super Admin Business context', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let store: BusinessContextStore;

  function boot(user: User | null, persisted?: Record<string, string>): BusinessContextService {
    localStorage.clear();
    localStorage.setItem('auth_token', 'token');
    if (user) {
      localStorage.setItem('current_user', JSON.stringify(user));
    }
    for (const [key, value] of Object.entries(persisted ?? {})) {
      localStorage.setItem(key, value);
    }
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    store = TestBed.inject(BusinessContextStore);
    return TestBed.inject(BusinessContextService);
  }

  function expectContextPost(businessId: number | null): TestRequest {
    const req = http.expectOne(CONTEXT_URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ businessId });
    expect(req.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
    return req;
  }

  function flushMe(user: User): void {
    const me = http.expectOne(ME_URL);
    me.flush(user);
  }

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('never selects a Business automatically when nothing is persisted', () => {
    const service = boot(superAdmin);
    http.expectNone(CONTEXT_URL);
    expect(store.current()).toBeNull();
    expect(service.restoring()).toBe(false);

    TestBed.inject(HttpClient).get(`${API}/purchase-orders`).subscribe();
    expect(http.expectOne(`${API}/purchase-orders`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
  });

  it('applies a selection only after server validation, then propagates the header to /me, PO and accounting calls', () => {
    const service = boot(superAdmin);
    let result: unknown;
    service.select(5).subscribe(r => (result = r));

    const validation = expectContextPost(5);
    expect(store.current()).toBeNull();
    validation.flush({ businessId: 5, businessName: 'Shop 5' });

    const me = http.expectOne(ME_URL);
    expect(me.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
    me.flush({ ...superAdmin, businessId: 5, businessName: 'Shop 5' });

    expect(result).toEqual({ businessId: 5, businessName: 'Shop 5' });
    expect(store.current()).toEqual({ userId: 1, businessId: 5, businessName: 'Shop 5' });
    expect(localStorage.getItem('business_context:1')).toBe('5');
    expect(auth.getUserValue()?.businessId).toBe(5);

    TestBed.inject(PurchaseOrderService).getAll().subscribe();
    const po = http.expectOne(r => r.url === `${API}/purchase-orders`);
    expect(po.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
    expect(po.request.headers.get('Authorization')).toBe('Bearer token');
    po.flush([]);

    TestBed.inject(AccountingService).getFinancialYears().subscribe();
    const fy = http.expectOne(r => r.url.endsWith('/financial-years'));
    expect(fy.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
    fy.flush([]);
  });

  it('does not send the previous context on the validation request when changing selection', () => {
    const service = boot(superAdmin);
    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);

    service.select(6).subscribe();
    expectContextPost(6).flush({ businessId: 6, businessName: 'Shop 6' });
    flushMe(superAdmin);
    expect(store.current()?.businessId).toBe(6);

    service.clear().subscribe();
    expectContextPost(null).flush({ businessId: null, businessName: null });
    flushMe(superAdmin);
    expect(store.current()).toBeNull();
    expect(localStorage.getItem('business_context:1')).toBeNull();
  });

  it('keeps the previous selection when the server rejects a change', () => {
    const service = boot(superAdmin);
    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);

    let failed = false;
    service.select(9).subscribe({ error: () => (failed = true) });
    expectContextPost(9).flush({ message: 'Inactive' }, { status: 400, statusText: 'Bad Request' });
    expect(failed).toBe(true);
    expect(store.current()?.businessId).toBe(5);
  });

  it('rejects a server response that does not confirm the requested Business', () => {
    const service = boot(superAdmin);
    let failed = false;
    service.select(5).subscribe({ error: () => (failed = true) });
    expectContextPost(5).flush({ businessId: 8, businessName: 'Other' });
    expect(failed).toBe(true);
    expect(store.current()).toBeNull();
    expect(localStorage.getItem('business_context:1')).toBeNull();
  });

  it('restores a persisted selection only after server validation', () => {
    const service = boot(superAdmin, { 'business_context:1': '5' });
    expect(service.restoring()).toBe(true);

    TestBed.inject(HttpClient).get(`${API}/purchase-orders`).subscribe();
    http.expectNone(`${API}/purchase-orders`);

    expectContextPost(5).flush({ data: { businessId: 5, businessName: 'Shop 5' } });
    // Still held until the restore's own /auth/me refresh completes.
    http.expectNone(`${API}/purchase-orders`);
    expect(service.restoring()).toBe(true);
    flushMe({ ...superAdmin, businessId: 5 });
    expect(http.expectOne(`${API}/purchase-orders`).request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
    expect(store.current()).toEqual({ userId: 1, businessId: 5, businessName: 'Shop 5' });
    expect(service.restoring()).toBe(false);
  });

  it('drops a persisted selection the server no longer accepts', () => {
    const service = boot(superAdmin, { 'business_context:1': '5' });
    expectContextPost(5).flush({ message: 'Inactive' }, { status: 403, statusText: 'Forbidden' });
    expect(store.current()).toBeNull();
    expect(localStorage.getItem('business_context:1')).toBeNull();
    expect(service.restoring()).toBe(false);
  });

  it('ignores malformed persisted values and another user\'s persisted selection', () => {
    boot(superAdmin, { 'business_context:1': '5abc', 'business_context:2': '6' });
    http.expectNone(CONTEXT_URL);
    expect(store.current()).toBeNull();
  });

  it('clears the selection and all persisted contexts on logout', () => {
    const service = boot(superAdmin);
    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);

    localStorage.removeItem('refresh_token');
    auth.logout();
    expect(store.current()).toBeNull();
    expect(localStorage.getItem('business_context:1')).toBeNull();
  });

  it('clears the selection when a different user signs in and does not restore it for them', () => {
    const service = boot(superAdmin);
    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);

    auth.login({ email: 'root2@x.test', password: 'pw' }).subscribe();
    http.expectOne(`${API}/auth/login`).flush({ token: 't2', refreshToken: 'r2', expiresAt: '', user: otherSuperAdmin });

    http.expectNone(CONTEXT_URL);
    expect(store.current()).toBeNull();
    expect(localStorage.getItem('business_context:1')).toBeNull();
    TestBed.inject(HttpClient).get(`${API}/purchase-orders`).subscribe();
    expect(http.expectOne(`${API}/purchase-orders`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
  });

  it('never attaches the header for a regular user, even with a stale persisted or stored value', () => {
    const service = boot(owner, { 'business_context:7': '5' });
    http.expectNone(CONTEXT_URL);
    store.setVerified({ userId: 7, businessId: 5, businessName: 'Injected' });

    TestBed.inject(PurchaseOrderService).getAll().subscribe();
    const po = http.expectOne(r => r.url === `${API}/purchase-orders`);
    expect(po.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);

    let failed = false;
    service.select(5).subscribe({ error: () => (failed = true) });
    expect(failed).toBe(true);
    http.expectNone(CONTEXT_URL);
  });

  it('does not attach the header for a role-switched Super Admin session', () => {
    boot({ ...superAdmin, roleName: 'Outlet Manager', isRoleSwitched: true });
    store.setVerified({ userId: 1, businessId: 5, businessName: 'Shop 5' });
    TestBed.inject(HttpClient).get(`${API}/purchase-orders`).subscribe();
    expect(http.expectOne(`${API}/purchase-orders`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
  });

  it('strips caller-supplied headers, skips opted-out requests and non-API origins', () => {
    const service = boot(superAdmin);
    const client = TestBed.inject(HttpClient);

    client.get(`${API}/products`, { headers: { [BUSINESS_CONTEXT_HEADER]: '99' } }).subscribe();
    expect(http.expectOne(`${API}/products`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);

    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);

    client.get(`${API}/products`, { headers: { [BUSINESS_CONTEXT_HEADER]: '99' } }).subscribe();
    expect(http.expectOne(`${API}/products`).request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');

    client.get('https://evil.example.com/api/products').subscribe();
    expect(http.expectOne('https://evil.example.com/api/products').request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);

    TestBed.inject(BusinessService).getBusinesses(undefined, true, new HttpContext().set(SKIP_BUSINESS_CONTEXT, true)).subscribe();
    expect(http.expectOne(r => r.url === `${API}/businesses`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
  });

  it('matches only the configured API origin and path prefix', () => {
    expect(isApiRequest(`${API}/products`)).toBe(true);
    expect(isApiRequest(`${API}`)).toBe(true);
    expect(isApiRequest(`${API}x/products`)).toBe(false);
    expect(isApiRequest('https://evil.example.com/api/products')).toBe(false);
  });

  it('hides Business-scoped menu items for a Super Admin until a context is verified', () => {
    const service = boot(superAdmin);
    const menu = TestBed.inject(MenuService);
    const ids = () => menu.sections().flatMap(s => flatIds(s.items));

    expect(ids()).toContain('businesses');
    expect(ids()).toContain('dashboard');
    expect(ids()).not.toContain('purchase-orders');

    service.select(5).subscribe();
    expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });
    flushMe(superAdmin);
    expect(ids()).toContain('purchase-orders');

    service.clear().subscribe();
    expectContextPost(null).flush({ businessId: null, businessName: null });
    flushMe(superAdmin);
    expect(ids()).not.toContain('purchase-orders');
  });

  it('does not restrict menu items for regular users', () => {
    boot(owner);
    const ids = TestBed.inject(MenuService).sections().flatMap(s => flatIds(s.items));
    expect(ids).toContain('purchase-orders');
    expect(ids).not.toContain('businesses');
  });

  describe('direct reload while a persisted selection is being restored', () => {
    const validJwt = `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }))}.s`;
    const unscoped401s: string[] = [];

    function runGuard(path: string, permissions: string[]): boolean | UrlTree | Observable<boolean | UrlTree> {
      const route = { data: { permissions } } as unknown as ActivatedRouteSnapshot;
      const state = { url: `/${path}` } as RouterStateSnapshot;
      return TestBed.runInInjectionContext(() => routePermissionGuard(route, state)) as boolean | UrlTree | Observable<boolean | UrlTree>;
    }

    function guardResult(result: ReturnType<typeof runGuard>): { value?: boolean | UrlTree } {
      const holder: { value?: boolean | UrlTree } = {};
      if (isObservable(result)) {
        result.subscribe(v => (holder.value = v));
      } else {
        holder.value = result as boolean | UrlTree;
      }
      return holder;
    }

    function startReload(): BusinessContextService {
      const service = boot({ ...superAdmin, permissions: ['*'] }, { 'business_context:1': '5' });
      localStorage.setItem('auth_token', validJwt);
      unscoped401s.length = 0;
      expect(service.restoring()).toBe(true);
      return service;
    }

    // Stand-in for the API: a Business-scoped call without a verified Business scope is a 401.
    function flushLikeApi(req: TestRequest): void {
      if (!req.request.headers.has(BUSINESS_CONTEXT_HEADER)) {
        unscoped401s.push(req.request.url);
        req.flush({ message: 'Business missing' }, { status: 401, statusText: 'Unauthorized' });
        return;
      }
      req.flush([]);
    }

    it('holds Accounting and PO route activation and API calls until restore succeeds, then sends them scoped', () => {
      startReload();
      const fyGuard = guardResult(runGuard('financial-years', ['accounting.financial_years.view', 'accounting.periods.view']));
      const poGuard = guardResult(runGuard('purchase-orders', ['purchases.view']));
      expect(fyGuard.value).toBeUndefined();
      expect(poGuard.value).toBeUndefined();

      TestBed.inject(AccountingService).getFinancialYears().subscribe();
      TestBed.inject(PurchaseOrderService).getAll().subscribe();
      expect(http.match(r => r.url.endsWith('/financial-years') || r.url === `${API}/purchase-orders`)).toHaveLength(0);

      expectContextPost(5).flush({ businessId: 5, businessName: 'Shop 5' });

      // Validation alone does not release them: the /auth/me refresh must finish first.
      expect(fyGuard.value).toBeUndefined();
      expect(poGuard.value).toBeUndefined();
      expect(http.match(r => r.url.endsWith('/financial-years') || r.url === `${API}/purchase-orders`)).toHaveLength(0);
      flushMe({ ...superAdmin, businessId: 5 });

      expect(fyGuard.value).toBe(true);
      expect(poGuard.value).toBe(true);
      const fy = http.expectOne(r => r.url.endsWith('/financial-years'));
      const po = http.expectOne(r => r.url === `${API}/purchase-orders`);
      expect(fy.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
      expect(po.request.headers.get(BUSINESS_CONTEXT_HEADER)).toBe('5');
      flushLikeApi(fy);
      flushLikeApi(po);

      expect(unscoped401s).toEqual([]);
      expect(auth.getUserValue()?.id).toBe(1);
    });

    it('redirects Accounting and PO routes to the dashboard without any scoped call or logout when restore is rejected', () => {
      startReload();
      const router = TestBed.inject(Router);
      const fyGuard = guardResult(runGuard('financial-years', ['accounting.financial_years.view', 'accounting.periods.view']));
      const poGuard = guardResult(runGuard('purchase-orders', ['purchases.view']));

      expectContextPost(5).flush({ message: 'Inactive Business' }, { status: 403, statusText: 'Forbidden' });

      for (const result of [fyGuard.value, poGuard.value]) {
        expect(result instanceof UrlTree).toBe(true);
        expect(router.serializeUrl(result as UrlTree)).toBe('/dashboard?businessContextRequired=1');
      }
      http.expectNone(r => r.url.endsWith('/financial-years') || r.url === `${API}/purchase-orders`);
      http.expectNone(ME_URL);
      expect(store.current()).toBeNull();
      expect(localStorage.getItem('business_context:1')).toBeNull();
      expect(unscoped401s).toEqual([]);
      expect(auth.getUserValue()?.id).toBe(1);
      expect(localStorage.getItem('auth_token')).toBe(validJwt);
    });

    it('bounds the wait: a hung validation times out, keeps the saved id for a later retry, never sends it unverified and releases held requests unscoped', () => {
      vi.useFakeTimers();
      try {
        const service = startReload();
        const poGuard = guardResult(runGuard('purchase-orders', ['purchases.view']));
        TestBed.inject(HttpClient).get(`${API}/currency`).subscribe({ error: () => undefined });
        const validation = expectContextPost(5);

        vi.advanceTimersByTime(15000);

        expect(validation.cancelled).toBe(true);
        expect(service.restoring()).toBe(false);
        expect(localStorage.getItem('business_context:1')).toBe('5');
        expect(poGuard.value instanceof UrlTree).toBe(true);
        expect(http.expectOne(`${API}/currency`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
      } finally {
        vi.useRealTimers();
      }
    });

    it('does not hold the selector validation, Business list or auth calls during restore', () => {
      startReload();
      const pendingRestore = expectContextPost(5);

      TestBed.inject(BusinessService).getBusinesses(undefined, true, new HttpContext().set(SKIP_BUSINESS_CONTEXT, true)).subscribe();
      const list = http.expectOne(r => r.url === `${API}/businesses`);
      expect(list.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
      list.flush([]);

      TestBed.inject(HttpClient).post(`${API}/auth/refresh-token`, {}).subscribe();
      http.expectOne(`${API}/auth/refresh-token`).flush({});

      TestBed.inject(BusinessContextService).select(6).subscribe();
      expect(pendingRestore.cancelled).toBe(true);
      expectContextPost(6).flush({ businessId: 6, businessName: 'Shop 6' });
      flushMe(superAdmin);
      expect(store.current()?.businessId).toBe(6);
    });

    it('keeps regular-user route activation synchronous and never waits or adds the header', () => {
      boot(owner, { 'business_context:7': '5' });
      localStorage.setItem('auth_token', validJwt);
      expect(runGuard('purchase-orders', ['purchases.view'])).toBe(true);
      TestBed.inject(PurchaseOrderService).getAll().subscribe();
      expect(http.expectOne(r => r.url === `${API}/purchase-orders`).request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
      http.expectNone(CONTEXT_URL);
    });
  });
});