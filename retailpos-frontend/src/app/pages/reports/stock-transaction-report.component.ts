import { Component, OnInit, OnDestroy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, debounceTime, Subscription } from 'rxjs';
import { ReportService } from '../../services/report.service';
import { ProductService } from '../../services/product.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

interface VariantOption {
  id: number;
  productId: number;
  productName: string;
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string;
}

interface ProductOption {
  id: number;
  name: string;
  sku?: string;
  barcode?: string;
}

type ScopeOption =
  | { kind: 'product'; product: ProductOption }
  | { kind: 'variant'; variant: VariantOption };

interface LocationOption {
  id: number;
  name: string;
  type: 'outlet' | 'warehouse';
}

@Component({
  selector: 'app-stock-transaction-report',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './stock-transaction-report.component.html',
  styleUrls: ['./stock-transaction-report.component.css']
})
export class StockTransactionReportComponent implements OnInit, OnDestroy {
  // Filter state
  scopeQuery = signal('');
  scopeResults = signal<ScopeOption[]>([]);
  scopeSearching = signal(false);
  scopeDropdownOpen = signal(false);
  selectedScope = signal<ScopeOption | null>(null);

  locations = signal<LocationOption[]>([]);
  selectedLocationKey = signal<string>(''); // "outlet:5" | "warehouse:2" | ""
  isBusinessOwner = signal(false);
  defaultLocationKey = signal<string>('');

  dateFrom = signal('');
  dateTo = signal('');
  isLoading = signal(false);

  // Report title
  reportTitle = computed(() => {
    const r = this.reportService.stockTransactionReport();
    if (!r) return 'Stock Transaction Report';
    if (r.level === 'variant') {
      const parts = [r.productName];
      if (r.variantCode) parts.push(r.variantCode);
      const attrs = this.formatAttributes(r.variantAttributes);
      if (attrs) parts.push(attrs);
      return `Stock Transaction Report for Variant: ${parts.join(' | ')}`;
    }
    const code = r.productCode ? r.productCode : '—';
    return `Stock Transaction Report for Product: ${r.productName} | ${code}`;
  });

  private scopeSearch$ = new Subject<string>();
  private subs: Subscription[] = [];

  constructor(
    public reportService: ReportService,
    private productService: ProductService,
    private userOutletAccess: UserOutletAccessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.setThisMonth();

    this.subs.push(
      this.scopeSearch$.pipe(debounceTime(300)).subscribe(q => this.runScopeSearch(q))
    );

    // Load authorized outlets first, then handle deep-link query params.
    this.userOutletAccess.load().subscribe({
      next: () => {
        this.applyAuthorizedLocations();

        this.subs.push(
          this.route.queryParamMap.subscribe(params => {
            const productIdRaw = params.get('productId');
            const variantIdRaw = params.get('variantId');
            if (!productIdRaw) return;

            const productId = Number(productIdRaw);
            const variantId = variantIdRaw ? Number(variantIdRaw) : undefined;
            this.bootstrapFromQuery(productId, variantId);
          })
        );
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.reportService.stockTransactionReport.set(null);
  }

  // ── Filters ────────────────────────────────────────────────────────

  private fmt(d: Date): string {
    return d.toISOString().substring(0, 10);
  }

  setThisMonth(): void {
    const now = new Date();
    const start = new Date(now.getFullYear(), now.getMonth(), 1);
    this.dateFrom.set(this.fmt(start));
    this.dateTo.set(this.fmt(new Date()));
  }

  /**
   * Populate the location dropdown from the authorization payload.
   * - Single authorized outlet → auto-select.
   * - Multiple → default to the user's home outlet.
   * - BusinessOwner → all outlets/warehouses, default = active outlet (or "All").
   */
  private applyAuthorizedLocations(): void {
    const auth = this.userOutletAccess.authorized();
    if (!auth) return;

    const merged: LocationOption[] = [
      ...auth.outlets.map(o => ({ id: o.id, name: o.name, type: 'outlet' as const })),
      ...auth.warehouses.map(w => ({ id: w.id, name: w.name, type: 'warehouse' as const }))
    ];
    this.locations.set(merged);
    this.isBusinessOwner.set(auth.isBusinessOwner);

    let defaultKey = '';
    if (merged.length === 1) {
      defaultKey = `${merged[0].type}:${merged[0].id}`;
    } else if (auth.defaultOutletId) {
      const match = merged.find(l => l.type === 'outlet' && l.id === auth.defaultOutletId);
      if (match) defaultKey = `${match.type}:${match.id}`;
    }

    this.defaultLocationKey.set(defaultKey);
    if (!this.selectedLocationKey()) {
      this.selectedLocationKey.set(defaultKey);
    }
  }

  // ── Searchable dropdown (product OR variant) ──────────────────────

  onScopeInput(value: string): void {
    this.scopeQuery.set(value);
    this.scopeDropdownOpen.set(true);
    if (this.selectedScope() && this.formatScopeLabel(this.selectedScope()!) !== value) {
      this.selectedScope.set(null);
    }
    this.scopeSearch$.next(value);
  }

  onScopeFocus(): void {
    this.scopeDropdownOpen.set(true);
    if (this.scopeQuery().length >= 2 && this.scopeResults().length === 0) {
      this.runScopeSearch(this.scopeQuery());
    }
  }

  onScopeBlur(): void {
    setTimeout(() => this.scopeDropdownOpen.set(false), 200);
  }

  /**
   * The variant search endpoint returns rows tagged with `productId` / `productName`,
   * which lets us derive both:
   *   - one "main product" entry per distinct product (matches name/code), and
   *   - per-variant entries (matches sku/barcode/variant name).
   */
  private runScopeSearch(query: string): void {
    if (!query || query.length < 2) {
      this.scopeResults.set([]);
      return;
    }
    this.scopeSearching.set(true);
    this.productService.searchVariants(query, 1, 30).subscribe({
      next: (res) => {
        const variants: VariantOption[] = res.data || [];

        // Aggregate distinct products from the result set.
        const productMap = new Map<number, ProductOption>();
        for (const v of variants) {
          if (!productMap.has(v.productId)) {
            productMap.set(v.productId, {
              id: v.productId,
              name: v.productName,
              sku: v.sku,
              barcode: v.barcode
            });
          }
        }

        const productOptions: ScopeOption[] = Array.from(productMap.values())
          .map(p => ({ kind: 'product' as const, product: p }));
        const variantOptions: ScopeOption[] = variants
          .map(v => ({ kind: 'variant' as const, variant: v }));

        this.scopeResults.set([...productOptions, ...variantOptions]);
        this.scopeSearching.set(false);
      },
      error: (err) => {
        this.scopeSearching.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  selectScope(option: ScopeOption): void {
    this.selectedScope.set(option);
    this.scopeQuery.set(this.formatScopeLabel(option));
    this.scopeDropdownOpen.set(false);
    this.scopeResults.set([]);
  }

  formatScopeLabel(option: ScopeOption): string {
    if (option.kind === 'product') {
      const parts = [`[Product] ${option.product.name}`];
      if (option.product.sku) parts.push(option.product.sku);
      return parts.join(' · ');
    }
    const v = option.variant;
    const parts = [v.productName];
    if (v.name && v.name !== v.productName) parts.push(v.name);
    if (v.sku) parts.push(`[${v.sku}]`);
    return parts.join(' · ');
  }

  clearScope(): void {
    this.selectedScope.set(null);
    this.scopeQuery.set('');
    this.scopeResults.set([]);
  }

  // ── Deep-link bootstrap ───────────────────────────────────────────

  private bootstrapFromQuery(productId: number, variantId?: number): void {
    let outletId: number | undefined;
    let locationType: string | undefined;
    const key = this.selectedLocationKey();
    if (key) {
      const [type, idStr] = key.split(':');
      locationType = type;
      outletId = Number(idStr);
    }

    this.isLoading.set(true);
    this.reportService.getStockTransactionReport(
      productId,
      variantId,
      outletId,
      locationType,
      this.dateFrom() || undefined,
      this.dateTo() || undefined
    ).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        const r = res?.data;
        if (!r) return;
        if (r.level === 'variant' && r.variantId) {
          this.selectScope({
            kind: 'variant',
            variant: {
              id: r.variantId,
              productId: r.productId,
              productName: r.productName,
              name: r.variantName ?? '',
              sku: r.variantCode,
              attributes: r.variantAttributes
            }
          });
        } else {
          this.selectScope({
            kind: 'product',
            product: { id: r.productId, name: r.productName, sku: r.productCode }
          });
        }
      },
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  // ── Run report ────────────────────────────────────────────────────

  runReport(): void {
    const scope = this.selectedScope();
    if (!scope) {
      this.alertService.error('Please select a product or variant');
      return;
    }

    let productId: number;
    let variantId: number | undefined;
    if (scope.kind === 'product') {
      productId = scope.product.id;
    } else {
      productId = scope.variant.productId;
      variantId = scope.variant.id;
    }

    let outletId: number | undefined;
    let locationType: string | undefined;
    const key = this.selectedLocationKey();
    if (key) {
      const [type, idStr] = key.split(':');
      locationType = type;
      outletId = Number(idStr);
    }

    this.isLoading.set(true);
    this.reportService.getStockTransactionReport(
      productId,
      variantId,
      outletId,
      locationType,
      this.dateFrom() || undefined,
      this.dateTo() || undefined
    ).subscribe({
      next: () => this.isLoading.set(false),
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  // ── Display helpers ───────────────────────────────────────────────

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return d.toLocaleString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }

  formatAttributes(json?: string): string {
    if (!json) return '';
    try {
      const obj = JSON.parse(json);
      if (!obj || typeof obj !== 'object') return '';
      const entries = Object.entries(obj).filter(([, v]) => v !== null && v !== undefined && v !== '');
      if (entries.length === 0) return '';
      return entries.map(([k, v]) => `${k}: ${v}`).join(', ');
    } catch {
      return '';
    }
  }

  transactionTypeLabel(type: string): string {
    switch (type) {
      case 'grn':           return 'Purchase Receive';
      case 'sale':          return 'Sale';
      case 'return':        return 'Sales Return';
      case 'exchange':      return 'Exchange';
      case 'adjustment':    return 'Stock Adjustment';
      case 'transfer_in':   return 'Transfer In';
      case 'transfer_out':  return 'Transfer Out';
      default:              return type;
    }
  }

  transactionTypeBadgeClass(type: string): string {
    switch (type) {
      case 'grn':
      case 'transfer_in':
      case 'return':
        return 'bg-green-100 text-green-800';
      case 'sale':
      case 'transfer_out':
        return 'bg-red-100 text-red-800';
      case 'adjustment':
        return 'bg-yellow-100 text-yellow-800';
      case 'exchange':
        return 'bg-blue-100 text-blue-800';
      default:
        return 'bg-gray-100 text-gray-700';
    }
  }

  referenceLink(type: string, id: number): string | null {
    switch (type) {
      case 'sale':              return `/sales/${id}`;
      case 'grn':               return `/grn/${id}`;
      case 'stock_transfer':    return `/stock-transfers/${id}`;
      case 'stock_adjustment':  return `/stock-adjustments`;
      default:                  return null;
    }
  }
}
