import { Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, map, switchMap, tap } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { CategoryService } from '../../services/category.service';
import { ProductImageService } from '../../services/product-image.service';
import { ListStateService } from '../../services/list-state.service';
import { Product, ProductSearchRequest, describeProductTax } from '../../models/product.model';
import { ProductStockBadgeComponent } from '../shared/product-stock-badge.component';
import { Category } from '../../models/category.model';
import { ImageLightboxComponent } from '../product-form/image-lightbox.component';
import { AdvancedFilterDrawerComponent } from '../advanced-filter-drawer/advanced-filter-drawer.component';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { UI_KIT, UiBreadcrumb, UiDataTableComponent, UiTableColumn, UiTableQuery, UiTableSort } from '../shared/ui';

/**
 * Product list on the shared UI kit. Search, sorting and pagination are driven by the Data Table's
 * query events and always run on the server (`POST /products/search`); this component owns the API
 * call, request cancellation (`switchMap`), stale-response detection (`responseSequence`), the
 * category/status/advanced filters and the URL state. The table only presents.
 */
@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [FormsModule, RouterLink, ImageLightboxComponent, AdvancedFilterDrawerComponent, AppCurrencyPipe, ProductStockBadgeComponent, ...UI_KIT],
  templateUrl: './product-list.html',
  styleUrl: './product-list.css',
})
export class ProductList implements OnInit {
  readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly router = inject(Router);
  private readonly imageSvc = inject(ProductImageService);
  private readonly route = inject(ActivatedRoute);
  private readonly listState = inject(ListStateService);
  private readonly auth = inject(AuthService);
  private readonly alerts = inject(AlertService);
  private readonly destroyRef = inject(DestroyRef);

  readonly table = viewChild<UiDataTableComponent<Product>>(UiDataTableComponent);

  readonly canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));
  readonly describeTax = (product: Product) => describeProductTax(product.taxRate, product.taxClassification);
  readonly lightboxImage = signal<{ medium: string; original: string } | null>(null);

  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Products' }];

  /** Column keys of the sortable columns are the `sortBy` values the products API already accepts. */
  readonly columns: UiTableColumn[] = [
    { key: 'name', header: 'Name', sortable: true, type: 'custom' },
    { key: 'sku', header: 'SKU / Barcode', type: 'custom' },
    { key: 'productCode', header: 'Product Code', type: 'custom' },
    { key: 'category', header: 'Category', field: 'categoryName', sortable: true, type: 'custom' },
    { key: 'price', header: 'Price', field: 'basePrice', sortable: true, align: 'end', type: 'custom' },
    { key: 'variants', header: 'Variants', align: 'center', type: 'custom' },
    { key: 'stock', header: 'Stock', field: 'totalStock', align: 'center', type: 'custom' },
    {
      key: 'status',
      header: 'Status',
      align: 'center',
      type: 'status',
      statusMap: {
        active: { label: 'Active', variant: 'success' },
        draft: { label: 'Draft', variant: 'warning' },
        inactive: { label: 'Inactive', variant: 'neutral' }
      }
    }
  ];

  // Feature-owned filters (the table owns search, sort, page and page size)
  selectedCategoryId = signal<number | undefined>(undefined);
  readonly stockOutletId = signal<number | undefined>(undefined);
  readonly stockWarehouseId = signal<number | undefined>(undefined);
  selectedStatus = signal<string | undefined>('active');
  selectedHasVariants = signal<boolean | undefined>(undefined);
  minPrice = signal<number | undefined>(undefined);
  maxPrice = signal<number | undefined>(undefined);

  // Advanced filter drawer state
  isAdvancedFilterDrawerOpen = signal(false);
  draftHasVariants = signal<boolean | undefined>(undefined);
  draftMinPrice = signal<number | undefined>(undefined);
  draftMaxPrice = signal<number | undefined>(undefined);

  // Starting table state, restored from the URL
  initialSearch = '';
  initialPage = 1;
  initialPageSize = 10;
  defaultSort: UiTableSort = { columnKey: 'name', direction: 'asc' };

  categories = signal<Category[]>([]);

  // Server data for the table
  readonly rows = signal<Product[]>([]);
  readonly total = signal<number | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly responseSequence = signal<number | null>(null);

  // Delete confirmation
  readonly deleteOpen = signal(false);
  readonly deleteTarget = signal<Product | null>(null);
  readonly deleting = signal(false);

  readonly advancedFilterCount = computed(() => {
    let count = 0;
    if (this.selectedHasVariants() !== undefined) count += 1;
    if (this.minPrice() !== undefined) count += 1;
    if (this.maxPrice() !== undefined) count += 1;
    return count;
  });

  /** True when any feature filter narrows the list beyond the default (active products only). */
  readonly filtersNarrowList = computed(
    () => this.selectedCategoryId() !== undefined || this.selectedStatus() !== 'active' || this.advancedFilterCount() > 0
  );

  private readonly requests = new Subject<{ sequence: number; body: ProductSearchRequest }>();
  private lastQuery: UiTableQuery | null = null;
  private skipNextUrlSync = false;

  constructor() {
    this.requests
      .pipe(
        tap(() => this.loading.set(true)),
        // A newer query cancels the in-flight request, so a slow older response can never overwrite newer data.
        switchMap(({ sequence, body }) => this.productService.search(body).pipe(map(response => ({ sequence, response })))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ sequence, response }) => this.applyResponse(sequence, response));
  }

  ngOnInit(): void {
    this.loadCategories();
    this.restoreFromUrl();
  }

  resolveImage(path?: string | null): string {
    return this.imageSvc.resolveUrl(path);
  }

  openLightbox(p: Product): void {
    if (!p.primaryImageMedium) return;
    this.lightboxImage.set({
      medium: this.imageSvc.resolveUrl(p.primaryImageMedium),
      original: this.imageSvc.resolveUrl(p.primaryImageMedium),
    });
  }

  // ---- Table query pipeline ----

  onQueryChange(query: UiTableQuery): void {
    this.run(query);
  }

  /** Retry after a failed load: repeats the last query. */
  reload(): void {
    if (this.lastQuery) {
      this.load(this.lastQuery);
    }
  }

  private run(query: UiTableQuery): void {
    this.lastQuery = query;
    if (this.skipNextUrlSync) {
      this.skipNextUrlSync = false;
    } else {
      this.syncUrl(query);
    }
    this.load(query);
  }

  private load(query: UiTableQuery): void {
    this.requests.next({ sequence: query.sequence, body: this.buildRequest(query) });
  }

  private buildRequest(query: UiTableQuery): ProductSearchRequest {
    return {
      outletId: this.stockOutletId(),
      warehouseId: this.stockWarehouseId(),
      searchQuery: query.search || undefined,
      categoryId: this.selectedCategoryId(),
      status: this.selectedStatus(),
      hasVariants: this.selectedHasVariants(),
      minPrice: this.minPrice(),
      maxPrice: this.maxPrice(),
      pageNumber: query.page,
      pageSize: query.pageSize,
      sortBy: query.sort?.columnKey ?? 'name',
      sortOrder: query.sort?.direction ?? 'asc'
    };
  }

  private applyResponse(sequence: number, response: { data?: { products?: Product[]; totalCount?: number } }): void {
    // ProductService turns request failures into an empty result and records the message.
    const error = this.productService.error();
    if (error) {
      this.rows.set([]);
      this.total.set(null);
      this.loadError.set(error);
    } else {
      this.rows.set(response?.data?.products ?? []);
      this.total.set(response?.data?.totalCount ?? 0);
      this.loadError.set(null);
    }
    this.responseSequence.set(sequence);
    this.loading.set(false);
  }

  /** Any change that is not a table query (filters) restarts from page 1 through the table so its state stays in sync. */
  private restartFromFirstPage(): void {
    const table = this.table();
    if (table && table.pageIndex() !== 1) {
      table.goTo(1);
    } else if (this.lastQuery) {
      this.run({ ...this.lastQuery, page: 1 });
    }
  }

  // ---- URL state ----

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.stockOutletId.set(this.listState.optionalId(p, 'outletId'));
    this.stockWarehouseId.set(this.listState.optionalId(p, 'warehouseId'));
    this.initialSearch = this.listState.str(p, 'search');
    this.selectedCategoryId.set(this.listState.optionalId(p, 'categoryId'));
    const status = p['status'] as string | undefined;
    this.selectedStatus.set(status !== undefined ? status : 'active');
    this.selectedHasVariants.set(this.listState.boolOrUndef(p, 'hasVariants'));
    const minP = p['minPrice'] != null ? Number(p['minPrice']) : undefined;
    this.minPrice.set(!isNaN(minP as number) && minP !== undefined ? minP : undefined);
    const maxP = p['maxPrice'] != null ? Number(p['maxPrice']) : undefined;
    this.maxPrice.set(!isNaN(maxP as number) && maxP !== undefined ? maxP : undefined);
    this.initialPage = Math.max(1, this.listState.num(p, 'page', 1));
    this.initialPageSize = Math.max(1, this.listState.num(p, 'pageSize', 10));
    this.defaultSort = {
      columnKey: this.listState.str(p, 'sortBy', 'name'),
      direction: this.listState.str(p, 'sortOrder', 'asc') === 'desc' ? 'desc' : 'asc'
    };
    this.syncAdvancedDraftsFromApplied();
  }

  private syncUrl(query: UiTableQuery): void {
    this.listState.update(this.route, {
      outletId: this.stockOutletId(),
      warehouseId: this.stockWarehouseId(),
      search: query.search || undefined,
      categoryId: this.selectedCategoryId(),
      status: this.selectedStatus(),
      hasVariants: this.selectedHasVariants(),
      minPrice: this.minPrice(),
      maxPrice: this.maxPrice(),
      page: query.page,
      pageSize: query.pageSize,
      sortBy: query.sort?.columnKey ?? 'name',
      sortOrder: query.sort?.direction ?? 'asc',
    });
  }

  // ---- Filters ----

  onBasicFilterChange(): void {
    this.restartFromFirstPage();
  }

  openAdvancedFilters(): void {
    this.syncAdvancedDraftsFromApplied();
    this.isAdvancedFilterDrawerOpen.set(true);
  }

  closeAdvancedFilters(): void {
    this.isAdvancedFilterDrawerOpen.set(false);
    this.syncAdvancedDraftsFromApplied();
  }

  applyAdvancedFilters(): void {
    this.selectedHasVariants.set(this.draftHasVariants());
    this.minPrice.set(this.draftMinPrice());
    this.maxPrice.set(this.draftMaxPrice());
    this.restartFromFirstPage();
    this.isAdvancedFilterDrawerOpen.set(false);
  }

  clearAdvancedFilters(): void {
    this.draftHasVariants.set(undefined);
    this.draftMinPrice.set(undefined);
    this.draftMaxPrice.set(undefined);
    this.applyAdvancedFilters();
  }

  private syncAdvancedDraftsFromApplied(): void {
    this.draftHasVariants.set(this.selectedHasVariants());
    this.draftMinPrice.set(this.minPrice());
    this.draftMaxPrice.set(this.maxPrice());
  }

  loadCategories(): void {
    this.categoryService.getAllCategories().subscribe({
      next: (response: any) => {
        this.categories.set(response.data || []);
      }
    });
  }

  clearFilters(): void {
    this.selectedCategoryId.set(undefined);
    this.selectedStatus.set('active');
    this.selectedHasVariants.set(undefined);
    this.minPrice.set(undefined);
    this.maxPrice.set(undefined);
    this.syncAdvancedDraftsFromApplied();
    if (this.stockOutletId() || this.stockWarehouseId()) {
      this.listState.update(this.route, { outletId: this.stockOutletId(), warehouseId: this.stockWarehouseId() });
    } else {
      this.listState.clear(this.route);
    }
    // Same as before: Clear wipes the URL instead of rewriting it, whichever path reloads the list.
    this.skipNextUrlSync = true;

    const before = this.lastQuery?.sequence;
    this.table()?.clearSearch();
    if (this.lastQuery?.sequence === before) {
      this.restartFromFirstPage();
    }
  }

  // ---- Navigation and actions ----

  createProduct(): void {
    this.router.navigate(['/products/create'], { queryParamsHandling: 'preserve' });
  }

  editProduct(id: number): void {
    this.router.navigate(['/products/edit', id], { queryParamsHandling: 'preserve' });
  }

  viewProduct(id: number): void {
    this.router.navigate(['/products', id], { queryParamsHandling: 'preserve' });
  }

  requestDelete(product: Product): void {
    this.deleteTarget.set(product);
    this.deleteOpen.set(true);
  }

  confirmDelete(): void {
    const product = this.deleteTarget();
    if (!product || this.deleting()) return;

    this.deleting.set(true);
    this.productService.delete(product.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.deleteOpen.set(false);
        this.alerts.success('Product deleted successfully');
        this.reload();
      },
      error: (error) => {
        this.deleting.set(false);
        this.deleteOpen.set(false);
        // The failure is reported in the alert; do not replace the whole list with an error state.
        this.productService.clearError();
        this.alerts.error(error.error?.error || 'Failed to delete product');
      }
    });
  }

  parseAttributes(attributesJson?: string): any {
    if (!attributesJson) return {};
    try {
      return JSON.parse(attributesJson);
    } catch {
      return {};
    }
  }

  formatAttributes(attributesJson?: string): string {
    const attrs = this.parseAttributes(attributesJson);
    return Object.entries(attrs)
      .map(([key, value]) => `${key}: ${value}`)
      .join(', ');
  }
}
