import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { EnhancedGeneralLedgerReport } from '../../models/accounting.model';

@Component({ selector: 'app-enhanced-general-ledger', standalone: true, imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule], templateUrl: './enhanced-general-ledger.component.html' })
export class EnhancedGeneralLedgerComponent implements OnInit {
  readonly accountId = signal<number | null>(null); readonly dateFrom = signal(new Date().toISOString().slice(0, 10)); readonly dateTo = signal(new Date().toISOString().slice(0, 10));
  readonly locationId = signal<number | null>(null); readonly locationType = signal<'outlet' | 'warehouse'>('outlet'); readonly accounts = signal<Array<{ id: number; accountCode: string; accountName: string }>>([]);
  readonly report = signal<EnhancedGeneralLedgerReport | null>(null); readonly isLoading = signal(false); readonly error = signal<string | null>(null);
  constructor(private route: ActivatedRoute, private accounting: AccountingService, private alerts: AlertService, private errors: ErrorHandlerService, private currency: CurrencyService) {}
  ngOnInit(): void { const queryAccountId = Number(this.route.snapshot.queryParamMap.get('accountId')); if (queryAccountId) this.accountId.set(queryAccountId); const queryDateTo = this.route.snapshot.queryParamMap.get('dateTo'); if (queryDateTo) this.dateTo.set(queryDateTo); this.accounting.getChartAccounts().subscribe({ next: response => { this.accounts.set(response.data); if (this.accountId()) this.load(); }, error: error => this.error.set(this.errors.extractErrorMessage(error)) }); }
  load(): void { if (!this.accountId()) { this.alerts.error('Select an account.'); return; } this.isLoading.set(true); this.error.set(null); this.accounting.getEnhancedGeneralLedger({ accountId: this.accountId()!, dateFrom: this.dateFrom(), dateTo: this.dateTo(), locationId: this.locationId() ?? undefined, locationType: this.locationId() ? this.locationType() : undefined }).subscribe({ next: response => { this.report.set(response.data); this.isLoading.set(false); }, error: error => { const message = this.errors.extractErrorMessage(error); this.error.set(message); this.alerts.error(message); this.isLoading.set(false); } }); }
  formatCurrency(value: number): string { return this.currency.format(value || 0); } print(): void { window.print(); }
}