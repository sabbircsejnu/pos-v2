import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportService } from '../../services/report.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { CategoryService } from '../../services/category.service';
import { forkJoin } from 'rxjs';
import { CurrentStockRowDto } from '../../models/report.model';

@Component({
  selector: 'app-current-stock-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './current-stock-report.component.html',
  styleUrls: ['./current-stock-report.component.css']
})
export class CurrentStockReportComponent implements OnInit {
  // ── Filter state ─────────────────────────────────────────────────────────
  outletId    = signal<number | null>(null);
  warehouseId = signal<number | null>(null);
  categoryId  = signal<number | null>(null);
  productId   = signal<number | null>(null);
  stockStatus = signal<string>('all');
  search      = signal<string>('');

  // ── Sort state ────────────────────────────────────────────────────────────
  sortBy  = signal<string>('productName');
  sortDir = signal<'asc' | 'desc'>('asc');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal<number>(1);
  pageSize = signal<number>(50);

  // ── UI state ──────────────────────────────────────────────────────────────
  isLoading        = signal(false);
  isLoadingFilters = signal(false);
  isExporting      = signal(false);

  // ── Column visibility ─────────────────────────────────────────────────────
  showColumnPanel = signal(false);
  columns = signal([
    { key: 'productCode',       label: 'Product Code',       visible: true },
    { key: 'barcode',           label: 'Barcode',            visible: true },
    { key: 'productName',       label: 'Product Name',       visible: true },
    { key: 'category',          label: 'Category',           visible: true },
    { key: 'location',          label: 'Location',           visible: true },
    { key: 'availableQuantity', label: 'Available Qty',      visible: true },
    { key: 'reservedQuantity',  label: 'Reserved Qty',       visible: false },
    { key: 'reorderLevel',      label: 'Reorder Level',      visible: true },
    { key: 'unitCost',          label: 'Unit Cost',          visible: true },
    { key: 'stockValue',        label: 'Stock Value',        visible: true },
    { key: 'lastPurchaseDate',  label: 'Last Purchase Date', visible: true },
    { key: 'lastSaleDate',      label: 'Last Sale Date',     visible: true },
    { key: 'stockStatus',       label: 'Status',             visible: true },
  ]);

  constructor(
    public reportService: ReportService,
    public outletService: OutletService,
    public warehouseService: WarehouseService,
    public categoryService: CategoryService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
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
      error: (err) => {
        this.isLoadingFilters.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.loadReport();
      }
    });
  }

  // ── Computed helpers ──────────────────────────────────────────────────────
  report     = computed(() => this.reportService.currentStockReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);

  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );

  visibleColumns = computed(() => this.columns().filter(c => c.visible));

  // ── Load report ───────────────────────────────────────────────────────────
  loadReport(): void {
    this.isLoading.set(true);
    this.reportService.getCurrentStockReport({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      productId:   this.productId()   ?? undefined,
      stockStatus: this.stockStatus() !== 'all' ? this.stockStatus() : undefined,
      search:      this.search()       || undefined,
      sortBy:      this.sortBy(),
      sortDir:     this.sortDir(),
      page:        this.page(),
      pageSize:    this.pageSize()
    }).subscribe({
      next: () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  applyFilters(): void {
    this.page.set(1);
    this.loadReport();
  }

  resetFilters(): void {
    this.outletId.set(null);
    this.warehouseId.set(null);
    this.categoryId.set(null);
    this.productId.set(null);
    this.stockStatus.set('all');
    this.search.set('');
    this.sortBy.set('productName');
    this.sortDir.set('asc');
    this.page.set(1);
    this.loadReport();
  }

  // ── Sort ──────────────────────────────────────────────────────────────────
  sort(column: string): void {
    if (this.sortBy() === column) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(column);
      this.sortDir.set('asc');
    }
    this.page.set(1);
    this.loadReport();
  }

  sortIcon(column: string): string {
    if (this.sortBy() !== column) return 'fa-sort';
    return this.sortDir() === 'asc' ? 'fa-sort-up' : 'fa-sort-down';
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
    const total = this.totalPages();
    const current = this.page();
    const delta = 2;
    const left = Math.max(1, current - delta);
    const right = Math.min(total, current + delta);
    const range: number[] = [];
    for (let i = left; i <= right; i++) range.push(i);
    return range;
  }

  // ── Export ────────────────────────────────────────────────────────────────
  export(format: 'csv' | 'excel'): void {
    const url = this.reportService.buildCurrentStockExportUrl({
      outletId:    this.outletId()    ?? undefined,
      warehouseId: this.warehouseId() ?? undefined,
      categoryId:  this.categoryId()  ?? undefined,
      productId:   this.productId()   ?? undefined,
      stockStatus: this.stockStatus() !== 'all' ? this.stockStatus() : undefined,
      search:      this.search()       || undefined,
      sortBy:      this.sortBy(),
      sortDir:     this.sortDir(),
      format
    });
    // Trigger download using a temporary anchor element
    const a = document.createElement('a');
    a.href = url;
    a.download = '';
    a.click();
  }

  // ── Formatting ────────────────────────────────────────────────────────────
  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '—';
    return new Date(dateStr).toLocaleDateString('en-GB', {
      year: 'numeric', month: 'short', day: '2-digit'
    });
  }

  stockStatusBadge(status: string): string {
    switch (status) {
      case 'in-stock':    return 'badge-success';
      case 'low-stock':   return 'badge-warning';
      case 'out-of-stock': return 'badge-error';
      default:            return 'badge-neutral';
    }
  }

  stockStatusLabel(status: string): string {
    switch (status) {
      case 'in-stock':    return 'In Stock';
      case 'low-stock':   return 'Low Stock';
      case 'out-of-stock': return 'Out of Stock';
      default:            return status;
    }
  }

  isColumnVisible(key: string): boolean {
    return this.columns().find(c => c.key === key)?.visible ?? false;
  }

  toggleColumn(key: string): void {
    this.columns.update(cols =>
      cols.map(c => c.key === key ? { ...c, visible: !c.visible } : c)
    );
  }
}
