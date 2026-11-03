import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { BusinessProfitabilityReport, OutletProfitabilityReport, ProductContributionReport } from '../../models/accounting.model';

type ProfitabilityView = 'business' | 'outlet' | 'product';

@Component({
  selector: 'app-profitability-report',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './profitability-report.component.html'
})
export class ProfitabilityReportComponent implements OnInit {
  readonly view = signal<ProfitabilityView>('business');
  readonly fromDate = signal(new Date().toISOString().slice(0, 10));
  readonly toDate = signal(new Date().toISOString().slice(0, 10));
  readonly comparisonFromDate = signal('');
  readonly comparisonToDate = signal('');
  readonly outletId = signal<number | null>(null);
  readonly productId = signal<number | null>(null);
  readonly categoryId = signal<number | null>(null);
  readonly business = signal<BusinessProfitabilityReport | null>(null);
  readonly businessComparison = signal<BusinessProfitabilityReport | null>(null);
  readonly outlet = signal<OutletProfitabilityReport | null>(null);
  readonly outletComparison = signal<OutletProfitabilityReport | null>(null);
  readonly product = signal<ProductContributionReport | null>(null);
  readonly productComparison = signal<ProductContributionReport | null>(null);
  readonly isLoading = signal(false);
  readonly isExporting = signal(false);
  readonly error = signal<string | null>(null);

  constructor(private accounting: AccountingService, private alerts: AlertService, private errors: ErrorHandlerService, private currency: CurrencyService) {}

  ngOnInit(): void { this.load(); }

  setView(view: ProfitabilityView): void {
    this.view.set(view);
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    this.clearReports();
    const filters = this.filters();
    const comparative = this.comparisonFromDate() && this.comparisonToDate();

    if (this.view() === 'outlet') {
      if (comparative) {
        this.accounting.getComparativeOutletProfitability(filters).subscribe({ next: response => { this.outlet.set(response.data.current); this.outletComparison.set(response.data.comparison); this.isLoading.set(false); }, error: error => this.handleError(error) });
      } else {
        this.accounting.getOutletProfitability(filters).subscribe({ next: response => { this.outlet.set(response.data); this.isLoading.set(false); }, error: error => this.handleError(error) });
      }
      return;
    }

    if (this.view() === 'product') {
      if (comparative) {
        this.accounting.getComparativeProductContribution(filters).subscribe({ next: response => { this.product.set(response.data.current); this.productComparison.set(response.data.comparison); this.isLoading.set(false); }, error: error => this.handleError(error) });
      } else {
        this.accounting.getProductContribution(filters).subscribe({ next: response => { this.product.set(response.data); this.isLoading.set(false); }, error: error => this.handleError(error) });
      }
      return;
    }

    if (comparative) {
      this.accounting.getComparativeBusinessProfitability(filters).subscribe({ next: response => { this.business.set(response.data.current); this.businessComparison.set(response.data.comparison); this.isLoading.set(false); }, error: error => this.handleError(error) });
    } else {
      this.accounting.getBusinessProfitability(filters).subscribe({ next: response => { this.business.set(response.data); this.isLoading.set(false); }, error: error => this.handleError(error) });
    }
  }

  export(): void {
    this.isExporting.set(true);
    this.accounting.exportProfitability(this.view(), this.filters()).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `profitability-${this.view()}-${this.fromDate()}-${this.toDate()}.csv`;
        anchor.click();
        URL.revokeObjectURL(url);
        this.isExporting.set(false);
      },
      error: error => { this.isExporting.set(false); this.handleError(error); }
    });
  }

  formatCurrency(value: number): string { return this.currency.format(value || 0); }
  formatPercent(value: number): string { return `${(value || 0).toFixed(2)}%`; }

  private filters(): Record<string, string | number | undefined> {
    return {
      dateFrom: this.fromDate(),
      dateTo: this.toDate(),
      comparisonDateFrom: this.comparisonFromDate() || undefined,
      comparisonDateTo: this.comparisonToDate() || undefined,
      locationId: this.outletId() ?? undefined,
      locationType: this.outletId() !== null ? 'outlet' : undefined,
      productId: this.productId() ?? undefined,
      categoryId: this.categoryId() ?? undefined
    };
  }

  private clearReports(): void {
    this.business.set(null); this.businessComparison.set(null);
    this.outlet.set(null); this.outletComparison.set(null);
    this.product.set(null); this.productComparison.set(null);
  }

  private handleError(error: unknown): void {
    const message = this.errors.extractErrorMessage(error);
    this.error.set(message);
    this.alerts.error(message);
    this.isLoading.set(false);
  }
}
