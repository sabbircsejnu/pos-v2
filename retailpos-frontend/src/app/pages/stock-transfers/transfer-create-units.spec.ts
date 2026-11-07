import { Location } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { PackStateView, VariantUomConfig } from '../../models/uom.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UnsavedChangesService } from '../../services/unsaved-changes.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { TransferCreateComponent } from './transfer-create.component';

const unit = (code: string, precision: number) => ({ id: 1, code, symbol: code, dimension: 'volume' as const, precision, isWhole: precision === 0 });

const attar = (tracked = false): VariantUomConfig => ({
  configId: 40,
  version: 2,
  variantId: 11,
  baseUnit: unit('ml', 2),
  packStateTrackingEnabled: tracked,
  repackingEnabled: false,
  sellingOptions: [],
  purchaseOptions: [
    { id: 1, code: 'L', label: 'Litre', unit: unit('L', 3), factorNumerator: 1000, factorDenominator: 1, baseQuantityPerUnit: 1000, isDefault: false, isSealedPack: false },
    { id: 2, code: 'JAR500', label: 'Jar 500 ml', unit: unit('jar', 0), factorNumerator: 500, factorDenominator: 1, baseQuantityPerUnit: 500, isDefault: false, isSealedPack: true }
  ]
});

const packView: PackStateView = {
  variantId: 11, locationId: 1, locationType: 'outlet', baseUnitCode: 'ml', onHand: 1700, allocatedTotal: 1700, isReconciled: true,
  balances: [
    { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, packCount: 3, baseQuantity: 1500, capitalizedValue: 0 },
    { state: 'loose', packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 200, capitalizedValue: 0 }
  ]
};

describe('TransferCreateComponent units', () => {
  const outlets = [{ id: 1, name: 'Outlet A' }, { id: 2, name: 'Outlet B' }];
  const alerts = { warning: vi.fn(), error: vi.fn(), success: vi.fn() };
  const create = vi.fn();
  const getVariantConfigs = vi.fn();
  const getPackState = vi.fn();
  const searchVariants = vi.fn();
  const variant = { id: 11, sku: 'SKU-11', productName: 'Attar', name: 'Oud', stockQuantity: 1700 };

  async function setup(query: Record<string, string> = {}) {
    await TestBed.configureTestingModule({
      imports: [TransferCreateComponent],
      providers: [
        { provide: StockTransferService, useValue: { create } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: {
          outlets, warehouses: [], destinationOutlets: outlets, destinationWarehouses: [],
          defaultLocationType: 'outlet', defaultLocationId: 1
        } }) } },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: {} },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: Error) => error.message } },
        { provide: ProductImageService, useValue: {} },
        { provide: ProductService, useValue: { searchVariants } },
        { provide: UomService, useValue: { getVariantConfigs, getPackState } },
        { provide: UnsavedChangesService, useValue: {} },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    })
      .overrideComponent(TransferCreateComponent, {
        remove: { imports: [VariantSearchPickerComponent] },
        add: { schemas: [NO_ERRORS_SCHEMA] }
      })
      .compileComponents();
    const fixture = TestBed.createComponent(TransferCreateComponent);
    const component = fixture.componentInstance;
    component.ngOnInit();
    component.toLocationId.set(2);
    return { fixture, component };
  }

  beforeEach(() => {
    vi.clearAllMocks();
    create.mockReturnValue(of({ data: {} }));
    getVariantConfigs.mockReturnValue(of([]));
    getPackState.mockReturnValue(of(packView));
  });

  afterEach(() => TestBed.resetTestingModule());

  it('leaves a plain pcs line and its request exactly as they were', async () => {
    const { component } = await setup();
    component.onVariantSelected(variant);
    component.updateTransferQuantity(0, 5);

    component.saveDraft();

    expect(getPackState).not.toHaveBeenCalled();
    expect(create.mock.calls[0][0].items).toEqual([
      { variantId: 11, quantity: 5, requestedQuantity: 5, transferQuantity: 5, unitCost: 0 }
    ]);
  });

  it('refuses a fractional quantity for plain pcs', async () => {
    const { component } = await setup();
    component.onVariantSelected(variant);
    component.updateTransferQuantity(0, 2.5);

    expect(component.getTransferQuantityError(component.lineItems()[0])).toBe('Transfer quantity must be a whole number.');
    expect(component.canSubmit()).toBe(false);
  });

  it('previews the base equivalent and sends the entered unit for the server to convert', async () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const { component } = await setup();
    component.onVariantSelected(variant);

    component.updateTransferEntry(0, { unitOptionId: 1, quantity: 0.5 });

    expect(component.lineItems()[0].transferQuantity).toBe(500);
    expect(component.totalTransferLabel()).toBe('500 ml');
    component.saveDraft();
    expect(create.mock.calls[0][0].items[0]).toEqual({
      variantId: 11, quantity: 500, requestedQuantity: 500, transferQuantity: 500, unitCost: 0,
      enteredQuantity: 0.5, unitOptionId: 1, expectedConfigVersionId: 40
    });
  });

  it('validates the precision of the chosen unit', async () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const { component } = await setup();
    component.onVariantSelected(variant);

    component.updateTransferEntry(0, { unitOptionId: 2, quantity: 1.5 });

    expect(component.getTransferQuantityError(component.lineItems()[0])).toBe('Enter a whole number for this unit.');
    expect(component.canSubmit()).toBe(false);
  });

  it('never adds unlike units into one total', async () => {
    getVariantConfigs.mockReturnValueOnce(of([])).mockReturnValueOnce(of([attar()]));
    const { component } = await setup();
    component.onVariantSelected({ ...variant, id: 12, sku: 'PCS' });
    component.onVariantSelected(variant);
    component.updateTransferQuantity(0, 2);
    component.updateTransferEntry(1, { unitOptionId: null, quantity: 300 });

    expect(component.totalTransferLabel()).toBe('2 pcs · 300 ml');
  });

  it('sends whole sealed packs and the loose remainder, adding up to the moved quantity', async () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    const { component } = await setup();
    component.onVariantSelected(variant);
    component.updateTransferEntry(0, { unitOptionId: null, quantity: 1100 });
    component.updatePackCount(0, packView.balances[0], 2);

    const line = component.lineItems()[0];
    expect(component.looseRemainder(line)).toBe(100);
    expect(component.getTransferQuantityError(line)).toBeNull();

    component.saveDraft();
    expect(create.mock.calls[0][0].items[0].allocations).toEqual([
      { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, packCount: 2, baseQuantity: 1000 },
      { state: 'loose', packCode: null, baseQtyPerPack: 0, packCount: 0, baseQuantity: 100 }
    ]);
  });

  it('moves the sealed packs a sealed unit names, by default', async () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    const { component } = await setup();
    component.onVariantSelected(variant);

    component.updateTransferEntry(0, { unitOptionId: 2, quantity: 2 });

    const line = component.lineItems()[0];
    expect(component.buildAllocations(line)).toEqual([
      { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, packCount: 2, baseQuantity: 1000 }
    ]);
  });

  it('blocks allocations the source cannot provide', async () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    const { component } = await setup();
    component.onVariantSelected(variant);
    const error = () => component.getAllocationError(component.lineItems()[0]);

    component.updateTransferEntry(0, { unitOptionId: null, quantity: 600 });
    expect(error()).toContain('not enough loose stock');

    component.updatePackCount(0, packView.balances[0], 2);
    expect(error()).toBe('The packs selected are more than the quantity being moved.');

    component.updatePackCount(0, packView.balances[0], 4);
    expect(error()).toContain('Only 3 sealed JAR500');

    component.updatePackCount(0, packView.balances[0], 1.5);
    expect(error()).toBe('Sealed and prepared stock moves in whole packs.');
  });

  it('blocks a pack-tracked line until its stock form can be read, and offers a retry', async () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    getPackState.mockReturnValueOnce(throwError(() => new Error('down')));
    const { component } = await setup();
    component.onVariantSelected(variant);
    expect(component.canSubmit()).toBe(false);
    expect(component.getAllocationError(component.lineItems()[0])).toContain('could not be loaded');

    component.loadPackState(11);

    expect(component.lineItems()[0].packStatus).toBe('ready');
  });

  it('still lets a line be entered in base units when the unit lookup fails', async () => {
    getVariantConfigs.mockReturnValue(throwError(() => new Error('403')));
    const { component } = await setup();
    component.onVariantSelected(variant);
    component.updateTransferQuantity(0, 5);

    expect(component.lineItems()[0].uomStatus).toBe('failed');
    expect(component.getTransferQuantityError(component.lineItems()[0])).toBeNull();
  });

  it('keeps the input and shows the API message when the server refuses', async () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    create.mockReturnValue(throwError(() => new Error('The unit configuration of this item has changed.')));
    const { component } = await setup();
    component.onVariantSelected(variant);
    component.updateTransferEntry(0, { unitOptionId: 1, quantity: 0.5 });

    component.saveDraft();

    expect(alerts.error).toHaveBeenCalledWith('The unit configuration of this item has changed.');
    expect(component.isSubmitting()).toBe(false);
    expect(component.lineItems()[0].enteredQuantity).toBe(0.5);
  });

  describe('recommendation prefill', () => {
    const prefill = {
      fromLocationId: '1', fromLocationType: 'outlet', toLocationId: '2', toLocationType: 'outlet',
      variantId: '11', sku: 'SKU-11', quantity: '2.5'
    };

    beforeEach(() => searchVariants.mockReturnValue(of({ data: [{ ...variant }] })));

    it('accepts a fractional base quantity for a configured item', async () => {
      getVariantConfigs.mockReturnValue(of([attar()]));
      const { component } = await setup(prefill);

      expect(alerts.warning).not.toHaveBeenCalled();
      expect(component.lineItems()[0]).toMatchObject({ transferQuantity: 2.5, enteredQuantity: 2.5, unitOptionId: null });
      expect(create).not.toHaveBeenCalled();
      expect(component.hasUnsavedChanges()).toBe(false);
    });

    it('refuses a fractional quantity for plain pcs', async () => {
      const { component } = await setup(prefill);

      expect(alerts.warning).toHaveBeenCalled();
      expect(component.lineItems()).toEqual([]);
    });

    it('refuses a fractional quantity when the unit cannot be confirmed', async () => {
      getVariantConfigs.mockReturnValue(throwError(() => new Error('403')));
      const { component } = await setup(prefill);

      expect(alerts.warning).toHaveBeenCalled();
      expect(component.lineItems()).toEqual([]);
    });

    it('applies a whole quantity to plain pcs', async () => {
      const { component } = await setup({ ...prefill, quantity: '5' });

      expect(component.lineItems()[0]).toMatchObject({ transferQuantity: 5 });
    });
  });
});
