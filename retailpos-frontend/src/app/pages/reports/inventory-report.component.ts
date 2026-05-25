import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportService } from '../../services/report.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { forkJoin } from 'rxjs';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-inventory-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory-report.component.html',
  styleUrls: ['./inventory-report.component.css']
})
export class InventoryReportComponent implements OnInit {
  activeTab = signal<'stock' | 'slow'>('stock');
  locationTypeFilter = signal('');
  lowStockOnly = signal(false);
  slowMovingDays = signal(90);
  isLoading = signal(false);

  constructor(
    public reportService: ReportService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.loadAll();
  }

  loadAll(): void {
    this.isLoading.set(true);
    forkJoin([
      this.reportService.getInventoryValuation(),
      this.reportService.getStockLevels(
        undefined,
        this.locationTypeFilter() || undefined,
        this.lowStockOnly()
      ),
      this.reportService.getSlowMovingItems(this.slowMovingDays())
    ]).subscribe({
      next: () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  applyStockFilter(): void {
    this.isLoading.set(true);
    this.reportService.getStockLevels(
      undefined,
      this.locationTypeFilter() || undefined,
      this.lowStockOnly()
    ).subscribe({
      next: () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  applySlowMovingFilter(): void {
    this.isLoading.set(true);
    this.reportService.getSlowMovingItems(this.slowMovingDays()).subscribe({
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
