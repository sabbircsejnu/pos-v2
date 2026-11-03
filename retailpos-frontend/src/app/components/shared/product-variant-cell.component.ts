import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { ProductImageService } from '../../services/product-image.service';
import { VariantAttributesDisplayPipe } from '../../pipes/variant-attributes-display.pipe';

@Component({
  selector: 'app-product-variant-cell',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex items-center gap-2.5">
      @if (primaryImageThumb) {
        <img
          [src]="resolveImage(primaryImageThumb)"
          [alt]="productName"
          loading="lazy"
          decoding="async"
          class="h-10 w-10 flex-shrink-0 rounded-lg border border-gray-200 bg-white object-cover"
        />
      } @else {
        <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-lg bg-gray-100">
          <i class="fas fa-box text-sm text-gray-400"></i>
        </div>
      }

      <div class="min-w-0">
        <div class="line-clamp-2 text-sm font-semibold leading-5 text-gray-900">{{ productName }}</div>
        @if (productCode?.trim()) {
          <div class="mt-0.5 text-xs font-medium text-gray-600">Main Code: {{ productCode!.trim() }}</div>
        }
        @if (variantDisplayText) {
          <div class="mt-0.5 text-xs text-gray-600 leading-4 break-words">
            <span class="font-medium text-gray-700">Variant:</span>
            <span class="ml-1">{{ variantDisplayText }}</span>
          </div>
        }
      </div>
    </div>
  `
})
export class ProductVariantCellComponent {
  private imageService = inject(ProductImageService);
  private variantAttributesPipe = new VariantAttributesDisplayPipe();

  @Input({ required: true }) productName = '';
  @Input() productCode: string | null | undefined;
  @Input() variantName: string | null | undefined;
  @Input() variantAttributes: string | null | undefined;
  @Input() variantDisplay: string | null | undefined;
  @Input() primaryImageThumb: string | null | undefined;
  @Input() showVariantDetails = true;

  get variantDisplayText(): string {
    if (!this.showVariantDetails) {
      return '';
    }

    if (this.variantDisplay?.trim()) {
      return this.variantDisplay.trim();
    }

    const formattedAttributes = this.variantAttributesPipe.transform(this.variantAttributes);
    if (formattedAttributes) {
      return formattedAttributes;
    }

    if (this.variantName?.trim()) {
      return this.variantName.trim();
    }

    return '';
  }

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
  }
}
