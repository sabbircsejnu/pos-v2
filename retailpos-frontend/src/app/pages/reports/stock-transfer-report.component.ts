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
  selector: 'app-stock-transfer-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-transfer-report.component.html',
  styleUrls: ['./stock-transfer-report.component.css']
})
export class StockTransferReportComponent implements OnInit, OnDestroy {
  // ── Filters ───────────────────────────────────────────────────────────────
  fromOutletId = signal<number | null>(null);
  toOutletId   = signal<number | null>(null);
  categoryId   = signal<number | null>(null);
  status       = signal('');
  search       = signal('');
  dateFrom     = signal('');
  dateTo       = signal('');

  // ── Sort ──────────────────────────────────────────────────────────────────
  sortBy  = signal('transferDate');
  sortDir = signal<'asc' | 'desc'>('desc');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal(1);
  pageSize = signal(50);

  // ── UI ────────────────────────────────────────────────────────────────────
  isLoading        = signal(false);
  isLoadingFilters = signal(false);

  readonly statusOptions = ['', 'pending', 'completed', 'cancelled'];

  constructor(
    public  reportService:   ReportService,
    public  outletService:   OutletService,
    public  categoryService: CategoryService,
    private alertService:    AlertService,
    private errorHandler:    ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    const now  = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    this.dateTo.set(now.toISOString().slice(0, 10));
    this.dateFrom.set(from.toISOString().slice(0, 10));

    this.isLoadingFilters.set(true);
    forkJoin([
      this.outletService.getAllOutlets(),
      this.categoryService.getAllCategories()
    ]).subscribe({
      next: () => { this.isLoadingFilters.set(false); this.loadReport(); },
      error: () => this.isLoadingFilters.set(false)
    });
  }

  ngOnDestroy(): void { this.reportService.stockTransferReport.set(null); }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.stockTransferReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  // ── Load ──────────────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getStockTransferReport({
      fromOutletId: this.fromOutletId() ?? undefined,
      toOutletId:   this.toOutletId()   ?? undefined,
      categoryId:   this.categoryId()   ?? undefined,
      status:       this.status()       || undefined,
      search:       this.search()       || undefined,
      dateFrom:     this.dateFrom()     || undefined,
      dateTo:       this.dateTo()       || undefined,
      sortBy:       this.sortBy(),
      sortDir:      this.sortDir(),
      page:         this.page(),
      pageSize:     this.pageSize()
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
    this.fromOutletId.set(null);
    this.toOutletId.set(null);
    this.categoryId.set(null);
    this.status.set('');
    this.search.set('');
    const now  = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    this.dateTo.set(now.toISOString().slice(0, 10));
    this.dateFrom.set(from.toISOString().slice(0, 10));
    this.page.set(1);
    this.loadReport();
  }

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
      this.sortDir.set(col === 'transferDate' ? 'desc' : 'asc');
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
    const url = this.reportService.buildStockTransferReportExportUrl({
      fromOutletId: this.fromOutletId() ?? undefined,
      toOutletId:   this.toOutletId()   ?? undefined,
      categoryId:   this.categoryId()   ?? undefined,
      status:       this.status()       || undefined,
      search:       this.search()       || undefined,
      dateFrom:     this.dateFrom()     || undefined,
      dateTo:       this.dateTo()       || undefined,
      sortBy:       this.sortBy(),
      sortDir:      this.sortDir(),
      format
    });
    const a = document.createElement('a');
    a.href = url; a.download = ''; a.click();
  }

  formatCurrency(v: number): string { return this.currencyService.format(v); }

  statusClass(s: string): string {
    switch (s) {
      case 'completed': return 'bg-green-100 text-green-800 border border-green-200';
      case 'pending':   return 'bg-amber-100 text-amber-800 border border-amber-200';
      case 'cancelled': return 'bg-red-100 text-red-800 border border-red-200';
      default:          return 'bg-gray-100 text-gray-800 border border-gray-200';
    }
  }
}
