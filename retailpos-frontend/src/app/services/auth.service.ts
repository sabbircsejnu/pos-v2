import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse, RegisterRequest, RoleSwitchRequest, User } from '../models/auth.models';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly permissionAliases: Record<string, string[]> = {
    'accounting.chart_accounts.view': ['accounts.view'],
    'accounting.chart_accounts.create': ['accounts.create'],
    'accounting.chart_accounts.edit': ['accounts.edit', 'accounts.delete'],
    'accounting.journals.view': ['transactions.view'],
    'accounting.journals.create': ['transactions.create'],
    'accounting.journals.update': ['transactions.edit'],
    'accounting.journals.cancel': ['transactions.delete'],
    'accounting.journals.post': ['transactions.create', 'transactions.edit'],
    'accounting.journals.reverse': ['transactions.edit', 'transactions.delete'],
    'accounting.expenses.view': ['expenses.view'],
    'accounting.expenses.create': ['expenses.create'],
    'accounting.expenses.edit': ['expenses.edit'],
    'accounting.expenses.submit': ['expenses.create', 'expenses.edit'],
    'accounting.expenses.approve': ['expenses.edit'],
    'accounting.expenses.reject': ['expenses.edit'],
    'accounting.expenses.cancel': ['expenses.delete']
  };

  private readonly API_URL = `${environment.apiUrl}/auth`;
  private readonly TOKEN_KEY = 'auth_token';
  private readonly REFRESH_TOKEN_KEY = 'refresh_token';
  private readonly USER_KEY = 'current_user';

  private currentUserSubject = new BehaviorSubject<User | null>(this.getUserFromStorage());
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {}

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.API_URL}/login`, request).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  register(request: RegisterRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.API_URL}/register`, request).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_TOKEN_KEY);
    localStorage.removeItem(this.USER_KEY);
    this.currentUserSubject.next(null);
  }

  getCurrentUser(): Observable<User> {
    return this.http.get<User>(`${this.API_URL}/me`).pipe(
      tap(user => {
        localStorage.setItem(this.USER_KEY, JSON.stringify(user));
        this.currentUserSubject.next(user);
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
    return localStorage.getItem(this.REFRESH_TOKEN_KEY);
  }

  getUserValue(): User | null {
    return this.currentUserSubject.value;
  }

  hasPermission(permission: string): boolean {
    const user = this.getUserValue();
    const permissions = user?.permissions ?? [];
    const accepted = [permission, ...(this.permissionAliases[permission] ?? [])];
    return permissions.includes('*') || accepted.some(item => permissions.includes(item));
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

  isBusinessOwner(): boolean {
    return this.getUserValue()?.isBusinessOwner === true;
  }

  isRoleSwitched(): boolean {
    return this.getUserValue()?.isRoleSwitched === true;
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
}
