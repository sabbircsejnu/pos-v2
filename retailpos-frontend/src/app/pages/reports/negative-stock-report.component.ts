import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ReportService } from '../../services/report.service';
import { OutletService } from '../../services/outlet.service';
import { CategoryService } from '../../services/category.service';
import { WarehouseService } from '../../services/warehouse.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

@Component({
  selector: 'app-negative-stock-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './negative-stock-report.component.html',
  styleUrls: ['./negative-stock-report.component.css']
})
export class NegativeStockReportComponent implements OnInit, OnDestroy {
  // ── Filters ───────────────────────────────────────────────────────────────
  outletId    = signal<number | null>(null);
  warehouseId = signal<number | null>(null);
  categoryId  = signal<number | null>(null);
  search      = signal('');

  // ── Sort ──────────────────────────────────────────────────────────────────
  sortBy  = signal('currentStock');
  sortDir = signal<'asc' | 'desc'>('asc');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal(1);
  pageSize = signal(50);

  // ── UI ────────────────────────────────────────────────────────────────────
  isLoading        = signal(false);
  isLoadingFilters = signal(false);

  constructor(
    public  reportService:    ReportService,
    public  outletService:    OutletService,
    public  categoryService:  CategoryService,
    public  warehouseService: WarehouseService,
    private alertService:     AlertService,
    private errorHandler:     ErrorHandlerService,
    private currencyService:  CurrencyService
  ) {}

  ngOnInit(): void {
    this.isLoadingFilters.set(true);
    forkJoin([
      this.outletService.getAllOutlets(),
      this.categoryService.getAllCategories(),
      this.warehouseService.getAllWarehouses()
    ]).subscribe({
      next: () => {
        this.isLoadingFilters.set(false);
        this.loadReport();
      },
      error: () => this.isLoadingFilters.set(false)
    });
  }

  ngOnDestroy(): void {
    this.reportService.negativeStockReport.set(null);
  }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.negativeStockReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  // ── Load ──────────────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getNegativeStockReport({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      search:      this.search()      || undefined,
      sortBy:      this.sortBy(),
      sortDir:     this.sortDir(),
      page:        this.page(),
      pageSize:    this.pageSize()
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
    this.warehouseId.set(null);
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
      this.sortDir.set('asc');
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
    const url = this.reportService.buildNegativeStockExportUrl({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      search:      this.search()      || undefined,
      sortBy:      this.sortBy(),
      sortDir:     this.sortDir(),
      format
    });
    const a = document.createElement('a');
    a.href = url; a.download = ''; a.click();
  }

  formatCurrency(v: number): string { return this.currencyService.format(v); }
}
