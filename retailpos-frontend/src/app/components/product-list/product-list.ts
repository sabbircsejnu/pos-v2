import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { ProductService } from '../../services/product.service';
import { CategoryService } from '../../services/category.service';
import { ProductImageService } from '../../services/product-image.service';
import { ListStateService } from '../../services/list-state.service';
import { Product, ProductSearchRequest } from '../../models/product.model';
import { Category } from '../../models/category.model';
import { ImageLightboxComponent } from '../product-form/image-lightbox.component';
import { AdvancedFilterDrawerComponent } from '../advanced-filter-drawer/advanced-filter-drawer.component';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ImageLightboxComponent, AdvancedFilterDrawerComponent, AppCurrencyPipe],
  templateUrl: './product-list.html',
  styleUrl: './product-list.css',
})
export class ProductList implements OnInit, OnDestroy {
  private imageSvc = inject(ProductImageService);
  private route = inject(ActivatedRoute);
  private listState = inject(ListStateService);
  private auth = inject(AuthService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));
  lightboxImage = signal<{ medium: string; original: string } | null>(null);

  resolveImage(path?: string | null): string {
    return this.imageSvc.resolveUrl(path);
  }

  openLightbox(p: Product) {
    if (!p.primaryImageMedium) return;
    this.lightboxImage.set({
      medium: this.imageSvc.resolveUrl(p.primaryImageMedium),
      original: this.imageSvc.resolveUrl(p.primaryImageMedium),
    });
  }

  // Expose Math to template
  Math = Math;
  
  // Search filters
  searchQuery = signal('');
  selectedCategoryId = signal<number | undefined>(undefined);
  selectedStatus = signal<string | undefined>('active');
  selectedHasVariants = signal<boolean | undefined>(undefined);
  minPrice = signal<number | undefined>(undefined);
  maxPrice = signal<number | undefined>(undefined);

  // Advanced filter drawer state
  isAdvancedFilterDrawerOpen = signal(false);
  draftHasVariants = signal<boolean | undefined>(undefined);
  draftMinPrice = signal<number | undefined>(undefined);
  draftMaxPrice = signal<number | undefined>(undefined);

  // Pagination
  pageNumber = signal(1);
  pageSize = signal(10);
  sortBy = signal('name');
  sortOrder = signal('asc');

  // Categories for filter
  categories = signal<Category[]>([]);

  // Debounced search
  private searchSubject = new Subject<void>();
  private searchSubscription?: Subscription;

  constructor(
    public productService: ProductService,
    private categoryService: CategoryService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadCategories();
    this.setupDebouncedSearch();
    this.restoreFromUrl();
    this.performSearch();
  }

  private restoreFromUrl(): void {
    const p = this.route.snapshot.queryParams;
    this.searchQuery.set(this.listState.str(p, 'search'));
    this.selectedCategoryId.set(this.listState.optionalId(p, 'categoryId'));
    const status = p['status'] as string | undefined;
    this.selectedStatus.set(status !== undefined ? status : 'active');
    this.selectedHasVariants.set(this.listState.boolOrUndef(p, 'hasVariants'));
    const minP = p['minPrice'] != null ? Number(p['minPrice']) : undefined;
    this.minPrice.set(!isNaN(minP as number) && minP !== undefined ? minP : undefined);
    const maxP = p['maxPrice'] != null ? Number(p['maxPrice']) : undefined;
    this.maxPrice.set(!isNaN(maxP as number) && maxP !== undefined ? maxP : undefined);
    this.pageNumber.set(this.listState.num(p, 'page', 1));
    this.pageSize.set(this.listState.num(p, 'pageSize', 10));
    this.sortBy.set(this.listState.str(p, 'sortBy', 'name'));
    this.sortOrder.set(this.listState.str(p, 'sortOrder', 'asc'));
    this.syncAdvancedDraftsFromApplied();
  }

  private syncUrl(): void {
    this.listState.update(this.route, {
      search: this.searchQuery() || undefined,
      categoryId: this.selectedCategoryId(),
      status: this.selectedStatus(),
      hasVariants: this.selectedHasVariants(),
      minPrice: this.minPrice(),
      maxPrice: this.maxPrice(),
      page: this.pageNumber(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      sortOrder: this.sortOrder(),
    });
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  private setupDebouncedSearch(): void {
    this.searchSubscription = this.searchSubject
      .pipe(
        debounceTime(500)
      )
      .subscribe(() => {
        this.syncUrl();
        this.performSearch();
      });
  }

  onSearchChange(): void {
    this.pageNumber.set(1);
    this.searchSubject.next();
  }

  onBasicFilterChange(): void {
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
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
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
    this.isAdvancedFilterDrawerOpen.set(false);
  }

  clearAdvancedFilters(): void {
    this.draftHasVariants.set(undefined);
    this.draftMinPrice.set(undefined);
    this.draftMaxPrice.set(undefined);
    this.applyAdvancedFilters();
  }

  advancedFilterCount(): number {
    let count = 0;
    if (this.selectedHasVariants() !== undefined) count += 1;
    if (this.minPrice() !== undefined) count += 1;
    if (this.maxPrice() !== undefined) count += 1;
    return count;
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

  private performSearch(): void {
    const searchRequest: ProductSearchRequest = {
      searchQuery: this.searchQuery() || undefined,
      categoryId: this.selectedCategoryId(),
      status: this.selectedStatus(),
      hasVariants: this.selectedHasVariants(),
      minPrice: this.minPrice(),
      maxPrice: this.maxPrice(),
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      sortOrder: this.sortOrder()
    };

    this.productService.search(searchRequest).subscribe();
  }

  search(): void {
    this.onBasicFilterChange();
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.selectedCategoryId.set(undefined);
    this.selectedStatus.set('active');
    this.selectedHasVariants.set(undefined);
    this.minPrice.set(undefined);
    this.maxPrice.set(undefined);
    this.syncAdvancedDraftsFromApplied();
    this.pageNumber.set(1);
    this.listState.clear(this.route);
    this.performSearch();
  }

  createProduct(): void {
    this.router.navigate(['/products/create'], { queryParamsHandling: 'preserve' });
  }

  editProduct(id: number): void {
    this.router.navigate(['/products/edit', id], { queryParamsHandling: 'preserve' });
  }

  viewProduct(id: number): void {
    this.router.navigate(['/products', id], { queryParamsHandling: 'preserve' });
  }

  confirmDelete(product: Product, event: Event): void {
    event.stopPropagation();
    
    if (confirm(`Are you sure you want to delete product "${product.name}"?`)) {
      this.productService.delete(product.id).subscribe({
        next: () => {
          alert('Product deleted successfully');
          this.performSearch();
        },
        error: (error) => {
          alert(error.error?.error || 'Failed to delete product');
        }
      });
    }
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.syncUrl();
    this.performSearch();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.syncUrl();
    this.performSearch();
  }

  sort(column: string): void {
    if (this.sortBy() === column) {
      this.sortOrder.set(this.sortOrder() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(column);
      this.sortOrder.set('asc');
    }
    this.syncUrl();
    this.performSearch();
  }

  getSortIcon(column: string): string {
    if (this.sortBy() !== column) return 'fas fa-sort';
    return this.sortOrder() === 'asc' ? 'fas fa-sort-up' : 'fas fa-sort-down';
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
