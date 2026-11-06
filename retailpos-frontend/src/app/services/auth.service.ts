import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, EMPTY, Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { LoginRequest, LoginResponse, RegisterRequest, RoleSwitchRequest, User, UserContextOptions } from '../models/auth.models';
import { environment } from '../../environments/environment';
import { BusinessContextStore } from './business-context.store';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly permissionAliases: Record<string, string[]> = {
    'accounting.chart_accounts.view': ['accounts.view'],
    'accounting.chart_accounts.create': ['accounts.create'],
    'accounting.chart_accounts.edit': ['accounts.edit', 'accounts.delete'],
    'accounting.journals.view': ['transactions.view', 'accounting.manual_journals.view'],
    'accounting.journals.create': ['transactions.create'],
    'accounting.journals.update': ['transactions.edit'],
    'accounting.journals.cancel': ['transactions.delete'],
    'accounting.journals.post': ['transactions.create', 'transactions.edit'],
    'accounting.journals.reverse': ['transactions.edit', 'transactions.delete'],
    'accounting.manual_journals.view': ['transactions.view', 'accounting.journals.view'],
    'accounting.manual_journals.create': ['transactions.create', 'accounting.journals.create'],
    'accounting.manual_journals.update': ['transactions.edit', 'accounting.journals.update'],
    'accounting.manual_journals.submit': ['transactions.edit', 'accounting.journals.update'],
    'accounting.manual_journals.approve': ['transactions.edit', 'accounting.journals.post'],
    'accounting.manual_journals.reject': ['transactions.edit', 'accounting.journals.update'],
    'accounting.manual_journals.cancel': ['transactions.delete', 'accounting.journals.cancel'],
    'accounting.manual_journals.post': ['transactions.create', 'transactions.edit', 'accounting.journals.post'],
    'accounting.manual_journals.reverse': ['transactions.edit', 'transactions.delete', 'accounting.journals.reverse'],
    'accounting.expenses.view': ['expenses.view'],
    'accounting.expenses.create': ['expenses.create'],
    'accounting.expenses.edit': ['expenses.edit'],
    'accounting.expenses.submit': ['expenses.create', 'expenses.edit'],
    'accounting.expenses.approve': ['expenses.edit'],
    'accounting.expenses.reject': ['expenses.edit'],
    'accounting.expenses.cancel': ['expenses.delete'],
    'accounting.supplier_payments.view': ['suppliers.view'],
    'accounting.supplier_payments.create': ['suppliers.edit'],
    'accounting.supplier_payments.submit': ['suppliers.edit'],
    'accounting.supplier_payments.approve': ['suppliers.edit'],
    'accounting.supplier_payments.reject': ['suppliers.edit'],
    'accounting.supplier_payments.cancel': ['suppliers.edit'],
    'accounting.supplier_advances.view': ['suppliers.view'],
    'accounting.supplier_advances.create': ['suppliers.edit'],
    'accounting.supplier_advances.submit': ['suppliers.edit'],
    'accounting.supplier_advances.approve': ['suppliers.edit'],
    'accounting.supplier_advances.reject': ['suppliers.edit'],
    'accounting.supplier_advances.cancel': ['suppliers.edit'],
    'accounting.other_income.view': [],
    'accounting.other_income.create': [],
    'accounting.other_income.edit': [],
    'accounting.other_income.submit': [],
    'accounting.other_income.approve': [],
    'accounting.other_income.reject': [],
    'accounting.other_income.cancel': [],
    'accounting.opening_balances.post': ['transactions.create', 'transactions.edit'],
    'accounting.opening_balances.adjust': ['transactions.edit']
  };

  private readonly API_URL = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly REFRESH_TOKEN_KEY = 'refresh_token';
  private readonly USER_KEY = 'current_user';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getUserFromStorage());
  public currentUser$ = this.currentUserSubject.asObservable();
  private logoutStarted = false;
  private refreshInFlight$: Observable<LoginResponse> | null = null;

  constructor(private http: HttpClient, private businessContextStore: BusinessContextStore) {}

  /**
   * True when the access token is unusable but a refresh token exists, so the session may still be
   * restorable without asking the user to sign in again.
   */
  hasRecoverableSession(): boolean {
    return !this.logoutStarted && !!this.getToken() && !!localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  /**
   * Exchanges the stored refresh token for a new access token. Concurrent callers share a single
   * request (single flight): refresh tokens rotate, so a second parallel refresh would present an
   * already-used token and revoke the whole session.
   */
  refreshSession(): Observable<LoginResponse> {
    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const token = this.getToken();
    const refreshToken = this.getRefreshToken();
    if (!token || !refreshToken) {
      return throwError(() => ({ status: 401, message: 'No refresh token is available.' }));
    }

    const previousUser = this.currentUserSubject.value;
    this.refreshInFlight$ = this.http.post<LoginResponse>(`${this.API_URL}/refresh-token`, { token, refreshToken }).pipe(
      tap(response => {
        // A Super Admin's selected Business is carried by a header, not by the token; keep the
        // profile's active Business until the next authoritative /auth/me.
        if (previousUser?.id === response.user?.id && previousUser?.businessId && !response.user.businessId
          && previousUser.roleName === 'Super Admin') {
          response.user = { ...response.user, businessId: previousUser.businessId, businessName: previousUser.businessName };
        }
        this.handleAuthResponse(response);
      }),
      finalize(() => { this.refreshInFlight$ = null; }),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    // Start immediately so the shared request always exists, even if the first caller never subscribes.
    this.refreshInFlight$.subscribe({ error: () => undefined });
    return this.refreshInFlight$;
  }

  /**
   * Resolves true when the caller holds a valid access token, silently refreshing an expired one
   * when a refresh token is available. A transient failure does not end the session.
   */
  ensureAuthenticated(): Observable<boolean> {
    if (this.isAuthenticated()) {
      return of(true);
    }
    if (!this.hasRecoverableSession()) {
      return of(false);
    }
    return this.refreshSession().pipe(
      map(() => true),
      catchError(error => {
        // Only a definitive rejection ends the session; offline/5xx leaves it restorable.
        if (error?.status === 400 || error?.status === 401 || error?.status === 403) {
          this.logout();
        }
        return of(false);
      })
    );
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    this.logoutStarted = false;
    return this.http.post<LoginResponse>(`${this.API_URL}/login`, request).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  register(request: RegisterRequest): Observable<LoginResponse> {
    this.logoutStarted = false;
    return this.http.post<LoginResponse>(`${this.API_URL}/register`, request).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  logout(): void {
    if (this.logoutStarted) {
      this.clearAuthState();
      return;
    }

    const refreshToken = this.getRefreshToken();
    this.logoutStarted = true;
    if (refreshToken) {
      this.http.post<void>(`${this.API_URL}/logout`, { refreshToken }).pipe(
        catchError(() => EMPTY),
        finalize(() => this.clearAuthState())
      ).subscribe();
    }
    this.clearAuthState();
  }

  getCurrentUser(): Observable<User> {
    return this.http.get<User>(`${this.API_URL}/me`).pipe(
      tap(user => {
        if (!this.logoutStarted) {
          localStorage.setItem(this.USER_KEY, JSON.stringify(user));
          this.currentUserSubject.next(user);
        }
      })
    );
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) return false;

    // Check if token is expired
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000; // Convert to milliseconds
      return Date.now() < exp;
    } catch {
      return false;
    }
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return this.logoutStarted ? null : localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  getUserValue(): User | null {
    return this.currentUserSubject.value;
  }

  hasPermission(permission: string): boolean {
    const granted = this.getUserValue()?.permissions ?? [];
    if (granted.includes('*')) {
      return true;
    }

    // Mirrors the backend evaluator (PermissionCatalog.IsGranted): wildcard, exact or alias, case-insensitive.
    const accepted = [permission, ...(this.permissionAliases[permission] ?? [])].map(item => item.toLowerCase());
    return granted.some(item => accepted.includes(item.toLowerCase()));
  }

  hasAnyPermission(permissions: string[]): boolean {
    const user = this.getUserValue();
    const userPermissions = user?.permissions ?? [];
    if (userPermissions.includes('*')) {
      return true;
    }

    return permissions.some(p => this.hasPermission(p));
  }

  hasAllPermissions(permissions: string[]): boolean {
    const user = this.getUserValue();
    const userPermissions = user?.permissions ?? [];
    if (userPermissions.includes('*')) {
      return true;
    }

    return permissions.every(p => this.hasPermission(p));
  }

  hasRole(role: string): boolean {
    const user = this.getUserValue();
    return user?.roleName === role;
  }

  /** True only for a real (not role-switched) Super Admin session. */
  isSuperAdmin(): boolean {
    const user = this.getUserValue();
    if (!user || user.isRoleSwitched === true) {
      return false;
    }
    return user.roleName === 'Super Admin' && (user.realRoleName ?? user.roleName) === 'Super Admin';
  }

  isBusinessOwner(): boolean {
    return this.getUserValue()?.isBusinessOwner === true;
  }

  isRoleSwitched(): boolean {
    return this.getUserValue()?.isRoleSwitched === true;
  }

  getSwitchOptions(): Observable<UserContextOptions> {
    return this.http.get<UserContextOptions>(`${this.API_URL}/switch-options`);
  }

  switchRole(request: RoleSwitchRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.API_URL}/switch-role`, request).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  returnOwnerMode(): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.API_URL}/return-owner`, {}).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  private handleAuthResponse(response: LoginResponse): void {
    if (this.logoutStarted) {
      return;
    }

    const previousUser = this.currentUserSubject.value;
    if (previousUser?.id !== response.user?.id || previousUser?.roleName !== response.user?.roleName) {
      this.businessContextStore.clearAll();
    }

    localStorage.setItem(this.TOKEN_KEY, response.token);
    localStorage.setItem(this.REFRESH_TOKEN_KEY, response.refreshToken);
    localStorage.setItem(this.USER_KEY, JSON.stringify(response.user));
    this.currentUserSubject.next(response.user);
  }

  private getUserFromStorage(): User | null {
    const userJson = localStorage.getItem(this.USER_KEY);
    if (!userJson) return null;
    
    try {
      return JSON.parse(userJson);
    } catch {
      return null;
    }
  }

  private clearAuthState(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.businessContextStore.clearAll();
    this.currentUserSubject.next(null);
  }
}
