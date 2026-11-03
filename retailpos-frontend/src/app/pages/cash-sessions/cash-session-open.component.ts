import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { CashSessionOpenContext, CashSessionService } from '../../services/cash-session.service';
import { AuthorizedLocationDto } from '../../models/report.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT } from '../../components/shared/ui';

/**
 * Open Cash Session (FR-CS-03, D9, D11). Outlet and terminal come from the authorized-outlet API, the BusinessDate,
 * opening time, time zone and duration limit are server-derived and read-only, and the opening cashier is the signed-in
 * user. A retry of the same submission reuses one idempotency key, so a double click cannot open two sessions.
 */
@Component({
  selector: 'app-cash-session-open',
  standalone: true,
  imports: [FormsModule, RouterLink, AppCurrencyPipe, ...UI_KIT],
  styleUrl: './cash-sessions.css',
  template: `
    <div class="container mx-auto px-4 py-4 cs-page" data-density="compact">
      <app-ui-page-header title="Open Cash Session" description="Start a drawer for a terminal. Opening cash is counted by you." [breadcrumbs]="breadcrumbs" />

      @if (loadError()) {
        <app-ui-state kind="error" title="Cash session setup could not be loaded" [message]="loadError()!">
          <button appUiButton type="button" variant="outline" (click)="loadContext()">Retry</button>
        </app-ui-state>
      } @else {
        @if (outlets().length > 1) {
          <app-ui-card title="Outlet">
            <app-ui-form-field label="Outlet" [required]="true">
              <select appUiControl name="outlet" [ngModel]="outletId()" (ngModelChange)="selectOutlet($event)">
                @for (outlet of outlets(); track outlet.id) { <option [ngValue]="outlet.id">{{ outlet.name }}</option> }
              </select>
            </app-ui-form-field>
          </app-ui-card>
        }

        @if (context(); as ctx) {
          @if (ctx.terminals.length === 0) {
            <app-ui-state kind="empty" title="No active terminal" message="Add or activate a terminal in the outlet settings before opening a cash session." />
          } @else {
            <form id="open-cash-form" class="cs-grid cs-grid--two" (ngSubmit)="submit()" novalidate>
              <app-ui-card title="Session details" [loading]="isLoading()">
                @if (submitError()) { <app-ui-alert variant="danger" title="Cash session not opened">{{ submitError() }}</app-ui-alert> }
                <dl class="cs-rows">
                  <div class="cs-row"><dt>Outlet</dt><dd>{{ ctx.outletName }}</dd></div>
                  <div class="cs-row"><dt>Business date</dt><dd>{{ ctx.businessDate | appDate:'dd MMM yyyy' }}</dd></div>
                  <div class="cs-row"><dt>Opening date/time</dt><dd>{{ ctx.serverNowUtc | appDate:'dd MMM yyyy, hh:mm a' }}</dd></div>
                  <div class="cs-row"><dt>Time zone</dt><dd>{{ ctx.timeZoneId }}</dd></div>
                  <div class="cs-row"><dt>Maximum duration</dt><dd>{{ ctx.maxDurationHours }} hours <span class="cs-hint">(Business setting; system maximum 20 h)</span></dd></div>
                </dl>
                <p class="cs-hint">The date, time and duration are set by the server and cannot be changed here.</p>

                @if (ctx.terminals.length > 1) {
                  <app-ui-form-field label="Terminal" [required]="true" [error]="terminalError()">
                    <select appUiControl name="terminal" [(ngModel)]="terminalId">
                      <option [ngValue]="null">Select a terminal</option>
                      @for (terminal of ctx.terminals; track terminal.id) {
                        <option [ngValue]="terminal.id" [disabled]="terminal.hasOpenSession">{{ terminal.name }} ({{ terminal.code }})@if (terminal.hasOpenSession) { — already open }</option>
                      }
                    </select>
                  </app-ui-form-field>
                } @else {
                  <dl class="cs-rows"><div class="cs-row"><dt>Terminal</dt><dd>{{ ctx.terminals[0].name }} ({{ ctx.terminals[0].code }})</dd></div></dl>
                  @if (ctx.terminals[0].hasOpenSession) {
                    <app-ui-alert variant="warning" title="Already open">This terminal already has an open cash session. Close it before opening a new one.</app-ui-alert>
                  }
                }

                <app-ui-form-field label="Opening cash" [required]="true" [error]="cashError()" hint="Zero or more, up to two decimals.">
                  <input appUiControl type="number" name="openingCash" min="0" step="0.01" inputmode="decimal" [(ngModel)]="openingCash" />
                </app-ui-form-field>
                <app-ui-form-field label="Notes" hint="Optional">
                  <textarea appUiControl name="notes" rows="3" maxlength="1000" [(ngModel)]="notes"></textarea>
                </app-ui-form-field>
              </app-ui-card>

              <app-ui-card title="Opening summary">
                <dl class="cs-rows">
                  <div class="cs-row"><dt>Opening cash</dt><dd>{{ (openingCash ?? 0) | appCurrency }}</dd></div>
                  <div class="cs-row"><dt>Opened by</dt><dd>You (from your sign-in)</dd></div>
                  <div class="cs-row cs-row--total"><dt>Sales stop at</dt><dd>{{ ctx.maxDurationHours }} h after opening</dd></div>
                </dl>
              </app-ui-card>
            </form>
            <div class="cs-sticky-footer">
              <a appUiButton variant="outline" routerLink="/cash-sessions">Cancel</a>
              <button appUiButton type="submit" form="open-cash-form" [loading]="isSubmitting()" [disabled]="isSubmitting()">Open Cash Session</button>
            </div>
          }
        } @else if (isLoading()) {
          <app-ui-state kind="loading" title="Loading" message="Loading the outlet's terminals…" />
        }
      }
    </div>
  `
})
export class CashSessionOpenComponent implements OnInit {
  private readonly sessions = inject(CashSessionService);
  private readonly outletAccess = inject(UserOutletAccessService);
  private readonly errors = inject(ErrorHandlerService);
  private readonly alerts = inject(AlertService);
  private readonly router = inject(Router);

  readonly breadcrumbs = [{ label: 'Sales', link: '/cash-sessions' }, { label: 'Cash Sessions', link: '/cash-sessions' }, { label: 'Open' }];

  outlets = signal<AuthorizedLocationDto[]>([]);
  outletId = signal<number | null>(null);
  context = signal<CashSessionOpenContext | null>(null);
  isLoading = signal(false);
  isSubmitting = signal(false);
  loadError = signal<string | null>(null);
  submitError = signal<string | null>(null);

  terminalId: number | null = null;
  openingCash: number | null = 0;
  notes = '';
  private idempotencyKey: string = crypto.randomUUID();

  terminalError = computed(() => '');
  cashError = computed(() => '');

  ngOnInit(): void {
    this.outletAccess.load('InventoryStockView').subscribe({
      next: (response) => {
        this.outlets.set(response.data.outlets);
        this.outletId.set(response.data.defaultOutletId ?? response.data.outlets[0]?.id ?? null);
        this.loadContext();
      },
      error: (err) => this.loadError.set(this.errors.extractErrorMessage(err))
    });
  }

  selectOutlet(id: number): void {
    this.outletId.set(Number(id));
    this.loadContext();
  }

  loadContext(): void {
    const outletId = this.outletId();
    if (!outletId) return;
    this.isLoading.set(true);
    this.loadError.set(null);
    this.sessions.openContext(outletId).subscribe({
      next: (response) => {
        this.context.set(response.data);
        this.terminalId = response.data.autoSelectedTerminalId ?? null;
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.loadError.set(this.errors.extractErrorMessage(err));
      }
    });
  }

  validationMessage(): string {
    const ctx = this.context();
    if (!ctx) return 'The outlet is not loaded.';
    if (ctx.terminals.length > 1 && !this.terminalId) return 'Select the terminal to open.';
    if (this.openingCash === null || Number.isNaN(Number(this.openingCash)) || Number(this.openingCash) < 0) return 'Opening cash must be zero or more.';
    return '';
  }

  submit(): void {
    if (this.isSubmitting()) return;
    const problem = this.validationMessage();
    this.submitError.set(problem || null);
    if (problem) return;

    this.isSubmitting.set(true);
    this.sessions.open({
      outletId: this.outletId()!,
      terminalId: this.terminalId ?? undefined,
      openingCashAmount: Number(this.openingCash),
      openingNotes: this.notes.trim() || undefined,
      idempotencyKey: this.idempotencyKey
    }).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.alerts.success('Cash session opened');
        this.router.navigate(['/pos']);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.submitError.set(this.errors.extractErrorMessage(err));
      }
    });
  }
}
