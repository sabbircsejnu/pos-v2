import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ReportService } from '../../services/report.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { CategoryService } from '../../services/category.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { AuthService } from '../../services/auth.service';

import { injectVariantUnits } from '../../utils/variant-units';

@Component({
  selector: 'app-stock-valuation-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-valuation-report.component.html',
  styleUrls: ['./stock-valuation-report.component.css']
})
export class StockValuationReportComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  /** U-13: cost-derived figures need products.view_cost; without it the API sends null. */
  readonly canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));

  // ── Filters ───────────────────────────────────────────────────────────────
  outletId    = signal<number | null>(null);
  warehouseId = signal<number | null>(null);
  categoryId  = signal<number | null>(null);
  search      = signal('');

  // ── Sort ──────────────────────────────────────────────────────────────────
  // Without products.view_cost the value sort is not offered (the server would refuse to rank by value anyway).
  sortBy  = signal(this.canViewCost() ? 'inventoryValue' : 'productCode');
  sortDir = signal<'asc' | 'desc'>(this.canViewCost() ? 'desc' : 'asc');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal(1);
  pageSize = signal(50);

  // ── UI ────────────────────────────────────────────────────────────────────
  isLoading        = signal(false);
  isLoadingFilters = signal(false);
  showCategoryPanel = signal(false);

  constructor(
    public  reportService:    ReportService,
    public  outletService:    OutletService,
    public  warehouseService: WarehouseService,
    public  categoryService:  CategoryService,
    private alertService:     AlertService,
    private errorHandler:     ErrorHandlerService,
    private currencyService:  CurrencyService
  ) {}

  ngOnInit(): void {
    this.isLoadingFilters.set(true);
    forkJoin([
      this.outletService.getAllOutlets(),
      this.warehouseService.getAllWarehouses(),
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
    this.reportService.stockValuationReport.set(null);
  }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.stockValuationReport());
  rows       = computed(() => this.report()?.items ?? []);
  readonly units = injectVariantUnits(() => this.rows().map(row => row.variantId));
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  // ── Load ──────────────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getStockValuationReport({
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
      this.sortDir.set(col === 'inventoryValue' || col === 'quantity' ? 'desc' : 'asc');
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
    const url = this.reportService.buildStockValuationExportUrl({
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

  // ── Formatting ────────────────────────────────────────────────────────────
  formatCurrency(v: number | null | undefined): string { return v == null ? '—' : this.currencyService.format(v); }

  valueBarWidth(pct: number | null | undefined): string {
    return `${Math.min(100, pct ?? 0)}%`;
  }
}
