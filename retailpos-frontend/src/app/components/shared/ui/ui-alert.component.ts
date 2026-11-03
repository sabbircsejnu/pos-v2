import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

export type UiAlertVariant = 'success' | 'warning' | 'danger' | 'info';

const ICONS: Record<UiAlertVariant, string> = {
  success: 'fas fa-circle-check',
  warning: 'fas fa-triangle-exclamation',
  danger: 'fas fa-circle-exclamation',
  info: 'fas fa-circle-info'
};

/**
 * Inline alert for messages that belong to a page region (not the modal `AlertService` dialogs):
 *
 * ```html
 * <app-ui-alert variant="danger" title="Could not save" [dismissible]="true" (dismissed)="error.set(null)">
 *   {{ error() }}
 *   <button uiAlertActions appUiButton variant="outline" size="sm" (click)="retry()">Retry</button>
 * </app-ui-alert>
 * ```
 *
 * Danger and warning alerts use role="alert" (announced immediately); success and info use
 * role="status" (announced politely). The text is always shown, with a distinct icon per variant.
 * Dismissing hides the alert locally and emits `dismissed`; the feature can also just remove it with `@if`.
 */
@Component({
  selector: 'app-ui-alert',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-variant]': 'variant()',
    '[attr.title]': 'null',
    '[attr.role]': 'role()',
    '[hidden]': 'hidden()'
  },
  template: `
    <i class="ui-alert__icon" [class]="icon()" aria-hidden="true"></i>
    <div class="ui-alert__content">
      @if (title()) {
        <p class="ui-alert__title">{{ title() }}</p>
      }
      <div class="ui-alert__message"><ng-content /></div>
      <div class="ui-alert__actions"><ng-content select="[uiAlertActions]" /></div>
    </div>
    @if (dismissible()) {
      <button type="button" class="ui-alert__dismiss" [attr.aria-label]="dismissLabel()" (click)="dismiss()">
        <i class="fas fa-times" aria-hidden="true"></i>
      </button>
    }
  `,
  styles: [`
    :host {
      display: flex;
      align-items: flex-start;
      gap: var(--rp-space-3);
      padding: var(--rp-space-3) var(--rp-space-4);
      border: 1px solid var(--rp-color-status-info-border);
      border-radius: var(--rp-radius-control);
      background: var(--rp-color-status-info-bg);
      color: var(--rp-color-status-info-fg);
      font-family: var(--rp-font-family-base);
      font-size: var(--rp-text-body-size);
      line-height: var(--rp-text-body-line);
    }

    :host([hidden]) {
      display: none;
    }

    :host([data-variant='success']) {
      border-color: var(--rp-color-status-success-border);
      background: var(--rp-color-status-success-bg);
      color: var(--rp-color-status-success-fg);
    }

    :host([data-variant='warning']) {
      border-color: var(--rp-color-status-warning-border);
      background: var(--rp-color-status-warning-bg);
      color: var(--rp-color-status-warning-fg);
    }

    :host([data-variant='danger']) {
      border-color: var(--rp-color-status-danger-border);
      background: var(--rp-color-status-danger-bg);
      color: var(--rp-color-status-danger-fg);
    }

    .ui-alert__icon {
      flex-shrink: 0;
      margin-top: 0.2rem;
      color: var(--rp-color-status-info-icon);
    }

    :host([data-variant='success']) .ui-alert__icon {
      color: var(--rp-color-status-success-icon);
    }

    :host([data-variant='warning']) .ui-alert__icon {
      color: var(--rp-color-status-warning-icon);
    }

    :host([data-variant='danger']) .ui-alert__icon {
      color: var(--rp-color-status-danger-icon);
    }

    .ui-alert__content {
      flex: 1;
      min-width: 0;
    }

    .ui-alert__title {
      margin: 0;
      font-weight: var(--rp-font-weight-semibold);
    }

    .ui-alert__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rp-space-2);
      margin-top: var(--rp-space-2);
    }

    .ui-alert__actions:empty {
      display: none;
    }

    .ui-alert__dismiss {
      display: inline-flex;
      flex-shrink: 0;
      align-items: center;
      justify-content: center;
      width: 1.75rem;
      height: 1.75rem;
      margin: -0.25rem -0.5rem -0.25rem 0;
      border-radius: var(--rp-radius-control);
      color: inherit;
    }

    .ui-alert__dismiss:hover {
      background: rgba(19, 25, 32, 0.08);
    }

    .ui-alert__dismiss:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: 1px;
    }
  `]
})
export class UiAlertComponent {
  readonly variant = input<UiAlertVariant>('info');
  readonly title = input('');
  readonly dismissible = input(false);
  readonly dismissLabel = input('Dismiss');

  readonly dismissed = output<void>();

  readonly hidden = signal(false);
  readonly icon = computed(() => ICONS[this.variant()]);
  readonly role = computed(() => (this.variant() === 'danger' || this.variant() === 'warning' ? 'alert' : 'status'));

  dismiss(): void {
    this.hidden.set(true);
    this.dismissed.emit();
  }
}
