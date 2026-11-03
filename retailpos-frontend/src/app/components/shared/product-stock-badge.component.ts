import { Component, computed, input } from '@angular/core';
import { Product } from '../../models/product.model';
import { UiBadgeComponent } from './ui';

@Component({
  selector: 'app-product-stock-badge',
  standalone: true,
  imports: [UiBadgeComponent],
  template: `<app-ui-badge [variant]="presentation().variant">{{ presentation().label }}</app-ui-badge>`,
})
export class ProductStockBadgeComponent {
  readonly product = input.required<Product>();
  readonly presentation = computed(() => {
    const product = this.product();
    switch (product.stockStatus) {
      case 'in_stock':
        return { variant: 'success' as const, label: product.hasVariants ? 'In Stock' : String(product.totalStock) };
      case 'partially_in_stock':
        return { variant: 'warning' as const, label: 'Partially In Stock' };
      case 'out_of_stock':
        return { variant: 'danger' as const, label: 'Out of Stock' };
      case 'not_initialized':
        return { variant: 'neutral' as const, label: 'Stock Not Initialized' };
      default:
        return { variant: 'neutral' as const, label: 'Stock Unavailable' };
    }
  });
}
