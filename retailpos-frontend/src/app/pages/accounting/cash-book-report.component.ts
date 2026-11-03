import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-cash-book-report',
  standalone: true,
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './cash-book-report.component.html'
})
export class CashBookReportComponent implements OnInit {
  readonly fromDate = signal(new Date().toISOString().slice(0, 10));
  readonly toDate = signal(new Date().toISOString().slice(0, 10));
  readonly outletId = signal<number | null>(null);
  readonly warehouseId = signal<number | null>(null);
  readonly accountSlots = signal<string[]>(['cash', 'petty_cash', 'bank']);
  readonly report = signal<any>(null);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly accounts = computed(() => this.report()?.accounts ?? []);

  constructor(
    private accounting: AccountingService,
    private alerts: AlertService,
    private errors: ErrorHandlerService,
    private currency: CurrencyService
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.isLoading.set(true);
    this.error.set(null);
    const isWarehouse = this.warehouseId() !== null;
    this.accounting.getCashBookReport({
      dateFrom: this.fromDate(), dateTo: this.toDate(), accountSlots: this.accountSlots(),
      locationId: isWarehouse ? this.warehouseId()! : this.outletId() ?? undefined,
      locationType: isWarehouse ? 'warehouse' : this.outletId() !== null ? 'outlet' : undefined
    }).subscribe({
      next: response => { this.report.set(response.data); this.isLoading.set(false); },
      error: error => {
        const message = this.errors.extractErrorMessage(error);
        this.error.set(message); this.alerts.error(message); this.isLoading.set(false);
      }
    });
  }

  toggleSlot(slot: string, checked: boolean): void {
    const slots = this.accountSlots();
    this.accountSlots.set(checked ? [...slots, slot] : slots.filter(item => item !== slot));
  }

  formatCurrency(value: number): string { return this.currency.format(value || 0); }
}