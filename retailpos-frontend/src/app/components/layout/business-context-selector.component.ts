import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpContext } from '@angular/common/http';
import { Router } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '../../services/auth.service';
import { BusinessService } from '../../services/business.service';
import { BusinessContextService } from '../../services/business-context.service';
import { SKIP_BUSINESS_CONTEXT } from '../../services/business-context.store';
import { BusinessSummaryDto } from '../../models/business.model';

/** Explicit Super Admin Business context selector. Nothing is selected automatically. */
@Component({
  selector: 'app-business-context-selector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    @if (isSuperAdmin()) {
      <div class="rp-context" role="group" aria-label="Business context"
           data-testid="business-context-selector">
        <label for="business-context-select" class="rp-context__label">Business context</label>
        <select id="business-context-select"
                class="rp-context__select"
                [(ngModel)]="pendingBusinessId"
                [disabled]="busy() || contextService.restoring()">
          <option [ngValue]="null">Platform (no Business selected)</option>
          @for (business of businesses(); track business.businessId) {
            <option [ngValue]="business.businessId">{{ business.businessName }}</option>
          }
        </select>
        <button type="button"
                class="rp-context__apply"
                [disabled]="busy() || contextService.restoring() || pendingBusinessId === (selected()?.businessId ?? null)"
                (click)="apply()">
          Apply
        </button>
        <span class="rp-context__active" data-testid="business-context-active" aria-live="polite">
          Active: {{ selected()?.businessName ?? (selected() ? 'Business #' + selected()!.businessId : 'Platform') }}
        </span>
        @if (error()) {
          <span class="rp-context__error" role="alert">{{ error() }}</span>
        }
      </div>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .rp-context {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      padding: 12px 16px;
      border: 1px solid var(--rp-color-border, #dbe0e5);
      border-left: 4px solid var(--rp-color-warning-500, #e58a00);
      border-radius: var(--rp-radius-control, 8px);
      background: var(--rp-color-surface-card, #ffffff);
      font-family: var(--rp-font-family-shell, system-ui, 'Segoe UI', Roboto, Arial, sans-serif);
      font-size: 14px;
    }

    .rp-context__label {
      color: var(--rp-color-text-primary, #1d2630);
      font-weight: 600;
    }

    .rp-context__select {
      min-width: 14rem;
      max-width: 100%;
      height: 40px;
      padding: 0 12px;
      border: 1px solid var(--rp-color-border, #dbe0e5);
      border-radius: var(--rp-radius-control, 8px);
      background: var(--rp-color-surface-card, #ffffff);
      color: var(--rp-color-text-primary, #1d2630);
    }

    .rp-context__select:focus {
      border-color: var(--rp-color-primary-500, #4680ff);
      box-shadow: 0 0 0 3px var(--rp-color-primary-soft, rgba(70, 128, 255, 0.1));
      outline: none;
    }

    .rp-context__select:disabled {
      background: var(--rp-color-neutral-200, #f3f5f7);
      cursor: not-allowed;
    }

    .rp-context__apply {
      height: 40px;
      padding: 0 20px;
      border-radius: var(--rp-radius-control, 8px);
      background: var(--rp-color-primary-700, #1c76da);
      color: #ffffff;
      font-weight: 500;
      transition: background-color 150ms ease;
    }

    .rp-context__apply:hover:not(:disabled) {
      background: var(--rp-color-primary-800, #176cd6);
    }

    .rp-context__apply:disabled {
      cursor: not-allowed;
      opacity: 0.5;
    }

    .rp-context__select:focus-visible,
    .rp-context__apply:focus-visible {
      outline: 2px solid var(--rp-color-focus-ring, #4680ff);
      outline-offset: 2px;
    }

    .rp-context__active {
      color: var(--rp-color-text-secondary, #5b6b79);
    }

    .rp-context__error {
      color: var(--rp-color-danger-700, #d31c1c);
      font-weight: 500;
    }

    @media (prefers-reduced-motion: reduce) {
      .rp-context__apply {
        transition: none;
      }
    }
  `]
})
export class BusinessContextSelectorComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly businessService = inject(BusinessService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly contextService = inject(BusinessContextService);

  readonly selected = toSignal(this.contextService.selected$, { initialValue: null });
  readonly currentUser = toSignal(this.authService.currentUser$, { initialValue: null });
  readonly businesses = signal<BusinessSummaryDto[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  pendingBusinessId: number | null = null;

  isSuperAdmin(): boolean {
    this.currentUser();
    return this.authService.isSuperAdmin();
  }

  ngOnInit(): void {
    this.contextService.selected$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(selected => {
      this.pendingBusinessId = selected?.businessId ?? null;
    });
    if (this.authService.isSuperAdmin()) {
      this.loadBusinesses();
    }
  }

  apply(): void {
    this.busy.set(true);
    this.error.set(null);
    this.contextService.select(this.pendingBusinessId).subscribe({
      next: () => {
        this.busy.set(false);
        this.router.navigateByUrl('/dashboard');
      },
      error: (err) => {
        this.busy.set(false);
        this.pendingBusinessId = this.selected()?.businessId ?? null;
        this.error.set(err?.error?.message ?? err?.message ?? 'Unable to change Business context.');
      }
    });
  }

  private loadBusinesses(): void {
    // Listing must not depend on the current selection, so a stale context can never block a change.
    const context = new HttpContext().set(SKIP_BUSINESS_CONTEXT, true);
    this.businessService.getBusinesses(undefined, true, context).subscribe({
      next: response => this.businesses.set((response?.data ?? []).filter(b => b.isActive)),
      error: () => this.error.set('Unable to load Businesses.')
    });
  }
}