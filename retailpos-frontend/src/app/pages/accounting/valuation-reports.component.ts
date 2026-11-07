import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { injectVariantUnits } from '../../utils/variant-units';

@Component({
  selector: 'app-valuation-reports',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './valuation-reports.component.html'
})
export class ValuationReportsComponent implements OnInit {
  private readonly companyToday = inject(CompanyDateService);
  readonly mode = signal<'cogs' | 'inventory'>('cogs');
  readonly fromDate = signal(this.companyToday.todayIso());
  readonly toDate = signal(this.companyToday.todayIso());
  readonly outletId = signal<number | null>(null);
  readonly warehouseId = signal<number | null>(null);
  readonly productId = signal<number | null>(null);
  readonly variantId = signal<number | null>(null);
  readonly report = signal<any>(null);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly isCogs = computed(() => this.mode() === 'cogs');
  /** The report's total quantity covers pcs only; its rows keep their own units. */
  readonly units = injectVariantUnits(() => this.isCogs()
    ? []
    : ((this.report()?.rows ?? []) as { variantId: number }[]).map(row => row.variantId));

  constructor(
    private route: ActivatedRoute,
    private accounting: AccountingService,
    private alerts: AlertService,
    private errors: ErrorHandlerService,
    private currency: CurrencyService
  ) {}

  ngOnInit(): void {
    this.mode.set(this.route.snapshot.data['valuationMode'] === 'inventory' ? 'inventory' : 'cogs');
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    const filters = {
      dateFrom: this.isCogs() ? this.fromDate() : undefined,
      dateTo: this.toDate(),
      outletId: this.outletId() ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      productId: this.productId() ?? undefined,
      variantId: this.variantId() ?? undefined
    };
    const request = this.isCogs()
      ? this.accounting.getCogsReport(filters)
      : this.accounting.getInventoryValuationReport(filters);
    request.subscribe({
      next: response => { this.report.set(response.data); this.isLoading.set(false); },
      error: error => {
        const message = this.errors.extractErrorMessage(error);
        this.error.set(message);
        this.alerts.error(message);
        this.isLoading.set(false);
      }
    });
  }

  clear(): void {
    this.outletId.set(null); this.warehouseId.set(null); this.productId.set(null); this.variantId.set(null);
    this.load();
  }

  formatCurrency(value: number): string { return this.currency.format(value || 0); }
}