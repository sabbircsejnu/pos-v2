import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { Product } from '../../models/product.model';
import { ProductDetailComponent } from './product-detail.component';

describe('ProductDetailComponent stock', () => {
  afterEach(() => TestBed.resetTestingModule());

  it.each([
    ['in_stock', 'In Stock'],
    ['partially_in_stock', 'Partially In Stock'],
    ['out_of_stock', 'Out of Stock'],
    ['not_initialized', 'Stock Not Initialized'],
  ] as const)('renders the API status %s and retains the selected location', async (stockStatus, label) => {
    const product: Product = {
      id: 1, name: 'Stock product', categoryId: 1, categoryName: 'Category',
      basePrice: 10, costPrice: 5, taxRate: 0, hasVariants: true,
      status: 'active', isActive: true, variantCount: 2, totalStock: 5,
      stockStatus, variants: [], createdAt: new Date(), updatedAt: new Date(),
    };
    const getById = vi.fn().mockReturnValue(of({ data: product }));
    TestBed.configureTestingModule({
      imports: [ProductDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '1']]), queryParams: { warehouseId: '3' } } } },
        { provide: ProductService, useValue: { getById } },
        { provide: AuthService, useValue: { hasPermission: () => false } },
        { provide: ProductImageService, useValue: { list: () => of([]), resolveUrl: () => '' } },
        { provide: CurrencyService, useValue: { format: (value: number) => String(value) } },
      ],
    });
    const fixture = TestBed.createComponent(ProductDetailComponent);
    await fixture.whenStable();
    expect(getById).toHaveBeenCalledWith(1, undefined, 3);
    expect(fixture.nativeElement.querySelector('app-product-stock-badge').textContent.trim()).toBe(label);
  });

  describe('Units & Pricing link', () => {
    async function render(permissions: string[], overrides: Partial<Product>) {
      const product: Product = {
        id: 1, name: 'Attar', categoryId: 1, categoryName: 'Category', basePrice: 10, costPrice: 5, taxRate: 0, hasVariants: true,
        status: 'active', isActive: true, variantCount: 1, totalStock: 5, stockStatus: 'in_stock',
        variants: [{ id: 7, productId: 1, name: 'Std', priceAdjustment: 0, finalPrice: 10, createdAt: new Date(), updatedAt: new Date() }],
        createdAt: new Date(), updatedAt: new Date(), ...overrides,
      };
      TestBed.configureTestingModule({
        imports: [ProductDetailComponent],
        providers: [
          provideRouter([]),
          { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '1']]), queryParams: {} } } },
          { provide: ProductService, useValue: { getById: vi.fn().mockReturnValue(of({ data: product })) } },
          { provide: AuthService, useValue: { hasPermission: (key: string) => permissions.includes(key) } },
          { provide: ProductImageService, useValue: { list: () => of([]), resolveUrl: () => '' } },
          { provide: CurrencyService, useValue: { format: (value: number) => String(value) } },
        ],
      });
      const fixture = TestBed.createComponent(ProductDetailComponent);
      await fixture.whenStable();
      return fixture.nativeElement as HTMLElement;
    }
    const link = (element: HTMLElement) => element.querySelector('a[href="/products/1/variants/7/units"]');

    it('links each variant for an editor', async () => {
      expect(link(await render(['products.edit'], {}))).not.toBeNull();
    });

    it('links the single variant of a product without variants', async () => {
      expect(link(await render(['products.edit'], { hasVariants: false }))).not.toBeNull();
    });

    it('adds nothing for a user who cannot edit products', async () => {
      const element = await render([], {});
      expect(link(element)).toBeNull();
      expect(element.textContent).not.toContain('Units & Pricing');
    });
  });

  it.each([
    [null, null, 'Business default'],
    [0, null, '0% (zero-rated)'],
    [7.5, 'standard', '7.5%'],
    [5, 'exempt', 'Exempt'],
    [5, 'zero_rated', '0% (zero-rated)'],
  ] as const)('shows tax rate %s / classification %s as "%s"', async (taxRate, taxClassification, expected) => {
    const product: Product = {
      id: 1, name: 'Tax product', categoryId: 1, categoryName: 'Category',
      basePrice: 10, costPrice: 5, taxRate, taxClassification, hasVariants: false,
      status: 'active', isActive: true, variantCount: 0, totalStock: 5,
      variants: [], createdAt: new Date(), updatedAt: new Date(),
    };
    TestBed.configureTestingModule({
      imports: [ProductDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', '1']]), queryParams: {} } } },
        { provide: ProductService, useValue: { getById: vi.fn().mockReturnValue(of({ data: product })) } },
        { provide: AuthService, useValue: { hasPermission: () => false } },
        { provide: ProductImageService, useValue: { list: () => of([]), resolveUrl: () => '' } },
        { provide: CurrencyService, useValue: { format: (value: number) => String(value) } },
      ],
    });
    const fixture = TestBed.createComponent(ProductDetailComponent);
    await fixture.whenStable();
    fixture.detectChanges();
    const label = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('p')).find(p => p.textContent!.trim() === 'Tax Rate')!;
    expect(label.nextElementSibling!.textContent!.trim()).toBe(expected);
  });
});
