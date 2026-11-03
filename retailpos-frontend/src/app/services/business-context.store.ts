import { Injectable } from '@angular/core';
import { HttpContextToken } from '@angular/common/http';
import { BehaviorSubject, Observable, filter, map, take } from 'rxjs';

export const BUSINESS_CONTEXT_HEADER = 'X-Business-Id';

/** Marks a request that must never carry the selected Business context header. */
export const SKIP_BUSINESS_CONTEXT = new HttpContextToken<boolean>(() => false);

/** Normalizes a user id that may arrive as a number or numeric string from storage or the API. */
export function normalizeUserId(userId: unknown): number | null {
  const id = typeof userId === 'string' && /^\d+$/.test(userId.trim()) ? Number(userId.trim()) : userId;
  return typeof id === 'number' && Number.isSafeInteger(id) && id > 0 ? id : null;
}

export interface SelectedBusinessContext {
  userId: number;
  businessId: number;
  businessName: string | null;
}

/**
 * Dependency-free holder for the server-verified Super Admin Business context.
 * Kept separate from HTTP services so the auth interceptor and AuthService can use it
 * without creating a circular HttpClient dependency.
 */
@Injectable({ providedIn: 'root' })
export class BusinessContextStore {
  private static readonly KEY_PREFIX = 'business_context:';

  private readonly selectedSubject = new BehaviorSubject<SelectedBusinessContext | null>(null);
  readonly selected$: Observable<SelectedBusinessContext | null> = this.selectedSubject.asObservable();

  /** User id whose persisted selection is currently being validated by the server, if any. */
  private readonly restoringSubject = new BehaviorSubject<number | null>(null);

  current(): SelectedBusinessContext | null {
    return this.selectedSubject.value;
  }

  /** Returns the verified Business id only when it belongs to the given user. */
  getVerifiedBusinessId(userId: unknown): number | null {
    const selected = this.selectedSubject.value;
    const id = normalizeUserId(userId);
    if (!selected || id === null || selected.userId !== id) {
      return null;
    }
    return selected.businessId;
  }

  setVerified(context: SelectedBusinessContext): void {
    const userId = normalizeUserId(context.userId);
    if (userId === null) {
      return;
    }
    this.selectedSubject.next({ ...context, userId });
    this.storage()?.setItem(this.key(userId), String(context.businessId));
  }

  /** Clears the in-memory selection and, when a user id is given, that user's persisted selection. */
  clearSelection(userId?: unknown): void {
    if (this.selectedSubject.value !== null) {
      this.selectedSubject.next(null);
    }
    const id = normalizeUserId(userId);
    if (id !== null) {
      this.storage()?.removeItem(this.key(id));
    }
  }

  readPersisted(userId: unknown): number | null {
    const id = normalizeUserId(userId);
    if (id === null) {
      return null;
    }
    const raw = this.storage()?.getItem(this.key(id));
    if (!raw || !/^\d+$/.test(raw)) {
      return null;
    }
    const businessId = Number(raw);
    return Number.isSafeInteger(businessId) && businessId > 0 ? businessId : null;
  }

  beginRestore(userId: unknown): void {
    const id = normalizeUserId(userId);
    if (id !== null) {
      this.restoringSubject.next(id);
    }
  }

  endRestore(userId?: unknown): void {
    const restoring = this.restoringSubject.value;
    const id = normalizeUserId(userId);
    if (restoring !== null && (userId == null || restoring === id)) {
      this.restoringSubject.next(null);
    }
  }

  isRestoring(userId: unknown): boolean {
    const id = normalizeUserId(userId);
    return id !== null && this.restoringSubject.value === id;
  }

  /** Emits once no restoration is pending for the given user (immediately if none is). */
  whenSettled(userId: unknown): Observable<void> {
    const id = normalizeUserId(userId);
    return this.restoringSubject.pipe(
      filter(restoring => id === null || restoring !== id),
      take(1),
      map(() => undefined)
    );
  }

  clearAll(): void {
    this.endRestore();
    this.clearSelection();
    const storage = this.storage();
    if (!storage) {
      return;
    }
    const keys: string[] = [];
    for (let i = 0; i < storage.length; i++) {
      const key = storage.key(i);
      if (key?.startsWith(BusinessContextStore.KEY_PREFIX)) {
        keys.push(key);
      }
    }
    keys.forEach(key => storage.removeItem(key));
  }

  private key(userId: number): string {
    return `${BusinessContextStore.KEY_PREFIX}${userId}`;
  }

  private storage(): Storage | null {
    return typeof localStorage === 'undefined' ? null : localStorage;
  }
}
