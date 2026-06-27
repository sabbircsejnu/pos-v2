import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ReportService } from '../../services/report.service';
import { OutletService } from '../../services/outlet.service';
import { CategoryService } from '../../services/category.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-outlet-wise-stock-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './outlet-wise-stock-report.component.html',
  styleUrls: ['./outlet-wise-stock-report.component.css']
})
export class OutletWiseStockReportComponent implements OnInit, OnDestroy {
  // ── Filters ───────────────────────────────────────────────────────────────
  outletId   = signal<number | null>(null);
  categoryId = signal<number | null>(null);
  search     = signal('');

  // ── Sort ──────────────────────────────────────────────────────────────────
  sortBy  = signal('outletName');
  sortDir = signal<'asc' | 'desc'>('asc');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal(1);
  pageSize = signal(50);

  // ── UI ────────────────────────────────────────────────────────────────────
  isLoading        = signal(false);
  isLoadingFilters = signal(false);
  showOutletPanel  = signal(false);

  constructor(
    public  reportService:   ReportService,
    public  outletService:   OutletService,
    public  categoryService: CategoryService,
    private alertService:    AlertService,
    private errorHandler:    ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.isLoadingFilters.set(true);
    forkJoin([
      this.outletService.getAllOutlets(),
      this.categoryService.getAllCategories()
    ]).subscribe({
      next: () => {
        this.isLoadingFilters.set(false);
        this.loadReport();
      },
      error: () => this.isLoadingFilters.set(false)
    });
  }

  ngOnDestroy(): void {
    this.reportService.outletWiseStockReport.set(null);
  }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.outletWiseStockReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  // ── Load ──────────────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getOutletWiseStockReport({
      outletId:   this.outletId()   ?? undefined,
      categoryId: this.categoryId() ?? undefined,
      search:     this.search()     || undefined,
      sortBy:     this.sortBy(),
      sortDir:    this.sortDir(),
      page:       this.page(),
      pageSize:   this.pageSize()
    }).subscribe({
      next:  () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  applyFilters(): void { this.page.set(1); this.loadReport(); }

  resetFilters(): void {
    this.outletId.set(null);
    this.categoryId.set(null);
    this.search.set('');
    this.page.set(1);
    this.loadReport();
  }

  // ── Sorting ───────────────────────────────────────────────────────────────
  sort(col: string): void {
    if (this.sortBy() === col) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(col);
      this.sortDir.set(col === 'quantity' || col === 'stockValue' ? 'desc' : 'asc');
    }
    this.page.set(1);
    this.loadReport();
  }

  sortIcon(col: string): string {
    if (this.sortBy() !== col) return 'fas fa-sort text-gray-300';
    return this.sortDir() === 'asc' ? 'fas fa-sort-up text-blue-600' : 'fas fa-sort-down text-blue-600';
  }

  // ── Pagination ────────────────────────────────────────────────────────────
  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages()) return;
    this.page.set(p);
    this.loadReport();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.loadReport();
  }

  pages(): number[] {
    const total = this.totalPages(), current = this.page(), delta = 2;
    const range: number[] = [];
    for (let i = Math.max(1, current - delta); i <= Math.min(total, current + delta); i++)
      range.push(i);
    return range;
  }

  // ── Export ────────────────────────────────────────────────────────────────
  export(format: 'csv' | 'excel'): void {
    const url = this.reportService.buildOutletWiseStockExportUrl({
      outletId:   this.outletId()   ?? undefined,
      categoryId: this.categoryId() ?? undefined,
      search:     this.search()     || undefined,
      sortBy:     this.sortBy(),
      sortDir:    this.sortDir(),
      format
    });
    const a = document.createElement('a');
    a.href = url; a.download = ''; a.click();
  }

  // ── Formatting ────────────────────────────────────────────────────────────
  formatCurrency(v: number): string { return this.currencyService.format(v); }

  outletShareWidth(value: number): string {
    const total = this.summary()?.totalStockValue ?? 0;
    if (!total) return '0%';
    return `${Math.min(100, (value / total) * 100)}%`;
  }
}
