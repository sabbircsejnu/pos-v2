import { CompanyDateService } from '../../services/company-date.service';
import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportService } from '../../services/report.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { forkJoin } from 'rxjs';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-sales-report',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule],
  templateUrl: './sales-report.component.html',
  styleUrls: ['./sales-report.component.css']
})
export class SalesReportComponent implements OnInit {
  private readonly companyToday = injectCompanyDates(CompanyDateService);
  private readonly companyDates = injectCompanyDates(CompanyDateService);
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

  /** Quick ranges are calendar days in the Company time zone, never the browser's or UTC. */
  private shiftDays(iso: string, days: number): string {
    const [year, month, day] = iso.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
  }

  setToday(): void {
    const today = this.companyToday.todayIso();
    this.startDate.set(today);
    this.endDate.set(today);
    this.loadAll();
  }

  setThisWeek(): void {
    const today = this.companyToday.todayIso();
    const [year, month, day] = today.split('-').map(Number);
    const weekday = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
    this.startDate.set(this.shiftDays(today, weekday === 0 ? -6 : 1 - weekday));
    this.endDate.set(today);
    this.loadAll();
  }

  setThisMonth(): void {
    this.startDate.set(this.companyToday.firstOfMonthIso());
    this.endDate.set(this.companyToday.todayIso());
    this.loadAll();
  }

  setLastMonth(): void {
    const lastDay = this.shiftDays(this.companyToday.firstOfMonthIso(), -1);
    this.startDate.set(`${lastDay.slice(0, 8)}01`);
    this.endDate.set(lastDay);
    this.loadAll();
  }

  loadAll(): void {
    const s = this.startDate() || undefined;
    const e = this.endDate() || undefined;
    this.isLoading.set(true);
    forkJoin([
      this.reportService.getSalesSummary(s, e),
      this.reportService.getTopProducts(s, e),
      this.reportService.getSalesByPaymentMethod(s, e),
      this.reportService.getDailySalesTrend(s, e)
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

  formatDate(dateStr: string): string {
    return this.companyDates.format(dateStr, false);
  }
}
