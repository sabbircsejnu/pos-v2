import { TestBed } from '@angular/core/testing';
import { Product } from '../../models/product.model';
import { ProductStockBadgeComponent } from './product-stock-badge.component';

const stockProduct = (overrides: Partial<Product> = {}): Product => ({
  id: 1, name: 'Stock product', categoryId: 1, categoryName: 'Category',
  basePrice: 10, costPrice: 5, taxRate: 0, hasVariants: true,
  status: 'active', isActive: true, variantCount: 2, totalStock: 5,
  stockStatus: 'partially_in_stock', variants: [], createdAt: new Date(), updatedAt: new Date(),
  ...overrides,
});

describe('ProductStockBadgeComponent', () => {
  it.each([
    ['in_stock', true, 5, 'In Stock', 'success'],
    ['partially_in_stock', true, 5, 'Partially In Stock', 'warning'],
    ['out_of_stock', true, 0, 'Out of Stock', 'danger'],
    ['not_initialized', true, 0, 'Stock Not Initialized', 'neutral'],
    ['unavailable', true, 0, 'Stock Unavailable', 'neutral'],
    [undefined, true, 0, 'Stock Unavailable', 'neutral'],
    ['in_stock', false, 7, '7', 'success'],
    ['out_of_stock', false, 0, 'Out of Stock', 'danger'],
  ] as const)('renders %s without inferring missing stock as zero', async (stockStatus, hasVariants, totalStock, label, variant) => {
    TestBed.configureTestingModule({ imports: [ProductStockBadgeComponent] });
    const fixture = TestBed.createComponent(ProductStockBadgeComponent);
    fixture.componentRef.setInput('product', stockProduct({ stockStatus, hasVariants, totalStock }));
    await fixture.whenStable();
    const badge = fixture.nativeElement.querySelector('app-ui-badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe(label);
    expect(badge.getAttribute('data-variant')).toBe(variant);
    TestBed.resetTestingModule();
  });
});
