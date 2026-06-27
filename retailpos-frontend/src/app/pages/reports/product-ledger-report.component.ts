import { Component, OnInit, OnDestroy, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { ReportService } from '../../services/report.service';
import { ProductService } from '../../services/product.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';

interface ProductOption {
  id: number;
  name: string;
  sku?: string;
  barcode?: string;
  variants: { id: number; name: string; sku: string }[];
}

export const TRANSACTION_TYPES = [
  { value: 'all',          label: 'All Types' },
  { value: 'grn',          label: 'Purchase (GRN)' },
  { value: 'sale',         label: 'Sale' },
  { value: 'return',       label: 'Sales Return' },
  { value: 'adjustment',   label: 'Stock Adjustment' },
  { value: 'transfer_in',  label: 'Transfer In' },
  { value: 'transfer_out', label: 'Transfer Out' },
  { value: 'exchange',     label: 'Exchange' },
];

@Component({
  selector: 'app-product-ledger-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './product-ledger-report.component.html',
  styleUrls: ['./product-ledger-report.component.css']
})
export class ProductLedgerReportComponent implements OnInit, OnDestroy {
  // ── Product search ────────────────────────────────────────────────────────
  productQuery    = signal('');
  productResults  = signal<ProductOption[]>([]);
  productSearching = signal(false);
  productDropdownOpen = signal(false);
  selectedProduct = signal<ProductOption | null>(null);
  selectedVariantId = signal<number | null>(null);

  // ── Filters ───────────────────────────────────────────────────────────────
  outletId         = signal<number | null>(null);
  warehouseId      = signal<number | null>(null);
  dateFrom         = signal('');
  dateTo           = signal('');
  transactionType  = signal('all');

  // ── Pagination ────────────────────────────────────────────────────────────
  page     = signal(1);
  pageSize = signal(50);

  // ── UI ────────────────────────────────────────────────────────────────────
  isLoading         = signal(false);
  isLoadingFilters  = signal(false);
  readonly txTypes  = TRANSACTION_TYPES;

  private productSearch$ = new Subject<string>();
  private subs: Subscription[] = [];

  constructor(
    public reportService: ReportService,
    private productService: ProductService,
    protected outletService: OutletService,
    protected warehouseService: WarehouseService,
    private userOutletAccess: UserOutletAccessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    this.setThisMonth();
    this.isLoadingFilters.set(true);

    this.subs.push(
      this.productSearch$.pipe(debounceTime(300)).subscribe(q => this.runProductSearch(q))
    );

    Promise.all([
      this.outletService.getAllOutlets().toPromise(),
      this.warehouseService.getAllWarehouses().toPromise(),
      this.userOutletAccess.load().toPromise()
    ]).finally(() => this.isLoadingFilters.set(false));
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.reportService.productLedgerReport.set(null);
  }

  // ── Computed ──────────────────────────────────────────────────────────────
  report     = computed(() => this.reportService.productLedgerReport());
  rows       = computed(() => this.report()?.items ?? []);
  summary    = computed(() => this.report()?.summary ?? null);
  totalCount = computed(() => this.report()?.totalCount ?? 0);
  totalPages = computed(() =>
    this.pageSize() > 0 ? Math.max(1, Math.ceil(this.totalCount() / this.pageSize())) : 1
  );
  canLoad    = computed(() => this.selectedProduct() !== null);

  // ── Date helpers ──────────────────────────────────────────────────────────
  private fmt(d: Date): string { return d.toISOString().substring(0, 10); }

  setToday(): void {
    const t = this.fmt(new Date());
    this.dateFrom.set(t); this.dateTo.set(t);
  }

  setThisMonth(): void {
    const now = new Date();
    this.dateFrom.set(this.fmt(new Date(now.getFullYear(), now.getMonth(), 1)));
    this.dateTo.set(this.fmt(new Date()));
  }

  setLastMonth(): void {
    const now = new Date();
    this.dateFrom.set(this.fmt(new Date(now.getFullYear(), now.getMonth() - 1, 1)));
    this.dateTo.set(this.fmt(new Date(now.getFullYear(), now.getMonth(), 0)));
  }

  setLast30Days(): void {
    const now = new Date();
    this.dateFrom.set(this.fmt(new Date(now.getTime() - 30 * 86400000)));
    this.dateTo.set(this.fmt(now));
  }

  // ── Product search ────────────────────────────────────────────────────────
  onProductInput(value: string): void {
    this.productQuery.set(value);
    this.productDropdownOpen.set(true);
    if (!value || !this.selectedProduct() || this.selectedProduct()!.name !== value) {
      this.selectedProduct.set(null);
      this.selectedVariantId.set(null);
    }
    this.productSearch$.next(value);
  }

  onProductFocus(): void { this.productDropdownOpen.set(true); }
  onProductBlur(): void { setTimeout(() => this.productDropdownOpen.set(false), 200); }

  private runProductSearch(query: string): void {
    if (!query || query.length < 2) { this.productResults.set([]); return; }
    this.productSearching.set(true);
    this.productService.search({ searchQuery: query, pageNumber: 1, pageSize: 10, sortBy: 'name', sortOrder: 'asc' }).subscribe({
      next: (res: any) => {
        const products: ProductOption[] = (res.data?.products ?? []).map((p: any) => ({
          id: p.id, name: p.name, sku: p.sku, barcode: p.barcode,
          variants: (p.variants ?? []).map((v: any) => ({ id: v.id, name: v.name, sku: v.sku }))
        }));
        this.productResults.set(products);
        this.productSearching.set(false);
      },
      error: () => { this.productSearching.set(false); this.productResults.set([]); }
    });
  }

  selectProduct(product: ProductOption): void {
    this.selectedProduct.set(product);
    this.selectedVariantId.set(null);
    this.productQuery.set(product.name);
    this.productDropdownOpen.set(false);
    this.productResults.set([]);
  }

  clearProduct(): void {
    this.selectedProduct.set(null);
    this.selectedVariantId.set(null);
    this.productQuery.set('');
    this.productResults.set([]);
    this.reportService.productLedgerReport.set(null);
  }

  // ── Load report ───────────────────────────────────────────────────────────
  loadReport(): void {
    const product = this.selectedProduct();
    if (!product) { this.alertService.warning('Please select a product first.'); return; }

    this.isLoading.set(true);
    this.reportService.getProductLedgerReport({
      productId:       product.id,
      variantId:       this.selectedVariantId() ?? undefined,
      outletId:        this.outletId()    ?? undefined,
      warehouseId:     this.warehouseId() ?? undefined,
      dateFrom:        this.dateFrom() || undefined,
      dateTo:          this.dateTo()   || undefined,
      transactionType: this.transactionType() !== 'all' ? this.transactionType() : undefined,
      page:            this.page(),
      pageSize:        this.pageSize()
    }).subscribe({
      next: () => this.isLoading.set(false),
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
    this.transactionType.set('all');
    this.setThisMonth();
    this.page.set(1);
    if (this.selectedProduct()) this.loadReport();
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
    const product = this.selectedProduct();
    if (!product) return;
    const url = this.reportService.buildProductLedgerExportUrl({
      productId:       product.id,
      variantId:       this.selectedVariantId() ?? undefined,
      outletId:        this.outletId()    ?? undefined,
      warehouseId:     this.warehouseId() ?? undefined,
      dateFrom:        this.dateFrom() || undefined,
      dateTo:          this.dateTo()   || undefined,
      transactionType: this.transactionType() !== 'all' ? this.transactionType() : undefined,
      format
    });
    const a = document.createElement('a');
    a.href = url; a.download = ''; a.click();
  }

  // ── Formatting ────────────────────────────────────────────────────────────
  formatCurrency(v: number): string { return this.currencyService.format(v); }

  formatDate(s?: string): string {
    if (!s) return '—';
    return new Date(s).toLocaleDateString('en-GB', { year: 'numeric', month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' });
  }

  txTypeBadge(type: string): string {
    switch (type) {
      case 'grn':          return 'badge-success';
      case 'sale':         return 'badge-primary';
      case 'return':       return 'badge-warning';
      case 'adjustment':   return 'badge-neutral';
      case 'transfer_in':  return 'badge-info';
      case 'transfer_out': return 'badge-secondary';
      default:             return 'badge-neutral';
    }
  }
}
