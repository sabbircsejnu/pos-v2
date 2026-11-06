import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { UI_KIT } from '../../../components/shared/ui';
import { buildScale, compactNumber, scalePercent } from './owner-dashboard.logic';

export interface OdBarRow {
  key: string;
  label: string;
  /** The number the bar is drawn from. */
  value: number;
  valueText: string;
  detail?: string;
}

export interface OdSeries {
  key: string;
  label: string;
  /** A `--rp-*` colour token. */
  color: string;
}

export interface OdPoint {
  label: string;
  values: number[];
  texts: string[];
}

/** Horizontal comparison bars with an equivalent table. Rows can act as a filter when `selectable`. */
@Component({
  selector: 'app-od-bar-list',
  standalone: true,
  imports: [NgTemplateOutlet, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: block; }
    .od-bars__toolbar { display: flex; justify-content: flex-end; gap: var(--rp-space-1); margin-bottom: var(--rp-space-3); }
    .od-bars__list { display: flex; flex-direction: column; gap: var(--rp-space-1); margin: 0; padding: 0; list-style: none; }
    .od-bar { display: block; width: 100%; padding: var(--rp-space-2); border: 0; border-radius: var(--rp-radius-control); background: transparent; color: inherit; font: inherit; text-align: left; }
    button.od-bar { cursor: pointer; }
    button.od-bar:hover { background: var(--rp-color-surface-hover); }
    button.od-bar:focus-visible { outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring); outline-offset: var(--rp-focus-ring-offset); }
    .od-bar__head { display: flex; flex-wrap: wrap; justify-content: space-between; gap: var(--rp-space-2); margin-bottom: var(--rp-space-1); font-size: var(--rp-text-body-size); }
    .od-bar__label { color: var(--rp-color-text-primary); font-weight: var(--rp-font-weight-medium); }
    .od-bar__value { color: var(--rp-color-text-primary); font-variant-numeric: tabular-nums; }
    .od-bar__track { overflow: hidden; height: var(--rp-space-2); border-radius: var(--rp-radius-pill); background: var(--rp-color-neutral-200); }
    .od-bar__fill { height: 100%; border-radius: var(--rp-radius-pill); background: var(--rp-color-primary); }
    .od-bar__detail { margin-top: var(--rp-space-1); color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    table { width: 100%; border-collapse: collapse; font-size: var(--rp-text-table-cell-size); }
    th, td { padding: var(--rp-space-2); border-bottom: 1px solid var(--rp-color-border); text-align: left; }
    th { color: var(--rp-color-text-secondary); font-size: var(--rp-text-table-head-size); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    .od-table-wrap { overflow-x: auto; }
  `],
  template: `
    <div class="od-bars__toolbar">
      <button appUiButton type="button" size="sm" [variant]="view() === 'chart' ? 'secondary' : 'ghost'"
        [attr.aria-pressed]="view() === 'chart'" (click)="view.set('chart')">Chart</button>
      <button appUiButton type="button" size="sm" [variant]="view() === 'table' ? 'secondary' : 'ghost'"
        [attr.aria-pressed]="view() === 'table'" (click)="view.set('table')">Table</button>
    </div>
    @if (view() === 'chart') {
      <ul class="od-bars__list" [attr.aria-label]="caption()">
        @for (row of rows(); track row.key) {
          <li>
            @if (selectable()) {
              <button type="button" class="od-bar" (click)="selected.emit(row.key)"
                [attr.aria-label]="row.label + ', ' + row.valueText + '. Filter to this location'">
                <ng-container [ngTemplateOutlet]="body" [ngTemplateOutletContext]="{ $implicit: row }" />
              </button>
            } @else {
              <div class="od-bar"><ng-container [ngTemplateOutlet]="body" [ngTemplateOutletContext]="{ $implicit: row }" /></div>
            }
          </li>
        }
      </ul>
    } @else {
      <div class="od-table-wrap">
        <table>
          <caption class="sr-only">{{ caption() }}</caption>
          <thead><tr><th scope="col">{{ nameHeader() }}</th><th scope="col" class="num">{{ valueHeader() }}</th><th scope="col">{{ detailHeader() }}</th></tr></thead>
          <tbody>
            @for (row of rows(); track row.key) {
              <tr><td>{{ row.label }}</td><td class="num">{{ row.valueText }}</td><td>{{ row.detail }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
    <ng-template #body let-row>
      <div class="od-bar__head"><span class="od-bar__label">{{ row.label }}</span><span class="od-bar__value">{{ row.valueText }}</span></div>
      <div class="od-bar__track" aria-hidden="true"><div class="od-bar__fill" [style.width.%]="width(row.value)"></div></div>
      @if (row.detail) {<div class="od-bar__detail">{{ row.detail }}</div>}
    </ng-template>
  `
})
export class OdBarListComponent {
  readonly rows = input.required<readonly OdBarRow[]>();
  readonly caption = input('');
  readonly nameHeader = input('Location');
  readonly valueHeader = input('Value');
  readonly detailHeader = input('Detail');
  readonly selectable = input(false);
  readonly selected = output<string>();

  readonly view = signal<'chart' | 'table'>('chart');

  private readonly top = computed(() => Math.max(0, ...this.rows().map(row => row.value)));

  width(value: number): number {
    return this.top() > 0 ? Math.max(0, (value / this.top()) * 100) : 0;
  }
}

/**
 * Time-series chart with bars (`columns`) or a line. One axis only. The plot is plain HTML and a stretched SVG, so axis
 * text keeps its size at every width. Pointer, focus and arrow keys all move the same readout, and a table carries the
 * same numbers for anyone who cannot use the chart.
 */
@Component({
  selector: 'app-od-time-chart',
  standalone: true,
  imports: [...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: block; }
    .od-chart__top { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--rp-space-2); margin-bottom: var(--rp-space-3); }
    .od-legend { display: flex; flex-wrap: wrap; gap: var(--rp-space-3); margin: 0; padding: 0; list-style: none; color: var(--rp-color-text-secondary); font-size: var(--rp-text-label-size); }
    .od-legend li { display: inline-flex; align-items: center; gap: var(--rp-space-1); }
    .od-legend__swatch { width: var(--rp-space-3); height: var(--rp-space-3); border-radius: var(--rp-radius-control); }
    .od-chart__views { display: flex; gap: var(--rp-space-1); margin-left: auto; }
    .od-chart { display: grid; grid-template-columns: auto minmax(0, 1fr); column-gap: var(--rp-space-2); }
    .od-chart__y { position: relative; width: 2.75rem; height: 14rem; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); font-variant-numeric: tabular-nums; }
    .od-chart__y span { position: absolute; right: 0; transform: translateY(50%); }
    .od-plot { position: relative; height: 14rem; border-radius: var(--rp-radius-control); }
    .od-plot:focus-visible { outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring); outline-offset: var(--rp-focus-ring-offset); }
    .od-grid { position: absolute; left: 0; right: 0; border-top: 1px solid var(--rp-color-neutral-200); }
    .od-grid--zero { border-top-color: var(--rp-color-neutral-500); }
    .od-columns { position: absolute; inset: 0; display: flex; }
    .od-column { position: relative; flex: 1 1 0; min-width: 0; }
    .od-column--active { background: var(--rp-color-surface-hover); }
    .od-mark { position: absolute; border-radius: var(--rp-space-1); }
    svg { position: absolute; inset: 0; width: 100%; height: 100%; overflow: visible; }
    polyline { fill: none; stroke: var(--rp-color-primary); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; vector-effect: non-scaling-stroke; }
    polyline.od-line--second { stroke: var(--rp-color-warning-600); stroke-dasharray: 4 4; }
    .od-marker { position: absolute; width: var(--rp-space-2); height: var(--rp-space-2); border: 2px solid var(--rp-color-surface-card); border-radius: 50%; background: var(--rp-color-primary); transform: translate(-50%, 50%); }
    .od-crosshair { position: absolute; top: 0; bottom: 0; border-left: 1px dashed var(--rp-color-neutral-500); }
    .od-tip { position: absolute; top: var(--rp-space-1); z-index: 1; min-width: 10rem; padding: var(--rp-space-2); border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-control); background: var(--rp-color-surface-card); color: var(--rp-color-text-primary); font-size: var(--rp-text-helper-size); pointer-events: none; }
    .od-tip__title { margin: 0 0 var(--rp-space-1); font-weight: var(--rp-font-weight-semibold); }
    .od-tip__row { display: flex; justify-content: space-between; gap: var(--rp-space-3); font-variant-numeric: tabular-nums; }
    .od-chart__x { grid-column: 2; display: flex; justify-content: space-between; gap: var(--rp-space-2); margin-top: var(--rp-space-1); color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    table { width: 100%; border-collapse: collapse; font-size: var(--rp-text-table-cell-size); }
    th, td { padding: var(--rp-space-2); border-bottom: 1px solid var(--rp-color-border); text-align: left; }
    th { color: var(--rp-color-text-secondary); font-size: var(--rp-text-table-head-size); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    .od-table-wrap { overflow-x: auto; max-height: 18rem; overflow-y: auto; }
  `],
  template: `
    <div class="od-chart__top">
      @if (series().length > 1) {
        <ul class="od-legend" aria-label="Legend">
          @for (item of series(); track item.key) {
            <li><span class="od-legend__swatch" [style.background]="'var(' + item.color + ')'" aria-hidden="true"></span>{{ item.label }}</li>
          }
        </ul>
      }
      <div class="od-chart__views">
        <button appUiButton type="button" size="sm" [variant]="view() === 'chart' ? 'secondary' : 'ghost'"
          [attr.aria-pressed]="view() === 'chart'" (click)="view.set('chart')">Chart</button>
        <button appUiButton type="button" size="sm" [variant]="view() === 'table' ? 'secondary' : 'ghost'"
          [attr.aria-pressed]="view() === 'table'" (click)="view.set('table')">Table</button>
      </div>
    </div>
    @if (view() === 'chart') {
      <div class="od-chart">
        <div class="od-chart__y" aria-hidden="true">
          @for (tick of scale().ticks; track tick) {
            <span [style.bottom.%]="percent(tick)">{{ compact(tick) }}</span>
          }
        </div>
        <div class="od-plot" tabindex="0" role="img" [attr.aria-label]="summary()"
          (keydown)="onKey($event)" (blur)="active.set(null)" (focus)="focusFirst()"
          (pointerleave)="active.set(null)">
          @for (tick of scale().ticks; track tick) {
            <div class="od-grid" [class.od-grid--zero]="tick === 0" [style.bottom.%]="percent(tick)" aria-hidden="true"></div>
          }
          @if (kind() === 'line') {
            <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
              <polyline [attr.points]="linePoints()" />
              @for (segment of secondLineSegments(); track $index) {
                <polyline class="od-line--second" [attr.points]="segment" />
              }
            </svg>
            @if (active() !== null) {
              <div class="od-crosshair" [style.left.%]="xPercent(active()!)" aria-hidden="true"></div>
              <div class="od-marker" [style.left.%]="xPercent(active()!)" [style.bottom.%]="percent(points()[active()!].values[0])" aria-hidden="true"></div>
            }
          }
          <div class="od-columns">
            @for (point of points(); track $index) {
              <div class="od-column" [class.od-column--active]="kind() === 'columns' && active() === $index"
                (pointerenter)="active.set($index)" aria-hidden="true">
                @if (kind() === 'columns') {
                  @for (value of point.values; track $index) {
                    <div class="od-mark"
                      [style.left.%]="barLeft($index)" [style.width.%]="barWidth()"
                      [style.bottom.%]="barBottom(value)" [style.height.%]="barHeight(value)"
                      [style.background]="'var(' + series()[$index].color + ')'"></div>
                  }
                }
              </div>
            }
          </div>
          @if (active() !== null) {
            <div class="od-tip" [style.left]="tipLeft()" [style.right]="tipRight()" role="status">
              <p class="od-tip__title">{{ points()[active()!].label }}</p>
              @for (item of series(); track item.key) {
                <div class="od-tip__row"><span>{{ item.label }}</span><span>{{ points()[active()!].texts[$index] }}</span></div>
              }
            </div>
          }
        </div>
        <div class="od-chart__x" aria-hidden="true">
          @if (points().length) {
            <span>{{ points()[0].label }}</span>
            @if (points().length > 1) {<span>{{ points()[points().length - 1].label }}</span>}
          }
        </div>
      </div>
    } @else {
      <div class="od-table-wrap">
        <table>
          <caption class="sr-only">{{ caption() }}</caption>
          <thead>
            <tr>
              <th scope="col">Period</th>
              @for (item of series(); track item.key) {<th scope="col" class="num">{{ item.label }}</th>}
            </tr>
          </thead>
          <tbody>
            @for (point of points(); track $index) {
              <tr>
                <td>{{ point.label }}</td>
                @for (text of point.texts; track $index) {<td class="num">{{ text }}</td>}
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `
})
export class OdTimeChartComponent {
  readonly kind = input<'columns' | 'line'>('columns');
  readonly caption = input.required<string>();
  readonly series = input.required<readonly OdSeries[]>();
  readonly points = input.required<readonly OdPoint[]>();

  readonly view = signal<'chart' | 'table'>('chart');
  readonly active = signal<number | null>(null);

  readonly scale = computed(() => buildScale(this.points().flatMap(point => point.values)));

  readonly linePoints = computed(() =>
    this.points()
      .map((point, index) => `${this.xPercent(index)},${100 - this.percent(point.values[0])}`)
      .join(' ')
  );

  /** An optional second line (values[1]). A point without one breaks the line, so a gap is never drawn as a value. */
  readonly secondLineSegments = computed(() => {
    const segments: string[][] = [[]];
    this.points().forEach((point, index) => {
      if (point.values.length > 1) {
        segments[segments.length - 1].push(`${this.xPercent(index)},${100 - this.percent(point.values[1])}`);
      } else if (segments[segments.length - 1].length) {
        segments.push([]);
      }
    });
    return segments.filter(segment => segment.length).map(segment => segment.join(' '));
  });

  readonly summary = computed(() =>
    `${this.caption()}. ${this.points().length} periods. Use the left and right arrow keys to read each one, or switch to the table view.`
  );

  readonly tipLeft = computed(() => {
    const index = this.active();
    return index !== null && this.xPercent(index) <= 50 ? `${this.xPercent(index)}%` : null;
  });

  readonly tipRight = computed(() => {
    const index = this.active();
    return index !== null && this.xPercent(index) > 50 ? `${100 - this.xPercent(index)}%` : null;
  });

  percent(value: number): number {
    return scalePercent(value, this.scale());
  }

  compact(value: number): string {
    return compactNumber(value);
  }

  /** The centre of a point's column, shared by the line, crosshair and tooltip. */
  xPercent(index: number): number {
    return ((index + 0.5) / Math.max(this.points().length, 1)) * 100;
  }

  barWidth(): number {
    return 72 / Math.max(this.series().length, 1);
  }

  barLeft(seriesIndex: number): number {
    return 14 + seriesIndex * this.barWidth();
  }

  barBottom(value: number): number {
    return Math.min(this.percent(0), this.percent(value));
  }

  barHeight(value: number): number {
    return Math.abs(this.percent(value) - this.percent(0));
  }

  focusFirst(): void {
    if (this.active() === null && this.points().length) {
      this.active.set(0);
    }
  }

  onKey(event: KeyboardEvent): void {
    const last = this.points().length - 1;
    if (last < 0 || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) {
      return;
    }

    event.preventDefault();
    const current = this.active() ?? 0;
    this.active.set(event.key === 'ArrowLeft' ? Math.max(0, current - 1) : Math.min(last, current + 1));
  }
}
