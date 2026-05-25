import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportService } from '../../services/report.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { forkJoin } from 'rxjs';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-purchase-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './purchase-report.component.html',
  styleUrls: ['./purchase-report.component.css']
})
export class PurchaseReportComponent implements OnInit {
  startDate = signal('');
  endDate = signal('');
  isLoading = signal(false);

  constructor(
    public reportService: ReportService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.setThisMonth();
    this.loadAll();
  }

  private fmt(d: Date): string {
    return d.toISOString().substring(0, 10);
  }

  setThisMonth(): void {
    const now = new Date();
    const start = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate.set(this.fmt(start));
    this.endDate.set(this.fmt(new Date()));
    this.loadAll();
  }

  setLastMonth(): void {
    const now = new Date();
    const start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    const end = new Date(now.getFullYear(), now.getMonth(), 0);
    this.startDate.set(this.fmt(start));
    this.endDate.set(this.fmt(end));
    this.loadAll();
  }

  loadAll(): void {
    const s = this.startDate() || undefined;
    const e = this.endDate() || undefined;
    this.isLoading.set(true);
    forkJoin([
      this.reportService.getPurchaseSummary(s, e),
      this.reportService.getPurchaseBySupplier(s, e)
    ]).subscribe({
      next: () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }
}
