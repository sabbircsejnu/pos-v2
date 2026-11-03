import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable, Subscription, catchError, finalize, map, of, switchMap, throwError, timeout } from 'rxjs';
import { environment } from '../../environments/environment';
import { User } from '../models/auth.models';
import { AuthService } from './auth.service';
import { BusinessContextStore, SKIP_BUSINESS_CONTEXT, normalizeUserId } from './business-context.store';

export interface BusinessContextResponse {
  businessId: number | null;
  businessName: string | null;
}

/**
 * Super Admin Business context selection. A selection only becomes active after the
 * server validates it; nothing is ever selected automatically.
 */
@Injectable({ providedIn: 'root' })
export class BusinessContextService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);
  private readonly store = inject(BusinessContextStore);

  private readonly url = `${environment.apiUrl}/auth/business-context`;
  private readonly userSubscription: Subscription;
  private lastUserId: number | null = null;
  private lastSuperAdmin = false;
  private restoreSubscription?: Subscription;
  private static readonly RESTORE_TIMEOUT_MS = 15000;

  readonly selected$ = this.store.selected$;
  readonly restoring = signal(false);

  constructor() {
    this.userSubscription = this.authService.currentUser$.subscribe(user => this.onUserChanged(user));
  }

  ngOnDestroy(): void {
    this.userSubscription.unsubscribe();
    this.restoreSubscription?.unsubscribe();
  }

  /** Validates and applies a selection. `null` returns the Super Admin to platform scope. */
  select(businessId: number | null): Observable<BusinessContextResponse> {
    const userId = normalizeUserId(this.authService.getUserValue()?.id);
    if (userId === null || !this.authService.isSuperAdmin()) {
      return throwError(() => new Error('Only Super Admin can select a Business context.'));
    }
    if (businessId !== null && !(Number.isSafeInteger(businessId) && businessId > 0)) {
      return throwError(() => new Error('Invalid Business selection.'));
    }

    // An explicit choice supersedes any in-flight restoration of the previously saved one.
    this.restoreSubscription?.unsubscribe();

    return this.validate(businessId).pipe(
      map(response => {
        if (normalizeUserId(this.authService.getUserValue()?.id) !== userId) {
          throw new Error('The signed-in user changed during Business selection.');
        }
        if (response.businessId !== businessId) {
          throw new Error('The server did not confirm the requested Business context.');
        }
        if (response.businessId === null) {
          this.store.clearSelection(userId);
        } else {
          this.store.setVerified({ userId, businessId: response.businessId, businessName: response.businessName });
        }
        return response;
      }),
      switchMap(response => this.refreshProfile().pipe(map(() => response)))
    );
  }

  clear(): Observable<BusinessContextResponse> {
    return this.select(null);
  }

  /**
   * Resolves once any persisted selection for the signed-in Super Admin has been validated by
   * the server (or there is nothing to restore). Starts restoration if it has not run yet or a
   * previous attempt failed transiently, so callers such as the route guard and app initializer
   * never decide before the saved context had a real chance to be verified.
   */
  ensureRestored(): Observable<void> {
    const userId = normalizeUserId(this.authService.getUserValue()?.id);
    if (userId === null || !this.authService.isSuperAdmin()) {
      return of(undefined);
    }
    if (this.store.getVerifiedBusinessId(userId) === null && !this.store.isRestoring(userId)) {
      this.restore(userId);
    }
    return this.store.whenSettled(userId);
  }

  private onUserChanged(user: User | null): void {
    // Numeric normalization: a /me re-emission of the same identity must never look like a user change.
    const userId = normalizeUserId(user?.id);
    const superAdmin = userId !== null && this.authService.isSuperAdmin();
    if (userId === this.lastUserId && superAdmin === this.lastSuperAdmin) {
      return;
    }

    const previousUserId = this.lastUserId;
    const sameUser = userId !== null && userId === previousUserId;
    this.lastUserId = userId;
    this.lastSuperAdmin = superAdmin;
    this.restoreSubscription?.unsubscribe();
    this.store.endRestore(previousUserId);
    // Leaving Super Admin scope (e.g. a role switch) drops the in-memory context but keeps the
    // user's saved choice; a different user never inherits the previous user's selection.
    this.store.clearSelection(sameUser ? null : previousUserId);
    this.restoring.set(false);

    if (userId !== null && superAdmin) {
      this.restore(userId);
    }
  }

  /**
   * Re-validates a persisted selection. While pending, Business-scoped API requests and
   * navigation for this user wait (see the auth interceptor and route guard) instead of
   * running unscoped; the saved id is never sent until the server confirms it. Only a definitive
   * server rejection forgets the saved id; transient failures (offline, timeout, 5xx) keep it so
   * a later navigation can retry. Restoration only settles after /auth/me has reported the
   * restored scope, so held requests and guarded navigation are released with a fully
   * restored profile rather than racing the profile refresh.
   */
  private restore(userId: number): void {
    const persisted = this.store.readPersisted(userId);
    if (persisted === null) {
      return;
    }

    this.restoreSubscription?.unsubscribe();
    this.restoring.set(true);
    this.store.beginRestore(userId);
    const settle = () => {
      this.store.endRestore(userId);
      this.restoring.set(false);
    };

    this.restoreSubscription = this.validate(persisted).pipe(
      timeout(BusinessContextService.RESTORE_TIMEOUT_MS),
      switchMap(response => {
        if (normalizeUserId(this.authService.getUserValue()?.id) !== userId) {
          return of(null);
        }
        if (response.businessId !== persisted) {
          this.store.clearSelection(userId);
          return of(null);
        }
        this.store.setVerified({ userId, businessId: persisted, businessName: response.businessName });
        return this.refreshProfile();
      }),
      catchError(error => {
        if (normalizeUserId(this.authService.getUserValue()?.id) === userId && BusinessContextService.isRejection(error)) {
          this.store.clearSelection(userId);
        }
        return of(null);
      }),
      finalize(settle)
    ).subscribe();
  }

  /** The server definitively refused the saved Business (inactive/unknown/not allowed). */
  private static isRejection(error: unknown): boolean {
    const status = (error as { status?: unknown } | null)?.status;
    return status === 400 || status === 403 || status === 404;
  }
  /** The validation request never carries the previous context so a stale header cannot block a change. */
  private validate(businessId: number | null): Observable<BusinessContextResponse> {
    return this.http.post<unknown>(
      this.url,
      { businessId },
      { context: new HttpContext().set(SKIP_BUSINESS_CONTEXT, true) }
    ).pipe(map(raw => this.normalize(raw)));
  }

  private normalize(raw: unknown): BusinessContextResponse {
    const body = (raw && typeof raw === 'object' && 'data' in raw ? (raw as { data: unknown }).data : raw) as
      Partial<BusinessContextResponse> | null | undefined;
    const businessId = typeof body?.businessId === 'number' ? body.businessId : null;
    const businessName = typeof body?.businessName === 'string' ? body.businessName : null;
    return { businessId, businessName };
  }

  /** Re-reads /auth/me so the profile's businessId reflects the active scope. */
  private refreshProfile(): Observable<unknown> {
    return this.authService.getCurrentUser().pipe(catchError(() => of(null)));
  }
}
