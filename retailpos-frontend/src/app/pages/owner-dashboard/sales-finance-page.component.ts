import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { UI_KIT } from '../../components/shared/ui';
import {
  CashFlow,
  MetricValue,
  OwnerDashboardMeta,
  SalesFinanceSummary,
  SalesTrend,
  ShowroomPerformance,
  ShowroomPerformanceRow
} from '../../models/owner-dashboard.model';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { OwnerDashboardService, SalesQuery } from '../../services/owner-dashboard.service';
import { OdBarListComponent, OdBarRow, OdPoint, OdSeries, OdTimeChartComponent } from './shared/od-charts.component';
import { OdFilterBarComponent } from './shared/od-filter-bar.component';
import { OdKpiCardComponent } from './shared/od-kpi-card.component';
import { OwnerDashboardFilterStore, SalesScreenState, WidgetPeriod } from './shared/owner-dashboard-filter.store';
import {
  AppliedFilter,
  DATE_PRESETS,
  DatePreset,
  DateRange,
  bucketLabel,
  createWidget,
  isIsoDateOnly,
  parseLocation,
  resolvePreset,
  validateRange
} from './shared/owner-dashboard.logic';

const CASH_SERIES: OdSeries[] = [
  { key: 'received', label: 'Cash Received', color: '--rp-color-primary' },
  { key: 'paid', label: 'Operational Expense Payments', color: '--rp-color-neutral-600' }
];
const TREND_SERIES: OdSeries[] = [{ key: 'net', label: 'Net Sales', color: '--rp-color-primary' }];
const TREND_TARGET_SERIES: OdSeries[] = [...TREND_SERIES, { key: 'target', label: 'Target', color: '--rp-color-warning-600' }];

const WIDGET_PERIODS: { value: Exclude<WidgetPeriod, null>; label: string }[] = [
  { value: 'last7', label: 'Last 7 Days' },
  { value: 'last30', label: 'Last 30 Days' },
  { value: 'thisMonth', label: 'This Month' },
  { value: 'lastMonth', label: 'Last Month' }
];

/** Sales, target, purchase, expense and profit for the Owner. Every figure is computed on the server. */
@Component({
  selector: 'app-sales-finance-page',
  standalone: true,
  imports: [OdBarListComponent, OdFilterBarComponent, OdKpiCardComponent, OdTimeChartComponent, ...UI_KIT],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './owner-dashboard.css',
  template: `
    <main class="od-page">
      <app-ui-page-header title="Sales & Finance"
        description="Sales, targets, purchases, expenses and estimated profit across the business."
        [breadcrumbs]="[{ label: 'Dashboard', link: '/dashboard' }, { label: 'Sales & Finance' }]" />

      @if (metaError()) {
        <app-ui-state kind="error" title="Could not open this dashboard" [message]="metaError()!">
          <button appUiButton variant="outline" (click)="loadMeta()">Retry</button>
        </app-ui-state>
      } @else if (!meta() || !state()) {
        <app-ui-state kind="loading" title="Loading dashboard…" />
      } @else {
        <div class="od-status">
          <span>Period: <strong>{{ state()!.filter.from | appDate }} to {{ state()!.filter.to | appDate }}</strong></span>
          @if (summary.data(); as data) {
            <span>Last updated: {{ data.context.dataAsOfUtc | appDate:'medium' }}</span>
          }
          <button appUiButton type="button" size="sm" variant="outline" [loading]="summary.loading()" (click)="refresh()">
            <i class="fas fa-rotate" aria-hidden="true"></i> Refresh
          </button>
        </div>

        <app-od-filter-bar ariaLabel="Sales and finance filters"
          [showrooms]="meta()!.showrooms" [today]="meta()!.today" [applied]="state()!.filter"
          (applyRequested)="apply($event)" (resetRequested)="reset()" />

        @for (warning of summary.data()?.dataQualityWarnings ?? []; track warning) {
          <app-ui-alert variant="warning">{{ warning }}</app-ui-alert>
        }
        @if (summary.error()) {
          <app-ui-state kind="error" [compact]="true" title="Could not load the summary" [message]="summary.error()!">
            <button appUiButton variant="outline" (click)="loadSummary()">Retry</button>
          </app-ui-state>
        }

        <section class="od-kpis" aria-label="Key figures">
          <app-od-kpi-card [label]="salesLabel()" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.netSales)" [status]="summary.data()?.netSales?.status ?? 'ok'"
            [note]="summary.data()?.netSales?.note ?? null" [detail]="changeText()" />
          <app-od-kpi-card [label]="targetLabel()" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.target)" [status]="summary.data()?.target?.status ?? 'ok'"
            [note]="summary.data()?.target?.note ?? null" [detail]="achievementText()" />
          <app-od-kpi-card [label]="exceeded() ? 'Target Exceeded' : 'Shortfall'" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="exceeded() ? money(summary.data()?.targetExceeded) : money(summary.data()?.shortfall)"
            [status]="summary.data()?.shortfall?.status ?? 'ok'" [note]="summary.data()?.shortfall?.note ?? null" />
          @if (!isTodayRange()) {
            <app-od-kpi-card label="Today's Target" [loading]="summary.loading()" [error]="!!summary.error()"
              [value]="money(summary.data()?.today?.target)" [status]="summary.data()?.today?.target?.status ?? 'ok'"
              [note]="summary.data()?.today?.target?.note ?? null" [detail]="todayAchievementText()" />
            <app-od-kpi-card [label]="todayExceeded() ? 'Today Exceeded' : 'Today Short'" [loading]="summary.loading()" [error]="!!summary.error()"
              [value]="todayExceeded() ? money(summary.data()?.today?.targetExceeded) : money(summary.data()?.today?.shortfall)"
              [status]="summary.data()?.today?.shortfall?.status ?? 'ok'" [note]="summary.data()?.today?.shortfall?.note ?? null" />
          }
          <app-od-kpi-card label="Purchase" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.purchase)" [status]="summary.data()?.purchase?.status ?? 'ok'"
            [note]="summary.data()?.purchase?.note ?? null" />
          <app-od-kpi-card label="Expense" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.expense)" [status]="summary.data()?.expense?.status ?? 'ok'"
            [note]="summary.data()?.expense?.note ?? null" />
          <app-od-kpi-card label="Estimated Profit" [loading]="summary.loading()" [error]="!!summary.error()"
            [value]="money(summary.data()?.profit)" [status]="summary.data()?.profit?.status ?? 'ok'"
            [note]="summary.data()?.profit?.note ?? null" />
        </section>

        <div class="od-grid-2">
          <app-ui-card title="Showroom Sales Performance" [headingLevel]="2"
            [subtitle]="state()!.filter.from + ' to ' + state()!.filter.to">
            <div uiCardActions class="od-inline">
              <label class="od-inline" for="od-showroom-sort">Sort by</label>
              <select id="od-showroom-sort" appUiControl [value]="showroomSort()"
                (change)="showroomSort.set($any($event.target).value)">
                <option value="sales">Sales</option>
                <option value="achievement">Achievement</option>
              </select>
            </div>
            @if (showrooms.loading()) {
              <app-ui-state kind="loading" [compact]="true" title="Loading showrooms…" />
            } @else if (showrooms.error()) {
              <app-ui-state kind="error" [compact]="true" title="Could not load showrooms" [message]="showrooms.error()!">
                <button appUiButton variant="outline" (click)="loadShowrooms()">Retry</button>
              </app-ui-state>
            } @else if (!showroomRows().length) {
              <app-ui-state kind="empty" [compact]="true" title="This business has no showrooms" />
            } @else {
              <app-od-bar-list caption="Net sales by showroom" nameHeader="Showroom" valueHeader="Net sales"
                detailHeader="Target" [rows]="showroomRows()" [selectable]="true"
                (selected)="filterToShowroom($event)" />
            }
          </app-ui-card>

          <app-ui-card title="Cash Received vs Expense Payments" [headingLevel]="2" [subtitle]="cashPeriodLabel()">
            <div uiCardActions class="od-inline">
              <label class="sr-only" for="od-cash-period">Chart period</label>
              <select id="od-cash-period" appUiControl [value]="state()!.cashPeriod ?? ''"
                (change)="setCashPeriod($any($event.target).value)">
                <option value="">{{ followLabel() }}</option>
                @for (period of widgetPeriods; track period.value) {
                  <option [value]="period.value">{{ period.label }}</option>
                }
              </select>
              @if (state()!.cashPeriod) {
                <app-ui-badge variant="info">Own period</app-ui-badge>
              }
            </div>
            @if (cash.loading()) {
              <app-ui-state kind="loading" [compact]="true" title="Loading cash figures…" />
            } @else if (cash.error()) {
              <app-ui-state kind="error" [compact]="true" title="Could not load cash figures" [message]="cash.error()!">
                <button appUiButton variant="outline" (click)="loadCash()">Retry</button>
              </app-ui-state>
            } @else if (cash.data(); as data) {
              @if (cashIsEmpty()) {
                <app-ui-state kind="empty" [compact]="true" title="No cash movements in this period" />
              } @else {
                <app-od-time-chart caption="Cash received and operational expense payments" [series]="cashSeries" [points]="cashPoints()" />
              }
              <dl class="od-totals">
                <div><dt>Cash Received</dt><dd>{{ currency.format(data.totalCashReceived) }}</dd></div>
                <div><dt>Expense Payments</dt><dd>{{ currency.format(data.totalExpensePayments) }}</dd></div>
                <div><dt>Cash difference</dt><dd>{{ currency.format(data.difference) }}</dd></div>
              </dl>
              <p class="od-helper">A cash movement, not accounting profit. Excludes transfers between cash and bank.</p>
            }
          </app-ui-card>
        </div>

        <app-ui-card title="Sales Trend" [headingLevel]="2" [subtitle]="trendPeriodLabel()">
          <div uiCardActions class="od-inline">
            <label class="sr-only" for="od-trend-period">Chart period</label>
            <select id="od-trend-period" appUiControl [value]="state()!.trendPeriod ?? ''"
              (change)="setTrendPeriod($any($event.target).value)">
              <option value="">{{ followLabel() }}</option>
              @for (period of widgetPeriods; track period.value) {
                <option [value]="period.value">{{ period.label }}</option>
              }
            </select>
            @if (state()!.trendPeriod) {
              <app-ui-badge variant="info">Own period</app-ui-badge>
            }
          </div>
          @if (trend.loading()) {
            <app-ui-state kind="loading" [compact]="true" title="Loading trend…" />
          } @else if (trend.error()) {
            <app-ui-state kind="error" [compact]="true" title="Could not load the trend" [message]="trend.error()!">
              <button appUiButton variant="outline" (click)="loadTrend()">Retry</button>
            </app-ui-state>
          } @else if (trend.data(); as data) {
            @if (data.targetStatus === 'not_configured') {
              <p class="od-helper"><app-ui-badge variant="neutral">Target not configured</app-ui-badge> Net sales only; no target line is drawn.</p>
            } @else if (data.targetStatus === 'partial') {
              <p class="od-helper"><app-ui-badge variant="warning">Partial</app-ui-badge> Some showrooms or dates have no target, so the target line covers only those that do.</p>
            }
            @if (trendIsEmpty()) {
              <app-ui-state kind="empty" [compact]="true" title="No sales in this period" />
            } @else {
              <app-od-time-chart kind="line" [caption]="data.targetStatus === 'not_configured' ? 'Net sales over time' : 'Net sales and target over time'"
                [series]="data.targetStatus === 'not_configured' ? trendSeries : trendTargetSeries" [points]="trendPoints()" />
            }
          }
        </app-ui-card>
      }
    </main>
  `
})
export class SalesFinancePageComponent implements OnInit {
  private showroomDetail(row: ShowroomPerformanceRow): string {
    if (row.targetStatus === 'not_configured') return 'No target configured';
    if (row.targetStatus === 'off_day') return 'Scheduled off day';
    const target = `Target ${this.currency.format(row.target)}`;
    const achieved = row.achievementPercent === null ? target : `${target} · ${row.achievementPercent}% achieved`;
    return row.targetStatus === 'partial' ? `${achieved} · part of the period only` : achieved;
  }

  private readonly api = inject(OwnerDashboardService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly store = inject(OwnerDashboardFilterStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly currency = inject(CurrencyService);

  private readonly toMessage = (error: unknown) => this.errors.extractErrorMessage(error);

  readonly cashSeries = CASH_SERIES;
  readonly trendSeries = TREND_SERIES;
  readonly trendTargetSeries = TREND_TARGET_SERIES;
  readonly widgetPeriods = WIDGET_PERIODS;

  readonly meta = signal<OwnerDashboardMeta | null>(null);
  readonly metaError = signal<string | null>(null);
  readonly showroomSort = signal<'sales' | 'achievement'>('sales');
  readonly state = this.store.sales;

  // If the Business context changes while this page is open, its locations are no longer valid: load them again.
  private readonly reloadAfterContextChange = effect(() => {
    if (this.meta() && !this.state()) {
      untracked(() => this.loadMeta());
    }
  });

  readonly summary = createWidget<SalesFinanceSummary>(this.destroyRef, this.toMessage);
  readonly showrooms = createWidget<ShowroomPerformance>(this.destroyRef, this.toMessage);
  readonly cash = createWidget<CashFlow>(this.destroyRef, this.toMessage);
  readonly trend = createWidget<SalesTrend>(this.destroyRef, this.toMessage);

  /** "Today" labels become "Selected Period" for any other range (SF-007). */
  readonly salesLabel = computed(() => {
    const filter = this.state()?.filter;
    return filter && filter.from === filter.to && filter.from === this.meta()?.today ? "Today's Sales" : 'Selected Period Sales';
  });

  readonly isTodayRange = computed(() => {
    const filter = this.state()?.filter;
    return !!filter && filter.from === filter.to && filter.from === this.meta()?.today;
  });

  readonly targetLabel = computed(() => (this.isTodayRange() ? "Today's Target" : 'Selected Period Target'));

  readonly exceeded = computed(() => (this.summary.data()?.targetExceeded.value ?? 0) > 0);
  readonly todayExceeded = computed(() => (this.summary.data()?.today?.targetExceeded.value ?? 0) > 0);

  readonly todayAchievementText = computed(() => {
    const achievement = this.summary.data()?.today?.achievementPercent;
    return achievement === null || achievement === undefined ? null : `${achievement}% achieved`;
  });

  readonly changeText = computed(() => {
    const change = this.summary.data()?.netSalesChangePercent;
    if (change === null || change === undefined) {
      return null;
    }
    return `${change > 0 ? '+' : ''}${change}% vs previous period`;
  });

  readonly achievementText = computed(() => {
    const achievement = this.summary.data()?.achievementPercent;
    return achievement === null || achievement === undefined ? null : `${achievement}% achieved`;
  });

  readonly showroomRows = computed<OdBarRow[]>(() => {
    const rows = [...(this.showrooms.data()?.showrooms ?? [])];
    if (this.showroomSort() === 'achievement') {
      rows.sort((left, right) => (right.achievementPercent ?? -1) - (left.achievementPercent ?? -1)
        || right.netSales - left.netSales);
    }
    return rows.map(row => ({
      key: String(row.outletId),
      label: row.outletName,
      value: row.netSales,
      valueText: this.currency.format(row.netSales),
      detail: this.showroomDetail(row)
    }));
  });

  readonly cashPoints = computed<OdPoint[]>(() =>
    (this.cash.data()?.points ?? []).map(point => ({
      label: bucketLabel(point.from, point.to),
      values: [point.cashReceived, point.expensePayments],
      texts: [this.currency.format(point.cashReceived), this.currency.format(point.expensePayments)]
    })));

  readonly trendPoints = computed<OdPoint[]>(() =>
    (this.trend.data()?.points ?? []).map(point => ({
      label: bucketLabel(point.from, point.to),
      // A bucket with no target has no second value, so the target line breaks there instead of dropping to zero.
      values: point.target === null ? [point.netSales] : [point.netSales, point.target],
      texts: [this.currency.format(point.netSales), point.target === null ? 'N/A' : this.currency.format(point.target)]
    })));

  readonly cashIsEmpty = computed(() => this.cashPoints().every(point => point.values.every(value => value === 0)));
  readonly trendIsEmpty = computed(() => this.trendPoints().every(point => point.values[0] === 0));

  readonly followLabel = computed(() => (this.state()?.applied ? 'Page period' : 'Default period'));
  readonly cashPeriodLabel = computed(() => this.describe(this.cashRange(), this.state()?.cashPeriod ?? null, 'last7'));
  readonly trendPeriodLabel = computed(() => this.describe(this.trendRange(), this.state()?.trendPeriod ?? null, 'last30'));

  ngOnInit(): void {
    this.loadMeta();
  }

  loadMeta(): void {
    this.metaError.set(null);
    this.api.meta().subscribe({
      next: meta => {
        this.meta.set(meta);
        this.state.set(this.initialState(meta));
        this.loadAll();
      },
      error: error => this.metaError.set(this.toMessage(error))
    });
  }

  apply(filter: AppliedFilter): void {
    this.state.update(state => ({ ...state!, filter, applied: true }));
    this.syncUrl();
    this.loadAll();
  }

  reset(): void {
    this.state.set(this.defaults(this.meta()!));
    this.syncUrl();
    this.loadAll();
  }

  refresh(): void {
    this.loadAll();
  }

  filterToShowroom(outletId: string): void {
    // Narrows the location only. It is not a period Apply, so charts keep the period they were showing.
    this.state.update(state => ({ ...state!, filter: { ...state!.filter, location: `outlet:${outletId}` } }));
    this.syncUrl();
    this.loadAll();
  }

  setCashPeriod(value: string): void {
    this.state.update(state => ({ ...state!, cashPeriod: (value || null) as WidgetPeriod }));
    this.loadCash();
  }

  setTrendPeriod(value: string): void {
    this.state.update(state => ({ ...state!, trendPeriod: (value || null) as WidgetPeriod }));
    this.loadTrend();
  }

  money(metric: MetricValue | undefined): string | null {
    return metric?.value === null || metric?.value === undefined ? null : this.currency.format(metric.value);
  }

  loadSummary(): void {
    const filter = this.state()!.filter;
    this.summary.load(this.api.salesSummary(this.query(filter)));
  }

  loadShowrooms(): void {
    const filter = this.state()!.filter;
    this.showrooms.load(this.api.showrooms(this.query(filter)));
  }

  loadCash(): void {
    this.cash.load(this.api.cashFlow(this.query(this.cashRange())));
  }

  loadTrend(): void {
    this.trend.load(this.api.salesTrend(this.query(this.trendRange())));
  }

  private loadAll(): void {
    this.loadSummary();
    this.loadShowrooms();
    this.loadCash();
    this.loadTrend();
  }

  private cashRange(): DateRange {
    return this.widgetRange(this.state()?.cashPeriod ?? null, 'last7');
  }

  private trendRange(): DateRange {
    return this.widgetRange(this.state()?.trendPeriod ?? null, 'last30');
  }

  /** Own period first; otherwise the page period once applied; otherwise the chart's default (FIL-008, FIL-009). */
  private widgetRange(own: WidgetPeriod, fallback: 'last7' | 'last30'): DateRange {
    const state = this.state();
    const today = this.meta()?.today;
    if (!state || !today) {
      return { from: '', to: '' };
    }
    if (own) {
      return resolvePreset(own, today);
    }
    return state.applied ? state.filter : resolvePreset(fallback, today);
  }

  private describe(range: DateRange, own: WidgetPeriod, fallback: 'last7' | 'last30'): string {
    const preset = own ?? (this.state()?.applied ? this.state()!.filter.preset : fallback);
    const name = DATE_PRESETS.find(item => item.value === preset)?.label ?? '';
    return preset === 'custom' ? bucketLabel(range.from, range.to) : `${name} · ${bucketLabel(range.from, range.to)}`;
  }

  private query(range: DateRange): SalesQuery {
    return {
      from: range.from,
      to: range.to,
      outletId: parseLocation(this.state()!.filter.location)?.id ?? null
    };
  }

  private defaults(meta: OwnerDashboardMeta): SalesScreenState {
    return {
      filter: { preset: 'today', location: '', categoryId: null, ...resolvePreset('today', meta.today) },
      applied: false,
      cashPeriod: null,
      trendPeriod: null
    };
  }

  /** A deep link wins; then what this screen last applied; then the documented defaults. */
  private initialState(meta: OwnerDashboardMeta): SalesScreenState {
    const params = this.route.snapshot.queryParamMap;
    const from = params.get('from');
    const to = params.get('to');
    if (isIsoDateOnly(from) && isIsoDateOnly(to) && !validateRange(from, to)) {
      const outlet = Number(params.get('outletId'));
      const location = meta.showrooms.some(showroom => showroom.id === outlet) ? `outlet:${outlet}` : '';
      const preset = DATE_PRESETS.some(item => item.value === params.get('preset'))
        ? (params.get('preset') as DatePreset)
        : 'custom';
      return {
        ...this.defaults(meta),
        filter: { preset, from, to, location, categoryId: null },
        applied: true
      };
    }

    return this.state() ?? this.defaults(meta);
  }

  private syncUrl(): void {
    const state = this.state()!;
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: state.applied
        ? {
            from: state.filter.from,
            to: state.filter.to,
            preset: state.filter.preset,
            outletId: parseLocation(state.filter.location)?.id ?? null
          }
        : {}
    });
  }
}
