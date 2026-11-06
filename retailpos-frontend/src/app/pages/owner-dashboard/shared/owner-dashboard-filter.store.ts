import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from '../../../services/auth.service';
import { BusinessContextStore } from '../../../services/business-context.store';
import { AppliedFilter } from './owner-dashboard.logic';

/** A chart's own period; `null` follows the page period. */
export type WidgetPeriod = 'last7' | 'last30' | 'thisMonth' | 'lastMonth' | null;

export interface SalesScreenState {
  filter: AppliedFilter;
  /** True once the user has pressed Apply: charts then inherit the page period (FIL-009). */
  applied: boolean;
  cashPeriod: WidgetPeriod;
  trendPeriod: WidgetPeriod;
}

/**
 * Keeps each screen's committed filters while the user moves between screens (NAV-004). Location and category IDs mean
 * nothing in another Business, so changing user or Business forgets them.
 */
@Injectable({ providedIn: 'root' })
export class OwnerDashboardFilterStore {
  readonly sales = signal<SalesScreenState | null>(null);
  readonly inventory = signal<AppliedFilter | null>(null);

  private userId: unknown = null;
  private businessId: unknown = null;

  constructor() {
    // These streams also emit on token refresh and on restore, so only a real change of identity forgets anything.
    inject(AuthService).currentUser$.subscribe(user => {
      this.forgetIfChanged(user?.id ?? null, this.businessId);
    });
    inject(BusinessContextStore).selected$.subscribe(selected => {
      this.forgetIfChanged(this.userId, selected?.businessId ?? null);
    });
  }

  private forgetIfChanged(userId: unknown, businessId: unknown): void {
    if (userId === this.userId && businessId === this.businessId) {
      return;
    }

    this.userId = userId;
    this.businessId = businessId;
    this.sales.set(null);
    this.inventory.set(null);
  }
}
