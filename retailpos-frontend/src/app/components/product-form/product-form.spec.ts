import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CategoryService } from '../../services/category.service';
import { CurrencyService } from '../../services/currency.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { ProductVariationService } from '../../services/product-variation.service';
import { SettingsService } from '../../services/settings.service';
import { VariationService } from '../../services/variation.service';
import { ProductForm } from './product-form';

const PRODUCT = {
  id: 7,
  name: 'Premium Mug',
  description: 'Ceramic',
  productCode: 'MUG-001',
  sku: 'SKU-MUG',
  barcode: '8901234567890',
  categoryId: 2,
  basePrice: 20,
  costPrice: 8,
  taxRate: 5,
  hasVariants: true,
  status: 'draft'
};

interface SetupOptions {
  id?: string;
  permissions?: string[];
  queryParams?: Record<string, string>;
  taxSettings?: { taxEnabled: boolean; defaultTaxRate: number };
  taxSettingsResult?: Subject<unknown>;
  categoriesResult?: Subject<unknown>;
  product?: Record<string, unknown>;
}

describe('ProductForm (shared UI migration)', () => {
  let fixture: ComponentFixture<ProductForm>;
  let cmp: ProductForm;
  let productService: { getById: ReturnType<typeof vi.fn>; create: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn>; generateSku: ReturnType<typeof vi.fn>; generateBarcode: ReturnType<typeof vi.fn> };
  let settingsService: { getTaxSettings: ReturnType<typeof vi.fn> };
  let imageService: { list: ReturnType<typeof vi.fn>; upload: ReturnType<typeof vi.fn>; resolveUrl: (p?: string | null) => string; placeholder: () => string };
  let location: { back: ReturnType<typeof vi.fn> };
  let router: Router;
  let alerts: AlertService;
  let create$: Subject<unknown>;

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string) => Array.from(root().querySelectorAll<E>(selector));
  const text = (selector: string) => q(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const field = (label: string) => qa('app-ui-form-field').find(f => f.querySelector('label')?.textContent?.replace(/\s*\*?\s*\(required\)/, '').trim().startsWith(label))!;
  const control = <E extends HTMLElement = HTMLInputElement>(label: string) => field(label).querySelector('input, select, textarea') as E;
  const type = async (label: string, value: string) => {
    const el = control<HTMLInputElement>(label);
    el.value = value;
    el.dispatchEvent(new Event('input'));
    await settle();
  };
  const choose = async (label: string, optionText: string) => {
    const el = control<HTMLSelectElement>(label);
    el.selectedIndex = Array.from(el.options).findIndex(o => o.textContent!.trim().startsWith(optionText));
    el.dispatchEvent(new Event('change'));
    await settle();
  };
  const button = (name: string) => qa<HTMLButtonElement>('.pf-actions button').find(b => b.textContent!.includes(name))!;
  const fillValid = async () => {
    await type('Product Name', 'Premium Mug');
    await choose('Category', 'Homeware');
    await type('Selling Price', '20');
  };

  async function setup(options: SetupOptions = {}) {
    const permissions = options.permissions ?? ['products.view_cost'];
    create$ = new Subject();
    productService = {
      getById: vi.fn(() => of({ data: options.product ?? PRODUCT })),
      create: vi.fn(() => create$),
      update: vi.fn(() => of({ data: PRODUCT })),
      generateSku: vi.fn(() => of({ data: 'MUG-GEN-1' })),
      generateBarcode: vi.fn(() => of({ data: '8800021234565' }))
    };
    settingsService = {
      getTaxSettings: vi.fn(() => options.taxSettingsResult ?? of({
        data: options.taxSettings ?? { taxEnabled: true, defaultTaxRate: 7.5 }
      }))
    };
    imageService = { list: vi.fn(() => of([])), upload: vi.fn(() => of({})), resolveUrl: (p?: string | null) => p ?? '', placeholder: () => '' };
    location = { back: vi.fn() };

    TestBed.configureTestingModule({
      imports: [ProductForm],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(options.id ? { id: options.id } : {}), queryParams: options.queryParams ?? {} } } },
        { provide: Location, useValue: location },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
        { provide: ProductService, useValue: productService },
        { provide: SettingsService, useValue: settingsService },
        { provide: ProductImageService, useValue: imageService },
        { provide: CategoryService, useValue: { getAllCategories: () => options.categoriesResult ?? of({ data: [{ id: 1, name: 'Beverages' }, { id: 2, name: 'Homeware' }] }) } },
        { provide: CurrencyService, useValue: { symbol: signal('$') } },
        { provide: ProductVariationService, useValue: { getProductVariations: () => of([]), getCombinations: () => of([]) } },
        { provide: VariationService, useValue: { variations: signal([]), loadVariations: () => undefined } }
      ]
    });
    router = TestBed.inject(Router);
    alerts = TestBed.inject(AlertService);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(window, 'alert').mockImplementation(() => undefined);

    fixture = TestBed.createComponent(ProductForm);
    cmp = fixture.componentInstance;
    await settle();
    await settle();
  }

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  describe('create mode rendering', () => {
    beforeEach(() => setup());

    it('uses the shared page header with breadcrumbs and the create copy', () => {
      expect(text('app-ui-page-header h1')).toBe('Create Product');
      expect(text('app-ui-page-header')).toContain('Create a product using a modern, section-based workflow.');
      expect(qa('app-ui-page-header nav li').map(li => li.textContent!.trim())).toEqual(['Dashboard', 'Products', 'Create']);
      expect(text('app-ui-card app-ui-badge')).toBe('New Product');
    });

    it('groups fields in titled cards', () => {
      const titles = qa('app-ui-card h2, app-ui-card h3').map(h => h.textContent!.trim());
      expect(titles).toEqual(expect.arrayContaining(['Basic Information', 'Identifiers', 'Classification', 'Pricing', 'Variants', 'Product Status', 'Media', 'Inventory', 'Product Setup Progress']));
    });

    it('gives every field a visible label tied to its control, and marks required ones', () => {
      for (const label of ['Product Name', 'Description', 'Product Code', 'SKU', 'Barcode', 'Category', 'Brand', 'Tags', 'Selling Price', 'Cost Price', 'Tax Rate', 'Tax Classification', 'Status']) {
        const el = control(label);
        const lab = field(label).querySelector('label')!;
        expect(el, label).toBeTruthy();
        expect(lab.getAttribute('for'), label).toBe(el.id);
      }
      const required = qa('app-ui-form-field').filter(f => f.querySelector('.ui-field__required')).map(f => f.querySelector('label')!.textContent!.replace(/\s*\*?\s*\(required\)/, '').trim());
      expect(required).toEqual(['Product Name', 'Category', 'Selling Price']);
      expect(control('Product Name').getAttribute('aria-required')).toBe('true');
    });

    it('keeps the existing hints, placeholders and product code presentation', () => {
      expect(text('app-ui-form-field:has(input[name="productCode"])')).toContain('Unique business code used across product, variant, and reporting screens.');
      expect(control('Product Name').placeholder).toBe('e.g., Premium Coffee Mug');
      expect(control('Product Code').classList).toContain('pf-uppercase');
      expect(control('Brand').closest('app-ui-form-field')!.textContent).toContain('Brand persistence will be enabled in a future release.');
    });

    it('offers Cancel, Save Draft and Save Product, and explains that variants unlock after saving', () => {
      expect(qa('.pf-actions button').map(b => b.textContent!.trim())).toEqual(['Cancel', 'Save Draft', 'Save Product']);
      expect(button('Save Product').type).toBe('submit');
      expect(text('app-ui-state')).toContain('Save the product first to unlock full variant management, bulk generation, and combination-level controls.');
      expect(q('app-combination-manager')).toBeNull();
    });

    it('lists the status options and shows the status as text', async () => {
      expect(Array.from(control<HTMLSelectElement>('Status').options).map(o => o.value)).toEqual(['active', 'draft', 'inactive']);
      expect(text('app-ui-card:has(#productStatus) app-ui-badge')).toBe('Active');
      await choose('Status', 'Draft');
      expect(text('app-ui-card:has(#productStatus) app-ui-badge')).toBe('Draft');
      expect(text('.pf-note')).toBe('Draft items stay out of POS and purchasing flows until activated.');
    });

    it('exposes landmarks, a labelled progress bar and text status for the checklist', async () => {
      expect(q('section[aria-label="Product details"]')).not.toBeNull();
      expect(q('aside[aria-label="Product settings"]')).not.toBeNull();
      expect(q('[role="group"][aria-label="Product actions"]')).not.toBeNull();
      const bar = q('[role="progressbar"]');
      expect(bar.getAttribute('aria-label')).toBe('Product setup completion');
      expect(bar.getAttribute('aria-valuenow')).toBe('20');
      const items = qa('.pf-checklist li').map(li => `${li.querySelector('span')!.textContent!.trim()}: ${li.querySelector('app-ui-badge')!.textContent!.trim()}`);
      expect(items).toEqual(['Basic Information: To do', 'Category: To do', 'Pricing: To do', 'Variants: Done', 'Media: To do']);
      await fillValid();
      await type('Product Code', 'MUG-1');
      expect(q('[role="progressbar"]').getAttribute('aria-valuenow')).toBe('80');
    });

    it('stacks to one column on small screens and splits on large ones (responsive grid classes)', () => {
      const grid = q('form > .grid');
      expect(grid.classList).toContain('grid-cols-1');
      expect(grid.classList).toContain('lg:grid-cols-10');
      expect(q('.pf-actions').getAttribute('role')).toBe('group');
    });
  });

  describe('Reactive/ngModel compatibility', () => {
    beforeEach(() => setup());

    it('binds every control to its signal in both directions', async () => {
      await type('Product Name', 'Mug');
      await type('Description', 'Nice');
      await type('Product Code', 'MUG-1');
      await type('SKU', 'S-1');
      await type('Barcode', '123');
      await type('Selling Price', '12.5');
      await type('Cost Price', '5');
      await type('Tax Rate', '7');
      await choose('Category', 'Beverages');
      expect(cmp.name()).toBe('Mug');
      expect(cmp.description()).toBe('Nice');
      expect(cmp.productCode()).toBe('MUG-1');
      expect(cmp.sku()).toBe('S-1');
      expect(cmp.barcode()).toBe('123');
      expect(cmp.basePrice()).toBe(12.5);
      expect(cmp.costPrice()).toBe(5);
      expect(cmp.taxRate()).toBe(7);
      expect(cmp.categoryId()).toBe(1);

      cmp.name.set('Changed in code');
      await settle();
      await settle();
      expect(control('Product Name').value).toBe('Changed in code');
    });

    it('toggles the future-ready inventory checkbox', async () => {
      const box = q<HTMLInputElement>('input[name="trackInventory"]');
      expect(box.checked).toBe(true);
      box.click();
      await settle();
      expect(cmp.trackInventory()).toBe(false);
      expect(box.classList).toContain('ui-check__input');
      expect(text('app-ui-card:has(input[name="trackInventory"]) app-ui-badge')).toBe('Not saved yet');
    });

    it('shows the calculated margin only when selling and cost are valid', async () => {
      expect(control('Margin').value).toBe('—');
      expect(control('Margin').readOnly).toBe(true);
      await type('Selling Price', '20');
      await type('Cost Price', '5');
      expect(control('Margin').value).toBe('75.0%');
    });
  });

  describe('permissions', () => {
    it('hides cost and margin without products.view_cost', async () => {
      await setup({ permissions: [] });
      expect(qa('app-ui-form-field').some(f => f.textContent!.includes('Cost Price'))).toBe(false);
      expect(qa('app-ui-form-field').some(f => f.textContent!.includes('Margin'))).toBe(false);
      expect(control('Selling Price')).toBeTruthy();
    });

    it('does not send an edited cost value into the margin when the user cannot see cost', async () => {
      await setup({ permissions: [] });
      expect(cmp.computedMargin()).toBeNull();
    });
  });

  describe('validation (existing rules)', () => {
    beforeEach(() => setup());

    it('blocks an empty submit, shows messages next to the fields and sends nothing', async () => {
      button('Save Product').click();
      await settle();
      await settle();
      expect(productService.create).not.toHaveBeenCalled();
      expect(field('Product Name').textContent).toContain('Product name is required');
      expect(field('Category').textContent).toContain('Please select a category');
      expect(field('Selling Price').textContent).toContain('Base price must be greater than 0');
      expect(field('Tax Rate').textContent).not.toContain('Tax rate');
    });

    it('marks invalid controls for assistive technology and focuses the first one', async () => {
      button('Save Product').click();
      await settle();
      await settle();
      expect(control('Product Name').getAttribute('aria-invalid')).toBe('true');
      expect(control('Category').getAttribute('aria-invalid')).toBe('true');
      expect(control('Selling Price').getAttribute('aria-invalid')).toBe('true');
      expect(control('Brand').getAttribute('aria-invalid')).toBeNull();
      expect(control('Product Name').getAttribute('aria-describedby')).toContain(field('Product Name').querySelector('.ui-field__error')!.id);
      expect(document.activeElement).toBe(control('Product Name'));
    });

    it('rejects a tax rate outside 0–100 and accepts the boundaries', async () => {
      await fillValid();
      await type('Tax Rate', '101');
      button('Save Product').click();
      await settle();
      expect(field('Tax Rate').textContent).toContain('Tax rate must be between 0 and 100');
      expect(productService.create).not.toHaveBeenCalled();

      await type('Tax Rate', '-1');
      button('Save Product').click();
      await settle();
      expect(productService.create).not.toHaveBeenCalled();

      await type('Tax Rate', '100');
      button('Save Product').click();
      await settle();
      expect(productService.create).toHaveBeenCalledTimes(1);
    });

    it('clears the messages once the data is valid', async () => {
      button('Save Product').click();
      await settle();
      await fillValid();
      button('Save Product').click();
      await settle();
      await settle();
      expect(qa('.ui-field__error').map(e => e.textContent!.trim()).filter(Boolean)).toEqual([]);
      expect(qa('[aria-invalid="true"]').length).toBe(0);
    });
  });

  describe('create workflow and payload', () => {
    beforeEach(() => setup());

    it('sends exactly the existing create contract', async () => {
      await type('Product Name', 'Premium Mug');
      await type('Description', 'Ceramic');
      await type('Product Code', 'MUG-001');
      await type('SKU', 'SKU-MUG');
      await type('Barcode', '8901234567890');
      await choose('Category', 'Homeware');
      await type('Selling Price', '20');
      await type('Cost Price', '8');
      await type('Tax Rate', '5');
      button('Save Product').click();
      await settle();
      expect(productService.create).toHaveBeenCalledTimes(1);
      expect(productService.create).toHaveBeenCalledWith({
        name: 'Premium Mug', description: 'Ceramic', productCode: 'MUG-001', sku: 'SKU-MUG', barcode: '8901234567890',
        categoryId: 2, basePrice: 20, costPrice: 8, taxRate: 5, taxClassification: null, hasVariants: false, status: 'active'
      });
      const sent = productService.create.mock.calls[0][0];
      for (const futureField of ['brand', 'tags', 'trackInventory', 'reorderLevel', 'openingStock', 'warehouse']) {
        expect(sent).not.toHaveProperty(futureField);
      }
    });

    it('omits empty optional fields', async () => {
      await fillValid();
      button('Save Product').click();
      await settle();
      expect(productService.create).toHaveBeenCalledWith({
        name: 'Premium Mug', description: undefined, productCode: undefined, sku: undefined, barcode: undefined,
        categoryId: 2, basePrice: 20, costPrice: 0, taxRate: null, taxClassification: null, hasVariants: false, status: 'active'
      });
    });

    it('Save Draft saves with status draft', async () => {
      await fillValid();
      button('Save Draft').click();
      await settle();
      expect(productService.create.mock.calls[0][0].status).toBe('draft');
      expect(control<HTMLSelectElement>('Status').value).toBe('draft');
    });

    it('opens the saved product for editing and keeps the list query parameters', async () => {
      TestBed.resetTestingModule();
      await setup({ queryParams: { page: '3', search: 'mug' } });
      await fillValid();
      button('Save Product').click();
      await settle();
      create$.next({ data: { id: 42 } });
      await settle();
      expect(router.navigate).toHaveBeenCalledWith(['/products', 'edit', 42], { queryParams: { page: '3', search: 'mug' } });
      expect(button('Save Product').getAttribute('aria-busy')).toBeNull();
    });

    it('returns to the list when the API does not return an id', async () => {
      await fillValid();
      button('Save Product').click();
      await settle();
      create$.next({});
      await settle();
      expect(router.navigate).toHaveBeenCalledWith(['/products']);
    });
  });

  describe('product images (products.edit is required for every image change)', () => {
    it('uploads queued images after creating, warns about failures and then opens the product', async () => {
      await setup({ permissions: ['products.create', 'products.edit'] });
      await fillValid();
      const good = new File(['a'], 'good.png', { type: 'image/png' });
      const bad = new File(['b'], 'bad.png', { type: 'image/png' });
      cmp.imageUploader!.pendingFiles.set([
        { file: good, previewUrl: 'blob:good' },
        { file: bad, previewUrl: 'blob:bad' }
      ]);
      imageService.upload.mockImplementation((_id: number, f: File) => (f.name === 'bad.png'
        ? throwError(() => ({ error: { message: 'The file is corrupted.' } }))
        : of({})));
      button('Save Product').click();
      await settle();
      create$.next({ data: { id: 9 } });
      await settle();
      expect(imageService.upload).toHaveBeenCalledWith(9, good);
      expect(imageService.upload).toHaveBeenCalledWith(9, bad);
      expect(alerts.config()).toMatchObject({ type: 'warning', title: 'Some Images Failed' });
      expect(alerts.config()!.message).toContain('bad.png (The file is corrupted.)');
      expect(router.navigate).toHaveBeenCalledWith(['/products', 'edit', 9], { queryParams: {} });
    });

    it('shows the image controls when the user can edit products', async () => {
      await setup({ permissions: ['products.create', 'products.edit'] });
      expect(q('app-product-image-uploader .pu-drop')).not.toBeNull();
      expect(text('app-product-image-uploader')).not.toContain('requires the Edit Products permission');
    });

    it('hides image upload and explains why for a create-only user, and never uploads after saving', async () => {
      await setup({ permissions: ['products.create'] });
      expect(q('app-product-image-uploader .pu-drop')).toBeNull();
      expect(text('app-product-image-uploader')).toContain('Adding, replacing or removing product images requires the Edit Products permission.');

      await fillValid();
      // Even a queued file (for example from an earlier session state) is not sent without products.edit.
      cmp.imageUploader!.pendingFiles.set([{ file: new File(['a'], 'a.png', { type: 'image/png' }), previewUrl: 'blob:a' }]);
      button('Save Product').click();
      await settle();
      create$.next({ data: { id: 9 } });
      await settle();

      expect(imageService.upload).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/products', 'edit', 9], { queryParams: {} });
    });
  });

  describe('loading, duplicate submit and server errors', () => {
    beforeEach(() => setup());

    it('shows a busy Save button and blocks a second submit while saving', async () => {
      await fillValid();
      const save = button('Save Product');
      save.click();
      await settle();
      expect(save.getAttribute('aria-busy')).toBe('true');
      expect(save.textContent).toContain('Saving...');
      expect(button('Save Draft').disabled).toBe(true);
      expect(button('Cancel').disabled).toBe(false);
      expect(q('form').getAttribute('aria-busy')).toBe('true');

      save.click();
      button('Save Draft').click();
      q<HTMLFormElement>('form').dispatchEvent(new Event('submit'));
      cmp.onSubmit();
      await settle();
      expect(productService.create).toHaveBeenCalledTimes(1);
    });

    it('shows the server message in an alert, re-enables the form and lets the user retry', async () => {
      await fillValid();
      button('Save Product').click();
      await settle();
      create$.error({ message: 'Product code MUG-001 already exists' });
      await settle();
      const alert = q('app-ui-alert[data-variant="danger"]');
      expect(alert.getAttribute('role')).toBe('alert');
      expect(alert.textContent).toContain('Product code MUG-001 already exists');
      expect(button('Save Product').getAttribute('aria-busy')).toBeNull();
      expect(button('Save Draft').disabled).toBe(false);
      expect(control('Product Name').value).toBe('Premium Mug');

      create$ = new Subject();
      productService.create.mockReturnValue(create$);
      button('Save Product').click();
      await settle();
      expect(productService.create).toHaveBeenCalledTimes(2);
      expect(q('app-ui-alert[data-variant="danger"]')).toBeNull();
    });
  });

  describe('cancel and navigation', () => {
    it('Cancel goes back without saving', async () => {
      await setup();
      await type('Product Name', 'Unsaved');
      button('Cancel').click();
      expect(location.back).toHaveBeenCalledTimes(1);
      expect(productService.create).not.toHaveBeenCalled();
      expect(productService.update).not.toHaveBeenCalled();
    });

    it('has no unsaved-changes prompt (existing behaviour is unchanged)', async () => {
      await setup();
      await type('Product Name', 'Unsaved');
      const event = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(false);
    });
  });

  describe('SKU generation', () => {
    beforeEach(() => setup());

    it('asks for a name first', async () => {
      qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === 'Generate SKU')!.click();
      expect(window.alert).toHaveBeenCalledWith('Please enter a product name first');
      expect(productService.generateSku).not.toHaveBeenCalled();
    });

    it('fills the SKU from the generator for the entered name', async () => {
      await type('Product Name', 'Premium Mug');
      qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === 'Generate SKU')!.click();
      await settle();
      await settle();
      expect(productService.generateSku).toHaveBeenCalledWith('Premium Mug');
      expect(control('SKU').value).toBe('MUG-GEN-1');
      expect(cmp.sku()).toBe('MUG-GEN-1');
    });

    it('reports a generator failure and keeps the SKU', async () => {
      productService.generateSku.mockReturnValue(throwError(() => new Error('x')));
      await type('Product Name', 'Premium Mug');
      qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === 'Generate SKU')!.click();
      expect(window.alert).toHaveBeenCalledWith('Failed to generate SKU');
      expect(cmp.sku()).toBe('');
    });
  });

  describe('barcode generation and tax defaults', () => {
    it('starts with a blank tax rate and shows the Business default only as a hint', async () => {
      await setup();

      expect(settingsService.getTaxSettings).toHaveBeenCalledTimes(1);
      expect(cmp.taxRate()).toBeNull();
      expect(control('Tax Rate').value).toBe('');
      expect(field('Tax Rate').textContent).toContain('Business default rate (7.5%)');
    });

    it('shows a 0% default hint when tax is disabled and still keeps the rate blank', async () => {
      await setup({ taxSettings: { taxEnabled: false, defaultTaxRate: 7.5 } });

      expect(cmp.taxRate()).toBeNull();
      expect(field('Tax Rate').textContent).toContain('Business default rate (0%)');
    });

    it('does not replace a tax rate edited before settings finish loading', async () => {
      const response = new Subject<unknown>();
      await setup({ taxSettingsResult: response });
      await type('Tax Rate', '4.25');

      response.next({ data: { taxEnabled: true, defaultTaxRate: 7.5 } });
      await settle();

      expect(cmp.taxRate()).toBe(4.25);
    });

    it('sends null for a blank rate', async () => {
      await setup();
      await fillValid();
      button('Save Product').click();
      await settle();
      expect(productService.create.mock.calls[0][0].taxRate).toBeNull();
    });

    it('keeps an explicit 0 as 0 and clearing the field returns to null', async () => {
      await setup();
      await fillValid();
      await type('Tax Rate', '0');
      expect(cmp.taxRate()).toBe(0);
      button('Save Product').click();
      await settle();
      expect(productService.create.mock.calls[0][0].taxRate).toBe(0);

      await type('Tax Rate', '12.5');
      await type('Tax Rate', '');
      expect(cmp.taxRate()).toBeNull();
    });

    it('sends the chosen tax classification and locks the rate for exempt and zero-rated products', async () => {
      await setup();
      await fillValid();
      expect(control('Tax Rate').disabled).toBe(false);
      await choose('Tax Classification', 'Exempt');
      expect(cmp.taxClassification()).toBe('exempt');
      expect(control('Tax Rate').disabled).toBe(true);
      expect(field('Tax Rate').textContent).toContain('always taxed at 0%');
      button('Save Product').click();
      await settle();
      expect(productService.create.mock.calls[0][0]).toMatchObject({ taxRate: null, taxClassification: 'exempt' });

      await choose('Tax Classification', 'Zero-rated');
      expect(cmp.taxClassification()).toBe('zero_rated');
      expect(control('Tax Rate').disabled).toBe(true);
      await choose('Tax Classification', 'Standard');
      expect(control('Tax Rate').disabled).toBe(false);
    });

    it('generates and displays a barcode using the selected category', async () => {
      await setup();
      await choose('Category', 'Homeware');

      qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === 'Generate Barcode')!.click();
      await settle();

      expect(productService.generateBarcode).toHaveBeenCalledWith(2);
      expect(cmp.barcode()).toBe('8800021234565');
      expect(control('Barcode').value).toBe('8800021234565');
    });

    it('reports barcode generation failures without clearing manual input', async () => {
      await setup();
      await type('Barcode', 'manual-barcode');
      productService.generateBarcode.mockReturnValue(throwError(() => new Error('generation failed')));

      qa<HTMLButtonElement>('button').find(b => b.getAttribute('aria-label') === 'Generate Barcode')!.click();
      await settle();

      expect(cmp.barcode()).toBe('manual-barcode');
      expect(alerts.config()?.type).toBe('error');
    });
  });

  describe('edit mode', () => {
    beforeEach(() => setup({ id: '7' }));

    it('loads the product into the form and keeps the authoritative id', () => {
      expect(productService.getById).toHaveBeenCalledWith(7);
      expect(text('app-ui-page-header h1')).toBe('Edit Product');
      expect(text('app-ui-card app-ui-badge')).toBe('Product ID #7');
      expect(control('Product Name').value).toBe('Premium Mug');
      expect(control('Product Code').value).toBe('MUG-001');
      expect(control('SKU').value).toBe('SKU-MUG');
      expect(control('Barcode').value).toBe('8901234567890');
      expect(control('Selling Price').value).toBe('20');
      expect(control('Cost Price').value).toBe('8');
      expect(control('Tax Rate').value).toBe('5');
      expect(control<HTMLSelectElement>('Tax Classification').selectedOptions[0].textContent!.trim()).toBe('Automatic (from tax rate)');
      expect(cmp.categoryId()).toBe(2);
      expect(control<HTMLSelectElement>('Status').value).toBe('draft');
      expect(control('Margin').value).toBe('60.0%');
    });

    it('shows variant management for the saved product and passes the product context to it', () => {
      const manager = q('app-combination-manager');
      expect(manager).not.toBeNull();
      expect(q('app-ui-state')).toBeNull();
      expect(text('app-ui-card:has(app-combination-manager) h2, app-ui-card:has(app-combination-manager) h3')).toContain('Variants');
    });

    it('passes the saved product id to the image uploader', () => {
      expect(imageService.list).toHaveBeenCalledWith(7);
    });

    it('sends the existing update contract (no hasVariants) and returns to the previous page', async () => {
      await type('Selling Price', '25');
      button('Save Product').click();
      await settle();
      expect(productService.update).toHaveBeenCalledWith(7, {
        name: 'Premium Mug', description: 'Ceramic', productCode: 'MUG-001', sku: 'SKU-MUG', barcode: '8901234567890',
        categoryId: 2, basePrice: 25, costPrice: 8, taxRate: 5, taxClassification: null, status: 'draft'
      });
      expect(productService.update.mock.calls[0][1]).not.toHaveProperty('hasVariants');
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Product updated successfully' });
      expect(location.back).toHaveBeenCalledTimes(1);
    });

    it('keeps a stored null rate and classification when saving an unchanged product', async () => {
      productService.getById.mockReturnValue(of({ data: { ...PRODUCT, taxRate: null, taxClassification: 'exempt' } }));
      cmp.loadProduct(7);
      await settle();
      expect(cmp.taxRate()).toBeNull();
      expect(control<HTMLSelectElement>('Tax Classification').value).toContain('exempt');
      expect(control('Tax Rate').disabled).toBe(true);
      button('Save Product').click();
      await settle();
      const sent = productService.update.mock.calls.at(-1)![1];
      expect(sent.taxRate).toBeNull();
      expect(sent.taxClassification).toBe('exempt');
    });

    it.each([
      { label: 'an explicit 0 rate', taxRate: 0, taxClassification: null, expectedRate: 0, expectedClassification: null },
      { label: 'a null rate', taxRate: null, taxClassification: null, expectedRate: null, expectedClassification: null },
      { label: 'exempt with a 0 rate', taxRate: 0, taxClassification: 'exempt', expectedRate: 0, expectedClassification: 'exempt' },
      { label: 'exempt with a stored non-zero rate', taxRate: 5, taxClassification: 'exempt', expectedRate: 5, expectedClassification: 'exempt' },
      { label: 'zero-rated with a null rate', taxRate: null, taxClassification: 'zero_rated', expectedRate: null, expectedClassification: 'zero_rated' }
    ])('round-trips $label unchanged when only the name is edited', async ({ taxRate, taxClassification, expectedRate, expectedClassification }) => {
      productService.getById.mockReturnValue(of({ data: { ...PRODUCT, taxRate, taxClassification } }));
      cmp.loadProduct(7);
      await settle();
      await type('Product Name', 'Renamed Mug');
      button('Save Product').click();
      await settle();

      const sent = productService.update.mock.calls.at(-1)![1];
      expect(sent.name).toBe('Renamed Mug');
      expect(sent.taxRate).toBe(expectedRate);
      expect(sent.taxClassification).toBe(expectedClassification);
    });

    it('shows a 0 rate as 0 (not blank) and disables the rate for exempt while keeping its value', async () => {
      productService.getById.mockReturnValue(of({ data: { ...PRODUCT, taxRate: 0, taxClassification: 'exempt' } }));
      cmp.loadProduct(7);
      await settle();
      expect(control('Tax Rate').value).toBe('0');
      expect(control('Tax Rate').disabled).toBe(true);
      expect(cmp.taxRate()).toBe(0);
    });

    it('shows update errors inline and stays on the page', async () => {
      productService.update.mockReturnValue(throwError(() => ({ message: 'Barcode already used' })));
      button('Save Product').click();
      await settle();
      expect(text('app-ui-alert[data-variant="danger"]')).toContain('Barcode already used');
      expect(location.back).not.toHaveBeenCalled();
      expect(button('Save Product').getAttribute('aria-busy')).toBeNull();
    });
  });

  describe('reference data access denied (403)', () => {
    it('shows an inline access-denied state for categories, keeps the form usable and never signs out', async () => {
      const categories$ = new Subject<unknown>();
      const tax$ = new Subject<unknown>();
      await setup({ categoriesResult: categories$, taxSettingsResult: tax$ });
      await type('Product Name', 'Premium Mug');

      categories$.error({ status: 403 });
      tax$.error({ status: 403 });
      await settle();

      expect(field('Category').textContent).toContain('You do not have access to the category list.');
      expect(field('Tax Rate').textContent).toContain('Leave blank to use the Business default rate. Enter 0');
      expect(control<HTMLInputElement>('Product Name').value).toBe('Premium Mug');
      expect(router.navigate).not.toHaveBeenCalled();
      expect(location.back).not.toHaveBeenCalled();
    });

    it('shows a retryable message for non-authorization category failures', async () => {
      const categories$ = new Subject<unknown>();
      await setup({ categoriesResult: categories$ });

      categories$.error({ status: 500 });
      await settle();

      expect(field('Category').textContent).toContain('Categories could not be loaded. Please try again.');
    });
  });

  describe('edit mode load failure', () => {
    it('keeps the existing alert and navigates back', async () => {
      TestBed.resetTestingModule();
      await setupFailingLoad();
      expect(window.alert).toHaveBeenCalledWith('Failed to load product');
      expect(location.back).toHaveBeenCalledTimes(1);
    });

    async function setupFailingLoad() {
      await setup({ id: '7' });
      TestBed.resetTestingModule();
      create$ = new Subject();
      location = { back: vi.fn() };
      TestBed.configureTestingModule({
        imports: [ProductForm],
        providers: [
          provideRouter([]),
          { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '7' }), queryParams: {} } } },
          { provide: Location, useValue: location },
          { provide: AuthService, useValue: { hasPermission: () => true } },
          { provide: ProductService, useValue: { getById: () => throwError(() => new Error('x')) } },
          { provide: SettingsService, useValue: { getTaxSettings: () => of({ data: { taxEnabled: false, defaultTaxRate: 0 } }) } },
          { provide: ProductImageService, useValue: imageService },
          { provide: CategoryService, useValue: { getAllCategories: () => of({ data: [] }) } },
          { provide: CurrencyService, useValue: { symbol: signal('$') } },
          { provide: ProductVariationService, useValue: { getProductVariations: () => of([]), getCombinations: () => of([]) } },
          { provide: VariationService, useValue: { variations: signal([]), loadVariations: () => undefined } }
        ]
      });
      fixture = TestBed.createComponent(ProductForm);
      await settle();
    }
  });

  describe('Units & Pricing entry point', () => {
    const unitsCard = () => qa('app-ui-card').find(card => card.textContent!.includes('Units & Pricing'))!;

    it('says units unlock after the first save when creating', async () => {
      await setup({ permissions: ['products.create', 'products.edit'] });
      expect(unitsCard().textContent).toContain('Units unlock after the first save');
      expect(unitsCard().querySelector('a')).toBeNull();
    });

    it('links a simple product straight to its standard variant', async () => {
      await setup({ id: '7', permissions: ['products.edit'], product: { ...PRODUCT, hasVariants: false, variants: [{ id: 70 }] } });
      const link = unitsCard().querySelector('a')!;
      expect(link.getAttribute('href')).toBe('/products/7/variants/70/units');
    });

    it('points a product with variants to the action on each variant', async () => {
      await setup({ id: '7', permissions: ['products.edit'], product: { ...PRODUCT, hasVariants: true, variants: [{ id: 70 }, { id: 71 }] } });
      expect(unitsCard().querySelector('a')).toBeNull();
      expect(unitsCard().textContent).toContain('Each variant has its own units');
    });
  });
});
