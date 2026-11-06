import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { CashFlowReport, CashFlowSection } from '../../models/accounting.model';

@Component({
  selector: 'app-cash-flow-report',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './cash-flow-report.component.html'
})
export class CashFlowReportComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  readonly fromDate = signal(this.companyToday.todayIso());
  readonly toDate = signal(this.companyToday.todayIso());
  readonly comparisonFromDate = signal('');
  readonly comparisonToDate = signal('');
  readonly outletId = signal<number | null>(null);
  readonly warehouseId = signal<number | null>(null);
  readonly report = signal<CashFlowReport | null>(null);
  readonly comparison = signal<CashFlowReport | null>(null);
  readonly isLoading = signal(false);
  readonly isExporting = signal(false);
  readonly error = signal<string | null>(null);
  readonly sections = computed(() => this.report()?.sections ?? []);
  readonly hasUnclassified = computed(() => Math.abs(this.report()?.unclassifiedNetMovement ?? 0) > 0.0001);

  constructor(private accounting: AccountingService, private alerts: AlertService, private errors: ErrorHandlerService, private currency: CurrencyService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    const filters = this.filters();
    if (this.comparisonFromDate() && this.comparisonToDate()) {
      this.accounting.getComparativeCashFlow(filters).subscribe({
        next: response => {
          this.report.set(response.data.current);
          this.comparison.set(response.data.comparison);
          this.isLoading.set(false);
        },
        error: error => this.handleError(error)
      });
      return;
    }

    this.accounting.getCashFlow(filters).subscribe({
      next: response => {
        this.report.set(response.data);
        this.comparison.set(null);
        this.isLoading.set(false);
      },
      error: error => this.handleError(error)
    });
  }

  export(): void {
    this.isExporting.set(true);
    this.accounting.exportCashFlow(this.filters()).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `cash-flow-${this.fromDate()}-${this.toDate()}.csv`;
        anchor.click();
        URL.revokeObjectURL(url);
        this.isExporting.set(false);
      },
      error: error => {
        this.isExporting.set(false);
        this.handleError(error);
      }
    });
  }

  sectionComparison(activityType: string): CashFlowSection | undefined {
    return this.comparison()?.sections.find(section => section.activityType === activityType);
  }

  formatCurrency(value: number): string { return this.currency.format(value || 0); }

  private filters(): Record<string, string | number | undefined> {
    const isWarehouse = this.warehouseId() !== null;
    return {
      dateFrom: this.fromDate(),
      dateTo: this.toDate(),
      comparisonDateFrom: this.comparisonFromDate() || undefined,
      comparisonDateTo: this.comparisonToDate() || undefined,
      locationId: isWarehouse ? this.warehouseId()! : this.outletId() ?? undefined,
      locationType: isWarehouse ? 'warehouse' : this.outletId() !== null ? 'outlet' : undefined
    };
  }

  private handleError(error: unknown): void {
    const message = this.errors.extractErrorMessage(error);
    this.error.set(message);
    this.alerts.error(message);
    this.isLoading.set(false);
  }
}
