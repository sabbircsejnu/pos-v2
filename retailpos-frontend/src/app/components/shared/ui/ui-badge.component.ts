import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type UiBadgeVariant = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

const ICONS: Record<UiBadgeVariant, string> = {
  success: 'fas fa-circle-check',
  warning: 'fas fa-triangle-exclamation',
  danger: 'fas fa-circle-xmark',
  info: 'fas fa-circle-info',
  neutral: ''
};

/**
 * Status badge. The status is always carried by its text (projected content), and every variant
 * except neutral also shows a distinct icon, so colour is never the only signal.
 *
 * `<app-ui-badge variant="success">Paid</app-ui-badge>`
 */
@Component({
  selector: 'app-ui-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-variant]': 'variant()'
  },
  template: `@if (showIcon() && icon()) {<i [class]="icon()" aria-hidden="true"></i>}<ng-content />`,
  styles: [`
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--rp-space-1);
      padding: 2px var(--rp-space-2);
      border: 1px solid var(--rp-color-status-neutral-border);
      border-radius: var(--rp-radius-pill);
      background: var(--rp-color-status-neutral-bg);
      color: var(--rp-color-status-neutral-fg);
      font-family: var(--rp-font-family-base);
      font-size: var(--rp-text-helper-size);
      font-weight: var(--rp-font-weight-semibold);
      line-height: 1.25;
      white-space: nowrap;
    }

    :host([data-variant='success']) {
      border-color: var(--rp-color-status-success-border);
      background: var(--rp-color-status-success-bg);
      color: var(--rp-color-status-success-fg);
    }

    :host([data-variant='success']) i {
      color: var(--rp-color-status-success-icon);
    }

    :host([data-variant='warning']) {
      border-color: var(--rp-color-status-warning-border);
      background: var(--rp-color-status-warning-bg);
      color: var(--rp-color-status-warning-fg);
    }

    :host([data-variant='warning']) i {
      color: var(--rp-color-status-warning-icon);
    }

    :host([data-variant='danger']) {
      border-color: var(--rp-color-status-danger-border);
      background: var(--rp-color-status-danger-bg);
      color: var(--rp-color-status-danger-fg);
    }

    :host([data-variant='danger']) i {
      color: var(--rp-color-status-danger-icon);
    }

    :host([data-variant='info']) {
      border-color: var(--rp-color-status-info-border);
      background: var(--rp-color-status-info-bg);
      color: var(--rp-color-status-info-fg);
    }

    :host([data-variant='info']) i {
      color: var(--rp-color-status-info-icon);
    }
  `]
})
export class UiBadgeComponent {
  readonly variant = input<UiBadgeVariant>('neutral');
  readonly showIcon = input(true);

  readonly icon = computed(() => ICONS[this.variant()]);
}
