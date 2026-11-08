import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { UI_KIT, UiTableColumn, UiTableQuery } from '../../components/shared/ui';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { ExchangeListItem, ExchangeListPage, ExchangeMetrics, exchangeReasonLabel, settlementDirectionLabel } from '../../models/exchange.model';
import { AuthService } from '../../services/auth.service';
import { ExchangeService } from '../../services/exchange.service';
import { readExchangeError } from './exchange-errors';

interface ListRequest {
  sequence: number;
  query: UiTableQuery;
  from: string;
  to: string;
}

/**
 * Exchange History (FR-EX-150, D-11): server-paged search by exchange or invoice number, customer, date range and
 * status. Access is `sales.view`; the server applies business and receiving-outlet read scope.
 */
@Component({
  selector: 'app-exchange-history-list',
  standalone: true,
  imports: [FormsModule, AppCurrencyPipe, ...UI_KIT],
  styleUrls: ['./exchange.css'],
  template: `
    <div class="ex-page" data-density="compact">
      <app-ui-page-header title="Exchanges" description="Completed and reversed product exchanges." [breadcrumbs]="breadcrumbs">
        @if (canCreate) {
          <div uiPagePrimaryActions>
            <button appUiButton type="button" (click)="startExchange()"><i class="fas fa-right-left" aria-hidden="true"></i> New exchange</button>
          </div>
        }
      </app-ui-page-header>

      <app-ui-card title="Exchange metrics" [subtitle]="metricsSubtitle()" [loading]="metricsLoading()" loadingLabel="Loading exchange metrics...">
        @if (metricsError(); as error) {
          <app-ui-state kind="error" title="Could not load exchange metrics" [message]="error" [compact]="true" />
        } @else if (metrics(); as m) {
          <dl class="ex-meta" data-testid="exchange-metrics">
            <div><dt>Exchanges</dt><dd>{{ m.exchangeCount }}</dd></div>
            <div><dt>Reversals</dt><dd>{{ m.reversalCount }}</dd></div>
            <div><dt>Replacements (gross)</dt><dd>{{ m.replacementGross | appCurrency }}</dd></div>
            <div><dt>Returns (gross)</dt><dd>{{ m.returnGross | appCurrency }}</dd></div>
            <div><dt>Net difference (gross)</dt><dd>{{ m.netAdjustmentGross | appCurrency }}</dd></div>
            <div><dt>Net sales effect (excl. VAT)</dt><dd>{{ m.netAdjustmentNet | appCurrency }}</dd></div>
          </dl>
          @if (m.bySalesperson.length) {
            <div class="ex-table-wrap">
              <table class="ex-table" aria-label="Exchange effect by salesperson">
                <thead>
                  <tr><th scope="col">Salesperson</th><th scope="col" class="ex-num">Positive</th><th scope="col" class="ex-num">Negative</th><th scope="col" class="ex-num">Net</th><th scope="col" class="ex-num">Target effect</th></tr>
                </thead>
                <tbody>
                  @for (row of m.bySalesperson; track row.salespersonUserId) {
                    <tr>
                      <td>{{ row.salespersonName ?? 'No salesperson' }}</td>
                      <td class="ex-num">{{ row.positiveNet | appCurrency }}</td>
                      <td class="ex-num">{{ row.negativeNet | appCurrency }}</td>
                      <td class="ex-num">{{ row.signedNet | appCurrency }}</td>
                      <td class="ex-num">{{ row.targetEffectNet | appCurrency }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </app-ui-card>

      <app-ui-data-table
        mode="server"
        ariaLabel="Exchanges"
        density="compact"
        [columns]="columns"
        [rows]="rows()"
        rowKey="id"
        [totalRecords]="total()"
        [loading]="loading()"
        [error]="loadError()"
        [responseSequence]="responseSequence()"
        errorTitle="Could not load exchanges"
        loadingLabel="Loading exchanges..."
        emptyTitle="No exchanges found"
        emptyMessage="No exchange matches the current filters."
        searchLabel="Search exchanges"
        searchPlaceholder="Exchange no, invoice no or customer…"
        [searchDebounceMs]="400"
        [rowClickable]="true"
        (queryChange)="onQueryChange($event)"
        (retry)="reload()"
        (rowClick)="open($event)">

        <div uiTableToolbar class="ex-row">
          <app-ui-form-field label="From">
            <input appUiControl type="date" name="from" [ngModel]="from()" (ngModelChange)="setFrom($event)" />
          </app-ui-form-field>
          <app-ui-form-field label="To">
            <input appUiControl type="date" name="to" [ngModel]="to()" (ngModelChange)="setTo($event)" />
          </app-ui-form-field>
        </div>

        <ng-template uiTableCell="originalInvoiceNumbers" let-row>{{ invoicesOf(row) }}</ng-template>
        <ng-template uiTableCell="reasonCode" let-row>{{ reasonLabel(row.reasonCode) }}</ng-template>
        <ng-template uiTableCell="netAdjustmentGross" let-row>{{ row.netAdjustmentGross > 0 ? '+' : '' }}{{ row.netAdjustmentGross | appCurrency }}</ng-template>
        <ng-template uiTableCell="settlement" let-row>{{ directionLabel(row.settlementDirection) }} · {{ row.settlementAmount | appCurrency }}</ng-template>
      </app-ui-data-table>
    </div>
  `
})
export class ExchangeHistoryListComponent {
  private readonly exchanges = inject(ExchangeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly canCreate = inject(AuthService).hasPermission('pos.exchange.create');

  readonly breadcrumbs = [{ label: 'Sales', link: '/sales' }, { label: 'Exchanges' }];
  readonly columns: UiTableColumn[] = [
    { key: 'exchangeNumber', header: 'Exchange No' },
    { key: 'businessDate', header: 'Business date' },
    { key: 'receivingOutletName', header: 'Outlet' },
    { key: 'customerName', header: 'Customer', emptyText: 'Walk-in' },
    { key: 'originalInvoiceNumbers', header: 'Original invoices', type: 'custom' },
    { key: 'reasonCode', header: 'Reason', type: 'custom' },
    { key: 'netAdjustmentGross', header: 'Difference', type: 'custom', align: 'end' },
    { key: 'settlement', header: 'Settlement', type: 'custom' },
    {
      key: 'status', header: 'Status', type: 'status',
      filter: { type: 'select', options: [{ value: 'completed', label: 'Completed' }, { value: 'reversed', label: 'Reversed' }] },
      statusMap: { completed: { label: 'Completed', variant: 'success' }, reversed: { label: 'Reversed', variant: 'warning' } }
    },
    { key: 'cashierName', header: 'Cashier' },
    { key: 'salespersonName', header: 'Salesperson' }
  ];

  readonly rows = signal<ExchangeListItem[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly responseSequence = signal<number | null>(null);
  readonly from = signal('');
  readonly to = signal('');

  readonly metrics = signal<ExchangeMetrics | null>(null);
  readonly metricsLoading = signal(false);
  readonly metricsError = signal<string | null>(null);

  private lastQuery: UiTableQuery | null = null;
  private readonly requests = new Subject<ListRequest>();
  private readonly metricsRequests = new Subject<{ from: string; to: string }>();

  constructor() {
    // Metrics follow the list's date range (EX-20); a newer range cancels the older request.
    this.metricsRequests.pipe(
      tap(() => {
        this.metricsLoading.set(true);
        this.metricsError.set(null);
      }),
      switchMap(range => this.exchanges.getMetrics({ from: range.from || undefined, to: range.to || undefined }).pipe(
        map(res => ({ metrics: res?.data ?? null, error: null as string | null })),
        catchError((err: unknown) => of({ metrics: null as ExchangeMetrics | null, error: readExchangeError(err).message }))
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ metrics, error }) => {
      this.metricsLoading.set(false);
      this.metricsError.set(error);
      this.metrics.set(metrics);
    });
    this.loadMetrics();

    this.requests.pipe(
      tap(() => {
        this.loading.set(true);
        this.loadError.set(null);
      }),
      // A newer query cancels the older request, so a slow response never overwrites newer results.
      switchMap(request => this.exchanges.list({
        search: request.query.search,
        status: request.query.filters['status'],
        from: request.from || undefined,
        to: request.to || undefined,
        page: request.query.page,
        pageSize: request.query.pageSize
      }).pipe(
        map(res => ({ sequence: request.sequence, page: res?.data ?? null, error: null as string | null })),
        catchError((err: unknown) => of({ sequence: request.sequence, page: null as ExchangeListPage | null, error: readExchangeError(err).message }))
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(({ sequence, page, error }) => {
      this.loading.set(false);
      this.responseSequence.set(sequence);
      if (error) {
        this.loadError.set(error);
        return;
      }
      this.rows.set(page?.items ?? []);
      this.total.set(page?.totalCount ?? 0);
    });
  }

  onQueryChange(query: UiTableQuery): void {
    this.lastQuery = query;
    this.requests.next({ sequence: query.sequence, query, from: this.from(), to: this.to() });
  }

  reload(): void {
    if (this.lastQuery) this.onQueryChange(this.lastQuery);
  }

  setFrom(value: string | null): void {
    this.from.set(value ?? '');
    this.reloadFirstPage();
    this.loadMetrics();
  }

  setTo(value: string | null): void {
    this.to.set(value ?? '');
    this.reloadFirstPage();
    this.loadMetrics();
  }

  loadMetrics(): void {
    this.metricsRequests.next({ from: this.from(), to: this.to() });
  }

  metricsSubtitle(): string {
    const m = this.metrics();
    if (!m) return 'Business dates in the selected range; reversals count on their own date.';
    return m.from === m.to ? `Business date ${m.from}` : `Business dates ${m.from} to ${m.to}`;
  }

  private reloadFirstPage(): void {
    if (this.lastQuery) this.onQueryChange({ ...this.lastQuery, page: 1 });
  }

  open(row: ExchangeListItem): void {
    void this.router.navigate(['/exchanges', row.id]);
  }

  startExchange(): void {
    void this.router.navigate(['/pos/exchange']);
  }

  invoicesOf(row: ExchangeListItem): string {
    return (row.originalInvoiceNumbers ?? []).join(', ');
  }

  reasonLabel(code: string): string {
    return exchangeReasonLabel(code);
  }

  directionLabel(direction: string): string {
    return settlementDirectionLabel(direction);
  }
}
