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

@Component({
  selector: 'app-stock-adjustment-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-adjustment-report.component.html',
  styleUrls: ['./stock-adjustment-report.component.css']
})
export class StockAdjustmentReportComponent implements OnInit, OnDestroy {
  // ── Filters ───────────────────────────────────────────────────────────────
  outletId    = signal<number | null>(null);
  warehouseId = signal<number | null>(null);
  categoryId  = signal<number | null>(null);
  search      = signal('');
  dateFrom    = signal('');
  dateTo      = signal('');

  // ── Sort ──────────────────────────────────────────────────────────────────
  sortBy  = signal('adjustmentDate');
  sortDir = signal<'asc' | 'desc'>('desc');

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
    private errorHandler:     ErrorHandlerService
  ) {}

  ngOnInit(): void {
    // Default to last 30 days
    const now  = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    this.dateTo.set(now.toISOString().slice(0, 10));
    this.dateFrom.set(from.toISOString().slice(0, 10));

    this.isLoadingFilters.set(true);
    forkJoin([
      this.outletService.getAllOutlets(),
      this.categoryService.getAllCategories(),
      this.warehouseService.getAllWarehouses()
    ]).subscribe({
      next: () => { this.isLoadingFilters.set(false); this.loadReport(); },
      error: () => this.isLoadingFilters.set(false)
    });
  }

  ngOnDestroy(): void { this.reportService.stockAdjustmentReport.set(null); }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.stockAdjustmentReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  // ── Load ──────────────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getStockAdjustmentReport({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      search:      this.search()      || undefined,
      dateFrom:    this.dateFrom()    || undefined,
      dateTo:      this.dateTo()      || undefined,
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
    const now  = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    this.dateTo.set(now.toISOString().slice(0, 10));
    this.dateFrom.set(from.toISOString().slice(0, 10));
    this.page.set(1);
    this.loadReport();
  }

  // ── Date shortcuts ────────────────────────────────────────────────────────
  setDateRange(range: 'today' | 'last30' | 'thisMonth' | 'lastMonth'): void {
    const now = new Date();
    if (range === 'today') {
      this.dateFrom.set(now.toISOString().slice(0, 10));
      this.dateTo.set(now.toISOString().slice(0, 10));
    } else if (range === 'last30') {
      const from = new Date(now); from.setDate(from.getDate() - 30);
      this.dateFrom.set(from.toISOString().slice(0, 10));
      this.dateTo.set(now.toISOString().slice(0, 10));
    } else if (range === 'thisMonth') {
      this.dateFrom.set(new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10));
      this.dateTo.set(now.toISOString().slice(0, 10));
    } else {
      const first = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const last  = new Date(now.getFullYear(), now.getMonth(), 0);
      this.dateFrom.set(first.toISOString().slice(0, 10));
      this.dateTo.set(last.toISOString().slice(0, 10));
    }
    this.page.set(1);
    this.loadReport();
  }

  // ── Sorting ───────────────────────────────────────────────────────────────
  sort(col: string): void {
    if (this.sortBy() === col) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(col);
      this.sortDir.set(col === 'adjustmentDate' ? 'desc' : 'asc');
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
    const url = this.reportService.buildStockAdjustmentExportUrl({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      search:      this.search()      || undefined,
      dateFrom:    this.dateFrom()    || undefined,
      dateTo:      this.dateTo()      || undefined,
      sortBy:      this.sortBy(),
      sortDir:     this.sortDir(),
      format
    });
    const a = document.createElement('a');
    a.href = url; a.download = ''; a.click();
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  typeClass(type: string): string {
    return type === 'Addition'
      ? 'bg-green-100 text-green-800 border border-green-200'
      : 'bg-red-100 text-red-800 border border-red-200';
  }

  typeRowClass(type: string): string {
    return type === 'Addition' ? 'bg-green-50' : 'bg-red-50';
  }
}
