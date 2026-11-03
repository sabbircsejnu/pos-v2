import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, inject, signal } from '@angular/core';
import { Subject, debounceTime } from 'rxjs';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { VariationService } from '../../services/variation.service';
import { AuthService } from '../../services/auth.service';
import { Variation } from '../../models/variation.model';
import { VariantAttributesDisplayPipe } from '../../pipes/variant-attributes-display.pipe';

export interface VariantSearchItem {
  id: number;
  productCode?: string;
  sku?: string;
  barcode?: string;
  productName: string;
  name: string;
  attributes?: string;
  variantAttributes?: string;
  variantDisplay?: string;
  stockQuantity?: number | null;
  primaryImageThumb?: string;
}

type VariationMeta = {
  name: string;
  displayOrder: number;
  options: string[];
};

@Component({
  selector: 'app-variant-search-picker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './variant-search-picker.component.html',
})
export class VariantSearchPickerComponent implements OnInit, OnDestroy {
  private productService = inject(ProductService);
  private imageService = inject(ProductImageService);
  private variationService = inject(VariationService);
  private auth = inject(AuthService);
  private variantAttributesPipe = new VariantAttributesDisplayPipe();
  private searchSubject = new Subject<string>();

  @Input() disabled = false;
  @Input() label = 'Search Product';
  @Input() selectedVariant: VariantSearchItem | null = null;
  @Input() placeholder = 'Search by main code, SKU, product name, or variant attributes...';
  @Input() locationId: number | null = null;
  @Input() locationType: string | null = null;
  @Input() existingVariantIds: number[] = [];
  @Input() hideExisting = false;
  @Input() useVariationMetadata = true;

  @Output() selected = new EventEmitter<VariantSearchItem>();
  @Output() cleared = new EventEmitter<void>();

  query = signal('');
  results = signal<VariantSearchItem[]>([]);
  searching = signal(false);
  inputFocused = signal(false);
  dropdownPosition = signal<{ top: number; left: number; width: number } | null>(null);
  currentSearchPage = signal(1);
  hasMore = signal(false);
  variationMetadata = signal<VariationMeta[]>([]);
  /** Index of the keyboard-highlighted result (-1 = none). */
  activeIndex = signal(-1);

  constructor() {
    this.searchSubject.pipe(debounceTime(300)).subscribe(query => this.searchVariants(query, 1));
  }

  ngOnInit(): void {
    this.loadVariationMetadata();
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

    const formattedVariant = this.getFormattedVariantDisplay(variant);
    if (formattedVariant) {
      return `${variant.productName} (${formattedVariant})`;
    }

    return variant.sku?.trim() ? `${variant.productName} - ${variant.sku}` : `${variant.productName} - ${variant.name}`;
  }

  getFormattedVariantDisplay(variant: VariantSearchItem): string {
    const fromAttributes = this.variantAttributesPipe.transform(variant.attributes || variant.variantAttributes);
    if (fromAttributes) {
      return this.normalizeVariantDisplay(fromAttributes);
    }

    const explicit = (variant.variantDisplay || '').trim();
    if (explicit) {
      const normalized = this.normalizeVariantDisplay(explicit);
      return this.formatFromHyphenValues(normalized) || normalized;
    }

    const name = (variant.name || '').trim();
    if (name) {
      return this.formatFromHyphenValues(name) || this.normalizeVariantDisplay(name);
    }

    return '';
  }

  getInputValue(): string {
    if (this.inputFocused()) {
      return this.query();
    }

    return this.getDisplayText(this.selectedVariant);
  }

  onInput(value: string): void {
    this.query.set(value);
    this.activeIndex.set(-1);

    if (this.selectedVariant && value !== this.getDisplayText(this.selectedVariant)) {
      this.cleared.emit();
    }

    this.searchSubject.next(value);
  }

  activeOptionId(): string | null {
    const index = this.activeIndex();
    return this.inputFocused() && index >= 0 && index < this.results().length ? `variant-picker-option-${index}` : null;
  }

  onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    if (event.key === 'ArrowDown' && count > 0) {
      event.preventDefault();
      this.activeIndex.set(Math.min(count - 1, this.activeIndex() + 1));
    } else if (event.key === 'ArrowUp' && count > 0) {
      event.preventDefault();
      this.activeIndex.set(Math.max(0, this.activeIndex() - 1));
    } else if (event.key === 'Enter') {
      const variant = this.results()[this.activeIndex()];
      if (this.inputFocused() && variant) {
        event.preventDefault();
        this.selectVariant(variant);
      }
    } else if (event.key === 'Escape' && this.results().length > 0 && this.inputFocused()) {
      // Close the list without also closing a dialog or drawer behind it.
      event.preventDefault();
      event.stopPropagation();
      this.results.set([]);
      this.activeIndex.set(-1);
    }
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

    const formattedVariant = this.getFormattedVariantDisplay(variant);
    this.selected.emit({
      ...variant,
      variantDisplay: formattedVariant || variant.variantDisplay,
    });
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

  private resolveAttributes(raw: any): string {
    const value = raw?.attributes ?? raw?.variantAttributes ?? raw?.Attributes ?? '';
    return typeof value === 'string' ? value : JSON.stringify(value ?? '');
  }

  private resolveVariantName(raw: any): string {
    const name = raw?.name ?? raw?.variantName ?? raw?.Name ?? '';
    return typeof name === 'string' ? name : '';
  }

  private resolveVariantDisplay(raw: any, attributes: string, name: string): string {
    const explicit = raw?.variantDisplay ?? raw?.variant_display ?? raw?.VariantDisplay;
    if (typeof explicit === 'string' && explicit.trim()) {
      return explicit.trim();
    }

    const formatted = this.variantAttributesPipe.transform(attributes);
    if (formatted) {
      return formatted;
    }

    // Keep empty for attribute-driven variants when labels are unavailable.
    // The API now provides a canonical VariantDisplay string.
    if (attributes.trim()) {
      return '';
    }

    if (name.trim()) {
      return name.trim();
    }

    return typeof raw?.sku === 'string' ? raw.sku.trim() : '';
  }

  private normalizeVariantDisplay(display: string): string {
    const parts = display.split('/').map(x => x.trim()).filter(Boolean);
    if (!parts.length) {
      return display;
    }

    return parts
      .map(part => {
        if (part.includes(':')) {
          const [rawLabel, ...rest] = part.split(':');
          const label = this.humanizeKey(rawLabel.trim());
          const value = rest.join(':').trim();
          return value ? `${label}: ${value}` : label;
        }

        const firstSpace = part.indexOf(' ');
        if (firstSpace <= 0) {
          return part;
        }

        const label = this.humanizeKey(part.substring(0, firstSpace).trim());
        const value = part.substring(firstSpace + 1).trim();
        return value ? `${label}: ${value}` : label;
      })
      .join(' / ');
  }

  private formatFromHyphenValues(raw: string): string {
    if (!raw || raw.includes(':') || raw.includes('/')) {
      return '';
    }

    const variations = this.variationMetadata();
    if (!variations.length) {
      return '';
    }

    const parts = raw
      .split(/\s+-\s+/)
      .map(x => x.trim())
      .filter(Boolean);

    if (parts.length < 2) {
      return '';
    }

    const usedVariations = new Set<string>();
    const mappedByVariation = new Map<string, { label: string; value: string; order: number }>();

    for (const part of parts) {
      const token = part.trim().toLowerCase();
      if (!token) {
        continue;
      }

      const matched = variations.find(variation => {
        if (usedVariations.has(variation.name)) {
          return false;
        }

        return variation.options.some(option => option.toLowerCase() === token);
      });

      if (!matched) {
        continue;
      }

      usedVariations.add(matched.name);
      mappedByVariation.set(matched.name, {
        label: this.humanizeKey(matched.name),
        value: part,
        order: matched.displayOrder
      });
    }

    if (!mappedByVariation.size) {
      return '';
    }

    return Array.from(mappedByVariation.values())
      .sort((a, b) => a.order - b.order)
      .map(entry => `${entry.label}: ${entry.value}`)
      .join(' / ');
  }

  private humanizeKey(key: string): string {
    return key
      .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^./, c => c.toUpperCase());
  }

  private loadVariationMetadata(): void {
    // Variation metadata is only a display fallback (results already carry a formatted variantDisplay), so
    // it is skipped for users without variations.view instead of triggering a permission error.
    if (!this.useVariationMetadata || !this.auth.hasPermission('variations.view')) {
      this.variationMetadata.set([]);
      return;
    }

    this.variationService.getAllVariations().subscribe({
      next: (variations: Variation[]) => {
        const prepared = (variations || [])
          .filter(v => v.isActive)
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map(v => ({
            name: v.name,
            displayOrder: v.displayOrder,
            options: (v.options || [])
              .filter(o => o.isActive)
              .sort((a, b) => a.displayOrder - b.displayOrder)
              .map(o => o.name)
          }));

        this.variationMetadata.set(prepared);
      },
      error: () => {
        this.variationMetadata.set([]);
      }
    });
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
        let variants: VariantSearchItem[] = (response.data || []).map((variant: any) => {
          const attributes = this.resolveAttributes(variant);
          const name = this.resolveVariantName(variant);

          return {
            id: variant.id,
            productCode: variant.productCode,
            sku: variant.sku,
            barcode: variant.barcode,
            productName: variant.productName,
            name,
            attributes,
            variantAttributes: attributes,
            variantDisplay: this.resolveVariantDisplay(variant, attributes, name),
            stockQuantity: variant.stockQuantity,
            primaryImageThumb: variant.primaryImageThumb,
          };
        });

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
