import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { PackStateView } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { VariationService } from '../../services/variation.service';
import { PackStatePageComponent } from './pack-state-page.component';

const VIEW: PackStateView = {
  variantId: 7, locationId: 3, locationType: 'outlet', baseUnitCode: 'ml', onHand: 1175, allocatedTotal: 1175, isReconciled: true,
  balances: [
    { state: 'sealed', packCode: 'jar500', baseQtyPerPack: 500, packCount: 2, baseQuantity: 1000, capitalizedValue: 0 },
    { state: 'loose', packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 175, capitalizedValue: 0 },
  ],
};
const VARIANT = { id: 7, productName: 'Attar', name: 'Std', sku: 'ATR-1' };

describe('PackStatePageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[], api: Record<string, unknown> = {}) {
    const service = { getPackState: vi.fn().mockReturnValue(of(VIEW)), openSealedPack: vi.fn().mockReturnValue(of(VIEW)), ...api };
    const access = {
      outlets: [{ id: 3, name: 'Dhanmondi', type: 'outlet' }], warehouses: [],
      isBusinessOwner: false, defaultLocationId: 3, defaultLocationType: 'outlet',
    };
    const load = vi.fn().mockReturnValue(of({ success: true, data: access }));
    TestBed.configureTestingModule({
      imports: [PackStatePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: UomService, useValue: service },
        { provide: UserOutletAccessService, useValue: { load } },
        { provide: AuthService, useValue: {
          hasPermission: (key: string) => permissions.includes(key),
          hasAnyPermission: (keys: string[]) => keys.some(key => permissions.includes(key)),
        } },
        { provide: CurrencyService, useValue: { format: (value: number) => String(value) } },
        { provide: ProductService, useValue: { searchVariants: vi.fn() } },
        { provide: ProductImageService, useValue: { resolveUrl: () => '' } },
        { provide: VariationService, useValue: { getAllVariations: () => of([]) } },
      ],
    });
    const fixture = TestBed.createComponent(PackStatePageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, page: fixture.componentInstance, service, load, settle, element: fixture.nativeElement as HTMLElement };
  }

  it('asks for an item first and uses the location the server defaults to', async () => {
    const { page, service, load, element, settle } = await create(['inventory.pack_state.view', 'products.view']);
    await settle();
    expect(load).toHaveBeenCalledWith('InventoryStockView');
    expect(page.locationKey.value).toBe('outlet:3');
    expect(service.getPackState).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Choose an item and a location');
  });

  it('loads and shows the breakdown for the chosen item and location', async () => {
    const { page, service, element, settle } = await create(['inventory.pack_state.view', 'products.view']);
    page.selectVariant(VARIANT);
    await settle();
    expect(service.getPackState).toHaveBeenCalledWith(7, 3, 'outlet');
    expect(element.textContent).toContain('1,175 ml');
    expect(element.textContent).toContain('Matches stock');
    expect(element.textContent).toContain('jar500');
  });

  it('warns in words when the breakdown does not match stock', async () => {
    const { page, element, settle } = await create(['inventory.pack_state.view', 'products.view'], {
      getPackState: vi.fn().mockReturnValue(of({ ...VIEW, allocatedTotal: 1000, isReconciled: false })),
    });
    page.selectVariant(VARIANT);
    await settle();
    expect(element.textContent).toContain('Pack state does not match stock');
    expect(element.textContent).toContain('Mismatch');
  });

  it('explains an item without tracking instead of showing an error', async () => {
    const { page, element, settle } = await create(['inventory.pack_state.view', 'products.view'], {
      getPackState: vi.fn().mockReturnValue(throwError(() => ({ status: 400, originalError: { error: { errorCode: 'UOM_PACK_STATE_NOT_ENABLED', message: 'No' } } }))),
    });
    page.selectVariant(VARIANT);
    await settle();
    expect(page.notTracked()).toBe(true);
    expect(element.textContent).toContain('does not track sealed and loose stock');
  });

  it('shows access denied on 403 with a retry, without clearing the selection', async () => {
    const { page, element, settle } = await create(['inventory.pack_state.view', 'products.view'], {
      getPackState: vi.fn().mockReturnValue(throwError(() => ({ status: 403 }))),
    });
    page.selectVariant(VARIANT);
    await settle();
    expect(element.querySelector('app-ui-state')?.textContent).toContain('You do not have permission');
    expect(page.variant()?.id).toBe(7);
  });

  it('ignores a slower answer for an item that is no longer selected', async () => {
    const first = new Subject<PackStateView>();
    const getPackState = vi.fn().mockReturnValueOnce(first).mockReturnValue(of({ ...VIEW, variantId: 8, onHand: 5 }));
    const { page } = await create(['inventory.pack_state.view', 'products.view'], { getPackState });
    page.selectVariant(VARIANT);
    page.selectVariant({ ...VARIANT, id: 8 });
    first.next(VIEW);
    expect(page.state()?.onHand).toBe(5);
  });

  it('offers Open only with the manage permission and only on sealed stock', async () => {
    const viewOnly = await create(['inventory.pack_state.view', 'products.view']);
    viewOnly.page.selectVariant(VARIANT);
    await viewOnly.settle();
    expect(viewOnly.element.querySelector('[aria-label="Open sealed pack jar500"]')).toBeNull();
    TestBed.resetTestingModule();

    const manager = await create(['inventory.pack_state.view', 'inventory.pack_state.manage', 'products.view']);
    manager.page.selectVariant(VARIANT);
    await manager.settle();
    expect(manager.element.querySelector('[aria-label="Open sealed pack jar500"]')).not.toBeNull();
    expect(manager.element.querySelectorAll('[aria-label^="Open sealed pack"]').length).toBe(1);
  });

  it('warns when the role cannot search items at all', async () => {
    const { element, settle } = await create(['inventory.pack_state.view']);
    await settle();
    expect(element.textContent).toContain('Finding an item needs a permission');
  });

  async function openDialogFor(api: Record<string, unknown> = {}) {
    const context = await create(['inventory.pack_state.view', 'inventory.pack_state.manage', 'products.view'], api);
    context.page.selectVariant(VARIANT);
    await context.settle();
    context.page.openDialog(context.page.sealedRows()[0]);
    return context;
  }

  it('validates the count against the balance on screen', async () => {
    const { page, service } = await openDialogFor();
    page.openForm.controls.count.setValue(3);
    page.submitOpen();
    page.openForm.controls.count.setValue(1.5);
    page.submitOpen();
    page.openForm.controls.count.setValue(0);
    page.submitOpen();
    expect(service.openSealedPack).not.toHaveBeenCalled();
  });

  it('previews the result without claiming it', async () => {
    const { page } = await openDialogFor();
    page.openForm.controls.count.setValue(2);
    expect(page.openPreview()).toBe('2 sealed jar500 becomes 1,000 ml loose.');
  });

  it('sends the pack identity and updates from the server response', async () => {
    const after: PackStateView = { ...VIEW, balances: [VIEW.balances[1]] };
    const openSealedPack = vi.fn().mockReturnValue(of(after));
    const { page } = await openDialogFor({ openSealedPack });
    page.submitOpen();
    const body = openSealedPack.mock.calls[0][0];
    expect(body).toMatchObject({ variantId: 7, locationId: 3, locationType: 'outlet', packCode: 'jar500', baseQtyPerPack: 500, count: 1 });
    expect(body.idempotencyKey).toMatch(/^pack-open-/);
    expect(page.dialogOpen()).toBe(false);
    expect(page.state()).toBe(after);
  });

  it('prevents a double submit while the request runs', async () => {
    const pending = new Subject<PackStateView>();
    const openSealedPack = vi.fn().mockReturnValue(pending);
    const { page } = await openDialogFor({ openSealedPack });
    page.submitOpen();
    page.submitOpen();
    expect(openSealedPack).toHaveBeenCalledTimes(1);
    pending.complete();
  });

  it('keeps one idempotency key across retries and takes a new one after success', async () => {
    const openSealedPack = vi.fn()
      .mockReturnValueOnce(throwError(() => ({ status: 0 })))
      .mockReturnValueOnce(throwError(() => ({ status: 500, originalError: { error: { message: 'Busy' } } })))
      .mockReturnValue(of(VIEW));
    const { page } = await openDialogFor({ openSealedPack });

    page.submitOpen();
    expect(page.dialogOpen()).toBe(true);
    expect(page.openError()).toContain('Unable to connect');
    page.submitOpen();
    expect(page.openError()).toBe('Busy');
    page.submitOpen();
    expect(page.dialogOpen()).toBe(false);

    const keys = openSealedPack.mock.calls.map(call => call[0].idempotencyKey);
    expect(new Set(keys).size).toBe(1);

    page.openDialog(page.sealedRows()[0]);
    page.submitOpen();
    expect(openSealedPack.mock.calls[3][0].idempotencyKey).not.toBe(keys[0]);
  });

  it('takes a new key when the retry asks for something different', async () => {
    const openSealedPack = vi.fn().mockReturnValueOnce(throwError(() => ({ status: 0 }))).mockReturnValue(of(VIEW));
    const { page } = await openDialogFor({ openSealedPack });
    page.submitOpen();
    page.openForm.controls.count.setValue(2);
    page.submitOpen();
    const [first, second] = openSealedPack.mock.calls.map(call => call[0].idempotencyKey);
    expect(second).not.toBe(first);
  });
});
