import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { environment } from '../../environments/environment';

describe('AuthService logout', () => {
  let service: AuthService;
  let httpTestingController: HttpTestingController;

  const accessToken = 'access-token-for-test';
  const refreshToken = 'refresh-token-for-test';
  const logoutUrl = `${environment.apiUrl}/auth/logout`;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('auth_token', accessToken);
    localStorage.setItem('refresh_token', refreshToken);
    localStorage.setItem('current_user', JSON.stringify({
      id: 7,
      name: 'Test User',
      email: 'test@example.com',
      roleName: 'Cashier',
      permissions: ['sales.create'],
      realRoleName: 'BusinessOwner',
      actingRoleName: 'Cashier',
      isRoleSwitched: true
    }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });

    service = TestBed.inject(AuthService);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  it('revokes the current refresh token and clears all local authentication state', () => {
    service.logout();

    const request = httpTestingController.expectOne(logoutUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken });
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${accessToken}`);
    expect(service.getRefreshToken()).toBeNull();
    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(localStorage.getItem('refresh_token')).toBeNull();
    expect(localStorage.getItem('current_user')).toBeNull();
    expect(service.getUserValue()).toBeNull();

    request.flush(null);
  });

  it('clears local authentication state when the server is unavailable', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    service.logout();

    const request = httpTestingController.expectOne(logoutUrl);
    request.flush(
      { message: refreshToken },
      { status: 503, statusText: 'Unavailable' }
    );

    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(localStorage.getItem('refresh_token')).toBeNull();
    expect(localStorage.getItem('current_user')).toBeNull();
    expect(service.getUserValue()).toBeNull();
    expect(JSON.stringify(consoleError.mock.calls)).not.toContain(refreshToken);
  });

  it('does not recursively log out when the logout request is unauthorized', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    service.logout();

    const request = httpTestingController.expectOne(logoutUrl);
    request.flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(httpTestingController.match(logoutUrl)).toHaveLength(0);
    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(localStorage.getItem('refresh_token')).toBeNull();
    expect(localStorage.getItem('current_user')).toBeNull();
  });

  it('does not restore profile state from an in-flight request after logout begins', () => {
    service.getCurrentUser().subscribe();
    const profileRequest = httpTestingController.expectOne(`${environment.apiUrl}/auth/me`);

    service.logout();
    const logoutRequest = httpTestingController.expectOne(logoutUrl);
    profileRequest.flush({
      id: 7,
      name: 'Test User',
      email: 'test@example.com',
      permissions: ['sales.create']
    });
    logoutRequest.flush(null);

    expect(localStorage.getItem('current_user')).toBeNull();
    expect(service.getUserValue()).toBeNull();
  });

  it('treats repeated logout and an already-revoked server response as idempotent', () => {
    service.logout();
    service.logout();

    const requests = httpTestingController.match(logoutUrl);
    expect(requests).toHaveLength(1);
    requests[0].flush(null);

    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(localStorage.getItem('refresh_token')).toBeNull();
    expect(localStorage.getItem('current_user')).toBeNull();
  });

  describe('permission evaluation', () => {
    function withPermissions(permissions: string[]): void {
      localStorage.setItem('current_user', JSON.stringify({
        id: 7, name: 'U', email: 'u@x.test', roleName: 'BusinessOwner', permissions
      }));
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
      });
      service = TestBed.inject(AuthService);
    }

    it('grants every Business permission to the wildcard, regardless of enumeration', () => {
      withPermissions(['*']);
      expect(service.hasPermission('payroll.run.create')).toBe(true);
      expect(service.hasPermission('accounting.reports.vat')).toBe(true);
      expect(service.hasAnyPermission(['made.up.permission'])).toBe(true);
      expect(service.hasAllPermissions(['users.view', 'roles.delete'])).toBe(true);
    });

    it('matches exact, aliased and differently-cased permissions like the backend evaluator', () => {
      withPermissions(['Accounts.View', 'sales.create']);
      expect(service.hasPermission('accounting.chart_accounts.view')).toBe(true);
      expect(service.hasPermission('sales.create')).toBe(true);
      expect(service.hasPermission('sales.void')).toBe(false);
      expect(service.hasAllPermissions(['sales.create', 'sales.void'])).toBe(false);
    });

    it('does not let general Settings permissions open accounting configuration the API would reject', () => {
      withPermissions(['settings.view', 'settings.edit']);
      expect(service.hasPermission('accounting.configuration.view')).toBe(false);
      expect(service.hasPermission('accounting.configuration.edit')).toBe(false);
    });

    it('does not grant owner-level permissions to a limited role', () => {
      withPermissions(['sales.create', 'sales.view']);
      expect(service.hasPermission('users.create')).toBe(false);
      expect(service.hasPermission('settings.edit')).toBe(false);
      expect(service.hasAnyPermission(['roles.view', 'roles.edit'])).toBe(false);
    });
  });
});
