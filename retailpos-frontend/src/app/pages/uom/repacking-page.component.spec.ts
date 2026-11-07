import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { UiTableQuery } from '../../components/shared/ui';
import { RepackJob, RepackJobPage, VariantUomConfigView } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { VariationService } from '../../services/variation.service';
import { RepackingPageComponent } from './repacking-page.component';

const job = (overrides: Partial<RepackJob> = {}): RepackJob => ({
  id: 1, reference: 'RPK-1', status: 'draft', locationId: 3, locationType: 'outlet', sourceVariantId: 7, sourceName: 'Attar', sourceSku: 'ATR-1',
  outputPackCode: '12ml', outputLabel: '12 ml', packBaseQuantity: 12, outputCount: 20, contentBaseQuantity: 240, baseUnitCode: 'ml',
  contentUnitCost: null, contentValue: null, packagingValue: null, costPerPack: null, notes: null,
  createdAt: '2026-11-01T00:00:00Z', executedAt: null, reversedAt: null,
  components: [{ componentVariantId: 90, componentName: 'Bottle', componentSku: 'BTL', quantity: 20, unitCost: null, value: null }],
  ...overrides,
});
const PAGE: RepackJobPage = { items: [job()], totalCount: 1 };
const resolved = { id: 2, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 2, isWhole: false };
const CONFIG: VariantUomConfigView = {
  variantId: 7, hasStockActivity: true, history: [],
  current: {
    configId: 50, version: 1, variantId: 7, baseUnit: resolved, packStateTrackingEnabled: true, repackingEnabled: true, purchaseOptions: [],
    sellingOptions: [
      {
        id: 21, kind: 'pack', code: '12ml', label: '12 ml', unit: null, baseQuantity: 12, factorNumerator: 1, factorDenominator: 1, price: 100,
        isDefault: true, sealedPackCode: null, isPreparedEligible: true,
        components: [{ variantId: 90, quantity: 1, isRequired: true }, { variantId: 91, quantity: 1, isRequired: false }],
      },
      {
        id: 22, kind: 'pack', code: '50ml', label: '50 ml', unit: null, baseQuantity: 50, factorNumerator: 1, factorDenominator: 1, price: 350,
        isDefault: false, sealedPackCode: null, isPreparedEligible: false, components: [],
      },
    ],
  },
};
const QUERY: UiTableQuery = { search: '', searchColumns: [], filters: {}, sort: null, page: 1, pageSize: 25, sequence: 1, reason: 'init' };
const VARIANT = { id: 7, productName: 'Attar', name: 'Std', sku: 'ATR-1' };
const EXECUTE = ['inventory.repacking.view', 'inventory.repacking.execute', 'products.view'];

describe('RepackingPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[], api: Record<string, unknown> = {}) {
    const service = {
      getRepackJobs: vi.fn().mockReturnValue(of(PAGE)),
      getVariantConfig: vi.fn().mockReturnValue(of(CONFIG)),
      getPackState: vi.fn().mockReturnValue(of({
        variantId: 7, locationId: 3, locationType: 'outlet', baseUnitCode: 'ml', onHand: 300, allocatedTotal: 300, isReconciled: true,
        balances: [{ state: 'loose', packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 300, capitalizedValue: 0 }],
      })),
      createRepackJob: vi.fn().mockReturnValue(of(job())),
      executeRepackJob: vi.fn().mockReturnValue(of(job({ status: 'executed', contentValue: 480, packagingValue: 160, costPerPack: 32 }))),
      cancelRepackJob: vi.fn().mockReturnValue(of(job({ status: 'cancelled' }))),
      reverseRepackJob: vi.fn().mockReturnValue(of(job({ status: 'reversed' }))),
      ...api,
    };
    const access = {
      outlets: [{ id: 3, name: 'Dhanmondi', type: 'outlet' }], warehouses: [],
      isBusinessOwner: false, defaultLocationId: 3, defaultLocationType: 'outlet',
    };
    TestBed.configureTestingModule({
      imports: [RepackingPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: UomService, useValue: service },
        { provide: UserOutletAccessService, useValue: { load: vi.fn().mockReturnValue(of({ success: true, data: access })) } },
        { provide: AuthService, useValue: {
          hasPermission: (key: string) => permissions.includes(key),
          hasAnyPermission: (keys: string[]) => keys.some(key => permissions.includes(key)),
        } },
        { provide: CurrencyService, useValue: { format: (value: number) => `Tk${value}` } },
        { provide: ProductService, useValue: { searchVariants: vi.fn() } },
        { provide: ProductImageService, useValue: { resolveUrl: () => '' } },
        { provide: VariationService, useValue: { getAllVariations: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(RepackingPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, page: fixture.componentInstance, service, settle, element: fixture.nativeElement as HTMLElement };
  }

  it('lists jobs from the server with location names and explains wastage', async () => {
    const { service, element } = await create(['inventory.repacking.view']);
    expect(service.getRepackJobs).toHaveBeenCalled();
    expect(element.textContent).toContain('RPK-1');
    expect(element.textContent).toContain('Dhanmondi');
    expect(element.textContent).toContain('20 x 12 ml');
    expect(element.textContent).toContain('record the loss as a stock adjustment');
  });

  it('offers a link to the stock adjustment only with its permission', async () => {
    const without = await create(['inventory.repacking.view']);
    expect(without.element.querySelector('a[href="/stock-adjustments/create"]')).toBeNull();
    TestBed.resetTestingModule();
    const allowed = await create(['inventory.repacking.view', 'stock_adjustments.create']);
    expect(allowed.element.querySelector('a[href="/stock-adjustments/create"]')).not.toBeNull();
  });

  it('maps table filters to the API', async () => {
    const { page, service } = await create(['inventory.repacking.view']);
    service.getRepackJobs.mockClear();
    page.onQuery({ ...QUERY, page: 2, sequence: 5, filters: { status: 'executed', locationText: 'warehouse:9' } });
    expect(service.getRepackJobs).toHaveBeenCalledWith({ status: 'executed', locationType: 'warehouse', locationId: 9, page: 2, pageSize: 25 });
    expect(page.responseSequence()).toBe(5);
  });

  it('shows a list failure through the table, with the API message', async () => {
    const { page } = await create(['inventory.repacking.view'], {
      getRepackJobs: vi.fn().mockReturnValue(throwError(() => ({ status: 403 }))),
    });
    expect(page.loadError()).toBe('You do not have permission to perform this action.');
    expect(page.loading()).toBe(false);
  });

  it('hides create, execute, cancel and reverse from a view-only user', async () => {
    const { fixture, page, element } = await create(['inventory.repacking.view']);
    expect(element.textContent).not.toContain('New repack job');
    page.review(job());
    fixture.detectChanges();
    await fixture.whenStable();
    expect(element.textContent).toContain('Calculated when executed');
    const labels = Array.from(element.querySelectorAll('.ui-dialog button')).map(button => button.textContent?.trim());
    expect(labels).not.toContain('Execute');
    expect(labels).not.toContain('Cancel job');
  });

  describe('create', () => {
    async function openWithSource(api: Record<string, unknown> = {}, permissions = EXECUTE) {
      const context = await create(permissions, api);
      context.page.openCreate();
      context.page.selectSource(VARIANT);
      await context.settle();
      return context;
    }

    it('offers only packs that can be prepared', async () => {
      const { page } = await openWithSource();
      expect(page.selectedLocation()?.key).toBe('outlet:3');
      expect(page.packs().map(pack => pack.id)).toEqual([21]);
    });

    it('explains an item that cannot be repacked', async () => {
      const off: VariantUomConfigView = { ...CONFIG, current: { ...CONFIG.current!, repackingEnabled: false } };
      const { page } = await openWithSource({ getVariantConfig: vi.fn().mockReturnValue(of(off)) });
      expect(page.packs()).toEqual([]);
      expect(page.sourceError()).toContain('Repacking is not turned on');
    });

    it('previews content and required packaging only, and shows loose stock when readable', async () => {
      const { page, element, settle } = await openWithSource({}, [...EXECUTE, 'inventory.view']);
      page.createForm.patchValue({ optionId: 21, count: 20 });
      await settle();
      expect(page.preview()?.content).toBe('240 ml');
      expect(page.preview()?.packaging).toEqual(['Packaging item #90: 20']);
      expect(page.looseAvailable()).toBe(300);
      expect(element.textContent).toContain('300 ml loose available');
    });

    it('does not ask for pack state without a permission that may read it', async () => {
      const { service } = await openWithSource({}, ['inventory.repacking.view', 'inventory.repacking.execute', 'products.edit']);
      expect(service.getPackState).not.toHaveBeenCalled();
    });

    it('requires a source item and a whole count', async () => {
      const { page, service } = await create(EXECUTE);
      page.openCreate();
      page.submitCreate();
      expect(page.createError()).toBe('Choose a source item.');
      page.selectSource(VARIANT);
      page.createForm.patchValue({ optionId: 21, count: 1.5 });
      page.submitCreate();
      expect(service.createRepackJob).not.toHaveBeenCalled();
    });

    it('creates a draft, opens it for review and refreshes the list', async () => {
      const { page, service } = await openWithSource();
      page.onQuery(QUERY);
      service.getRepackJobs.mockClear();
      page.createForm.patchValue({ optionId: 21, count: 20, notes: ' batch 4 ' });
      page.submitCreate();
      const body = service.createRepackJob.mock.calls[0][0];
      expect(body).toMatchObject({ sourceVariantId: 7, locationId: 3, locationType: 'outlet', outputSellingOptionId: 21, outputCount: 20, notes: 'batch 4' });
      expect(body.idempotencyKey).toMatch(/^repack-/);
      expect(page.createOpen()).toBe(false);
      expect(page.reviewOpen()).toBe(true);
      expect(page.job()?.reference).toBe('RPK-1');
      expect(service.getRepackJobs).toHaveBeenCalledTimes(1);
    });

    it('keeps the input and the key across a failed create, and prevents double submit', async () => {
      const pending = new Subject<RepackJob>();
      const createRepackJob = vi.fn()
        .mockReturnValueOnce(throwError(() => ({ status: 0 })))
        .mockReturnValueOnce(pending);
      const { page } = await openWithSource({ createRepackJob });
      page.createForm.patchValue({ optionId: 21, count: 20 });
      page.submitCreate();
      expect(page.createOpen()).toBe(true);
      expect(page.createError()).toContain('Unable to connect');
      expect(page.createForm.controls.count.value).toBe(20);
      page.submitCreate();
      page.submitCreate();
      expect(createRepackJob).toHaveBeenCalledTimes(2);
      expect(createRepackJob.mock.calls[1][0].idempotencyKey).toBe(createRepackJob.mock.calls[0][0].idempotencyKey);
      pending.complete();
    });
  });

  describe('review actions', () => {
    it('executes only after confirmation and shows server values, never computing its own', async () => {
      const { fixture, page, service, element } = await create(EXECUTE);
      page.review(job());
      page.askAction('execute');
      expect(service.executeRepackJob).not.toHaveBeenCalled();
      page.runAction();
      expect(service.executeRepackJob).toHaveBeenCalledWith(1);
      expect(page.confirmOpen()).toBe(false);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(element.textContent).toContain('Tk480');
      expect(element.textContent).toContain('Tk160');
      expect(element.textContent).toContain('Tk32');
    });

    it('renders the allowed actions in the dialog footer, by status', async () => {
      const { page, settle, element } = await create(EXECUTE);
      const footer = () => Array.from(element.querySelectorAll('.ui-dialog__footer button')).map(button => button.textContent?.trim());
      page.review(job());
      await settle();
      expect(footer()).toEqual(['Cancel job', 'Execute', 'Close']);
      page.review(job({ status: 'executed' }));
      await settle();
      expect(footer()).toEqual(['Reverse', 'Close']);
      page.review(job({ status: 'reversed' }));
      await settle();
      expect(footer()).toEqual(['Close']);
    });

    it('never shows a missing value as zero', async () => {
      const { page } = await create(EXECUTE);
      const executed = job({ status: 'executed', contentValue: null, packagingValue: 0 });
      expect(page.money(executed, executed.contentValue)).toBe('Not valued');
      expect(page.money(executed, executed.packagingValue)).toBe('Tk0');
      expect(page.money(job(), null)).toBe('Calculated when executed');
    });

    it('prevents a double execute', async () => {
      const pending = new Subject<RepackJob>();
      const executeRepackJob = vi.fn().mockReturnValue(pending);
      const { page } = await create(EXECUTE, { executeRepackJob });
      page.review(job());
      page.askAction('execute');
      page.runAction();
      page.runAction();
      expect(executeRepackJob).toHaveBeenCalledTimes(1);
      pending.complete();
    });

    it('cancels a draft and reverses an executed job', async () => {
      const { page, service } = await create(EXECUTE);
      page.review(job());
      page.askAction('cancel');
      page.runAction();
      expect(service.cancelRepackJob).toHaveBeenCalledWith(1);
      expect(page.job()?.status).toBe('cancelled');

      page.review(job({ status: 'executed' }));
      page.askAction('reverse');
      page.runAction();
      expect(service.reverseRepackJob).toHaveBeenCalledWith(1);
      expect(page.job()?.status).toBe('reversed');
    });

    it('shows a blocked reverse and refreshes the list', async () => {
      const reverseRepackJob = vi.fn().mockReturnValue(throwError(() => ({
        status: 409, originalError: { error: { errorCode: 'UOM_REPACK_REVERSE_BLOCKED', message: 'Some of these packs have already been sold or moved.' } },
      })));
      const { page, service } = await create(EXECUTE, { reverseRepackJob });
      page.onQuery(QUERY);
      service.getRepackJobs.mockClear();
      page.review(job({ status: 'executed' }));
      page.askAction('reverse');
      page.runAction();
      expect(page.actionError()).toBe('Some of these packs have already been sold or moved.');
      expect(page.reviewOpen()).toBe(true);
      expect(page.job()?.status).toBe('executed');
      expect(service.getRepackJobs).toHaveBeenCalledTimes(1);
    });
  });
});
