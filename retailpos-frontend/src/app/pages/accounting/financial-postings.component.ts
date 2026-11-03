import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { FinancialPostingService, FinancialPostingSummary, SaleFinancialPosting } from '../../services/financial-posting.service';
import { UI_KIT } from '../../components/shared/ui';

/**
 * Sale GL postings (D10 reconciliation, Phase 3.2 permissions). Sales and their journals are decoupled: a sale is never
 * affected here. Finance sees pending/failed postings with the reason and worker liveness, and may retry a posting
 * exactly as recorded (the server accepts no amount, date or snapshot override and audits every retry).
 */
@Component({
  selector: 'app-financial-postings',
  standalone: true,
  imports: [FormsModule, RouterLink, ...UI_KIT],
  styleUrl: '../cash-sessions/cash-sessions.css',
  template: `
    <div class="container mx-auto px-4 py-4 cs-page" data-density="compact">
      <app-ui-page-header title="Sale GL Postings" description="Asynchronous sale and reversal journals: backlog, failures and worker liveness." [breadcrumbs]="breadcrumbs">
        <button uiPageSecondaryActions appUiButton type="button" variant="outline" (click)="reload()">Refresh</button>
      </app-ui-page-header>

      @if (loadError()) {
        <app-ui-state kind="error" title="Postings could not be loaded" [message]="loadError()!">
          <button appUiButton type="button" variant="outline" (click)="reload()">Retry</button>
        </app-ui-state>
      } @else {
        @if (summary(); as s) {
          <div class="cs-grid" aria-label="Posting backlog">
            <app-ui-card title="Pending"><div class="cs-metric"><span class="cs-metric__value">{{ s.sales.pending }}</span><span class="cs-hint">@if (s.sales.oldestUnpostedCreatedAtUtc) { Oldest {{ s.sales.oldestUnpostedCreatedAtUtc | appDate:'dd MMM, hh:mm a' }} } @else { Nothing waiting }</span></div></app-ui-card>
            <app-ui-card title="Failed"><div class="cs-metric"><span class="cs-metric__value">{{ s.sales.failed }}</span><span class="cs-hint">Needs a fix or a retry</span></div></app-ui-card>
            <app-ui-card title="Waiting for the sale journal"><div class="cs-metric"><span class="cs-metric__value">{{ s.sales.awaitingSalePosting }}</span><span class="cs-hint">Reversals post after their sale</span></div></app-ui-card>
            <app-ui-card title="Cash differences due"><div class="cs-metric"><span class="cs-metric__value">{{ s.cashDifferencesDue }}</span><span class="cs-hint">Approved variances not yet posted</span></div></app-ui-card>
          </div>

          <app-ui-card title="Workers">
            @if (s.workers.length === 0) {
              <app-ui-state kind="empty" [compact]="true" title="No worker heartbeat" message="No posting worker reported a pass in the last 24 hours." />
            } @else {
              <div class="cs-table-wrap"><table class="cs-table">
                <thead><tr><th scope="col">Worker</th><th scope="col">Role</th><th scope="col">Host / instance</th><th scope="col">Last pass</th><th scope="col" class="num">Failed passes</th><th scope="col">State</th></tr></thead>
                <tbody>
                  @for (w of s.workers; track w.worker + w.instanceId) {
                    <tr>
                      <td>{{ w.worker }}</td><td>{{ w.hostRole }}</td><td>{{ w.hostName }} <span class="cs-hint">{{ w.instanceId }}</span></td>
                      <td>{{ w.lastRunCompletedAtUtc ? (w.lastRunCompletedAtUtc | appDate:'dd MMM, hh:mm:ss a') : '—' }}</td>
                      <td class="num">{{ w.consecutiveRunFailures }}</td>
                      <td><app-ui-badge [variant]="w.stale || w.consecutiveRunFailures >= 3 ? 'danger' : 'success'">{{ w.stale ? 'Stale' : w.consecutiveRunFailures >= 3 ? 'Failing' : 'Alive' }}</app-ui-badge></td>
                    </tr>
                  }
                </tbody>
              </table></div>
            }
          </app-ui-card>
        }

        <app-ui-card title="Postings" [loading]="isLoading()">
          <form class="cs-filters" (ngSubmit)="loadPostings()" novalidate>
            <app-ui-form-field label="Status">
              <select appUiControl name="status" [(ngModel)]="status">
                <option value="failed">Failed</option><option value="pending">Pending</option><option value="posted">Posted</option><option value="">All</option>
              </select>
            </app-ui-form-field>
            <button appUiButton type="submit">Apply</button>
          </form>
          @if (actionError()) { <app-ui-alert variant="danger" title="Retry failed">{{ actionError() }}</app-ui-alert> }
          @if (postings().length === 0 && !isLoading()) {
            <app-ui-state kind="empty" [compact]="true" title="No postings" message="No posting matches this filter." />
          } @else {
            <div class="cs-table-wrap"><table class="cs-table">
              <caption class="cs-hint">Newest first, up to 50</caption>
              <thead><tr><th scope="col">Sale</th><th scope="col">Kind</th><th scope="col">Business date</th><th scope="col">Status</th><th scope="col" class="num">Attempts</th><th scope="col">Reason</th><th scope="col">Journal</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
              <tbody>
                @for (p of postings(); track p.id) {
                  <tr>
                    <td><a [routerLink]="['/sales', p.saleId]">Sale {{ p.saleId }}</a></td>
                    <td>{{ p.kind }}@if (p.reversalType) { · {{ p.reversalType }} }</td>
                    <td>{{ p.businessDate | appDate:'dd MMM yyyy' }}</td>
                    <td><app-ui-badge [variant]="p.status === 'posted' ? 'success' : p.status === 'failed' ? 'danger' : 'warning'">{{ p.status }}</app-ui-badge></td>
                    <td class="num">{{ p.attempts }}</td>
                    <td>@if (p.lastErrorCode) { <strong>{{ p.lastErrorCode }}</strong><br /><span class="cs-hint">{{ p.lastError }}</span> } @else { — }
                      @if (p.status !== 'posted' && p.nextAttemptAtUtc) { <br /><span class="cs-hint">Next automatic attempt {{ p.nextAttemptAtUtc | appDate:'dd MMM, hh:mm a' }}</span> }</td>
                    <td>{{ p.journalEntryId ?? '—' }}</td>
                    <td>
                      @if (canRetry && p.status !== 'posted') {
                        <button appUiButton type="button" variant="outline" size="sm" [loading]="retryingId() === p.id" [disabled]="retryingId() !== null" (click)="retry(p)">Retry</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table></div>
          }
        </app-ui-card>
      }
    </div>
  `
})
export class FinancialPostingsComponent implements OnInit {
  private readonly api = inject(FinancialPostingService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly alerts = inject(AlertService);
  private readonly auth = inject(AuthService);

  readonly breadcrumbs = [{ label: 'Finance' }, { label: 'Sale GL Postings' }];
  readonly canRetry = this.auth.hasPermission('sales.financial_postings.retry');

  summary = signal<FinancialPostingSummary | null>(null);
  postings = signal<SaleFinancialPosting[]>([]);
  isLoading = signal(false);
  loadError = signal<string | null>(null);
  actionError = signal<string | null>(null);
  retryingId = signal<number | null>(null);
  status = 'failed';

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.loadError.set(null);
    this.api.summary().subscribe({
      next: (response) => this.summary.set(response.data),
      error: (err) => this.loadError.set(this.errors.extractErrorMessage(err))
    });
    this.loadPostings();
  }

  loadPostings(): void {
    this.isLoading.set(true);
    this.api.list(this.status || null).subscribe({
      next: (response) => {
        this.postings.set(response.data ?? []);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.loadError.set(this.errors.extractErrorMessage(err));
      }
    });
  }

  retry(posting: SaleFinancialPosting): void {
    if (this.retryingId() !== null) return;
    this.retryingId.set(posting.id);
    this.actionError.set(null);
    this.api.retry(posting.id).subscribe({
      next: (response) => {
        this.retryingId.set(null);
        const result = response.data;
        this.alerts.success(`Retry ${result?.outcome ?? 'done'}: posting is ${result?.posting?.status ?? 'unchanged'}`);
        this.reload();
      },
      error: (err) => {
        this.retryingId.set(null);
        this.actionError.set(this.errors.extractErrorMessage(err));
      }
    });
  }
}
