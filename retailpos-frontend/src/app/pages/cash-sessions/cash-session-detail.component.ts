import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CashDifferenceApproval, CashSessionDetail, CashSessionService } from '../../services/cash-session.service';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT } from '../../components/shared/ui';
import { formatElapsed, handoverMethodLabel, handoverStatusLabel, movementLabel, stateLabel, stateVariant } from './cash-session.format';

/**
 * Cash Session detail (FR-CS-05): overview and status, the source-based breakdown, expected/actual/variance from the
 * immutable closing snapshot when closed, handovers, movements, related sales, attachments, the variance decision and the
 * audit timeline. A closed session offers Print Summary (this page, print layout) and Download PDF; the PDF is rendered
 * by the server from the immutable close snapshot, never recomputed from current settings.
 */
@Component({
  selector: 'app-cash-session-detail',
  standalone: true,
  imports: [FormsModule, RouterLink, AppCurrencyPipe, ...UI_KIT],
  styleUrl: './cash-sessions.css',
  template: `
    <div class="container mx-auto px-4 py-4 cs-page" data-density="compact">
      <app-ui-page-header title="Cash Session" [description]="subtitle()" [breadcrumbs]="breadcrumbs">
        <a uiPageSecondaryActions appUiButton variant="outline" routerLink="/cash-sessions">Back to Sessions</a>
        <!-- Header actions are projected unconditionally and toggled with a class: control-flow blocks wrapping several
             nodes are not routed into the named header slots. -->
        <a uiPagePrimaryActions appUiButton [class.cs-hidden]="!canCloseThis()" [routerLink]="['/cash-sessions', detail()?.session?.id, 'close']">Close session</a>
        <button uiPageSecondaryActions appUiButton type="button" variant="outline" [class.cs-hidden]="!detail()?.closeSnapshot" (click)="print()"><i class="fas fa-print" aria-hidden="true"></i><span>Print Summary</span></button>
        <button uiPageSecondaryActions appUiButton type="button" variant="outline" [class.cs-hidden]="!detail()?.closeSnapshot" [loading]="downloadingPdf()" (click)="downloadPdf()"><i class="fas fa-file-pdf" aria-hidden="true"></i><span>Download PDF</span></button>
      </app-ui-page-header>

      @if (loadError()) {
        <app-ui-state kind="error" title="The session could not be loaded" [message]="loadError()!">
          <button appUiButton type="button" variant="outline" (click)="load()">Retry</button>
        </app-ui-state>
      } @else if (detail(); as d) {
        @if (actionError()) { <app-ui-alert variant="danger" title="Action failed">{{ actionError() }}</app-ui-alert> }

        <div class="cs-grid cs-grid--two">
          <app-ui-card title="Overview">
            <dl class="cs-rows">
              <div class="cs-row"><dt>Status</dt><dd><app-ui-badge [variant]="variant(d.session.state)">{{ label(d.session.state) }}</app-ui-badge></dd></div>
              <div class="cs-row"><dt>Outlet / terminal</dt><dd>{{ d.outletName }} · {{ d.terminalName }}</dd></div>
              <div class="cs-row"><dt>Business date</dt><dd>{{ d.session.businessDate | appDate:'dd MMM yyyy' }} ({{ d.session.timeZoneId }})</dd></div>
              <div class="cs-row"><dt>Opened</dt><dd>{{ d.session.openedAt | appDate:'dd MMM yyyy, hh:mm a' }} by {{ d.openedByName }}</dd></div>
              @if (d.session.closedAt) { <div class="cs-row"><dt>Closed</dt><dd>{{ d.session.closedAt | appDate:'dd MMM yyyy, hh:mm a' }} by {{ d.closedByName }}</dd></div> }
              <div class="cs-row"><dt>Duration limit</dt><dd>{{ d.session.maxDurationHours }} h (snapshot at opening)</dd></div>
              <div class="cs-row"><dt>Elapsed</dt><dd>{{ elapsed(d) }}</dd></div>
            </dl>
          </app-ui-card>

          <app-ui-card title="Cash reconciliation" [subtitle]="d.closeSnapshot ? 'Frozen closing snapshot' : 'Live computation'">
            <dl class="cs-rows">
              <div class="cs-row"><dt>Opening cash</dt><dd>{{ d.summary.openingCash | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Cash sales retained</dt><dd>{{ d.summary.cashSales | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Drawer expenses</dt><dd>− {{ d.summary.drawerExpenses | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Cash refunds</dt><dd>− {{ d.summary.cashRefunds | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Corrections</dt><dd>{{ d.summary.corrections | appCurrency }}</dd></div>
              <div class="cs-row cs-row--total"><dt>Expected</dt><dd>{{ (d.closeSnapshot?.expectedCash ?? d.summary.expectedClosingCash) | appCurrency }}</dd></div>
              @if (d.closeSnapshot; as s) {
                <div class="cs-row"><dt>Counted</dt><dd>{{ s.countedCash | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Variance</dt><dd>{{ s.variance | appCurrency }}</dd></div>
                @if (s.varianceReason) { <div class="cs-row"><dt>Variance reason</dt><dd>{{ s.varianceReason }}</dd></div> }
                <div class="cs-row"><dt>Handed over / retained</dt><dd>{{ s.handedOverTotal | appCurrency }} / {{ s.retainedAtOutlet | appCurrency }}</dd></div>
                <div class="cs-row"><dt>Snapshot hash</dt><dd class="cs-hash">{{ s.snapshotHash }}</dd></div>
              }
            </dl>
            <dl class="cs-rows">
              <div class="cs-row"><dt>Card sales</dt><dd>{{ d.summary.cardSales | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Mobile sales</dt><dd>{{ d.summary.mobileSales | appCurrency }}</dd></div>
            </dl>
          </app-ui-card>
        </div>

        @if (d.variance; as v) {
          <app-ui-card title="Cash difference" subtitle="Approval comes first; the journal is posted afterwards and never changes the closed session.">
            <dl class="cs-rows">
              <div class="cs-row"><dt>Amount</dt><dd>{{ v.varianceAmount | appCurrency }}</dd></div>
              <div class="cs-row"><dt>Reason</dt><dd>{{ v.reason }}</dd></div>
              <div class="cs-row"><dt>Decision</dt><dd><app-ui-badge [variant]="v.status === 'approved' ? 'success' : v.status === 'rejected' ? 'danger' : 'warning'">{{ v.status }}</app-ui-badge></dd></div>
              <div class="cs-row"><dt>Accounting posting</dt><dd><app-ui-badge [variant]="v.postingStatus === 'posted' ? 'success' : v.postingStatus === 'failed' ? 'danger' : 'neutral'">{{ v.postingStatus.replace('_', ' ') }}</app-ui-badge></dd></div>
              @if (v.lastPostingError) { <div class="cs-row"><dt>Last posting error</dt><dd>{{ v.lastPostingError }}</dd></div> }
            </dl>
            <div uiCardFooter class="cs-actions">
              @if (v.status === 'pending' && canApprove) {
                <input appUiControl type="text" aria-label="Review notes" placeholder="Review notes" [ngModel]="reviewNotes()" (ngModelChange)="reviewNotes.set($event)" />
                <button appUiButton type="button" [disabled]="busy()" (click)="review(v, true)">Approve</button>
                <button appUiButton type="button" variant="outline" [disabled]="busy()" (click)="review(v, false)">Reject</button>
              }
              @if (v.status === 'approved' && v.postingStatus !== 'posted' && canRetry) {
                <button appUiButton type="button" variant="outline" [disabled]="busy()" (click)="retryPosting(v)">Retry posting</button>
              }
            </div>
          </app-ui-card>
        }

        <app-ui-card title="Handovers">
          @if (d.handovers.length === 0) { <app-ui-state kind="empty" [compact]="true" title="No handovers" message="Nothing was handed over or deposited at close." /> } @else {
            <div class="cs-table-wrap"><table class="cs-table">
              <caption class="cs-hint">Custody transfers (not expenses)</caption>
              <thead><tr><th scope="col">Method</th><th scope="col" class="num">Amount</th><th scope="col">To</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
              <tbody>
                @for (h of d.handovers; track h.id) {
                  <tr>
                    <td>{{ method(h.method) }}</td><td class="num">{{ h.amount | appCurrency }}</td>
                    <td>{{ h.recipientName || h.bankName || '—' }} {{ h.bankAccountReference }} {{ h.depositReference }}</td>
                    <td><app-ui-badge [variant]="h.status === 'confirmed' ? 'success' : 'warning'">{{ handoverStatus(h.status) }}</app-ui-badge></td>
                    <td>@if (h.status !== 'confirmed' && canConfirmHandover) { <button appUiButton type="button" variant="outline" [disabled]="busy()" (click)="confirmHandover(h.id)">Confirm</button> }</td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </app-ui-card>

        <app-ui-card title="Cash movements">
          @if (d.movements.length === 0) { <app-ui-state kind="empty" [compact]="true" title="No cash movements" message="No refunds, drawer expenses or corrections were paid from this session." /> } @else {
            <div class="cs-table-wrap"><table class="cs-table">
              <thead><tr><th scope="col">When</th><th scope="col">Type</th><th scope="col" class="num">Effect</th><th scope="col">Source</th></tr></thead>
              <tbody>
                @for (m of d.movements; track m.id) {
                  <tr><td>{{ m.recordedAtUtc | appDate:'dd MMM, hh:mm a' }}</td><td>{{ movement(m.movementType) }}</td><td class="num">{{ m.cashEffect | appCurrency }}</td>
                    <td>{{ m.sourceType }} {{ m.sourceId }} @if (m.originalSaleId) { · sale {{ m.originalSaleId }} }</td></tr>
                }
              </tbody>
            </table></div>
          }
        </app-ui-card>

        <app-ui-card title="Related sales">
          @if (d.sales.length === 0) { <app-ui-state kind="empty" [compact]="true" title="No sales" message="No sale was completed in this session." /> } @else {
            <div class="cs-table-wrap"><table class="cs-table">
              <thead><tr><th scope="col">Invoice</th><th scope="col">Time</th><th scope="col">Cashier</th><th scope="col">Status</th><th scope="col" class="num">Total</th><th scope="col" class="num">Cash kept</th></tr></thead>
              <tbody>
                @for (s of d.sales; track s.saleId) {
                  <tr><td><a [routerLink]="['/sales', s.saleId]">{{ s.saleNumber }}</a></td><td>{{ s.createdAt | appDate:'hh:mm a' }}</td><td>{{ s.cashierName }}</td>
                    <td>{{ s.status }}</td><td class="num">{{ s.totalAmount | appCurrency }}</td><td class="num">{{ s.cashRetained | appCurrency }}</td></tr>
                }
              </tbody>
            </table></div>
          }
        </app-ui-card>

        <app-ui-card title="Attachments" subtitle="PDF, JPEG, PNG or WebP up to 5 MB. Attachments never change the closed figures.">
          @for (a of d.attachments; track a.id) {
            <div class="cs-row"><span>{{ a.fileName }} <span class="cs-hint">({{ a.sizeBytes }} bytes)</span></span>
              @if (canDownload) { <button appUiButton type="button" variant="outline" (click)="download(a.id, a.fileName)">Download</button> }</div>
          } @empty { <p class="cs-hint">No attachments yet.</p> }
          @if (canUpload && d.session.state === 'closed') {
            <label class="cs-hint" for="cs-attachment">Add evidence</label>
            <input id="cs-attachment" type="file" accept=".pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/jpeg,image/png,image/webp" (change)="upload($event)" [disabled]="busy()" />
          }
        </app-ui-card>

        <app-ui-card title="Activity timeline">
          <ol class="cs-timeline">
            @for (t of d.timeline; track $index) {
              <li><strong>{{ t.title }}</strong><span>{{ t.detail }}</span><time [attr.datetime]="t.atUtc">{{ t.atUtc | appDate:'dd MMM yyyy, hh:mm a' }}</time></li>
            }
          </ol>
        </app-ui-card>
      } @else {
        <app-ui-state kind="loading" title="Loading" message="Loading the session…" />
      }
    </div>
  `
})
export class CashSessionDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly sessions = inject(CashSessionService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly alerts = inject(AlertService);
  private readonly auth = inject(AuthService);

  readonly breadcrumbs = [{ label: 'Sales', link: '/cash-sessions' }, { label: 'Cash Sessions', link: '/cash-sessions' }, { label: 'Details' }];
  readonly canClose = this.auth.hasPermission('cash_sessions.close');
  readonly canApprove = this.auth.hasPermission('cash_sessions.variance.approve');
  readonly canRetry = this.auth.hasPermission('cash_sessions.variance.retry_posting');
  readonly canConfirmHandover = this.auth.hasPermission('cash_sessions.handover.confirm');
  readonly canUpload = this.auth.hasPermission('cash_sessions.attachments.upload');
  readonly canDownload = this.auth.hasPermission('cash_sessions.attachments.download');

  readonly label = stateLabel;
  readonly variant = stateVariant;
  readonly method = handoverMethodLabel;
  readonly handoverStatus = handoverStatusLabel;
  readonly movement = movementLabel;

  detail = signal<CashSessionDetail | null>(null);
  loadError = signal<string | null>(null);
  actionError = signal<string | null>(null);
  busy = signal(false);
  reviewNotes = signal('');
  downloadingPdf = signal(false);

  canCloseThis(): boolean {
    const d = this.detail();
    return !!d && d.session.state !== 'closed' && this.canClose;
  }

  /** Prints this page with the print layout (app shell hidden); the figures shown come from the close snapshot. */
  print(): void {
    window.print();
  }

  downloadPdf(): void {
    const id = this.detail()?.session.id;
    if (!id || this.downloadingPdf()) return;
    this.downloadingPdf.set(true);
    this.actionError.set(null);
    this.sessions.closingSummaryPdf(id).subscribe({
      next: (blob) => {
        this.downloadingPdf.set(false);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `cash-session-${id}-closing-summary.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.downloadingPdf.set(false);
        this.actionError.set(this.errors.extractErrorMessage(err));
      }
    });
  }

  ngOnInit(): void {
    this.load();
  }

  subtitle(): string {
    const d = this.detail();
    return d ? `Session ${d.session.id} · ${d.outletName} · ${d.terminalName}` : '';
  }

  elapsed(d: CashSessionDetail): string {
    return formatElapsed(d.session.elapsedSeconds);
  }

  load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loadError.set(null);
    this.sessions.detail(id).subscribe({
      next: (response) => this.detail.set(response.data),
      error: (err) => this.loadError.set(this.errors.extractErrorMessage(err))
    });
  }

  review(approval: CashDifferenceApproval, approve: boolean): void {
    this.run(this.sessions.reviewVariance(approval.id, approve, this.reviewNotes().trim() || undefined), approve ? 'Cash difference approved' : 'Cash difference rejected');
  }

  retryPosting(approval: CashDifferenceApproval): void {
    this.run(this.sessions.retryVariancePosting(approval.id), 'Posting retried');
  }

  confirmHandover(handoverId: number): void {
    this.run(this.sessions.confirmHandover(this.detail()!.session.id, handoverId), 'Handover confirmed');
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.run(this.sessions.uploadAttachment(this.detail()!.session.id, file), 'Attachment added');
    input.value = '';
  }

  download(attachmentId: number, fileName: string): void {
    this.sessions.downloadAttachment(this.detail()!.session.id, attachmentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.actionError.set(this.errors.extractErrorMessage(err))
    });
  }

  private run(request: import('rxjs').Observable<unknown>, success: string): void {
    this.busy.set(true);
    this.actionError.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.alerts.success(success);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.actionError.set(this.errors.extractErrorMessage(err));
      }
    });
  }
}
