import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AlertService } from '../../services/alert.service';
import { CurrencyService } from '../../services/currency.service';
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
  /** Fields the cashier has edited; their inline error shows from then on (all of them after Review). */
  touched: Partial<Record<HandoverField, true>>;
}

type HandoverField = 'amount' | 'bank' | 'recipient';
type HandoverPatch = Partial<Omit<HandoverRow, 'touched'>>;

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
    <!-- Layout follows the approved POS prototype (Docs/08_Sales_And_POS/iranimart_pos_prototype.html, "Close Cash Session"):
         one centred column of section cards A–D, a two-column reconciliation, and a sticky action bar. -->
    <div class="container mx-auto px-4 py-4 cs-page cs-closing" data-density="compact">
      <app-ui-page-header title="Close Cash Session" description="Reconcile the drawer, record cash handover, and confirm session closing." [breadcrumbs]="breadcrumbs" />

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

          <app-ui-card title="A. Session Information">
            <dl class="cs-info-grid">
              <div><dt>Outlet</dt><dd>{{ d.outletName }}</dd></div>
              <div><dt>Terminal</dt><dd>{{ d.terminalName }}</dd></div>
              <div><dt>Business Date</dt><dd>{{ d.session.businessDate | appDate:'dd MMM yyyy' }}</dd></div>
              <div><dt>Opened By</dt><dd>{{ d.openedByName }}</dd></div>
              <div><dt>Opening Time</dt><dd class="cs-info-grid__plain">{{ d.session.openedAt | appDate:'dd MMM yyyy, hh:mm a' }} <span class="cs-muted">· open for {{ elapsed(d) }}</span></dd></div>
              <div><dt>Status</dt><dd>
                @if (d.session.state === 'overdue') { <app-ui-badge variant="danger">Overdue · Close required</app-ui-badge> }
                @else { <app-ui-badge variant="success">Open · Ready to close</app-ui-badge> }
              </dd></div>
              <div><dt>Closing Cashier</dt><dd class="cs-info-grid__plain">You (from your sign-in)</dd></div>
            </dl>
          </app-ui-card>

          <app-ui-card title="B. Cash Reconciliation">
            <div class="cs-close-grid">
              <div>
                <dl class="cs-close-rows">
                  <div class="cs-close-row"><dt>Opening Cash</dt><dd>{{ d.summary.openingCash | appCurrency }}</dd></div>
                  <div class="cs-close-row"><dt>Cash Sales retained ({{ d.summary.saleCount }} sales)</dt><dd>{{ d.summary.cashSales | appCurrency }}</dd></div>
                  <div class="cs-close-row cs-close-row--info"><dt>Change given back (not retained)</dt><dd>{{ d.summary.changeGiven | appCurrency }}</dd></div>
                  <div class="cs-close-row"><dt>Cash Expenses</dt><dd>−{{ d.summary.drawerExpenses | appCurrency }}</dd></div>
                  <div class="cs-close-row"><dt>Cash Refunds</dt><dd>−{{ d.summary.cashRefunds | appCurrency }}</dd></div>
                  <div class="cs-close-row"><dt>Approved Corrections</dt><dd>{{ d.summary.corrections | appCurrency }}</dd></div>
                  <div class="cs-close-row cs-close-row--highlight"><dt>Expected Closing Cash</dt><dd>{{ d.summary.expectedClosingCash | appCurrency }}</dd></div>
                </dl>
                <p class="cs-muted cs-close-note">Non-cash reconciliation: Card {{ d.summary.cardSales | appCurrency }} · Mobile {{ d.summary.mobileSales | appCurrency }}</p>
              </div>
              <div class="cs-close-entry">
                <app-ui-form-field [label]="'Actual Counted Cash (' + currencyCode() + ')'" [required]="true" [error]="countFieldError()">
                  <span uiFieldPrefix class="cs-addon">{{ currencySymbol() }}</span>
                  <input appUiControl type="number" name="counted" min="0" step="0.01" inputmode="decimal" placeholder="0.00" [ngModel]="counted()" (ngModelChange)="setCounted($event)" />
                </app-ui-form-field>
                <p class="cs-hint-box" [attr.data-tone]="varianceTone()" aria-live="polite">{{ varianceHint() }}</p>
                @if (variance() !== null && variance() !== 0) {
                  <app-ui-form-field label="Reason for the difference" [required]="true" [error]="showErrors() ? reasonError() : ''" hint="A difference is recorded for approval. It never changes the ledger by itself.">
                    <textarea appUiControl name="reason" rows="3" maxlength="1000" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea>
                  </app-ui-form-field>
                }
                <app-ui-form-field label="Closing Notes">
                  <textarea appUiControl name="notes" rows="4" maxlength="1000" placeholder="Add closing notes..." [ngModel]="notes()" (ngModelChange)="notes.set($event)"></textarea>
                </app-ui-form-field>
              </div>
            </div>
          </app-ui-card>

          <app-ui-card title="C. Cash Denomination (optional)">
            <button appUiButton type="button" variant="outline" [attr.aria-expanded]="denominationsOpen()" aria-controls="cs-denominations" (click)="denominationsOpen.set(!denominationsOpen())">
              <i class="fas fa-calculator" aria-hidden="true"></i><span>{{ denominationsOpen() ? 'Hide denomination counter' : 'Use denomination counter' }}</span>
            </button>
            @if (denominationsOpen()) {
              <div id="cs-denominations" class="cs-denomination-grid">
                @for (row of denominations(); track row.value; let i = $index) {
                  <app-ui-form-field [label]="currencySymbol() + row.value">
                    <input appUiControl type="number" min="0" step="1" inputmode="numeric" placeholder="0" [name]="'denom' + i"
                      [ngModel]="row.count" (ngModelChange)="setDenomination(i, $event)" />
                  </app-ui-form-field>
                }
              </div>
              <div class="cs-denomination-total">
                <span>Denomination total <strong>{{ denominationTotal() | appCurrency }}</strong></span>
                @if (denominationTotal() > 0) {
                  <button appUiButton type="button" variant="outline" size="sm" (click)="setCounted(denominationTotal())">Use as counted cash</button>
                }
              </div>
            }
          </app-ui-card>

          <app-ui-card title="D. Cash Handover &amp; Attachments">
            @for (row of handovers(); track $index; let i = $index) {
              <div class="cs-handover-row" role="group" [attr.aria-label]="'Handover ' + (i + 1)">
                <div class="cs-handover-row__head">
                  <p class="cs-handover-row__title">Handover {{ i + 1 }}</p>
                  <button appUiButton type="button" variant="ghost" size="sm" (click)="removeHandover(i)" [attr.aria-label]="'Remove handover ' + (i + 1)"><i class="fas fa-trash-alt" aria-hidden="true"></i><span>Remove</span></button>
                </div>
                <div class="cs-form-2">
                  <app-ui-form-field label="Handover Head / Recipient" [required]="true">
                    <select appUiControl [name]="'method' + i" [ngModel]="row.method" (ngModelChange)="updateHandover(i, { method: $event })">
                      <option value="bank_deposit">Bank Deposit / Cash Collection</option>
                      <option value="cash_to_manager">Manager</option>
                      <option value="cash_to_owner">Owner</option>
                    </select>
                  </app-ui-form-field>
                  <app-ui-form-field label="Amount" [required]="true" [error]="rowError(i, 'amount')">
                    <span uiFieldPrefix class="cs-addon">{{ currencySymbol() }}</span>
                    <input appUiControl type="number" min="0" step="0.01" inputmode="decimal" placeholder="0.00" [name]="'amount' + i" [ngModel]="row.amount" (ngModelChange)="updateHandover(i, { amount: parseAmount($event) })" />
                  </app-ui-form-field>
                  <div class="cs-readonly-field">
                    <span class="cs-readonly-field__label">Given By</span>
                    <span class="cs-readonly-field__value">You (closing cashier)</span>
                  </div>
                  @if (row.method === 'bank_deposit') {
                    <app-ui-form-field label="Bank" [error]="rowError(i, 'bank')"><input appUiControl type="text" [name]="'bank' + i" [ngModel]="row.bankName" (ngModelChange)="updateHandover(i, { bankName: $event })" /></app-ui-form-field>
                    <app-ui-form-field label="Account / Reference"><input appUiControl type="text" [name]="'acct' + i" [ngModel]="row.bankAccountReference" (ngModelChange)="updateHandover(i, { bankAccountReference: $event })" /></app-ui-form-field>
                    <app-ui-form-field label="Deposit Slip No."><input appUiControl type="text" [name]="'slip' + i" [ngModel]="row.depositReference" (ngModelChange)="updateHandover(i, { depositReference: $event })" /></app-ui-form-field>
                  } @else {
                    <app-ui-form-field label="Taken By" [required]="true" [error]="rowError(i, 'recipient')"><input appUiControl type="text" [name]="'to' + i" [ngModel]="row.recipientName" (ngModelChange)="updateHandover(i, { recipientName: $event })" /></app-ui-form-field>
                  }
                  <app-ui-form-field label="Notes" hint="Optional"><input appUiControl type="text" [name]="'hn' + i" [ngModel]="row.notes" (ngModelChange)="updateHandover(i, { notes: $event })" /></app-ui-form-field>
                </div>
              </div>
            } @empty {
              <p class="cs-muted cs-handover-empty">No cash handed over. All counted cash stays at the outlet.</p>
            }
            <button appUiButton type="button" variant="outline" class="cs-add-handover" (click)="addHandover()"><i class="fas fa-plus" aria-hidden="true"></i><span>Add Handover</span></button>

            <div class="cs-stat-row">
              <div class="cs-stat">Physical cash counted<strong>{{ (counted() ?? 0) | appCurrency }}</strong></div>
              <div class="cs-stat">Total handed over<strong data-testid="handed-over">{{ handedOver() | appCurrency }}</strong></div>
              <div class="cs-stat" [attr.data-tone]="retained() < 0 ? 'danger' : null">Retained at outlet<strong>{{ retained() | appCurrency }}</strong></div>
            </div>
            @if (overHandoverError()) { <app-ui-alert variant="danger" title="Handover">{{ overHandoverError() }}</app-ui-alert> }
            <p class="cs-hint-box">Receipts, deposit slips or handover documents (photo or PDF) are attached on the session page after closing. A bank deposit stays "pending bank verification" until confirmed.</p>
          </app-ui-card>

          <div class="cs-sticky-footer">
            <a appUiButton variant="outline" [routerLink]="['/cash-sessions', d.session.id]">Cancel</a>
            <button appUiButton type="button" variant="outline" (click)="review()">Review Closing Summary</button>
            <button appUiButton type="button" [loading]="isSubmitting()" [disabled]="!reviewing() || isSubmitting()" (click)="confirm()">Confirm &amp; Close Cash Session</button>
          </div>

          <app-ui-dialog [(open)]="reviewDialogOpen" title="Review Closing Summary" size="md" (closed)="reviewing.set(false)">
            <p class="cs-muted cs-dialog-sub">{{ d.outletName }} · {{ d.terminalName }}</p>
            <dl class="cs-close-rows">
              <div class="cs-close-row"><dt>Expected closing cash</dt><dd>{{ d.summary.expectedClosingCash | appCurrency }}</dd></div>
              <div class="cs-close-row"><dt>Counted</dt><dd>{{ (counted() ?? 0) | appCurrency }}</dd></div>
              <div class="cs-close-row"><dt>Variance</dt><dd>{{ (variance() ?? 0) | appCurrency }}</dd></div>
              <div class="cs-close-row"><dt>Handover</dt><dd>{{ handedOver() | appCurrency }}</dd></div>
              <div class="cs-close-row cs-close-row--highlight"><dt>Retained at outlet</dt><dd>{{ retained() | appCurrency }}</dd></div>
            </dl>
            <p class="cs-hint-box">Closing is final: the figures are frozen and later corrections need a separate approved workflow.</p>
            <button uiDialogFooter appUiButton type="button" variant="outline" (click)="cancelReview()">Cancel</button>
            <button uiDialogFooter appUiButton type="button" data-autofocus (click)="continueReview()">Continue</button>
          </app-ui-dialog>
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
  private readonly currency = inject(CurrencyService);

  readonly currencySymbol = this.currency.symbol;
  readonly currencyCode = this.currency.code;

  readonly breadcrumbs = [{ label: 'Sales', link: '/cash-sessions' }, { label: 'Cash Sessions', link: '/cash-sessions' }, { label: 'Close' }];

  detail = signal<CashSessionDetail | null>(null);
  loadError = signal<string | null>(null);
  submitError = signal<string | null>(null);
  isSubmitting = signal(false);
  reviewing = signal(false);
  /** Set by Review: every inline error is shown from then on. */
  showErrors = signal(false);
  reviewDialogOpen = signal(false);
  denominationsOpen = signal(false);

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
  countFieldError = computed(() => (this.showErrors() || this.counted() !== null ? this.countError() : ''));
  overHandoverError = computed(() => (this.retained() < 0 ? 'Handed-over cash cannot exceed the counted cash.' : ''));
  varianceTone = computed(() => {
    const variance = this.variance();
    return variance === null || variance === 0 ? 'neutral' : variance > 0 ? 'info' : 'danger';
  });
  varianceHint = computed(() => {
    const variance = this.variance();
    if (variance === null) return 'Enter the counted cash to see the difference.';
    const difference = `Difference ${this.currency.format(variance)}`;
    if (variance === 0) return `${difference} · Exact Match`;
    return `${difference} · ${variance > 0 ? 'Over' : 'Short'}, a reason is required`;
  });
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

  setCounted(value: number | string | null): void {
    this.counted.set(this.parseAmount(value));
    this.reviewing.set(false);
  }

  /** A number input yields a number, or null while empty/incomplete; anything non-finite counts as no amount. */
  parseAmount(value: number | string | null | undefined): number | null {
    if (value === null || value === undefined || (typeof value === 'string' && value.trim() === '')) return null;
    const parsed = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  /**
   * Replaces the row instead of mutating it: the totals and validation are computed signals over handovers(), which
   * only recompute when the array itself changes (two-way binding into row.amount never notified them).
   */
  updateHandover(index: number, patch: HandoverPatch): void {
    const touched: Partial<Record<HandoverField, true>> = {};
    if ('amount' in patch) touched.amount = true;
    if ('bankName' in patch || 'bankAccountReference' in patch) touched.bank = true;
    if ('recipientName' in patch) touched.recipient = true;
    this.handovers.update(rows => rows.map((row, i) => (i === index ? { ...row, ...patch, touched: { ...row.touched, ...touched } } : row)));
    this.reviewing.set(false);
  }

  /** Inline message for one handover field; the same rules as handoverError(). */
  rowError(index: number, field: HandoverField): string {
    const row = this.handovers()[index];
    if (!row || !(this.showErrors() || row.touched[field])) return '';
    switch (field) {
      case 'amount':
        return !row.amount || row.amount <= 0 ? 'Each handover line needs an amount above zero.' : '';
      case 'bank':
        return row.method === 'bank_deposit' && !row.bankName.trim() && !row.bankAccountReference.trim() ? 'A bank deposit needs the bank or account reference.' : '';
      default:
        return row.method !== 'bank_deposit' && !row.recipientName.trim() ? 'A cash handover needs the person who took the cash.' : '';
    }
  }

  addHandover(): void {
    this.handovers.update(rows => [...rows, { method: 'cash_to_manager', amount: null, recipientName: '', bankName: '', bankAccountReference: '', depositReference: '', notes: '', touched: {} }]);
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

  /** Validates, then shows the closing summary; Confirm is enabled only from the reviewed state. */
  review(): void {
    this.showErrors.set(true);
    const problem = this.blockingProblem();
    this.submitError.set(problem || null);
    this.reviewing.set(!problem);
    this.reviewDialogOpen.set(!problem);
  }

  continueReview(): void {
    this.reviewDialogOpen.set(false);
  }

  cancelReview(): void {
    this.reviewDialogOpen.set(false);
    this.reviewing.set(false);
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
