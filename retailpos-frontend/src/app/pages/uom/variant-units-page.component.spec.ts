import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { UomCatalog, UomUnit, VariantUomConfig, VariantUomConfigView } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { ProductService } from '../../services/product.service';
import { UomService } from '../../services/uom.service';
import { VariantUnitsPageComponent } from './variant-units-page.component';

const unit = (id: number, code: string, dimension: UomUnit['dimension'], quantityPrecision = 0): UomUnit => ({
  id, code, name: code, symbol: code, dimension, quantityPrecision, isGlobal: true, isWholeUnit: quantityPrecision === 0, isActive: true, isInUse: true,
});
const CATALOG: UomCatalog = {
  units: [unit(1, 'pcs', 'count'), unit(2, 'ml', 'volume', 2), unit(3, 'jar', 'packaging'), { ...unit(4, 'old', 'volume', 2), isActive: false }],
  conversions: [],
};
const resolved = (id: number, code: string, dimension: UomUnit['dimension']) => ({ id, code, symbol: code, dimension, precision: 2, isWhole: false });
const CONFIG: VariantUomConfig = {
  configId: 50, version: 3, variantId: 7, baseUnit: resolved(2, 'ml', 'volume'), packStateTrackingEnabled: true, repackingEnabled: false,
  purchaseOptions: [{
    id: 11, code: 'jar500', label: '500 ml Jar', unit: resolved(3, 'jar', 'packaging'), factorNumerator: 500, factorDenominator: 1,
    baseQuantityPerUnit: 500, isDefault: true, isSealedPack: true,
  }],
  sellingOptions: [{
    id: 21, kind: 'pack', code: '12ml', label: '12 ml', unit: null, baseQuantity: 12, factorNumerator: 1, factorDenominator: 1, price: 100,
    isDefault: true, sealedPackCode: null, isPreparedEligible: true, components: [{ variantId: 90, quantity: 1, isRequired: true }],
  }],
};
const PLAIN: VariantUomConfigView = { variantId: 7, hasStockActivity: false, current: null, history: [] };
const CONFIGURED: VariantUomConfigView = {
  variantId: 7, hasStockActivity: true, current: CONFIG,
  history: [{ configId: 50, version: 3, baseUnitCode: 'ml', effectiveFromUtc: '2026-11-01T00:00:00Z', effectiveToUtc: null, createdBy: 5 }],
};
const conflict = () => ({ status: 409, originalError: { error: { message: 'Changed', errorCode: 'UOM_CONFIG_CHANGED' } } });

describe('VariantUnitsPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create(view: VariantUomConfigView, api: Record<string, unknown> = {}, permissions = ['products.edit']) {
    const service = {
      getVariantConfig: vi.fn().mockReturnValue(of(view)),
      getCatalog: vi.fn().mockReturnValue(of(CATALOG)),
      saveVariantConfig: vi.fn().mockReturnValue(of(view)),
      ...api,
    };
    const getById = vi.fn().mockReturnValue(of({ data: { name: 'Attar', hasVariants: false, variants: [{ id: 7, name: 'Std', sku: 'ATR-1' }] } }));
    TestBed.configureTestingModule({
      imports: [VariantUnitsPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: UomService, useValue: service },
        { provide: ProductService, useValue: { getById, searchVariants: vi.fn() } },
        { provide: AuthService, useValue: { hasPermission: (key: string) => permissions.includes(key) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['productId', '1'], ['variantId', '7']]) } } },
      ],
    });
    const fixture = TestBed.createComponent(VariantUnitsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, service, getById, element: fixture.nativeElement as HTMLElement };
  }

  it('shows a pcs item with no form at all until the user asks for setup', async () => {
    const { fixture, page, element } = await create(PLAIN);
    expect(element.textContent).toContain('Sold and stocked in pcs');
    expect(element.querySelector('form')).toBeNull();
    page.startSetup();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(element.querySelector('form')).not.toBeNull();
    expect(page.form.controls.baseUnitId.value).toBe(1);
  });

  it('skips the product lookup for an editor without products.view', async () => {
    const { getById, element } = await create(PLAIN);
    expect(getById).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Item #7');
  });

  it('labels the page from the product when it may be read', async () => {
    const { getById, page } = await create(PLAIN, {}, ['products.edit', 'products.view']);
    expect(getById).toHaveBeenCalledWith(1);
    expect(page.variantLabel()).toBe('Attar (ATR-1)');
  });

  it('shows a load failure with a retry', async () => {
    const getVariantConfig = vi.fn().mockReturnValueOnce(throwError(() => ({ status: 403 }))).mockReturnValue(of(PLAIN));
    const { fixture, element } = await create(PLAIN, { getVariantConfig });
    expect(element.querySelector('app-ui-state')?.textContent).toContain('You do not have permission');
    (element.querySelector('app-ui-state button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Sold and stocked in pcs');
  });

  it('locks the base unit and explains why once the item has stock activity', async () => {
    const { page, element } = await create(CONFIGURED);
    expect(page.form.controls.baseUnitId.disabled).toBe(true);
    expect(element.textContent).toContain('already has stock or transactions');
    expect(page.form.controls.purchaseOptions.at(0).controls.code.disabled).toBe(true);
  });

  it('renders saved options with their packaging', async () => {
    const { element } = await create(CONFIGURED);
    expect(element.querySelector('[aria-label="Purchase option 1"]')).not.toBeNull();
    const pack = element.querySelector('[aria-label="Selling pack 1"]');
    expect(pack?.textContent).toContain('Item #90');
    expect(pack?.textContent).toContain('Add packaging');
    expect(pack?.textContent).toContain('Can be prepared by repacking');
  });

  it('leaves the base unit open for an item with no activity', async () => {
    const { page } = await create({ ...CONFIGURED, hasStockActivity: false });
    expect(page.form.controls.baseUnitId.enabled).toBe(true);
    expect(page.baseUnitOptions().map(option => option.code)).toEqual(['pcs', 'ml']);
  });

  it('shows the version history', async () => {
    const { element } = await create(CONFIGURED);
    expect(element.textContent).toContain('Version history');
    expect(element.textContent).toContain('Version 3');
    expect(element.textContent).toContain('User #5');
  });

  it('saves with the loaded version, the locked base unit and untouched packaging', async () => {
    const { page, service } = await create(CONFIGURED);
    page.save();
    expect(service.saveVariantConfig).toHaveBeenCalledTimes(1);
    const [variantId, expected, input] = service.saveVariantConfig.mock.calls[0];
    expect(variantId).toBe(7);
    expect(expected).toBe(3);
    expect(input.baseUnitId).toBe(2);
    expect(input.purchaseOptions[0]).toMatchObject({ code: 'jar500', unitId: 3, factorNumerator: 500, isSealedPack: true });
    expect(input.sellingOptions[0].components).toEqual([{ componentVariantId: 90, quantity: 1, isRequired: true }]);
    expect(page.notice()).toContain('version 3');
  });

  it('expects version 0 for a first save, so two first setups conflict', async () => {
    const { page, service } = await create(PLAIN);
    page.startSetup();
    page.save();
    expect(service.saveVariantConfig.mock.calls[0][1]).toBe(0);
  });

  it('blocks an invalid setup on the client and lists every problem', async () => {
    const { page, service } = await create(PLAIN);
    page.startSetup();
    page.form.controls.baseUnitId.setValue(2);
    page.save();
    expect(service.saveVariantConfig).not.toHaveBeenCalled();
    expect(page.validationErrors()).toContain('An item measured in a unit other than pcs needs at least one selling option.');
  });

  it('keeps one default purchase option and one default selling option', async () => {
    const { page } = await create(CONFIGURED);
    page.addPurchase();
    const options = page.form.controls.purchaseOptions;
    expect(options.at(1).controls.isDefault.value).toBe(false);
    page.setPurchaseDefault(options.at(1));
    expect(options.controls.map(row => row.controls.isDefault.value)).toEqual([false, true]);
    page.removePurchase(1);
    expect(options.at(0).controls.isDefault.value).toBe(true);

    page.addCustom();
    const custom = page.form.controls.customs.at(0);
    page.toggleSellingDefault(custom, true);
    expect(page.form.controls.packs.at(0).controls.isDefault.value).toBe(false);
    expect(custom.controls.isDefault.value).toBe(true);
    page.toggleSellingDefault(custom, false);
    expect(custom.controls.isDefault.value).toBe(false);
  });

  it('clears dependent marks when tracking is turned off', async () => {
    const { page } = await create(CONFIGURED);
    page.form.controls.repackingEnabled.setValue(true);
    page.form.controls.packStateTrackingEnabled.setValue(false);
    expect(page.form.controls.repackingEnabled.value).toBe(false);
    expect(page.form.controls.purchaseOptions.at(0).controls.isSealedPack.value).toBe(false);
    expect(page.form.controls.packs.at(0).controls.isPreparedEligible.value).toBe(false);
  });

  it('suggests a code for a new option only', async () => {
    const { page } = await create(CONFIGURED);
    page.addPack();
    const row = page.form.controls.packs.at(1);
    row.controls.label.setValue('50 ml Pack');
    page.fillCode(row);
    expect(row.controls.code.value).toBe('50-ml-pack');
    const saved = page.form.controls.packs.at(0);
    saved.controls.label.setValue('Renamed');
    page.fillCode(saved);
    expect(saved.controls.code.value).toBe('12ml');
  });

  it('adds packaging through the picker and refuses the item itself', async () => {
    const { page } = await create(CONFIGURED);
    const row = page.form.controls.packs.at(0);
    page.openPicker(row);
    expect(page.pickerExistingIds()).toEqual([7, 90]);
    page.addComponent();
    expect(page.pickerError()).toBe('Choose a packaging item.');
    page.pickedVariant.set({ id: 91, productName: 'Bottle', name: 'Std', sku: 'BTL' });
    page.pickerQuantity.setValue(0);
    page.addComponent();
    expect(page.pickerError()).toContain('greater than zero');
    page.pickerQuantity.setValue(2);
    page.addComponent();
    expect(page.pickerOpen()).toBe(false);
    expect(row.controls.components.at(1).getRawValue()).toEqual({ componentVariantId: 91, name: 'Bottle (BTL)', quantity: 2, isRequired: true });
  });

  it('keeps the edits on a 409 config change and only replaces them after Reload', async () => {
    const latest: VariantUomConfigView = { ...CONFIGURED, current: { ...CONFIG, version: 4, sellingOptions: [] } };
    const saveVariantConfig = vi.fn().mockReturnValue(throwError(conflict));
    const getVariantConfig = vi.fn().mockReturnValueOnce(of(CONFIGURED)).mockReturnValue(of(latest));
    const { fixture, page, element } = await create(CONFIGURED, { saveVariantConfig, getVariantConfig });

    page.form.controls.packs.at(0).controls.price.setValue(120);
    page.save();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(page.conflict()).toBe(true);
    expect(element.textContent).toContain('Reload latest setup');
    expect(page.form.controls.packs.at(0).controls.price.value).toBe(120);
    expect(getVariantConfig).toHaveBeenCalledTimes(1);

    page.save();
    expect(saveVariantConfig).toHaveBeenCalledTimes(1);

    page.reloadAfterConflict();
    expect(page.conflict()).toBe(false);
    expect(page.current()?.version).toBe(4);
    expect(page.form.controls.packs.length).toBe(0);
  });

  it('shows other API errors and keeps the input', async () => {
    const saveVariantConfig = vi.fn().mockReturnValue(throwError(() => ({
      status: 409, originalError: { error: { message: 'Sealed or prepared packs are still held.', errorCode: 'UOM_PACK_STATE_MISMATCH' } },
    })));
    const { page } = await create(CONFIGURED, { saveVariantConfig });
    page.form.controls.packs.at(0).controls.price.setValue(120);
    page.save();
    expect(page.conflict()).toBe(false);
    expect(page.saveError()).toBe('Sealed or prepared packs are still held.');
    expect(page.form.controls.packs.at(0).controls.price.value).toBe(120);
  });

  it('never saves without products.edit', async () => {
    const { page, service } = await create(CONFIGURED, {}, ['products.view']);
    page.save();
    expect(service.saveVariantConfig).not.toHaveBeenCalled();
  });
});
