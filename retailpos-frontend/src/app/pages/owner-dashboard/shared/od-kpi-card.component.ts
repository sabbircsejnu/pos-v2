import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { UI_KIT, UiBadgeVariant } from '../../../components/shared/ui';
import { MetricStatus } from '../../../models/owner-dashboard.model';

const STATUS_LABELS: Partial<Record<MetricStatus, { text: string; variant: UiBadgeVariant }>> = {
  estimated: { text: 'Estimated', variant: 'info' },
  partial: { text: 'Partial', variant: 'warning' },
  unavailable: { text: 'Unavailable', variant: 'warning' },
  not_configured: { text: 'Not configured', variant: 'neutral' },
  disabled: { text: 'Switched off', variant: 'neutral' },
  off_day: { text: 'Scheduled Off Day', variant: 'neutral' }
};

/** A single headline figure. A missing value always reads as a labelled state, never as zero. */
@Component({
  selector: 'app-od-kpi-card',
  standalone: true,
  imports: [...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: block; min-width: 0; }
    app-ui-card { height: 100%; }
    .od-kpi { display: flex; flex-direction: column; gap: var(--rp-space-1); min-width: 0; }
    .od-kpi__head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--rp-space-2); }
    .od-kpi__label { margin: 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-medium); }
    .od-kpi__value { margin: 0; color: var(--rp-color-text-primary); font-size: var(--rp-text-page-title-size); font-weight: var(--rp-font-weight-semibold); line-height: var(--rp-text-page-title-line); font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
    .od-kpi__value--empty { color: var(--rp-color-text-secondary); }
    .od-kpi__detail { margin: 0; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
  `],
  template: `
    <app-ui-card [loading]="loading()" loadingLabel="Loading">
      <div class="od-kpi" [attr.title]="note() || null">
        <div class="od-kpi__head">
          <p class="od-kpi__label">{{ label() }}</p>
          @if (badge(); as status) {
            <app-ui-badge [variant]="status.variant">{{ status.text }}</app-ui-badge>
          }
        </div>
        @if (error()) {
          <p class="od-kpi__value od-kpi__value--empty">—</p>
          <p class="od-kpi__detail">Could not load</p>
        } @else {
          <p class="od-kpi__value" [class.od-kpi__value--empty]="!value()">{{ value() || emptyText() }}</p>
          @if (detail() || note()) {
            <p class="od-kpi__detail">{{ detail() || note() }}</p>
          }
        }
      </div>
    </app-ui-card>
  `
})
export class OdKpiCardComponent {
  readonly label = input.required<string>();
  /** The already formatted value; empty renders N/A. */
  readonly value = input<string | null>(null);
  readonly status = input<MetricStatus>('ok');
  /** Replaces the status badge's wording where a figure needs its own, such as Partial Value. */
  readonly statusLabel = input<string | null>(null);
  readonly detail = input<string | null>(null);
  readonly note = input<string | null>(null);
  readonly loading = input(false);
  readonly error = input(false);

  readonly badge = computed(() => {
    const badge = STATUS_LABELS[this.status()];
    return badge ? { ...badge, text: this.statusLabel() ?? badge.text } : null;
  });

  readonly emptyText = computed(() => (this.status() === 'not_valued' ? 'Not Valued' : 'N/A'));
}
