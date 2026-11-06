import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { BusinessDaySales, BusinessDayTarget, CashSessionListItem, CashSessionService, CashSessionTerminalOption } from '../../services/cash-session.service';
import { AuthorizedLocationDto } from '../../models/report.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT } from '../../components/shared/ui';
import { formatElapsed, stateLabel, stateVariant, varianceVariant } from './cash-session.format';

/**
 * Cash Sessions landing and history (FR-CS-01/02). It shows the sessions that are open or overdue for the chosen outlet
 * (with the server-derived elapsed time and warnings), the outlet's sales for the current business date, the daily
 * target against net sales excluding VAT, and the paged history with filters. No target is invented: an outlet without
 * one says "Not configured". Everything the user may do is decided by the API; the buttons only reflect the
 * permission claims.
 */
@Component({
  selector: 'app-cash-session-list',
  standalone: true,
  imports: [FormsModule, RouterLink, AppCurrencyPipe, ...UI_KIT],
  styleUrl: './cash-sessions.css',
  template: `
    <div class="container mx-auto px-4 py-4 cs-page" data-density="compact">
      <app-ui-page-header
        title="Cash Sessions"
        description="Open, monitor and reconcile cash drawers for your outlet."
        [breadcrumbs]="breadcrumbs">
        @if (canOpen) {
          <a uiPagePrimaryActions appUiButton routerLink="/cash-sessions/open"><i class="fas fa-plus" aria-hidden="true"></i><span>Open Cash Session</span></a>
        }
      </app-ui-page-header>

      @if (outlets().length > 1) {
        <app-ui-card title="Outlet">
          <app-ui-form-field label="Outlet">
            <select appUiControl name="outlet" [ngModel]="outletId()" (ngModelChange)="selectOutlet($event)">
              @for (outlet of outlets(); track outlet.id) {
                <option [ngValue]="outlet.id">{{ outlet.name }}</option>
              }
            </select>
          </app-ui-form-field>
        </app-ui-card>
      }

      @if (loadError()) {
        <app-ui-state kind="error" title="Cash sessions could not be loaded" [message]="loadError()!">
          <button appUiButton type="button" variant="outline" (click)="reload()">Retry</button>
        </app-ui-state>
      } @else if (!outletId()) {
        <app-ui-state kind="empty" title="No outlet available" message="You are not assigned to an outlet that can run cash sessions." />
      } @else {
        <div class="cs-grid" aria-label="Business day performance">
          <app-ui-card title="Daily target" [loading]="dayTargetLoading()">
            @if (dayTargetError()) {
              <app-ui-state kind="error" [compact]="true" title="Not available" [message]="dayTargetError()!">
                <button appUiButton type="button" variant="outline" (click)="loadDayTarget()">Retry</button>
              </app-ui-state>
            } @else if (dayTarget(); as t) {
              <div class="cs-metric">
                @switch (t.status) {
                  @case ('not_configured') {
                    <span class="cs-metric__value">Not configured</span>
                    <span class="cs-hint">No sales target is set for this outlet on this date.</span>
                  }
                  @case ('off_day') {
                    <span class="cs-metric__value">{{ t.target | appCurrency }}</span>
                    <span class="cs-hint"><app-ui-badge variant="neutral">Scheduled Off Day</app-ui-badge> Any sales today are still recorded.</span>
                  }
                  @default {
                    <span class="cs-metric__value">{{ t.target | appCurrency }}</span>
                    @if (t.achievementPercent !== null) {
                      <span class="cs-hint">{{ t.achievementPercent }}% achieved</span>
                    }
                  }
                }
              </div>
            }
          </app-ui-card>
          <app-ui-card title="This business day's sales" [loading]="daySalesLoading()">
            @if (daySalesError()) {
              <app-ui-state kind="error" [compact]="true" title="Not available" [message]="daySalesError()!">
                <button appUiButton type="button" variant="outline" (click)="loadDaySales()">Retry</button>
              </app-ui-state>
            } @else if (daySales(); as s) {
              <div class="cs-metric">
                <span class="cs-metric__value">{{ s.completedTotal | appCurrency }}</span>
                <span class="cs-hint">{{ s.completedCount }} completed sale(s) · business date {{ s.businessDate | appDate:'dd MMM yyyy' }} · all terminals</span>
                <span class="cs-hint">Excluding VAT {{ s.completedNetOfTax | appCurrency }} · VAT {{ s.completedTax | appCurrency }}</span>
                @if (s.voidedCount || s.refundedCount) {
                  <span class="cs-hint">Not included: {{ s.voidedCount }} voided, {{ s.refundedCount }} refunded (original value {{ s.refundedOriginalTotal | appCurrency }})</span>
                }
                <span class="cs-hint">As of {{ s.serverNowUtc | appDate:'hh:mm a' }}
                  <button appUiButton type="button" variant="ghost" size="sm" (click)="loadDaySales()" aria-label="Refresh today's sales">Refresh</button>
                </span>
              </div>
            }
          </app-ui-card>
          <app-ui-card title="Variance vs target" [loading]="dayTargetLoading()">
            @if (dayTarget(); as t) {
              <div class="cs-metric">
                @if (t.variance === null) {
                  <span class="cs-metric__value">—</span>
                  <span class="cs-hint">{{ t.status === 'off_day' ? 'No target applies on an off day.' : 'Shown once a target is configured.' }}</span>
                } @else {
                  <span class="cs-metric__value">
                    <i class="fas" [class.fa-face-smile]="t.variance > 0" [class.fa-face-frown]="t.variance < 0" [class.fa-circle-check]="t.variance === 0" aria-hidden="true"></i>
                    {{ t.variance | appCurrency }}
                  </span>
                  <span class="cs-hint">
                    <app-ui-badge [variant]="t.variance > 0 ? 'success' : t.variance < 0 ? 'danger' : 'neutral'">{{ t.variance > 0 ? 'Above target' : t.variance < 0 ? 'Below target' : 'On target' }}</app-ui-badge>
                    Net sales excluding VAT {{ t.netSales | appCurrency }}
                  </span>
                }
              </div>
            } @else {
              <div class="cs-metric"><span class="cs-metric__value">—</span></div>
            }
          </app-ui-card>
        </div>

        @for (session of active(); track session.id) {
          <app-ui-card [title]="session.terminalName + ' — active session'" [subtitle]="session.outletName">
            @if (warning(session); as message) {
              <app-ui-alert [variant]="session.state === 'overdue' ? 'danger' : 'warning'" title="Time limit">{{ message }}</app-ui-alert>
            }
            <dl class="cs-rows">
              <div class="cs-row"><dt>Status</dt><dd><app-ui-badge [variant]="variant(session.state)">{{ label(session.state) }}</app-ui-badge></dd></div>
              <div class="cs-row"><dt>Business date</dt><dd>{{ session.businessDate | appDate:'dd MMM yyyy' }}</dd></div>
              <div class="cs-row"><dt>Opened</dt><dd>{{ session.openedAt | appDate:'dd MMM yyyy, hh:mm a' }} by {{ session.openedByName || ('user ' + session.openedByUserId) }}</dd></div>
              <div class="cs-row"><dt>Elapsed (server clock)</dt><dd>{{ elapsed(session) }} of {{ session.durationHours }}h</dd></div>
              <div class="cs-row"><dt>Opening cash</dt><dd>{{ session.openingCash | appCurrency }}</dd></div>
            </dl>
            <div uiCardFooter class="cs-actions">
              <a appUiButton variant="outline" [routerLink]="['/cash-sessions', session.id]">View</a>
              @if (canClose) {
                <a appUiButton [routerLink]="['/cash-sessions', session.id, 'close']">Close session</a>
              }
            </div>
          </app-ui-card>
        } @empty {
          @if (!isLoading()) {
            <app-ui-state kind="empty" title="No active cash session" message="Open a cash session to start selling at this outlet." />
          }
        }

        <app-ui-card title="Session history" [loading]="isLoading()">
          <form class="cs-filters" (ngSubmit)="applyFilters()" novalidate>
            <app-ui-form-field label="From"><input appUiControl type="date" name="from" [(ngModel)]="from" /></app-ui-form-field>
            <app-ui-form-field label="To"><input appUiControl type="date" name="to" [(ngModel)]="to" /></app-ui-form-field>
            <app-ui-form-field label="Status">
              <select appUiControl name="state" [(ngModel)]="state">
                <option value="">All</option><option value="open">Open</option><option value="overdue">Overdue</option><option value="closed">Closed</option>
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Terminal">
              <select appUiControl name="terminal" [(ngModel)]="terminalId">
                <option [ngValue]="null">All terminals</option>
                @for (terminal of terminals(); track terminal.id) {
                  <option [ngValue]="terminal.id">{{ terminal.name }} ({{ terminal.code }})</option>
                }
              </select>
            </app-ui-form-field>
            <button appUiButton type="submit">Apply filters</button>
          </form>

          @if (items().length === 0 && !isLoading()) {
            <app-ui-state kind="empty" title="No sessions found" message="Try a wider date range or a different status." />
          } @else {
            <div class="cs-table-wrap">
              <table class="cs-table">
                <caption class="cs-hint">Cash sessions, newest first</caption>
                <thead>
                  <tr>
                    <th scope="col">Business date</th><th scope="col">Terminal</th><th scope="col">Opened</th><th scope="col">Closed</th>
                    <th scope="col" class="num">Opening cash</th><th scope="col" class="num">Closing cash</th><th scope="col" class="num">Variance</th>
                    <th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of items(); track row.id) {
                    <tr>
                      <td>{{ row.businessDate | appDate:'dd MMM yyyy' }}</td>
                      <td>{{ row.terminalName }}</td>
                      <td>{{ row.openedAt | appDate:'dd MMM, hh:mm a' }}<br /><span class="cs-hint">{{ row.openedByName }}</span></td>
                      <td>@if (row.closedAt) { {{ row.closedAt | appDate:'dd MMM, hh:mm a' }}<br /><span class="cs-hint">{{ row.closedByName }}</span> } @else { — }</td>
                      <td class="num">{{ row.openingCash | appCurrency }}</td>
                      <td class="num">@if (row.countedCash !== null && row.countedCash !== undefined) { {{ row.countedCash | appCurrency }} } @else { — }</td>
                      <td class="num">
                        @if (row.variance !== null && row.variance !== undefined) {
                          <app-ui-badge [variant]="varianceTone(row.variance)">{{ row.variance | appCurrency }}</app-ui-badge>
                        } @else { — }
                      </td>
                      <td><app-ui-badge [variant]="variant(row.state)">{{ label(row.state) }}</app-ui-badge></td>
                      <td><a [routerLink]="['/cash-sessions', row.id]" [attr.aria-label]="'View cash session ' + row.id">View</a></td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="cs-pager">
              <span>Page {{ page() }} of {{ totalPages() }} · {{ totalCount() }} sessions</span>
              <span class="cs-actions">
                <button appUiButton type="button" variant="outline" [disabled]="page() <= 1" (click)="goTo(page() - 1)">Previous</button>
                <button appUiButton type="button" variant="outline" [disabled]="page() >= totalPages()" (click)="goTo(page() + 1)">Next</button>
              </span>
            </div>
          }
        </app-ui-card>
      }
    </div>
  `
})
export class CashSessionListComponent implements OnInit {
  private readonly sessions = inject(CashSessionService);
  private readonly outletAccess = inject(UserOutletAccessService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly auth = inject(AuthService);

  readonly breadcrumbs = [{ label: 'Sales' }, { label: 'Cash Sessions' }];
  readonly canOpen = this.auth.hasPermission('cash_sessions.open');
  readonly canClose = this.auth.hasPermission('cash_sessions.close');

  readonly label = stateLabel;
  readonly variant = stateVariant;
  readonly varianceTone = varianceVariant;

  outlets = signal<AuthorizedLocationDto[]>([]);
  terminals = signal<CashSessionTerminalOption[]>([]);
  daySales = signal<BusinessDaySales | null>(null);
  daySalesLoading = signal(false);
  daySalesError = signal<string | null>(null);
  dayTarget = signal<BusinessDayTarget | null>(null);
  dayTargetLoading = signal(false);
  dayTargetError = signal<string | null>(null);
  outletId = signal<number | null>(null);
  items = signal<CashSessionListItem[]>([]);
  active = signal<CashSessionListItem[]>([]);
  page = signal(1);
  pageSize = 20;
  totalCount = signal(0);
  isLoading = signal(false);
  loadError = signal<string | null>(null);

  from = '';
  to = '';
  state = '';
  terminalId: number | null = null;

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));

  /** Server clock minus browser clock from the last response; elapsed time never trusts the browser alone. */
  private serverOffsetMs = 0;

  ngOnInit(): void {
    this.outletAccess.load('InventoryStockView').subscribe({
      next: (response) => {
        const list = response.data.outlets;
        this.outlets.set(list);
        const preferred = response.data.defaultOutletId ?? list[0]?.id ?? null;
        this.outletId.set(preferred);
        this.reload();
      },
      error: (err) => this.loadError.set(this.errors.extractErrorMessage(err))
    });
  }

  selectOutlet(id: number): void {
    this.outletId.set(Number(id));
    this.page.set(1);
    this.terminalId = null;
    this.reload();
  }

  applyFilters(): void {
    this.page.set(1);
    this.reload();
  }

  goTo(page: number): void {
    this.page.set(page);
    this.loadHistory();
  }

  reload(): void {
    this.loadError.set(null);
    this.loadHistory();
    this.loadActive();
    this.loadDaySales();
    this.loadDayTarget();
    this.loadTerminals();
  }

  /** Daily target and signed variance from the server, using the same calculation as the Owner Dashboard. */
  loadDayTarget(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.dayTargetLoading.set(true);
    this.dayTargetError.set(null);
    this.sessions.businessDayTarget(outletId).subscribe({
      next: (response) => {
        if (this.outletId() !== outletId) return;
        this.dayTarget.set(response.data ?? null);
        this.dayTargetLoading.set(false);
      },
      error: (err) => {
        if (this.outletId() !== outletId) return;
        this.dayTarget.set(null);
        this.dayTargetError.set(this.errors.extractErrorMessage(err));
        this.dayTargetLoading.set(false);
      }
    });
  }

  /** Server-computed sales of the outlet's current business date (effective time zone), never the browser cart. */
  loadDaySales(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.daySalesLoading.set(true);
    this.daySalesError.set(null);
    this.sessions.businessDaySales(outletId).subscribe({
      next: (response) => {
        this.daySales.set(response.data ?? null);
        this.daySalesLoading.set(false);
      },
      error: (err) => {
        this.daySales.set(null);
        this.daySalesError.set(this.errors.extractErrorMessage(err));
        this.daySalesLoading.set(false);
      }
    });
  }

  private loadTerminals(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.sessions.openContext(outletId).subscribe({
      next: (response) => this.terminals.set(response.data?.terminals ?? []),
      error: () => this.terminals.set([])
    });
  }

  elapsed(session: CashSessionListItem): string {
    const opened = new Date(session.openedAt).getTime();
    return formatElapsed((Date.now() + this.serverOffsetMs - opened) / 1000);
  }

  warning(session: CashSessionListItem): string {
    const remaining = new Date(session.openedAt).getTime() + session.durationHours * 3_600_000 - (Date.now() + this.serverOffsetMs);
    if (session.state === 'overdue' || remaining <= 0) return `This session reached its ${session.durationHours}-hour limit. Sales are blocked; close it to continue.`;
    if (remaining <= 3_600_000) return 'Less than 1 hour left before sales are blocked.';
    if (remaining <= 7_200_000) return 'Less than 2 hours left before sales are blocked.';
    return '';
  }

  private loadActive(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.sessions.list({ outletId, state: 'open', pageSize: 50 }).subscribe({
      next: (open) => {
        this.captureServerClock(open.data?.serverNowUtc);
        this.sessions.list({ outletId, state: 'overdue', pageSize: 50 }).subscribe({
          next: (overdue) => this.active.set([...(overdue.data?.items ?? []), ...(open.data?.items ?? [])]),
          error: () => this.active.set(open.data?.items ?? [])
        });
      },
      error: () => this.active.set([])
    });
  }

  private captureServerClock(serverNowUtc?: string): void {
    if (serverNowUtc) this.serverOffsetMs = new Date(serverNowUtc).getTime() - Date.now();
  }

  private loadHistory(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.isLoading.set(true);
    this.sessions.list({
      outletId,
      terminalId: this.terminalId,
      from: this.from || null,
      to: this.to || null,
      state: this.state || null,
      page: this.page(),
      pageSize: this.pageSize
    }).subscribe({
      next: (response) => {
        this.items.set(response.data?.items ?? []);
        this.totalCount.set(response.data?.totalCount ?? 0);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.loadError.set(this.errors.extractErrorMessage(err));
      }
    });
  }
}
