import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CashHandoverInput, CloseCashSessionDto } from '../../services/pos.service';
import { CashSessionDetail, CashSessionService } from '../../services/cash-session.service';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT } from '../../components/shared/ui';
import { formatElapsed } from './cash-session.format';

interface HandoverRow {
  method: CashHandoverInput['method'];
  amount: number | null;
  recipientName: string;
  bankName: string;
  bankAccountReference: string;
  depositReference: string;
  notes: string;
}

interface DenominationRow {
  value: number;
  count: number | null;
}

const DEFAULT_DENOMINATIONS = [1000, 500, 200, 100, 50, 20, 10, 5, 2, 1];

/**
 * Close Cash Session (FR-CLOSE-01..05): A information, B reconciliation, C optional denominations, D handover, with a
 * review step before the sticky Confirm & Close. The expected cash shown here is the server's live computation; the
 * authoritative figure is recomputed under the session lock when the close is submitted.
 */
@Component({
  selector: 'app-cash-session-close',
  standalone: true,
  imports: [FormsModule, RouterLink, AppCurrencyPipe, ...UI_KIT],
  styleUrl: './cash-sessions.css',
  template: `
    <div class="container mx-auto px-4 py-4 cs-page" data-density="compact">
      <app-ui-page-header title="Close Cash Session" description="Count the drawer, explain any difference and record where the cash goes." [breadcrumbs]="breadcrumbs" />

      @if (loadError()) {
        <app-ui-state kind="error" title="The session could not be loaded" [message]="loadError()!">
          <button appUiButton type="button" variant="outline" (click)="load()">Retry</button>
        </app-ui-state>
      } @else if (detail(); as d) {
        @if (d.session.state === 'closed') {
          <app-ui-state kind="empty" title="This session is already closed" message="Closed sessions are read-only.">
            <a appUiButton variant="outline" [routerLink]="['/cash-sessions', d.session.id]">View session</a>
          </app-ui-state>
        } @else {
          @if (d.session.state === 'overdue') {
            <app-ui-alert variant="danger" title="Overdue session">Sales are blocked on this session. You can still close it; the cart in progress is kept for after the next session opens.</app-ui-alert>
          }
          @if (submitError()) { <app-ui-alert variant="danger" title="Cash session not closed">{{ submitError() }}</app-ui-alert> }

          <app-ui-card title="A. Session information">
            <dl class="cs-rows">
              <div class="cs-row"><dt>Outlet / terminal</dt><dd>{{ d.outletName }} · {{ d.terminalName }}</dd></div>
              <div class="cs-row"><dt>Business date</dt><dd>{{ d.session.businessDate | appDate:'dd MMM yyyy' }}</dd></div>
              <div class="cs-row"><dt>Opened</dt><dd>{{ d.session.openedAt | appDate:'dd MMM yyyy, hh:mm a' }} by {{ d.openedByName }}</dd></div>
              <div class="cs-row"><dt>Open for</dt><dd>{{ elapsed(d) }}</dd></div>
              <div class="cs-row"><dt>Closing cashier</dt><dd>You (from your sign-in)</dd></div>
            </dl>
          </app-ui-card>

          <div class="cs-grid cs-grid--two">
            <app-ui-card title="B. Reconciliation">
              <dl class="cs-rows">
                <div class="cs-row"><dt>Opening cash</dt><dd>{{ d.summary.openingCash | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Cash sales retained ({{ d.summary.saleCount }} sales)</dt><dd>{{ d.summary.cashSales | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Change given back (not retained)</dt><dd>{{ d.summary.changeGiven | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Drawer expenses paid</dt><dd>− {{ d.summary.drawerExpenses | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Cash refunds paid</dt><dd>− {{ d.summary.cashRefunds | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Approved corrections</dt><dd>{{ d.summary.corrections | appCurrency }}</dd></div>
                <div class="cs-row cs-row--total"><dt>Expected drawer cash</dt><dd>{{ d.summary.expectedClosingCash | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Card sales (not in drawer)</dt><dd>{{ d.summary.cardSales | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Mobile sales (not in drawer)</dt><dd>{{ d.summary.mobileSales | appCurrency }}</dd></div>
              </dl>
              <app-ui-form-field label="Counted cash" [required]="true" [error]="countError()">
                <input appUiControl type="number" name="counted" min="0" step="0.01" inputmode="decimal" [ngModel]="counted()" (ngModelChange)="counted.set($event === null || $event === '' ? null : +$event)" />
              </app-ui-form-field>
              <dl class="cs-rows">
                <div class="cs-row cs-row--total"><dt>Variance (counted − expected)</dt>
                  <dd><app-ui-badge [variant]="variance() === 0 ? 'neutral' : variance()! > 0 ? 'info' : 'danger'">{{ variance() === null ? '—' : (variance()! | appCurrency) }}</app-ui-badge></dd></div>
              </dl>
              @if (variance() !== null && variance() !== 0) {
                <app-ui-form-field label="Reason for the difference" [required]="true" [error]="reasonError()">
                  <textarea appUiControl name="reason" rows="3" maxlength="1000" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea>
                </app-ui-form-field>
                <p class="cs-hint">A difference is recorded for approval. It never changes the ledger by itself.</p>
              }
              <app-ui-form-field label="Closing notes" hint="Optional">
                <textarea appUiControl name="notes" rows="2" maxlength="1000" [ngModel]="notes()" (ngModelChange)="notes.set($event)"></textarea>
              </app-ui-form-field>
            </app-ui-card>

            <app-ui-card title="C. Denominations (optional)">
              <div class="cs-denominations">
                @for (row of denominations(); track row.value; let i = $index) {
                  <app-ui-form-field [label]="'× ' + row.value">
                    <input appUiControl type="number" min="0" step="1" inputmode="numeric" [name]="'denom' + i"
                      [ngModel]="row.count" (ngModelChange)="setDenomination(i, $event)" />
                  </app-ui-form-field>
                }
              </div>
              <dl class="cs-rows"><div class="cs-row cs-row--total"><dt>Denomination total</dt><dd>{{ denominationTotal() | appCurrency }}</dd></div></dl>
              @if (denominationTotal() > 0) {
                <button appUiButton type="button" variant="outline" (click)="counted.set(denominationTotal())">Use as counted cash</button>
              }
            </app-ui-card>
          </div>

          <app-ui-card title="D. Handover and deposit" subtitle="Custody transfers are not operating expenses.">
            @for (row of handovers(); track $index; let i = $index) {
              <div class="cs-handover">
                <app-ui-form-field label="Method">
                  <select appUiControl [name]="'method' + i" [(ngModel)]="row.method">
                    <option value="cash_to_manager">Cash to manager</option>
                    <option value="cash_to_owner">Cash to owner</option>
                    <option value="bank_deposit">Bank deposit</option>
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Amount"><input appUiControl type="number" min="0" step="0.01" [name]="'amount' + i" [(ngModel)]="row.amount" /></app-ui-form-field>
                @if (row.method === 'bank_deposit') {
                  <app-ui-form-field label="Bank"><input appUiControl type="text" [name]="'bank' + i" [(ngModel)]="row.bankName" /></app-ui-form-field>
                  <app-ui-form-field label="Account / reference"><input appUiControl type="text" [name]="'acct' + i" [(ngModel)]="row.bankAccountReference" /></app-ui-form-field>
                  <app-ui-form-field label="Deposit slip no."><input appUiControl type="text" [name]="'slip' + i" [(ngModel)]="row.depositReference" /></app-ui-form-field>
                } @else {
                  <app-ui-form-field label="Taken by"><input appUiControl type="text" [name]="'to' + i" [(ngModel)]="row.recipientName" /></app-ui-form-field>
                }
                <app-ui-form-field label="Notes"><input appUiControl type="text" [name]="'hn' + i" [(ngModel)]="row.notes" /></app-ui-form-field>
                <button appUiButton type="button" variant="outline" (click)="removeHandover(i)" [attr.aria-label]="'Remove handover line ' + (i + 1)">Remove</button>
              </div>
            }
            <button appUiButton type="button" variant="outline" (click)="addHandover()"><i class="fas fa-plus" aria-hidden="true"></i><span>Add handover line</span></button>
            <dl class="cs-rows">
              <div class="cs-row"><dt>Counted</dt><dd>{{ (counted() ?? 0) | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Handed over</dt><dd>{{ handedOver() | appCurrency }}</dd></div>
              <div class="cs-row cs-row--total"><dt>Retained at outlet</dt><dd>{{ retained() | appCurrency }}</dd></div>
            </dl>
            @if (handoverError()) { <app-ui-alert variant="danger" title="Handover">{{ handoverError() }}</app-ui-alert> }
            <p class="cs-hint">Photo or PDF evidence can be attached on the session page after closing. A bank deposit stays "pending bank verification" until confirmed.</p>
          </app-ui-card>

          @if (reviewing()) {
            <app-ui-alert variant="info" title="Review before closing">
              Counted {{ (counted() ?? 0) | appCurrency }} against expected {{ d.summary.expectedClosingCash | appCurrency }}
              (variance {{ (variance() ?? 0) | appCurrency }}); {{ handedOver() | appCurrency }} handed over, {{ retained() | appCurrency }} retained.
              Closing is final: the figures are frozen and later corrections need a separate approved workflow.
            </app-ui-alert>
          }

          <div class="cs-sticky-footer">
            <a appUiButton variant="outline" [routerLink]="['/cash-sessions', d.session.id]">Cancel</a>
            <button appUiButton type="button" variant="outline" (click)="review()">Review Closing Summary</button>
            <button appUiButton type="button" [loading]="isSubmitting()" [disabled]="!reviewing() || isSubmitting()" (click)="confirm()">Confirm &amp; Close</button>
          </div>
        }
      } @else {
        <app-ui-state kind="loading" title="Loading" message="Loading the session…" />
      }
    </div>
  `
})
export class CashSessionCloseComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sessions = inject(CashSessionService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly alerts = inject(AlertService);

  readonly breadcrumbs = [{ label: 'Sales', link: '/cash-sessions' }, { label: 'Cash Sessions', link: '/cash-sessions' }, { label: 'Close' }];

  detail = signal<CashSessionDetail | null>(null);
  loadError = signal<string | null>(null);
  submitError = signal<string | null>(null);
  isSubmitting = signal(false);
  reviewing = signal(false);

  counted = signal<number | null>(null);
  reason = signal('');
  notes = signal('');
  denominations = signal<DenominationRow[]>(DEFAULT_DENOMINATIONS.map(value => ({ value, count: null })));
  handovers = signal<HandoverRow[]>([]);

  variance = computed(() => {
    const d = this.detail();
    const counted = this.counted();
    return d && counted !== null ? Math.round((counted - d.summary.expectedClosingCash) * 100) / 100 : null;
  });
  denominationTotal = computed(() => Math.round(this.denominations().reduce((sum, row) => sum + row.value * (row.count ?? 0), 0) * 100) / 100);
  handedOver = computed(() => Math.round(this.handovers().reduce((sum, row) => sum + (row.amount ?? 0), 0) * 100) / 100);
  retained = computed(() => Math.round(((this.counted() ?? 0) - this.handedOver()) * 100) / 100);

  countError = computed(() => {
    const counted = this.counted();
    if (counted === null) return 'Enter the counted cash.';
    if (counted < 0) return 'Counted cash cannot be negative.';
    if (this.denominationTotal() > 0 && Math.abs(this.denominationTotal() - counted) > 0.004) return 'The denominations do not add up to the counted cash.';
    return '';
  });
  reasonError = computed(() => (this.variance() !== 0 && !this.reason().trim() ? 'A reason is required when the count differs from the expected cash.' : ''));
  handoverError = computed(() => {
    if (this.retained() < 0) return 'Handed-over cash cannot exceed the counted cash.';
    for (const row of this.handovers()) {
      if (!row.amount || row.amount <= 0) return 'Each handover line needs an amount above zero.';
      if (row.method === 'bank_deposit' && !row.bankName.trim() && !row.bankAccountReference.trim()) return 'A bank deposit needs the bank or account reference.';
      if (row.method !== 'bank_deposit' && !row.recipientName.trim()) return 'A cash handover needs the person who took the cash.';
    }
    return '';
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loadError.set(null);
    this.sessions.detail(id).subscribe({
      next: (response) => {
        this.detail.set(response.data);
        if (this.counted() === null) this.counted.set(null);
      },
      error: (err) => this.loadError.set(this.errors.extractErrorMessage(err))
    });
  }

  elapsed(detail: CashSessionDetail): string {
    return formatElapsed(detail.session.elapsedSeconds);
  }

  setDenomination(index: number, value: number | string | null): void {
    this.denominations.update(rows => rows.map((row, i) => (i === index ? { ...row, count: value === null || value === '' ? null : Math.max(0, Math.floor(+value)) } : row)));
    this.reviewing.set(false);
  }

  addHandover(): void {
    this.handovers.update(rows => [...rows, { method: 'cash_to_manager', amount: null, recipientName: '', bankName: '', bankAccountReference: '', depositReference: '', notes: '' }]);
    this.reviewing.set(false);
  }

  removeHandover(index: number): void {
    this.handovers.update(rows => rows.filter((_, i) => i !== index));
    this.reviewing.set(false);
  }

  /** The first blocking problem, or an empty string when the form can be submitted. */
  blockingProblem(): string {
    return this.countError() || this.reasonError() || this.handoverError();
  }

  review(): void {
    const problem = this.blockingProblem();
    this.submitError.set(problem || null);
    this.reviewing.set(!problem);
  }

  buildPayload(): CloseCashSessionDto {
    const denominations = this.denominations().filter(row => (row.count ?? 0) > 0).map(row => ({ value: row.value, count: row.count! }));
    return {
      actualClosingCashAmount: this.counted()!,
      closingNotes: this.notes().trim() || undefined,
      varianceReason: this.variance() !== 0 ? this.reason().trim() : undefined,
      denominations: denominations.length ? denominations : undefined,
      handovers: this.handovers().length
        ? this.handovers().map(row => ({
            method: row.method,
            amount: row.amount!,
            recipientName: row.recipientName.trim() || undefined,
            bankName: row.bankName.trim() || undefined,
            bankAccountReference: row.bankAccountReference.trim() || undefined,
            depositReference: row.depositReference.trim() || undefined,
            notes: row.notes.trim() || undefined
          }))
        : undefined
    };
  }

  confirm(): void {
    if (this.isSubmitting()) return;
    // The close is final, so it is only ever submitted from the reviewed state.
    if (!this.reviewing()) {
      this.review();
      return;
    }

    const problem = this.blockingProblem();
    if (problem) {
      this.submitError.set(problem);
      this.reviewing.set(false);
      return;
    }

    const id = this.detail()!.session.id;
    this.isSubmitting.set(true);
    this.sessions.close(id, this.buildPayload()).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.alerts.success('Cash session closed');
        this.router.navigate(['/cash-sessions', id]);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.submitError.set(this.errors.extractErrorMessage(err));
      }
    });
  }
}
