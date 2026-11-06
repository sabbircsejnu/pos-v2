import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { BalanceSheetAccount, BalanceSheetReport } from '../../models/accounting.model';

interface AccountRow { account: BalanceSheetAccount; depth: number; }

@Component({ selector: 'app-balance-sheet-report', standalone: true, imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule], templateUrl: './balance-sheet-report.component.html' })
export class BalanceSheetReportComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  readonly asOfDate = signal(this.companyToday.todayIso());
  readonly comparisonAsOfDate = signal('');
  readonly locationId = signal<number | null>(null);
  readonly locationType = signal<'outlet' | 'warehouse'>('outlet');
  readonly report = signal<BalanceSheetReport | null>(null);
  readonly comparison = signal<BalanceSheetReport | null>(null);
  readonly expanded = signal(new Set<number>());
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  constructor(private accounting: AccountingService, private router: Router, private alerts: AlertService, private errors: ErrorHandlerService, private currency: CurrencyService) {}
  ngOnInit(): void { this.load(); }

  load(): void {
    this.isLoading.set(true); this.error.set(null);
    const filters = { asOfDate: this.asOfDate(), comparisonAsOfDate: this.comparisonAsOfDate() || undefined, locationId: this.locationId() ?? undefined, locationType: this.locationId() ? this.locationType() : undefined };
    if (this.comparisonAsOfDate()) {
      this.accounting.getComparativeBalanceSheet(filters).subscribe({
        next: response => { this.report.set(response.data.current); this.comparison.set(response.data.comparison); this.isLoading.set(false); },
        error: error => this.handleError(error)
      });
      return;
    }
    this.accounting.getBalanceSheet(filters).subscribe({
      next: response => { this.report.set(response.data); this.comparison.set(null); this.isLoading.set(false); },
      error: error => this.handleError(error)
    });
  }

  private handleError(error: unknown): void { const message = this.errors.extractErrorMessage(error); this.error.set(message); this.alerts.error(message); this.isLoading.set(false); }

  rows(accounts: BalanceSheetAccount[], depth = 0): AccountRow[] { return accounts.flatMap(account => [{ account, depth }, ...(this.expanded().has(account.accountId) ? this.rows(account.children, depth + 1) : [])]); }
  toggle(account: BalanceSheetAccount): void { this.expanded.update(expanded => { const next = new Set(expanded); next.has(account.accountId) ? next.delete(account.accountId) : next.add(account.accountId); return next; }); }
  openLedger(accountId: number): void { this.router.navigate(['/reports/enhanced-general-ledger'], { queryParams: { accountId, dateTo: this.asOfDate() } }); }
  formatCurrency(value: number): string { return this.currency.format(value || 0); }
  print(): void { window.print(); }
}