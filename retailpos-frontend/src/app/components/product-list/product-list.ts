import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { ProductService } from '../../services/product.service';
import { CategoryService } from '../../services/category.service';
import { ProductImageService } from '../../services/product-image.service';
import { Product, ProductSearchRequest } from '../../models/product.model';
import { Category } from '../../models/category.model';
import { ImageLightboxComponent } from '../product-form/image-lightbox.component';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ImageLightboxComponent, AppCurrencyPipe],
  templateUrl: './product-list.html',
  styleUrl: './product-list.css',
})
export class ProductList implements OnInit, OnDestroy {
  private imageSvc = inject(ProductImageService);
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
  selectedIsActive = signal<boolean | undefined>(true);
  selectedHasVariants = signal<boolean | undefined>(undefined);
  minPrice = signal<number | undefined>(undefined);
  maxPrice = signal<number | undefined>(undefined);

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
    this.search();
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
        this.performSearch();
      });
  }

  onSearchChange(): void {
    this.searchSubject.next();
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
      isActive: this.selectedIsActive(),
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
    this.performSearch();
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.selectedCategoryId.set(undefined);
    this.selectedIsActive.set(true);
    this.selectedHasVariants.set(undefined);
    this.minPrice.set(undefined);
    this.maxPrice.set(undefined);
    this.pageNumber.set(1);
    this.search();
  }

  createProduct(): void {
    this.router.navigate(['/products/create']);
  }

  editProduct(id: number): void {
    this.router.navigate(['/products/edit', id]);
  }

  viewProduct(id: number): void {
    this.router.navigate(['/products', id]);
  }

  confirmDelete(product: Product, event: Event): void {
    event.stopPropagation();
    
    if (confirm(`Are you sure you want to delete product "${product.name}"?`)) {
      this.productService.delete(product.id).subscribe({
        next: () => {
          alert('Product deleted successfully');
          this.search();
        },
        error: (error) => {
          alert(error.error?.error || 'Failed to delete product');
        }
      });
    }
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.search();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.pageNumber.set(1);
    this.search();
  }

  sort(column: string): void {
    if (this.sortBy() === column) {
      this.sortOrder.set(this.sortOrder() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortBy.set(column);
      this.sortOrder.set('asc');
    }
    this.search();
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
