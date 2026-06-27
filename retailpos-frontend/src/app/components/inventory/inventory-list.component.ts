import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { AuthService } from '../../services/auth.service';
import { InventoryService } from '../../services/inventory.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { Inventory, InventorySearchRequest } from '../../models/inventory.model';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { ListStateService } from '../../services/list-state.service';
import { Outlet } from '../../models/outlet.model';
import { Warehouse } from '../../models/warehouse.model';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { AdvancedFilterDrawerComponent } from '../advanced-filter-drawer/advanced-filter-drawer.component';
import { ProductService } from '../../services/product.service';
import { VariantSearchPickerComponent, VariantSearchItem } from '../variant-search-picker/variant-search-picker.component';
import { AuthorizedOutletsDto } from '../../models/report.model';

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppCurrencyPipe, AdvancedFilterDrawerComponent, VariantSearchPickerComponent],
  template: `
    <div class="container mx-auto px-4 py-3">
      <div class="card px-3 py-2 mb-3">
        <div class="flex flex-col lg:flex-row lg:items-center gap-2">
          <div class="shrink-0">
            <h1 class="text-base font-semibold text-gray-900 leading-tight">Inventory Management</h1>
            <p class="text-xs text-gray-500">Stock by location</p>
          </div>

          <div class="hidden lg:block w-px self-stretch bg-gray-200 mx-1"></div>

          <div class="flex flex-1 flex-col sm:flex-row items-stretch sm:items-center gap-2 min-w-0">
            <div class="relative flex-1 min-w-0">
              <i class="fas fa-search absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400 text-xs pointer-events-none"></i>
              <input
                type="text"
                class="input pl-8 h-8 text-sm"
                [(ngModel)]="filters.productSearch"
                (ngModelChange)="onProductSearchChange()"
                (keyup.enter)="searchInventory()"
                placeholder="Search by product name or product code"
              />
            </div>
          </div>

          <div class="hidden lg:block w-px self-stretch bg-gray-200 mx-1"></div>

          <div class="flex items-center gap-2 shrink-0">
            <button
              type="button"
              (click)="openAdvancedFilters()"
              class="btn btn-outline h-8 text-sm px-3 gap-1.5 whitespace-nowrap"
              [class.btn-active]="advancedFilterCount() > 0"
            >
              <i class="fas fa-sliders-h text-xs"></i>
              <span>Advanced Filters</span>
              @if (advancedFilterCount() > 0) {
                <span class="inline-flex items-center justify-center w-5 h-5 rounded-full bg-gray-900 text-white text-xs font-semibold leading-none">{{ advancedFilterCount() }}</span>
              }
            </button>
            <button
              type="button"
              (click)="searchInventory()"
              class="btn btn-primary h-8 text-sm px-3 gap-1.5 whitespace-nowrap"
            >
              <i class="fas fa-search text-xs"></i>
              <span>Search</span>
            </button>
            <button
              type="button"
              (click)="clearFilters()"
              class="btn btn-outline h-8 text-sm px-3 gap-1.5 whitespace-nowrap text-gray-600"
            >
              <i class="fas fa-times text-xs"></i>
              <span>Clear</span>
            </button>
          </div>
        </div>

        @if (filters.variantId) {
          <div class="mt-2 flex flex-wrap items-center gap-2 border-t border-gray-100 pt-2">
            <span class="text-xs font-medium text-gray-500">Active Variant</span>
            <span class="inline-flex items-center gap-2 rounded-full bg-indigo-50 px-2.5 py-1 text-xs text-indigo-700">
              <i class="fas fa-tag"></i>
              <span>{{ selectedVariantLabel() }}</span>
              <button
                type="button"
                class="text-indigo-600 hover:text-indigo-900"
                (click)="clearVariantFilter()"
                title="Clear variant filter"
              >
                <i class="fas fa-times"></i>
              </button>
            </span>
          </div>
        }
      </div>

      <!-- Loading State -->
      @if (isLoading()) {
        <div class="flex justify-center items-center p-8">
          <div class="animate-spin rounded-full h-10 w-10 border-b-2 border-gray-900"></div>
        </div>
      }

      <!-- Inventory Table -->
      @if (!isLoading() && inventories().length > 0) {
        <div class="bg-white rounded-lg shadow-sm overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Product</th>
                <th>Product Code</th>
                <th>Variant</th>
                <th>Location</th>
                <th>Quantity</th>
                <th>Status</th>
                @if (canViewCost()) {
                  <th>Cost Price</th>
                }
                <th>Retail Price</th>
                @if (canViewCost()) {
                  <th>Total Value</th>
                }
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (item of inventories(); track item.id) {
                <tr>
                  <td class="font-medium">{{ item.productName }}</td>
                  <td>
                    @if (item.productCode) {
                      <span class="font-mono text-xs font-semibold text-indigo-700 bg-indigo-50 px-2 py-0.5 rounded">{{ item.productCode }}</span>
                    } @else {
                      <span class="text-gray-400 text-xs">-</span>
                    }
                  </td>
                  <td>{{ item.variantName }}</td>
                  <td>
                    <div class="text-sm">
                      <div class="font-medium">{{ item.outletName || item.warehouseName }}</div>
                      <div class="text-gray-500">{{ item.locationType }}</div>
                    </div>
                  </td>
                  <td class="font-medium">{{ item.quantity }}</td>
                  <td>
                    @if (item.isOutOfStock) {
                      <span class="badge badge-danger">Out of Stock</span>
                    } @else if (item.isLowStock) {
                      <span class="badge badge-warning">Low Stock</span>
                    } @else if (item.isExpiringSoon) {
                      <span class="badge badge-warning">Expiring Soon</span>
                    } @else {
                      <span class="badge badge-success">In Stock</span>
                    }
                  </td>
                  @if (canViewCost()) {
                    <td>{{ item.costPrice | appCurrency }}</td>
                  }
                  <td>{{ item.retailPrice | appCurrency }}</td>
                  @if (canViewCost()) {
                    <td class="font-medium">{{ item.totalValue | appCurrency }}</td>
                  }
                  <td class="px-4 py-2.5 text-center">
                    <div class="flex items-center justify-center gap-1.5">
                      @if (item.productId) {
                        <a
                          [routerLink]="['/reports/stock-transactions']"
                          [queryParams]="{ productId: item.productId, variantId: item.productVariantId }"
                          class="text-gray-500 hover:text-gray-900"
                          title="View stock transactions"
                        >
                          <i class="fas fa-list-alt"></i>
                        </a>
                      }
                      @if (auth.hasPermission('barcode.view')) {
                        <a
                          [routerLink]="['/barcode-labels']"
                          [queryParams]="{ query: item.sku || item.barcode || item.productCode || item.productName, sourceModule: 'inventory', sourceReferenceType: 'variant', sourceReferenceId: item.productVariantId }"
                          class="text-indigo-600 hover:text-indigo-900"
                          title="Print barcode labels"
                        >
                          <i class="fas fa-barcode"></i>
                        </a>
                      }
                      @if (!item.productId && !auth.hasPermission('barcode.view')) {
                        <span class="text-gray-400 text-sm">-</span>
                      }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        <div class="mt-4 flex justify-between items-center">
          <div class="text-sm text-gray-600">
            Showing {{ inventories().length }} items
          </div>
          <div class="flex gap-1">
            <button
              (click)="changePage(currentPage() - 1)"
              [disabled]="currentPage() === 1"
              class="px-3 py-1.5 text-sm border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Previous
            </button>
            <button
              (click)="changePage(currentPage() + 1)"
              [disabled]="inventories().length < pageSize()"
              class="px-3 py-1.5 text-sm border rounded hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Next
            </button>
          </div>
        </div>
      }

      <!-- Empty State -->
      @if (!isLoading() && inventories().length === 0) {
        <div class="bg-white rounded-lg shadow-sm p-8 text-center">
          <p class="text-gray-500">No inventory items found</p>
        </div>
      }
    </div>

    @if (isAdvancedFilterDrawerOpen()) {
      <app-advanced-filter-drawer
        title="Advanced Filters"
        applyLabel="Apply Filters"
        clearLabel="Clear Filters"
        closeLabel="Close Drawer"
        (closed)="closeAdvancedFilters()"
        (clear)="clearAdvancedFilters()"
        (apply)="applyAdvancedFilters()"
      >
        <div class="grid grid-cols-1 gap-4">
          <div>
            <app-variant-search-picker
              [label]="'Search Variant'"
              [selectedVariant]="draftSelectedVariant()"
              [placeholder]="'Search and select variant'"
              (selected)="onDraftVariantSelected($event)"
              (cleared)="onDraftVariantCleared()"
            ></app-variant-search-picker>
          </div>

          <div>
            <label class="label">Location Type</label>
            <select
              class="select"
              [(ngModel)]="draftAdvancedFilters.locationType"
              (change)="onDraftLocationTypeChange()"
              [disabled]="isLocationFilterLocked()"
            >
              @if (!isLocationFilterLocked()) {
                <option value="">All Locations</option>
              }
              @if (outlets().length > 0) {
                <option value="outlet">Outlets</option>
              }
              @if (warehouses().length > 0) {
                <option value="warehouse">Warehouses</option>
              }
            </select>
          </div>

          <div>
            <label class="label">Specific Location</label>
            <select
              class="select"
              [(ngModel)]="draftAdvancedFilters.locationId"
              [disabled]="!draftAdvancedFilters.locationType || isLocationFilterLocked()"
            >
              @if (!isLocationFilterLocked()) {
                <option value="">All</option>
              }
              @if (draftAdvancedFilters.locationType === 'outlet') {
                @for (outlet of outlets(); track outlet.id) {
                  <option [value]="outlet.id">{{ outlet.name }}</option>
                }
              }
              @if (draftAdvancedFilters.locationType === 'warehouse') {
                @for (warehouse of warehouses(); track warehouse.id) {
                  <option [value]="warehouse.id">{{ warehouse.name }}</option>
                }
              }
            </select>
          </div>

          <div class="space-y-2">
            <label class="label mb-0">Stock Conditions</label>
            <label class="inline-flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" [(ngModel)]="draftAdvancedFilters.lowStockOnly" />
              <span>Low Stock Only</span>
            </label>
            <label class="inline-flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" [(ngModel)]="draftAdvancedFilters.outOfStockOnly" />
              <span>Out of Stock Only</span>
            </label>
            <label class="inline-flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" [(ngModel)]="draftAdvancedFilters.expiringSoon" />
              <span>Expiring Soon</span>
            </label>
          </div>
        </div>
      </app-advanced-filter-drawer>
    }
  `
})
export class InventoryListComponent implements OnInit, OnDestroy {
  auth = inject(AuthService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));

  inventories = signal<Inventory[]>([]);
  outlets = signal<Outlet[]>([]);
  warehouses = signal<Warehouse[]>([]);
  authorizedAccess = signal<AuthorizedOutletsDto | null>(null);
  isLoading = signal(false);
  currentPage = signal(1);
  pageSize = signal(25);

  isAdvancedFilterDrawerOpen = signal(false);

  filters = {
    productSearch: '',
    variantId: undefined as number | undefined,
    locationType: '',
    locationId: '',
    lowStockOnly: false,
    outOfStockOnly: false,
    expiringSoon: false
  };

  draftAdvancedFilters = {
    variantId: undefined as number | undefined,
    locationType: '',
    locationId: '',
    lowStockOnly: false,
    outOfStockOnly: false,
    expiringSoon: false
  };

  selectedVariant = signal<VariantSearchItem | null>(null);
  draftSelectedVariant = signal<VariantSearchItem | null>(null);

  private productSearchSubject = new Subject<void>();
  private productSearchSubscription?: Subscription;

  constructor(
    private inventoryService: InventoryService,
    private productService: ProductService,
    private userOutletAccess: UserOutletAccessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private route: ActivatedRoute,
    private listState: ListStateService,
  ) {}

  ngOnInit(): void {
    this.setupDebouncedProductSearch();
    this.restoreFromUrl();
    this.loadData();
  }

  ngOnDestroy(): void {
    this.productSearchSubscription?.unsubscribe();
  }

  private setupDebouncedProductSearch(): void {
    this.productSearchSubscription = this.productSearchSubject
      .pipe(debounceTime(500))
      .subscribe(() => {
        this.searchInventory();
      });
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.filters.productSearch = this.listState.str(p, 'productSearch');
    this.filters.variantId = this.listState.optionalId(p, 'variantId');
    this.filters.locationType = this.listState.str(p, 'locationType');
    this.filters.locationId = this.listState.str(p, 'locationId');
    this.filters.lowStockOnly = p['lowStockOnly'] === 'true';
    this.filters.outOfStockOnly = p['outOfStockOnly'] === 'true';
    this.filters.expiringSoon = p['expiringSoon'] === 'true';
    this.currentPage.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 25));
    this.syncAdvancedDraftsFromApplied();

    if (this.filters.variantId) {
      this.prefillSelectedVariant(this.filters.variantId);
    }
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      productSearch: this.filters.productSearch || undefined,
      variantId: this.filters.variantId,
      locationType: this.filters.locationType || undefined,
      locationId: this.filters.locationId || undefined,
      lowStockOnly: this.filters.lowStockOnly || undefined,
      outOfStockOnly: this.filters.outOfStockOnly || undefined,
      expiringSoon: this.filters.expiringSoon || undefined,
      page: this.currentPage(),
      pageSize: this.pageSize(),
    });
  }

  loadData(): void {
    this.loadAuthorizedLocations();
  }

  loadAuthorizedLocations(): void {
    this.userOutletAccess.load().subscribe({
      next: (response: any) => {
        const auth = (response?.data ?? null) as AuthorizedOutletsDto | null;
        this.authorizedAccess.set(auth);
        this.outlets.set((auth?.destinationOutlets as Outlet[] | undefined) || (auth?.outlets as Outlet[] | undefined) || []);
        this.warehouses.set((auth?.destinationWarehouses as Warehouse[] | undefined) || (auth?.warehouses as Warehouse[] | undefined) || []);
        this.applyDefaultLocationFilter(auth);
        this.searchInventory();
      },
      error: (err: any) => {
        console.error('Failed to load authorized locations', err);
        this.searchInventory();
      }
    });
  }

  isLocationFilterLocked(): boolean {
    return false;
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
    this.filters.variantId = this.draftAdvancedFilters.variantId;
    this.filters.locationType = this.draftAdvancedFilters.locationType;
    this.filters.locationId = this.draftAdvancedFilters.locationId;
    this.filters.lowStockOnly = this.draftAdvancedFilters.lowStockOnly;
    this.filters.outOfStockOnly = this.draftAdvancedFilters.outOfStockOnly;
    this.filters.expiringSoon = this.draftAdvancedFilters.expiringSoon;
    this.selectedVariant.set(this.draftSelectedVariant());

    this.currentPage.set(1);
    this.searchInventory();
    this.isAdvancedFilterDrawerOpen.set(false);
  }

  clearAdvancedFilters(): void {
    this.draftAdvancedFilters = {
      variantId: undefined,
      locationType: '',
      locationId: '',
      lowStockOnly: false,
      outOfStockOnly: false,
      expiringSoon: false
    };
    this.draftSelectedVariant.set(null);
    this.applyAdvancedFilters();
  }

  advancedFilterCount(): number {
    let count = 0;
    if (this.filters.variantId) count += 1;
    if (this.filters.locationType) count += 1;
    if (this.filters.locationId) count += 1;
    if (this.filters.lowStockOnly) count += 1;
    if (this.filters.outOfStockOnly) count += 1;
    if (this.filters.expiringSoon) count += 1;
    return count;
  }

  onDraftLocationTypeChange(): void {
    this.draftAdvancedFilters.locationId = '';
  }

  onDraftVariantSelected(variant: VariantSearchItem): void {
    this.draftSelectedVariant.set(variant);
    this.draftAdvancedFilters.variantId = variant.id;
  }

  onDraftVariantCleared(): void {
    this.draftSelectedVariant.set(null);
    this.draftAdvancedFilters.variantId = undefined;
  }

  onProductSearchChange(): void {
    this.currentPage.set(1);
    this.productSearchSubject.next();
  }

  selectedVariantLabel(): string {
    const variant = this.selectedVariant();
    if (!variant) {
      return this.filters.variantId ? `Variant #${this.filters.variantId}` : 'Variant selected';
    }

    if (variant.attributes) {
      return `${variant.productName} (${variant.attributes}) - ${variant.name}`;
    }

    return `${variant.productName} - ${variant.name}`;
  }

  clearVariantFilter(): void {
    this.filters.variantId = undefined;
    this.selectedVariant.set(null);
    this.syncAdvancedDraftsFromApplied();
    this.currentPage.set(1);
    this.searchInventory();
  }

  private syncAdvancedDraftsFromApplied(): void {
    this.draftAdvancedFilters = {
      variantId: this.filters.variantId,
      locationType: this.filters.locationType,
      locationId: this.filters.locationId,
      lowStockOnly: this.filters.lowStockOnly,
      outOfStockOnly: this.filters.outOfStockOnly,
      expiringSoon: this.filters.expiringSoon
    };
    this.draftSelectedVariant.set(this.selectedVariant());
  }

  searchInventory(): void {
    this.syncUrl();
    this.isLoading.set(true);

    const request: InventorySearchRequest = {
      productSearch: this.filters.productSearch || undefined,
      variantId: this.filters.variantId,
      variantSearch: this.selectedVariant()?.name || undefined,
      outletId: this.filters.locationType === 'outlet' && this.filters.locationId ? +this.filters.locationId : undefined,
      warehouseId: this.filters.locationType === 'warehouse' && this.filters.locationId ? +this.filters.locationId : undefined,
      lowStockOnly: this.filters.lowStockOnly || undefined,
      outOfStockOnly: this.filters.outOfStockOnly || undefined,
      expiringSoon: this.filters.expiringSoon || undefined,
      expiringWithinDays: this.filters.expiringSoon ? 30 : undefined,
      page: this.currentPage(),
      pageSize: this.pageSize()
    };

    this.inventoryService.search(request).subscribe({
      next: (response) => {
        this.inventories.set(response.data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  clearFilters(): void {
    this.filters = {
      productSearch: '',
      variantId: undefined,
      locationType: '',
      locationId: '',
      lowStockOnly: false,
      outOfStockOnly: false,
      expiringSoon: false
    };
    this.selectedVariant.set(null);
    this.syncAdvancedDraftsFromApplied();

    const auth = this.authorizedAccess();
    if (auth) {
      this.applyDefaultLocationFilter(auth, true);
    }

    this.currentPage.set(1);
    this.listState.clear(this.route);
    this.searchInventory();
  }

  changePage(page: number): void {
    if (page < 1) return;
    this.currentPage.set(page);
    this.searchInventory();
  }

  private enforceLockedScopeBeforeSearch(): void {
    // Inventory check is not restricted by assigned locations.
  }

  private prefillSelectedVariant(variantId: number): void {
    this.productService.searchVariants(String(variantId), 1, 50).subscribe({
      next: (response) => {
        const match = (response?.data || []).find((v: any) => Number(v.id) === Number(variantId));
        if (!match) return;

        const variant: VariantSearchItem = {
          id: match.id,
          productCode: match.productCode,
          sku: match.sku,
          barcode: match.barcode,
          productName: match.productName,
          name: match.name,
          attributes: match.attributes,
          stockQuantity: match.stockQuantity,
          primaryImageThumb: match.primaryImageThumb,
        };

        this.selectedVariant.set(variant);
        this.draftSelectedVariant.set(variant);
      }
    });
  }

  private applyDefaultLocationFilter(auth: AuthorizedOutletsDto | null, force = false): void {
    if (!auth) return;

    const selectedType = this.filters.locationType;
    const selectedId = Number(this.filters.locationId);
    const hasSelectedLocation = !!selectedType && !!this.filters.locationId;
    const selectedIsValid = hasSelectedLocation && this.isAuthorizedLocation(auth, selectedType, selectedId);

    if (!force && selectedIsValid && !this.isLocationFilterLocked()) {
      this.syncAdvancedDraftsFromApplied();
      return;
    }

    const fallback = this.resolveDefaultLocation(auth);
    if (!fallback) {
      this.syncAdvancedDraftsFromApplied();
      return;
    }

    this.filters.locationType = fallback.type;
    this.filters.locationId = String(fallback.id);
    this.syncAdvancedDraftsFromApplied();
  }

  private resolveDefaultLocation(auth: AuthorizedOutletsDto | null): { type: 'outlet' | 'warehouse'; id: number } | null {
    if (!auth) return null;

    if (auth.defaultLocationId && auth.defaultLocationType) {
      if (this.isAuthorizedLocation(auth, auth.defaultLocationType, auth.defaultLocationId)) {
        return { type: auth.defaultLocationType, id: auth.defaultLocationId };
      }
    }

    if (auth.defaultOutletId) {
      const isAuthorizedDefaultOutlet = auth.outlets?.some(o => o.id === auth.defaultOutletId) ?? false;
      if (isAuthorizedDefaultOutlet) {
        return { type: 'outlet', id: auth.defaultOutletId };
      }
    }

    if (auth.outlets?.length) {
      return { type: 'outlet', id: auth.outlets[0].id };
    }

    if (auth.warehouses?.length) {
      return { type: 'warehouse', id: auth.warehouses[0].id };
    }

    return null;
  }

  private isAuthorizedLocation(auth: AuthorizedOutletsDto, locationType: string, locationId: number): boolean {
    if (!locationId || !locationType) return false;

    if (locationType === 'outlet') {
      return (auth.outlets ?? []).some(o => o.id === locationId);
    }

    if (locationType === 'warehouse') {
      return (auth.warehouses ?? []).some(w => w.id === locationId);
    }

    return false;
  }
}
