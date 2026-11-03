import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Product } from '../../models/product.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CategoryService } from '../../services/category.service';
import { CurrencyService } from '../../services/currency.service';
import { ListStateService } from '../../services/list-state.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductList } from './product-list';

const makeProduct = (id: number, over: Partial<Product> = {}): Product => ({
  id,
  name: `Product ${id}`,
  description: `Description ${id}`,
  productCode: `PC${id}`,
  sku: `SKU${id}`,
  barcode: `BC${id}`,
  categoryId: 1,
  categoryName: 'Beverages',
  basePrice: 10 * id,
  costPrice: 4 * id,
  taxRate: 0,
  hasVariants: false,
  status: 'active',
  isActive: true,
  variantCount: 0,
  totalStock: 5,
  stockStatus: 'in_stock',
  variants: [],
  createdAt: new Date(),
  updatedAt: new Date(),
  ...over
});

const listResponse = (products: Product[], totalCount = products.length) => ({
  data: { products, totalCount, pageNumber: 1, pageSize: 10, totalPages: 1, hasNextPage: false, hasPreviousPage: false }
});

interface SetupOptions {
  queryParams?: Record<string, string>;
  permissions?: string[];
}

describe('ProductList (shared UI migration)', () => {
  let fixture: ComponentFixture<ProductList>;
  let http: HttpTestingController;
  let listState: ListStateService;
  let router: Router;
  let alerts: AlertService;

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string) => Array.from(root().querySelectorAll<E>(selector));
  const text = (selector: string) => q(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const flush = () => {
    fixture.detectChanges();
    fixture.detectChanges();
  };
  const searchRequests = () => http.match(r => r.method === 'POST' && r.url.endsWith('/products/search'));
  const expectSearch = (): TestRequest => {
    const matches = searchRequests();
    expect(matches.length).toBe(1);
    return matches[0];
  };
  const respond = async (request: TestRequest, products: Product[], total = products.length) => {
    request.flush(listResponse(products, total));
    await settle();
  };
  const sortButton = (header: string) => qa<HTMLButtonElement>('.ui-table__sort').find(b => b.textContent!.trim() === header)!;
  const choose = (select: HTMLSelectElement, label: string) => {
    select.selectedIndex = Array.from(select.options).findIndex(o => o.textContent!.trim() === label);
    select.dispatchEvent(new Event('change'));
  };

  async function setup(options: SetupOptions = {}, initial: Product[] | null = [makeProduct(1), makeProduct(2)], total?: number) {
    const permissions = options.permissions ?? ['products.view_cost'];
    TestBed.configureTestingModule({
      imports: [ProductList],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParams: options.queryParams ?? {} } } },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } },
        { provide: CategoryService, useValue: { getAllCategories: () => of({ data: [{ id: 1, name: 'Beverages' }, { id: 2, name: 'Snacks' }] }) } },
        { provide: ProductImageService, useValue: { resolveUrl: (p?: string | null) => (p ? `/img/${p}` : '') } },
        { provide: CurrencyService, useValue: { format: (v: number) => `$${Number(v).toFixed(2)}`, formatNumber: (v: number) => Number(v).toFixed(2) } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    listState = TestBed.inject(ListStateService);
    router = TestBed.inject(Router);
    alerts = TestBed.inject(AlertService);
    vi.spyOn(listState, 'update').mockImplementation(() => undefined);
    vi.spyOn(listState, 'clear').mockImplementation(() => undefined);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(ProductList);
    await settle();
    if (initial) {
      await respond(expectSearch(), initial, total);
    }
  }

  afterEach(() => {
    vi.useRealTimers();
    http?.match(() => true);
    TestBed.resetTestingModule();
  });

  describe('initial load and presentation', () => {
    it('shows the tax treatment under the price (null = Business default, 0 = zero-rated, exempt)', async () => {
      await setup(undefined, [
        makeProduct(1, { taxRate: null }),
        makeProduct(2, { taxRate: 0 }),
        makeProduct(3, { taxRate: 8, taxClassification: 'exempt' }),
        makeProduct(4, { taxRate: 8 })
      ]);
      const taxLines = qa('.pl-small').map(el => el.textContent!.trim()).filter(t => t.startsWith('Tax:'));
      expect(taxLines).toEqual(['Tax: Business default', 'Tax: 0% (zero-rated)', 'Tax: Exempt', 'Tax: 8%']);
    });
    it('renders partial and uninitialized statuses from the API independently of the total', async () => {
      await setup(undefined, [
        makeProduct(1, { hasVariants: true, stockStatus: 'partially_in_stock', totalStock: 5 }),
        makeProduct(2, { hasVariants: true, stockStatus: 'not_initialized', totalStock: 0 })
      ]);
      const badges = qa('app-product-stock-badge');
      expect(badges[0].textContent).toContain('Partially In Stock');
      expect(badges[1].textContent).toContain('Stock Not Initialized');
      expect(badges[1].textContent).not.toContain('Out of Stock');
    });

    it('keeps location scope in requests and URL state across filtering and clearing', async () => {
      await setup({ queryParams: { outletId: '7' } }, null);
      const first = expectSearch();
      expect(first.request.body.outletId).toBe(7);
      await respond(first, [makeProduct(1)]);
      expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ outletId: 7 }));
      fixture.componentInstance.clearFilters();
      await settle();
      const cleared = expectSearch();
      expect(cleared.request.body.outletId).toBe(7);
      await respond(cleared, [makeProduct(1)]);
      expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), { outletId: 7, warehouseId: undefined });
    });

    it('shows the shared page header with the Add Product action', async () => {
      await setup();
      expect(text('app-ui-page-header h1')).toBe('Products');
      expect(qa('app-ui-page-header nav[aria-label="Breadcrumb"] li').length).toBe(2);
      expect(text('app-ui-page-header button')).toContain('Add Product');
    });

    it('does not leave a native tooltip on the page header or dialog hosts', async () => {
      await setup();
      expect(q('app-ui-page-header').hasAttribute('title')).toBe(false);
      expect(q('app-ui-confirm-dialog').hasAttribute('title')).toBe(false);
    });

    it('requests the first page through the existing search API with the default filters', async () => {
      await setup(undefined, null);
      const request = expectSearch();
      expect(request.request.body).toEqual({ status: 'active', pageNumber: 1, pageSize: 10, sortBy: 'name', sortOrder: 'asc' });
      expect(q('.ui-table').getAttribute('aria-busy')).toBe('true');
      expect(text('app-ui-state')).toContain('Loading products...');
      await respond(request, [makeProduct(1)]);
      expect(q('.ui-table').getAttribute('aria-busy')).toBeNull();
    });

    it('renders product, variant, image-less, stock and price information', async () => {
      await setup(undefined, [
        makeProduct(1, { hasVariants: true, variantCount: 3, totalStock: 12, status: 'draft' }),
        makeProduct(2, { totalStock: 0, stockStatus: 'out_of_stock', status: 'inactive', productCode: undefined, sku: undefined, barcode: undefined, description: undefined })
      ]);
      const rows = qa('tbody tr');
      expect(rows.length).toBe(2);
      const first = rows[0].textContent!.replace(/\s+/g, ' ');
      expect(first).toContain('Product 1');
      expect(first).toContain('Description 1');
      expect(first).toContain('SKU1');
      expect(first).toContain('BC1');
      expect(first).toContain('PC1');
      expect(first).toContain('Beverages');
      expect(first).toContain('$10.00');
      expect(first).toContain('Cost: $4.00');
      expect(first).toContain('3 variants');
      expect(first).toContain('In Stock');
      expect(first).toContain('Draft');

      const second = rows[1].textContent!.replace(/\s+/g, ' ');
      expect(second).toContain('Single');
      expect(second).toContain('Out of Stock');
      expect(second).toContain('Inactive');
      expect(rows[1].querySelector('.pl-thumb--empty')).not.toBeNull();
      expect(text('.ui-table__range')).toBe('Showing 1–2 of 2');
    });

    it('shows status as visible text, never colour only', async () => {
      await setup(undefined, [makeProduct(1), makeProduct(2, { status: 'draft' })]);
      const badges = qa('tbody app-ui-badge').map(b => b.textContent!.trim());
      expect(badges).toContain('Active');
      expect(badges).toContain('Draft');
    });
  });

  describe('permissions', () => {
    it('shows cost only with products.view_cost', async () => {
      await setup({ permissions: [] });
      expect(q('tbody').textContent).toContain('$10.00');
      expect(q('tbody').textContent).not.toContain('Cost:');
    });

    it('does not show cost when it is zero', async () => {
      await setup(undefined, [makeProduct(1, { costPrice: 0 })]);
      expect(q('tbody').textContent).not.toContain('Cost:');
    });

    it('keeps every existing row action available with accessible names', async () => {
      await setup();
      const actions = qa('tbody tr:first-child .pl-actions > *').map(a => a.getAttribute('aria-label'));
      expect(actions).toEqual(['View details of Product 1', 'Stock transactions of Product 1', 'Edit Product 1', 'Delete Product 1']);
    });
  });

  describe('server-side search', () => {
    it('waits for the 500 ms debounce, then sends one request with page 1', async () => {
      await setup();
      vi.useFakeTimers();
      const input = q<HTMLInputElement>('input[type="search"]');
      for (const value of ['r', 'ri', 'ric', 'rice']) {
        input.value = value;
        input.dispatchEvent(new Event('input'));
        vi.advanceTimersByTime(200);
      }
      flush();
      expect(searchRequests().length).toBe(0);

      vi.advanceTimersByTime(500);
      flush();
      const request = expectSearch();
      expect(request.request.body).toMatchObject({ searchQuery: 'rice', pageNumber: 1 });
      expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ search: 'rice', page: 1 }));
    });

    it('goes back to page 1 when the search changes while on a later page and keeps the search while paging', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      const page2 = expectSearch();
      expect(page2.request.body).toMatchObject({ pageNumber: 2 });
      await respond(page2, [makeProduct(11)], 45);

      const input = q<HTMLInputElement>('input[type="search"]');
      input.value = 'cola';
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      await settle();
      const searchRequest = expectSearch();
      expect(searchRequest.request.body).toMatchObject({ searchQuery: 'cola', pageNumber: 1 });
      await respond(searchRequest, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 30);

      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      expect(expectSearch().request.body).toMatchObject({ searchQuery: 'cola', pageNumber: 2 });
    });

    it('shows the table\'s no-results state with a way back when a search finds nothing', async () => {
      await setup();
      const input = q<HTMLInputElement>('input[type="search"]');
      input.value = 'zzz';
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      await settle();
      await respond(expectSearch(), []);
      expect(text('app-ui-state')).toContain('No matching results');
      q<HTMLButtonElement>('app-ui-state button').click();
      await settle();
      expect(expectSearch().request.body.searchQuery).toBeUndefined();
    });
  });

  describe('server-side sorting', () => {
    it('toggles the default name sort to descending and marks it with aria-sort', async () => {
      await setup();
      const nameHeader = () => qa('thead tr:first-child th').find(th => th.textContent!.includes('Name'))!;
      expect(nameHeader().getAttribute('aria-sort')).toBe('ascending');
      sortButton('Name').click();
      await settle();
      const request = expectSearch();
      expect(request.request.body).toMatchObject({ sortBy: 'name', sortOrder: 'desc', pageNumber: 1 });
      await respond(request, [makeProduct(2), makeProduct(1)]);
      expect(nameHeader().getAttribute('aria-sort')).toBe('descending');
    });

    it.each([['Category', 'category'], ['Price', 'price']])('sorts by %s using the API sortBy value %s', async (header, sortBy) => {
      await setup();
      sortButton(header).click();
      await settle();
      expect(expectSearch().request.body).toMatchObject({ sortBy, sortOrder: 'asc', pageNumber: 1 });
    });

    it('never goes unsorted and keeps columns the API cannot sort non-sortable', async () => {
      await setup();
      sortButton('Name').click();
      await settle();
      await respond(expectSearch(), [makeProduct(1)]);
      sortButton('Name').click();
      await settle();
      expect(expectSearch().request.body).toMatchObject({ sortBy: 'name', sortOrder: 'asc' });
      expect(qa('.ui-table__sort').map(b => b.textContent!.trim())).toEqual(['Name', 'Category', 'Price']);
    });

    it('resets to page 1 when the sort changes', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      await respond(expectSearch(), [makeProduct(11)], 45);
      sortButton('Price').click();
      await settle();
      expect(expectSearch().request.body).toMatchObject({ sortBy: 'price', pageNumber: 1 });
    });
  });

  describe('server-side pagination', () => {
    it('requests the next, last, previous and first pages and shows the total', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      expect(text('.ui-table__range')).toBe('Showing 1–10 of 45');
      expect(text('.ui-table__page-indicator')).toBe('Page 1 of 5');

      q<HTMLButtonElement>('button[aria-label="Last page"]').click();
      await settle();
      const last = expectSearch();
      expect(last.request.body).toMatchObject({ pageNumber: 5, pageSize: 10 });
      await respond(last, [makeProduct(41)], 45);
      expect(text('.ui-table__page-indicator')).toBe('Page 5 of 5');
      expect(q<HTMLButtonElement>('button[aria-label="Next page"]').disabled).toBe(true);

      q<HTMLButtonElement>('button[aria-label="Previous page"]').click();
      await settle();
      expect(expectSearch().request.body).toMatchObject({ pageNumber: 4 });
    });

    it('changes the page size, returns to page 1 and records it in the URL', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      await respond(expectSearch(), [makeProduct(11)], 45);

      const size = q<HTMLSelectElement>('select[aria-label="Rows per page"]');
      size.value = '25';
      size.dispatchEvent(new Event('change'));
      await settle();
      expect(expectSearch().request.body).toMatchObject({ pageSize: 25, pageNumber: 1 });
      expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ pageSize: 25, page: 1 }));
    });
  });

  describe('feature filters', () => {
    it('sends the category and returns to page 1', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      await respond(expectSearch(), [makeProduct(11)], 45);

      choose(q<HTMLSelectElement>('select[aria-label="Filter by category"]'), 'Snacks');
      await settle();
      expect(expectSearch().request.body).toMatchObject({ categoryId: 2, pageNumber: 1, status: 'active' });
    });

    it('sends "all statuses" as no status filter and a chosen status as is', async () => {
      await setup();
      const status = q<HTMLSelectElement>('select[aria-label="Filter by status"]');
      choose(status, 'All Status');
      await settle();
      const all = expectSearch();
      expect(all.request.body.status).toBeUndefined();
      await respond(all, [makeProduct(1)]);

      choose(status, 'Draft');
      await settle();
      expect(expectSearch().request.body).toMatchObject({ status: 'draft' });
    });

    it('explains an empty result differently when filters are narrowing the list', async () => {
      await setup(undefined, []);
      expect(text('app-ui-state')).toContain('No products found');
      choose(q<HTMLSelectElement>('select[aria-label="Filter by category"]'), 'Snacks');
      await settle();
      await respond(expectSearch(), []);
      expect(text('app-ui-state')).toContain('No matching products');
    });

    it('applies advanced filters from the drawer, shows the applied count and keeps page 1', async () => {
      await setup();
      q<HTMLButtonElement>('button:has(.fa-sliders-h)').click();
      await settle();
      const drawer = q('app-advanced-filter-drawer');
      expect(drawer.querySelector('[role="dialog"]')).not.toBeNull();

      choose(drawer.querySelectorAll('select')[0] as HTMLSelectElement, 'With Variants');
      const min = drawer.querySelector<HTMLInputElement>('input[type="number"]')!;
      min.value = '10';
      min.dispatchEvent(new Event('input'));
      await settle();
      qa<HTMLButtonElement>('.afd__footer button').find(b => b.textContent!.includes('Apply Filters'))!.click();
      await settle();

      const request = expectSearch();
      expect(request.request.body).toMatchObject({ hasVariants: true, minPrice: 10, pageNumber: 1 });
      expect(q('app-advanced-filter-drawer')).toBeNull();
      expect(text('button:has(.fa-sliders-h)')).toContain('2');
      expect(listState.update).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ hasVariants: true, minPrice: 10 }));
    });

    it('clears the advanced filters from the drawer', async () => {
      await setup({ queryParams: { hasVariants: 'true', minPrice: '5' } });
      q<HTMLButtonElement>('button:has(.fa-sliders-h)').click();
      await settle();
      expect(text('app-advanced-filter-drawer app-ui-badge')).toContain('2 applied');
      qa<HTMLButtonElement>('.afd__footer button').find(b => b.textContent!.includes('Clear Filters'))!.click();
      await settle();
      const body = expectSearch().request.body;
      expect(body.hasVariants).toBeUndefined();
      expect(body.minPrice).toBeUndefined();
    });

    it('Clear resets search and filters, wipes the URL and reloads the first page', async () => {
      await setup({ queryParams: { search: 'abc', categoryId: '2', status: 'draft', page: '2', pageSize: '25' } }, null);
      await respond(expectSearch(), [makeProduct(1)], 60);
      (listState.update as ReturnType<typeof vi.fn>).mockClear();

      const clear = qa<HTMLButtonElement>('.pl-filters button').find(b => b.textContent!.trim() === 'Clear')!;
      clear.click();
      await settle();

      const request = expectSearch();
      expect(request.request.body).toEqual({ status: 'active', pageNumber: 1, pageSize: 25, sortBy: 'name', sortOrder: 'asc' });
      expect(listState.clear).toHaveBeenCalledTimes(1);
      expect(listState.update).not.toHaveBeenCalled();
      expect(q<HTMLInputElement>('input[type="search"]').value).toBe('');
    });
  });

  describe('URL state', () => {
    it('restores every filter, the search, page, page size and sort from the URL', async () => {
      await setup(
        {
          queryParams: {
            search: 'rice', categoryId: '2', status: 'draft', hasVariants: 'false', minPrice: '5', maxPrice: '9',
            page: '3', pageSize: '25', sortBy: 'price', sortOrder: 'desc'
          }
        },
        null
      );
      const request = expectSearch();
      expect(request.request.body).toEqual({
        searchQuery: 'rice', categoryId: 2, status: 'draft', hasVariants: false, minPrice: 5, maxPrice: 9,
        pageNumber: 3, pageSize: 25, sortBy: 'price', sortOrder: 'desc'
      });
      await respond(request, [makeProduct(1)], 60);
      expect(q<HTMLInputElement>('input[type="search"]').value).toBe('rice');
      const priceHeader = qa('thead tr:first-child th').find(th => th.textContent!.includes('Price'))!;
      expect(priceHeader.getAttribute('aria-sort')).toBe('descending');
      expect(text('.ui-table__page-indicator')).toBe('Page 3 of 3');
      expect(q<HTMLSelectElement>('select[aria-label="Rows per page"]').value).toBe('25');
    });

    it('writes the current state to the URL after each query', async () => {
      await setup();
      sortButton('Price').click();
      await settle();
      expect(listState.update).toHaveBeenLastCalledWith(
        expect.anything(),
        expect.objectContaining({ sortBy: 'price', sortOrder: 'asc', page: 1, pageSize: 10, status: 'active' })
      );
    });
  });

  describe('request cancellation and stale responses', () => {
    it('cancels the in-flight request when a newer query starts and only shows the newer data', async () => {
      await setup(undefined, Array.from({ length: 10 }, (_, i) => makeProduct(i + 1)), 45);
      q<HTMLButtonElement>('button[aria-label="Next page"]').click();
      await settle();
      sortButton('Price').click();
      await settle();

      const [slowPage2, latestSort] = searchRequests();
      expect(slowPage2.request.body).toMatchObject({ pageNumber: 2, sortBy: 'name' });
      expect(latestSort.request.body).toMatchObject({ pageNumber: 1, sortBy: 'price' });
      expect(slowPage2.cancelled).toBe(true);
      expect(latestSort.cancelled).toBe(false);
      expect(q('.ui-table').getAttribute('aria-busy')).toBe('true');

      await respond(latestSort, [makeProduct(7, { name: 'Latest' })], 45);
      expect(q('tbody').textContent).toContain('Latest');
      expect(q('.ui-table').getAttribute('aria-busy')).toBeNull();
    });
  });

  describe('loading and error states', () => {
    it('shows the API error with a Retry that repeats the same query', async () => {
      await setup(undefined, null);
      expectSearch().flush({ error: 'Database unavailable' }, { status: 500, statusText: 'Server Error' });
      await settle();
      const state = q('app-ui-state[kind="error"], app-ui-state');
      expect(state.textContent).toContain('Could not load products');
      expect(state.textContent).toContain('Database unavailable');
      expect(q('tbody')).toBeNull();

      qa<HTMLButtonElement>('app-ui-state button').find(b => b.textContent!.trim() === 'Retry')!.click();
      await settle();
      const retry = expectSearch();
      expect(retry.request.body).toMatchObject({ pageNumber: 1, pageSize: 10, sortBy: 'name' });
      await respond(retry, [makeProduct(1)]);
      expect(q('tbody').textContent).toContain('Product 1');
      expect(q('app-ui-state')).toBeNull();
    });

    it('does not lose the requested page when the load fails', async () => {
      await setup({ queryParams: { page: '3' } }, null);
      expectSearch().flush({ error: 'Nope' }, { status: 500, statusText: 'Server Error' });
      await settle();
      expect(text('.ui-table__page-indicator')).toBe('Page 3');
      expect(searchRequests().length).toBe(0);
    });

    it('shows the empty state for a catalogue without products and offers the page action', async () => {
      await setup(undefined, []);
      expect(text('app-ui-state')).toContain('No products found');
      expect(text('app-ui-state')).toContain('Start by creating your first product.');
      expect(text('app-ui-page-header')).toContain('Add Product');
    });
  });

  describe('navigation and actions', () => {
    it('opens the product when a row is clicked, preserving query params', async () => {
      await setup();
      qa('tbody tr')[0].querySelectorAll('td')[1].click();
      expect(router.navigate).toHaveBeenCalledWith(['/products', 1], { queryParamsHandling: 'preserve' });
    });

    it('edits a product without also opening it', async () => {
      await setup();
      q<HTMLButtonElement>('button[aria-label="Edit Product 2"]').click();
      expect(router.navigate).toHaveBeenCalledTimes(1);
      expect(router.navigate).toHaveBeenCalledWith(['/products/edit', 2], { queryParamsHandling: 'preserve' });
    });

    it('creates a product from the page header action', async () => {
      await setup();
      qa<HTMLButtonElement>('app-ui-page-header button').find(b => b.textContent!.includes('Add Product'))!.click();
      expect(router.navigate).toHaveBeenCalledWith(['/products/create'], { queryParamsHandling: 'preserve' });
    });

    it('opens the image lightbox from a named button without navigating', async () => {
      await setup(undefined, [makeProduct(1, { primaryImageThumb: 't1.jpg', primaryImageMedium: 'm1.jpg' })]);
      const button = q<HTMLButtonElement>('button[aria-label="View image of Product 1"]');
      expect(button.querySelector('img')!.getAttribute('src')).toBe('/img/t1.jpg');
      button.click();
      await settle();
      expect(q('app-image-lightbox')).not.toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('delete', () => {
    const openDelete = async () => {
      q<HTMLButtonElement>('button[aria-label="Delete Product 1"]').click();
      await settle();
    };
    const dialogButtons = () => qa<HTMLButtonElement>('app-ui-confirm-dialog .ui-dialog__footer button');

    it('asks for confirmation first and does nothing when cancelled', async () => {
      await setup();
      await openDelete();
      expect(text('app-ui-confirm-dialog .ui-dialog__body')).toContain('Are you sure you want to delete product "Product 1"?');
      expect(router.navigate).not.toHaveBeenCalled();
      dialogButtons()[0].click();
      await settle();
      http.expectNone(r => r.method === 'DELETE');
      expect(q('app-ui-confirm-dialog .ui-dialog')).toBeNull();
    });

    it('deletes through the API, confirms with an alert and reloads the current page', async () => {
      await setup();
      await openDelete();
      dialogButtons()[1].click();
      await settle();
      const request = http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/products/1'));
      expect(dialogButtons()[1].getAttribute('aria-busy')).toBe('true');
      request.flush({ success: true });
      await settle();
      expect(alerts.config()).toMatchObject({ type: 'success', message: 'Product deleted successfully' });
      const reload = expectSearch();
      expect(reload.request.body).toMatchObject({ pageNumber: 1, pageSize: 10 });
      await respond(reload, [makeProduct(2)]);
      expect(q('tbody').textContent).not.toContain('Product 1');
    });

    it('reports a failed delete in an alert and keeps the list on screen', async () => {
      await setup();
      await openDelete();
      dialogButtons()[1].click();
      await settle();
      http.expectOne(r => r.method === 'DELETE').flush({ error: 'Product has stock' }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(alerts.config()).toMatchObject({ type: 'error', message: 'Product has stock' });
      expect(q('tbody').textContent).toContain('Product 1');
      expect(q('app-ui-state')).toBeNull();
      expect(searchRequests().length).toBe(0);
    });
  });
});
