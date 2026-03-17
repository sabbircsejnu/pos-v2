import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportService } from '../../services/report.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'app-sales-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './sales-report.component.html',
  styleUrls: ['./sales-report.component.css']
})
export class SalesReportComponent implements OnInit {
  startDate = signal('');
  endDate = signal('');
  isLoading = signal(false);

  constructor(
    public reportService: ReportService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.setThisMonth();
    this.loadAll();
  }

  private fmt(d: Date): string {
    return d.toISOString().substring(0, 10);
  }

  setToday(): void {
    const today = this.fmt(new Date());
    this.startDate.set(today);
    this.endDate.set(today);
    this.loadAll();
  }

  setThisWeek(): void {
    const now = new Date();
    const day = now.getDay();
    const diff = now.getDate() - day + (day === 0 ? -6 : 1);
    const start = new Date(now.setDate(diff));
    const end = new Date();
    this.startDate.set(this.fmt(start));
    this.endDate.set(this.fmt(end));
    this.loadAll();
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
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount || 0);
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
