import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../environments/environment';
import { AlertService } from '../services/alert.service';
import { AuthService } from '../services/auth.service';
import { authInterceptor } from './auth.interceptor';
import { FORBIDDEN_MESSAGE, errorInterceptor } from './error.interceptor';
import { sessionInterceptor } from './session.interceptor';

function jwt(expiresInSeconds: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
  return `header.${payload}.signature`;
}

describe('sessionInterceptor and error handling', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  let alerts: AlertService;
  const dataUrl = `${environment.apiUrl}/products`;
  const refreshUrl = `${environment.apiUrl}/auth/refresh-token`;
  const user = { id: 7, name: 'Owner', email: 'o@x.test', roleName: 'BusinessOwner', permissions: ['*'] };

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('auth_token', jwt(-60));
    localStorage.setItem('refresh_token', 'refresh-1');
    localStorage.setItem('current_user', JSON.stringify(user));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([sessionInterceptor, authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    alerts = TestBed.inject(AlertService);
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    controller.verify();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
    localStorage.clear();
  });

  function loginResponse(token: string, refreshToken: string) {
    return { token, refreshToken, expiresAt: new Date().toISOString(), user };
  }

  it('does not sign out, clear tokens or navigate on 403 and shows the permission message once', () => {
    const navigate = vi.spyOn(router, 'navigate');
    const logout = vi.spyOn(auth, 'logout');
    const token = localStorage.getItem('auth_token');
    const errors: unknown[] = [];

    http.get(dataUrl).subscribe({ error: e => errors.push(e) });
    controller.expectOne(dataUrl).flush({ message: 'Unauthorized access' }, { status: 403, statusText: 'Forbidden' });
    http.get(dataUrl).subscribe({ error: e => errors.push(e) });
    controller.expectOne(dataUrl).flush({}, { status: 403, statusText: 'Forbidden' });

    expect(controller.match(refreshUrl)).toHaveLength(0);
    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(localStorage.getItem('auth_token')).toBe(token);
    expect(localStorage.getItem('refresh_token')).toBe('refresh-1');
    expect(auth.getUserValue()).not.toBeNull();
    expect((errors[0] as { message: string }).message).toBe(FORBIDDEN_MESSAGE);
    expect(alerts.config()?.message).toBe(FORBIDDEN_MESSAGE);
  });

  it('keeps the page and form state intact on 403 (no route change)', async () => {
    await router.navigateByUrl('/');
    const before = router.url;
    http.post(dataUrl, { name: 'draft' }).subscribe({ error: () => {} });
    controller.expectOne(dataUrl).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(router.url).toBe(before);
  });

  it('refreshes an expired access token and replays the request with the new token', () => {
    let result: unknown;
    http.get(dataUrl).subscribe(value => (result = value));

    controller.expectOne(dataUrl).flush({}, { status: 401, statusText: 'Unauthorized' });
    const refresh = controller.expectOne(refreshUrl);
    expect(refresh.request.body).toEqual({ token: localStorage.getItem('auth_token'), refreshToken: 'refresh-1' });
    const fresh = jwt(600);
    refresh.flush(loginResponse(fresh, 'refresh-2'));

    const replay = controller.expectOne(dataUrl);
    expect(replay.request.headers.get('Authorization')).toContain(fresh);
    replay.flush({ ok: true });

    expect(result).toEqual({ ok: true });
    expect(localStorage.getItem('refresh_token')).toBe('refresh-2');
  });

  it('uses a single refresh request for concurrent 401 responses', () => {
    const results: unknown[] = [];
    for (let i = 0; i < 3; i++) {
      http.get(`${dataUrl}/${i}`).subscribe(value => results.push(value));
    }
    for (let i = 0; i < 3; i++) {
      controller.expectOne(`${dataUrl}/${i}`).flush({}, { status: 401, statusText: 'Unauthorized' });
    }

    const refreshes = controller.match(refreshUrl);
    expect(refreshes).toHaveLength(1);
    refreshes[0].flush(loginResponse(jwt(600), 'refresh-2'));

    for (let i = 0; i < 3; i++) {
      controller.expectOne(`${dataUrl}/${i}`).flush({ i });
    }
    expect(results).toHaveLength(3);
  });

  it('replays without a second refresh when another request already renewed the session', () => {
    const results: unknown[] = [];
    http.get(`${dataUrl}/a`).subscribe(value => results.push(value));
    http.get(`${dataUrl}/b`).subscribe(value => results.push(value));
    const a = controller.expectOne(`${dataUrl}/a`);
    const b = controller.expectOne(`${dataUrl}/b`);

    a.flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(refreshUrl).flush(loginResponse(jwt(600), 'refresh-2'));
    controller.expectOne(`${dataUrl}/a`).flush({ a: true });

    b.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(controller.match(refreshUrl)).toHaveLength(0);
    controller.expectOne(`${dataUrl}/b`).flush({ b: true });
    expect(results).toHaveLength(2);
  });

  it('signs out and redirects to login when the refresh token is invalid', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let failed = false;
    http.get(dataUrl).subscribe({ error: () => (failed = true) });

    controller.expectOne(dataUrl).flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(refreshUrl).flush({ message: 'Invalid refresh token' }, { status: 401, statusText: 'Unauthorized' });
    controller.match(`${environment.apiUrl}/auth/logout`).forEach(request => request.flush(null));

    expect(failed).toBe(true);
    expect(navigate).toHaveBeenCalledWith(['/login'], undefined);
    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(auth.getUserValue()).toBeNull();
  });

  it('keeps the session when the refresh endpoint is temporarily unavailable', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    http.get(dataUrl).subscribe({ error: () => {} });

    controller.expectOne(dataUrl).flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(refreshUrl).flush({}, { status: 503, statusText: 'Unavailable' });

    expect(navigate).not.toHaveBeenCalled();
    expect(localStorage.getItem('refresh_token')).toBe('refresh-1');
  });

  it('does not loop when a replayed request is still unauthorized', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    http.get(dataUrl).subscribe({ error: () => {} });

    controller.expectOne(dataUrl).flush({}, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne(refreshUrl).flush(loginResponse(jwt(600), 'refresh-2'));
    controller.expectOne(dataUrl).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(controller.match(refreshUrl)).toHaveLength(0);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('never tries to refresh when the login endpoint itself returns 401', () => {
    http.post(`${environment.apiUrl}/auth/login`, {}).subscribe({ error: () => {} });
    controller.expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ message: 'Invalid email or password' }, { status: 401, statusText: 'Unauthorized' });
    expect(controller.match(refreshUrl)).toHaveLength(0);
  });
});
