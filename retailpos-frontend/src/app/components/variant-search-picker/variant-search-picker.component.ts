import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnDestroy, Output, inject, signal } from '@angular/core';
import { Subject, debounceTime } from 'rxjs';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';

export interface VariantSearchItem {
  id: number;
  productCode?: string;
  sku?: string;
  barcode?: string;
  productName: string;
  name: string;
  attributes?: string;
  stockQuantity?: number | null;
  primaryImageThumb?: string;
}

@Component({
  selector: 'app-variant-search-picker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './variant-search-picker.component.html',
})
export class VariantSearchPickerComponent implements OnDestroy {
  private productService = inject(ProductService);
  private imageService = inject(ProductImageService);
  private searchSubject = new Subject<string>();

  @Input() disabled = false;
  @Input() label = 'Search Product';
  @Input() selectedVariant: VariantSearchItem | null = null;
  @Input() placeholder = 'Search by main code, SKU, product name, or variant attributes...';
  @Input() locationId: number | null = null;
  @Input() locationType: string | null = null;
  @Input() existingVariantIds: number[] = [];
  @Input() hideExisting = false;

  @Output() selected = new EventEmitter<VariantSearchItem>();
  @Output() cleared = new EventEmitter<void>();

  query = signal('');
  results = signal<VariantSearchItem[]>([]);
  searching = signal(false);
  inputFocused = signal(false);
  dropdownPosition = signal<{ top: number; left: number; width: number } | null>(null);
  currentSearchPage = signal(1);
  hasMore = signal(false);

  constructor() {
    this.searchSubject.pipe(debounceTime(300)).subscribe(query => this.searchVariants(query, 1));
  }

  ngOnDestroy(): void {
    this.searchSubject.complete();
  }

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
  }

  getDisplayText(variant: VariantSearchItem | null): string {
    if (!variant) {
      return '';
    }

    if (variant.attributes) {
      return `${variant.productName} (${variant.attributes}) - ${variant.name}`;
    }

    return `${variant.productName} - ${variant.name}`;
  }

  getInputValue(): string {
    if (this.inputFocused()) {
      return this.query();
    }

    return this.getDisplayText(this.selectedVariant);
  }

  onInput(value: string): void {
    this.query.set(value);

    if (this.selectedVariant && value !== this.getDisplayText(this.selectedVariant)) {
      this.cleared.emit();
    }

    this.searchSubject.next(value);
  }

  isExistingVariant(variantId: number): boolean {
    return this.existingVariantIds.includes(variantId);
  }

  onFocus(): void {
    this.inputFocused.set(true);

    setTimeout(() => {
      const inputElement = document.querySelector('#variant-input-shared-picker') as HTMLElement | null;
      if (!inputElement) {
        return;
      }

      const rect = inputElement.getBoundingClientRect();
      this.dropdownPosition.set({
        top: rect.bottom + window.scrollY + 4,
        left: rect.left + window.scrollX,
        width: inputElement.offsetWidth,
      });
    }, 0);
  }

  onBlur(): void {
    setTimeout(() => {
      this.inputFocused.set(false);
      this.dropdownPosition.set(null);

      if (!this.selectedVariant) {
        this.query.set('');
        this.results.set([]);
      }
    }, 200);
  }

  onScroll(event: Event): void {
    if (!this.hasMore() || this.searching()) {
      return;
    }

    const target = event.target as HTMLElement;
    const threshold = 30;
    if (target.scrollTop + target.clientHeight + threshold < target.scrollHeight) {
      return;
    }

    this.searchVariants(this.query(), this.currentSearchPage() + 1);
  }

  selectVariant(variant: VariantSearchItem): void {
    if (this.isExistingVariant(variant.id)) {
      return;
    }

    this.selected.emit(variant);
    this.query.set('');
    this.results.set([]);
    this.inputFocused.set(false);
    this.dropdownPosition.set(null);
  }

  clearSelection(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();

    this.query.set('');
    this.results.set([]);
    this.cleared.emit();
  }

  private searchVariants(query: string, page: number): void {
    if (!query || query.length < 2) {
      this.results.set([]);
      this.hasMore.set(false);
      this.currentSearchPage.set(1);
      return;
    }

    this.searching.set(true);
    this.currentSearchPage.set(page);

    this.productService.searchVariants(
      query,
      page,
      20,
      this.locationId,
      this.locationType
    ).subscribe({
      next: (response) => {
        let variants: VariantSearchItem[] = (response.data || []).map((variant: any) => ({
          id: variant.id,
          productCode: variant.productCode,
          sku: variant.sku,
          barcode: variant.barcode,
          productName: variant.productName,
          name: variant.name,
          attributes: variant.attributes,
          stockQuantity: variant.stockQuantity,
          primaryImageThumb: variant.primaryImageThumb,
        }));

        if (this.hideExisting) {
          variants = variants.filter(v => !this.isExistingVariant(v.id));
        }

        if (page === 1) {
          this.results.set(variants);
        } else {
          this.results.update(current => [...current, ...variants]);
        }

        this.hasMore.set(variants.length === 20);
        this.searching.set(false);
      },
      error: () => {
        this.searching.set(false);
      }
    });
  }
}
