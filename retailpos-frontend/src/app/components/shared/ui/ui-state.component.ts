import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type UiStateKind = 'loading' | 'empty' | 'error';

const DEFAULT_TITLES: Record<UiStateKind, string> = {
  loading: 'Loading…',
  empty: 'Nothing to show yet',
  error: 'Something went wrong'
};

const ICONS: Record<UiStateKind, string> = {
  loading: '',
  empty: 'fas fa-inbox',
  error: 'fas fa-circle-exclamation'
};

/**
 * Standard loading / empty / error block for lists and content areas.
 *
 * ```html
 * <app-ui-state kind="error" title="Could not load products" [message]="error()">
 *   <button appUiButton variant="outline" (click)="reload()">Retry</button>
 * </app-ui-state>
 * ```
 *
 * It is presentation only: the feature decides when to show it and supplies any retry or
 * call-to-action through the projected content, so existing error handling is untouched.
 */
@Component({
  selector: 'app-ui-state',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-kind]': 'kind()',
    '[attr.title]': 'null',
    '[attr.data-compact]': 'compact() ? "" : null',
    '[attr.role]': 'role()',
    '[attr.aria-live]': 'kind() === "loading" ? "polite" : null'
  },
  template: `
    <div class="ui-state__icon" aria-hidden="true">
      @if (kind() === 'loading') {
        <span class="ui-state__spinner"></span>
      } @else {
        <i [class]="icon()"></i>
      }
    </div>
    <p class="ui-state__title">{{ resolvedTitle() }}</p>
    @if (message()) {
      <p class="ui-state__message">{{ message() }}</p>
    }
    <div class="ui-state__actions"><ng-content /></div>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--rp-space-2);
      padding: var(--rp-space-10) var(--rp-space-4);
      color: var(--rp-color-text-secondary);
      font-family: var(--rp-font-family-base);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
      text-align: center;
    }

    :host([data-compact]) {
      padding: var(--rp-space-5) var(--rp-space-4);
    }

    .ui-state__icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 3rem;
      height: 3rem;
      border-radius: var(--rp-radius-pill);
      background: var(--rp-color-status-neutral-bg);
      color: var(--rp-color-status-neutral-fg);
      font-size: 1.25rem;
    }

    :host([data-kind='error']) .ui-state__icon {
      background: var(--rp-color-status-danger-bg);
      color: var(--rp-color-status-danger-icon);
    }

    :host([data-kind='loading']) .ui-state__icon {
      background: var(--rp-color-status-info-bg);
      color: var(--rp-color-status-info-icon);
    }

    .ui-state__spinner {
      width: 1.25rem;
      height: 1.25rem;
      border: 2px solid currentColor;
      border-right-color: transparent;
      border-radius: var(--rp-radius-pill);
      animation: ui-state-spin 800ms linear infinite;
    }

    .ui-state__title {
      margin: 0;
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-section-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: var(--rp-text-section-line);
    }

    .ui-state__message {
      max-width: 32rem;
      margin: 0;
    }

    .ui-state__actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--rp-space-2);
      margin-top: var(--rp-space-2);
    }

    .ui-state__actions:empty {
      display: none;
    }

    @keyframes ui-state-spin {
      to {
        transform: rotate(360deg);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .ui-state__spinner {
        animation-duration: 2s;
      }
    }
  `]
})
export class UiStateComponent {
  readonly kind = input<UiStateKind>('empty');
  readonly title = input('');
  readonly message = input('');
  readonly compact = input(false);

  readonly resolvedTitle = computed(() => this.title() || DEFAULT_TITLES[this.kind()]);
  readonly icon = computed(() => ICONS[this.kind()]);
  /** Errors are announced immediately, loading politely; an empty list is just content. */
  readonly role = computed(() => (this.kind() === 'error' ? 'alert' : this.kind() === 'loading' ? 'status' : null));
}
