import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, isObservable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../services/auth.service';
import { authGuard, routePermissionGuard } from './auth.guard';

function jwt(expiresInSeconds: number): string {
  return `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }))}.s`;
}

describe('auth guards', () => {
  let controller: HttpTestingController;
  let router: Router;
  const refreshUrl = `${environment.apiUrl}/auth/refresh-token`;
  const state = { url: '/products' } as RouterStateSnapshot;
  const owner = { id: 7, businessId: 3, name: 'Owner', email: 'o@x.test', roleName: 'BusinessOwner', permissions: ['*'] };

  function seed(token: string | null, refresh: string | null) {
    localStorage.clear();
    if (token) localStorage.setItem('auth_token', token);
    if (refresh) localStorage.setItem('refresh_token', refresh);
    localStorage.setItem('current_user', JSON.stringify(owner));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  }

  function run(guard: typeof authGuard, data: Record<string, unknown> = {}): Promise<unknown> {
    const result = TestBed.runInInjectionContext(() =>
      guard({ data } as unknown as ActivatedRouteSnapshot, state));
    return isObservable(result) ? firstValueFrom(result as Observable<unknown>) : Promise.resolve(result);
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('admits a user with a valid access token immediately, without any HTTP call', async () => {
    seed(jwt(600), 'r');
    expect(await run(authGuard)).toBe(true);
    controller.expectNone(refreshUrl);
  });

  it('silently restores an expired access token after a browser refresh', async () => {
    seed(jwt(-30), 'r1');
    const pending = run(authGuard);
    const request = controller.expectOne(refreshUrl);
    request.flush({ token: jwt(600), refreshToken: 'r2', expiresAt: '', user: owner });

    expect(await pending).toBe(true);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(localStorage.getItem('refresh_token')).toBe('r2');
  });

  it('redirects to login only when the session cannot be restored', async () => {
    seed(jwt(-30), 'bad');
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const pending = run(authGuard);
    controller.expectOne(refreshUrl).flush({ message: 'Invalid refresh token' }, { status: 401, statusText: 'Unauthorized' });
    controller.match(`${environment.apiUrl}/auth/logout`).forEach(request => request.flush(null));

    expect(await pending).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/products' } });
  });

  it('redirects to login without any request when no refresh token exists', async () => {
    seed(jwt(-30), null);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    expect(await run(authGuard)).toBe(false);
    controller.expectNone(refreshUrl);
    expect(navigate).toHaveBeenCalled();
  });

  it('grants a wildcard Business owner access to any permission-gated route', async () => {
    seed(jwt(600), 'r');
    expect(await run(routePermissionGuard, { permissions: ['accounting.reports.vat'] })).toBe(true);
    expect(await run(routePermissionGuard, { permissions: ['hr.employee.view', 'payroll.run.create'] })).toBe(true);
  });

  it('keeps denying routes to users without the permission', async () => {
    seed(jwt(600), 'r');
    localStorage.setItem('current_user', JSON.stringify({ ...owner, roleName: 'SalesPerson', permissions: ['sales.create'] }));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    expect(await run(routePermissionGuard, { permissions: ['users.view'] })).toBe(false);
  });
});
