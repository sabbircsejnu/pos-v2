import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Route, RouterStateSnapshot, provideRouter } from '@angular/router';
import { routes } from '../app.routes';
import { routePermissionGuard } from '../guards/auth.guard';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { environment } from '../../environments/environment';
import { MenuItem } from '../models/menu.model';
import { AuthService } from './auth.service';
import { BUSINESS_CONTEXT_HEADER } from './business-context.store';
import { MenuService } from './menu.service';
import { SettingsService } from './settings.service';

// Exactly the Cashier role in DbSeeder (categories.view is the only new grant); tax/receipt GETs are
// authorized server-side by the existing sales.create, with no settings.view/edit grant.
const cashierPermissions = [
  'products.view',
  'categories.view',
  'inventory.view',
  'low_stock_alerts.view',
  'sales.view', 'sales.create',
  'customers.view', 'customers.create',
];

function fakeJwt(): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));
  return `h.${payload}.s`;
}

function flatIds(items: MenuItem[]): string[] {
  return items.flatMap(i => [i.id, ...flatIds(i.children ?? [])]);
}

describe('Cashier permission scope', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('auth_token', fakeJwt());
    localStorage.setItem('current_user', JSON.stringify({
      id: 21, businessId: 3, name: 'Till', email: 'cashier@x.test', roleName: 'Cashier', permissions: cashierPermissions,
    }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  function canActivate(path: string): boolean {
    const route = routes.flatMap(r => [r, ...(r.children ?? [])]).find((r: Route) => r.path === path);
    expect(route, path).toBeDefined();
    const snapshot = { data: route!.data ?? {} } as unknown as ActivatedRouteSnapshot;
    return TestBed.runInInjectionContext(() =>
      routePermissionGuard(snapshot, { url: `/${path}` } as RouterStateSnapshot)) as boolean;
  }

  it('grants POS and Categories but no Settings or accounting configuration', () => {
    const auth = TestBed.inject(AuthService);
    expect(auth.hasPermission('categories.view')).toBe(true);
    expect(auth.hasPermission('settings.view')).toBe(false);
    expect(auth.hasPermission('settings.edit')).toBe(false);
    expect(auth.hasPermission('accounting.configuration.view')).toBe(false);

    const ids = TestBed.inject(MenuService).sections().flatMap(s => flatIds(s.items));
    expect(ids).toContain('pos-sales');
    expect(ids).toContain('categories');
    expect(ids).not.toContain('settings');
    expect(ids).not.toContain('accounting-config');

    expect(canActivate('pos')).toBe(true);
    expect(canActivate('categories')).toBe(true);
    expect(canActivate('settings')).toBe(false);
    expect(canActivate('settings/accounting')).toBe(false);
  });

  it('reads tax and receipt settings for POS using the signed claim only', () => {
    const settings = TestBed.inject(SettingsService);
    settings.getTaxSettings().subscribe();
    settings.getReceiptSettings().subscribe();

    for (const path of ['tax', 'receipt']) {
      const req = http.expectOne(`${environment.apiUrl}/settings/${path}`);
      expect(req.request.method).toBe('GET');
      expect(req.request.headers.has(BUSINESS_CONTEXT_HEADER)).toBe(false);
      req.flush({ data: {} });
    }
  });
});