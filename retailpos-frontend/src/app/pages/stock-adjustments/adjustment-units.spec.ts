import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { StockAdjustmentDto } from '../../models/stock-adjustment.model';
import { PackStateView, VariantUomConfig } from '../../models/uom.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { SettingsService } from '../../services/settings.service';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AdjustmentCreateComponent } from './adjustment-create.component';
import { AdjustmentDetailsComponent } from './adjustment-details.component';
import { AdjustmentEditComponent } from './adjustment-edit.component';
import { LOOSE_CHOICE, adjustmentEntryError, adjustmentUomPayload, holdsPreparedStock, sealedChoices, signedBaseChange } from './adjustment-uom.logic';

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

const view: PackStateView = {
  variantId: 11, locationId: 1, locationType: 'outlet', baseUnitCode: 'ml', onHand: 1000, allocatedTotal: 1000, isReconciled: true,
  balances: [
    { state: 'sealed', packCode: 'OLD250', baseQtyPerPack: 250, packCount: 2, baseQuantity: 500, capitalizedValue: 0 },
    { state: 'prepared', packCode: 'BOTTLE12', baseQtyPerPack: 12, packCount: 5, baseQuantity: 60, capitalizedValue: 40 }
  ]
};

describe('Adjustment unit rules', () => {
  it('keeps the sign through conversion', () => {
    expect(signedBaseChange(attar(), { unitOptionId: 1, quantity: -0.5 })).toBe(-500);
    expect(signedBaseChange(attar(), { unitOptionId: null, quantity: 2.5 })).toBe(2.5);
    expect(signedBaseChange(attar(), { unitOptionId: null, quantity: null })).toBe(0);
  });

  it('offers held and purchasable sealed packs, never prepared ones', () => {
    expect(sealedChoices(attar(true), view).map(choice => choice.packCode)).toEqual(['OLD250', 'JAR500']);
    expect(holdsPreparedStock(view)).toBe(true);
  });

  it('adjusts sealed stock in whole packs', () => {
    const line = { uomConfig: attar(true), unitOptionId: null, enteredQuantity: 750, packView: view, packChoice: 'sealed|JAR500|500' };

    expect(adjustmentEntryError(line)).toBe('Sealed stock is adjusted in whole packs.');
    expect(adjustmentEntryError({ ...line, enteredQuantity: -1000 })).toBeNull();
  });

  it('requires a stock form for a tracked item', () => {
    expect(adjustmentEntryError({ uomConfig: attar(true), enteredQuantity: 5, packChoice: null })).toContain('loose or sealed');
    expect(adjustmentEntryError({ uomConfig: attar(true), enteredQuantity: 5, packChoice: LOOSE_CHOICE })).toBeNull();
  });

  it('sends the stock form only for a tracked item', () => {
    expect(adjustmentUomPayload({})).toEqual({});
    expect(adjustmentUomPayload({ uomConfig: attar(), unitOptionId: 1, enteredQuantity: -0.5 })).toEqual({
      enteredQuantity: -0.5, unitOptionId: 1, expectedConfigVersionId: 40
    });
    expect(adjustmentUomPayload({ uomConfig: attar(true), enteredQuantity: 5, packChoice: LOOSE_CHOICE })).toMatchObject({ packState: 'loose' });
    expect(adjustmentUomPayload({ uomConfig: attar(true), enteredQuantity: 1000, packChoice: 'sealed|JAR500|500' })).toMatchObject({
      packState: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500
    });
  });
});

describe('Adjustment screens', () => {
  const alerts = { warning: vi.fn(), error: vi.fn(), success: vi.fn() };
  const createBatch = vi.fn();
  const update = vi.fn();
  const getById = vi.fn();
  const getVariantConfigs = vi.fn();
  const getPackState = vi.fn();
  const variant = { id: 11, sku: 'SKU-11', productName: 'Attar', name: 'Oud', stockQuantity: 1000 };

  const draft = (): StockAdjustmentDto => ({
    id: 3, adjustmentNumber: 'ADJ-3', status: 'Draft', locationId: 1, locationType: 'outlet', reason: 'Other',
    lines: [{ id: 1, variantId: 11, productName: 'Attar', variantSku: 'SKU-11', productCode: 'A', barcode: '', previousQuantity: 1000, quantityChange: -500, newQuantity: 500, reason: 'Other' }]
  } as unknown as StockAdjustmentDto);

  const withLine = (overrides: Partial<StockAdjustmentDto['lines'][number]>): StockAdjustmentDto => {
    const adjustment = draft();
    return { ...adjustment, lines: [{ ...adjustment.lines[0], ...overrides }] };
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    createBatch.mockReturnValue(of({ data: {} }));
    update.mockReturnValue(of({ data: {} }));
    getById.mockReturnValue(of({ data: draft() }));
    getVariantConfigs.mockReturnValue(of([]));
    getPackState.mockReturnValue(of(view));
    await TestBed.configureTestingModule({
      imports: [AdjustmentCreateComponent, AdjustmentEditComponent],
      providers: [
        { provide: StockAdjustmentService, useValue: { createBatch, update, getById } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: { outlets: [{ id: 1, name: 'A' }], warehouses: [], defaultLocationType: 'outlet', defaultLocationId: 1 } }) } },
        { provide: SettingsService, useValue: { getInventorySettings: () => of({ data: { allowNegativeStock: false } }) } },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (key: string) => (key === 'id' ? '3' : null) } } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: Error) => error.message } },
        { provide: ProductImageService, useValue: { resolveUrl: () => '' } },
        { provide: UomService, useValue: { getVariantConfigs, getPackState } }
      ]
    })
      .overrideComponent(AdjustmentCreateComponent, {
        remove: { imports: [VariantSearchPickerComponent] },
        add: { schemas: [NO_ERRORS_SCHEMA] }
      })
      .compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  function createPage(): AdjustmentCreateComponent {
    const page = TestBed.createComponent(AdjustmentCreateComponent).componentInstance;
    page.isEditMode.set(false);
    page.locationId.set(1);
    page.reason.set('Damaged');
    return page;
  }

  it('leaves a plain pcs line and its request exactly as they were', () => {
    const page = createPage();
    page.onVariantSelected(variant);
    page.updateLineQuantity(0, -2);

    page.saveDraft();

    expect(createBatch.mock.calls[0][0].items).toEqual([{ variantId: 11, quantityChange: -2, reason: undefined }]);
    expect(page.hasNonPcsLines()).toBe(false);
  });

  it('sends the signed entry and previews the new stock in base units', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const page = createPage();
    page.onVariantSelected(variant);

    page.updateLineEntry(0, { unitOptionId: 1, quantity: -0.25 });

    const line = page.items()[0];
    expect(line.quantityChange).toBe(-250);
    expect(page.getLineNewStock(line)).toBe(750);
    expect(page.lineStockLabel(line, 750)).toBe('750 ml');
    expect(page.totalLabel(-1)).toBe('250 ml');

    page.saveDraft();
    expect(createBatch.mock.calls[0][0].items[0]).toEqual({
      variantId: 11, quantityChange: -250, reason: undefined,
      enteredQuantity: -0.25, unitOptionId: 1, expectedConfigVersionId: 40
    });
  });

  it('blocks a quantity finer than the unit allows', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const page = createPage();
    page.onVariantSelected(variant);

    page.updateLineEntry(0, { unitOptionId: null, quantity: 1.234 });

    expect(page.isLineInvalid(page.items()[0])).toBe(true);
    expect(page.canSubmit()).toBe(false);
  });

  it('defaults a tracked item to loose and sends the chosen sealed form', () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    const page = createPage();
    page.onVariantSelected(variant);
    expect(page.items()[0].packChoice).toBe(LOOSE_CHOICE);

    page.updateLineEntry(0, { unitOptionId: 2, quantity: -1 });
    page.updateLinePackChoice(0, 'sealed|JAR500|500');
    page.saveDraft();

    expect(createBatch.mock.calls[0][0].items[0]).toMatchObject({
      enteredQuantity: -1, unitOptionId: 2, packState: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500
    });
  });

  it('keeps the input and shows the API message when the server refuses', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    createBatch.mockReturnValue(throwError(() => new Error('Prepared packs cannot be adjusted directly.')));
    const page = createPage();
    page.onVariantSelected(variant);
    page.updateLineEntry(0, { unitOptionId: null, quantity: -5 });

    page.saveDraft();

    expect(alerts.error).toHaveBeenCalledWith('Prepared packs cannot be adjusted directly.');
    expect(page.isSubmitting()).toBe(false);
    expect(page.items()[0].enteredQuantity).toBe(-5);
  });

  function editPage(): AdjustmentEditComponent {
    const page = TestBed.createComponent(AdjustmentEditComponent).componentInstance;
    page.ngOnInit();
    return page;
  }

  it('leaves a plain pcs draft request exactly as it was', () => {
    const page = editPage();

    page.save();

    expect(update.mock.calls[0][1].items).toEqual([{ variantId: 11, quantityChange: -500, reason: 'Other', notes: undefined }]);
    expect(page.configuredLines()).toEqual([]);
  });

  it('reopens a tracked draft line as it was entered, and resends its stock form', () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    getById.mockReturnValue(of({ data: withLine({
      quantityChange: -500, packState: 'sealed', packCode: 'OLD250', baseQtyPerPack: 250, uomOptionId: 1, uomLabel: 'Litre', uomUnitCode: 'L', uomEnteredQuantity: -0.5
    }) }));
    const page = editPage();

    expect(page.items()[0]).toMatchObject({ unitOptionId: 1, enteredQuantity: -0.5, packChoice: 'sealed|OLD250|250' });
    expect(page.canSave()).toBe(true);

    page.save();

    expect(update.mock.calls[0][1].items[0]).toEqual({
      variantId: 11, quantityChange: -500, reason: 'Other', notes: undefined,
      enteredQuantity: -0.5, unitOptionId: 1, expectedConfigVersionId: 40,
      packState: 'sealed', packCode: 'OLD250', baseQtyPerPack: 250
    });
  });

  it('keeps a saved sealed pack selectable when it is no longer held or purchasable', () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    getPackState.mockReturnValue(of({ ...view, balances: [] }));
    getById.mockReturnValue(of({ data: withLine({ packState: 'sealed', packCode: 'GONE100', baseQtyPerPack: 100 }) }));
    const page = editPage();

    page.save();

    expect(update.mock.calls[0][1].items[0]).toMatchObject({ packState: 'sealed', packCode: 'GONE100', baseQtyPerPack: 100 });
  });

  it('makes a tracked draft with no saved stock form choose one', () => {
    getVariantConfigs.mockReturnValue(of([attar(true)]));
    const page = editPage();

    page.save();
    expect(update).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalled();

    page.updateLinePackChoice(0, LOOSE_CHOICE);
    page.save();
    expect(update.mock.calls[0][1].items[0]).toMatchObject({ packState: 'loose' });
  });

  it('lets the unit change on a draft and sends the entry for the server to convert', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const page = editPage();
    expect(page.items()[0]).toMatchObject({ enteredQuantity: -500, unitOptionId: null });

    page.updateLineEntry(0, { unitOptionId: 1, quantity: -0.25 });
    page.save();

    expect(update.mock.calls[0][1].items[0]).toEqual({
      variantId: 11, quantityChange: -250, reason: 'Other', notes: undefined,
      enteredQuantity: -0.25, unitOptionId: 1, expectedConfigVersionId: 40
    });
  });

  it('falls back to the base quantity when the saved unit is gone', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    getById.mockReturnValue(of({ data: withLine({ uomOptionId: 99, uomLabel: 'Old', uomEnteredQuantity: -5 }) }));

    expect(editPage().items()[0]).toMatchObject({ unitOptionId: null, enteredQuantity: -500 });
  });

  it('blocks a quantity finer than the unit allows on a draft', () => {
    getVariantConfigs.mockReturnValue(of([attar()]));
    const page = editPage();

    page.updateLineEntry(0, { unitOptionId: null, quantity: -1.234 });
    page.save();

    expect(update).not.toHaveBeenCalled();
  });

  it('keeps each line as it is when the units cannot be loaded, and can retry the lookup', () => {
    getVariantConfigs.mockReturnValueOnce(throwError(() => new Error('down')));
    getById.mockReturnValue(of({ data: withLine({
      packState: 'sealed', packCode: 'OLD250', baseQtyPerPack: 250, uomOptionId: 1, uomEnteredQuantity: -0.5
    }) }));
    const page = editPage();
    expect(page.unitsFailed()).toBe(true);

    page.save();
    expect(update.mock.calls[0][1].items[0]).toEqual({
      variantId: 11, quantityChange: -500, reason: 'Other', notes: undefined,
      enteredQuantity: -0.5, unitOptionId: 1, packState: 'sealed', packCode: 'OLD250', baseQtyPerPack: 250
    });

    getVariantConfigs.mockReturnValue(of([attar()]));
    page.loadLineConfigs(page.adjustment()!);
    expect(page.unitsFailed()).toBe(false);
  });

  it('prevents a double submit of an edit and allows a retry after an error', () => {
    update.mockReturnValueOnce(throwError(() => new Error('Refused')));
    const page = editPage();

    page.save();
    expect(alerts.error).toHaveBeenCalledWith('Refused');
    expect(page.isSaving()).toBe(false);

    page.isSaving.set(true);
    page.save();
    expect(update).toHaveBeenCalledTimes(1);
  });
});

describe('AdjustmentDetailsComponent units', () => {
  const adjustment = (): StockAdjustmentDto => ({
    id: 3, adjustmentNumber: 'ADJ-3', status: 'Approved', locationId: 1, locationType: 'outlet', locationName: 'A', adjusterName: 'B',
    lineCount: 2, totalIncrease: 300, totalDecrease: 2, netQuantityChange: 298,
    lines: [
      { id: 1, variantId: 11, productName: 'Attar', variantSku: 'A', productCode: 'A', barcode: '', previousQuantity: 100, quantityChange: 300, newQuantity: 400, reason: 'Found' },
      { id: 2, variantId: 12, productName: 'Borka', variantSku: 'B', productCode: 'B', barcode: '', previousQuantity: 5, quantityChange: -2, newQuantity: 3, reason: 'Lost' }
    ]
  } as unknown as StockAdjustmentDto);

  function render(configs: VariantUomConfig[], data = adjustment()) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: StockAdjustmentService, useValue: { getById: () => of({ data }) } },
        { provide: UomService, useValue: { getVariantConfigs: () => of(configs) } },
        { provide: AuthService, useValue: { hasPermission: () => false } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '3' } } } },
        { provide: Router, useValue: {} },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: AlertService, useValue: {} },
        { provide: ErrorHandlerService, useValue: {} }
      ]
    });
    const fixture = TestBed.createComponent(AdjustmentDetailsComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.nativeElement.textContent as string;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('keeps the server totals for pcs', () => {
    const text = render([]);

    expect(text).toContain('+298');
    expect(text).toContain('+300');
  });

  it('never shows a total that adds unlike units', () => {
    const text = render([attar()]);

    expect(text).not.toContain('298');
    expect(text).toContain('300 ml · -2 pcs');
    expect(text).toContain('400 ml');
    expect(text).toContain('3 pcs');
  });

  it('shows the entered unit and stock form of a line', () => {
    const lines = adjustment().lines;
    lines[0] = { ...lines[0], uomLabel: 'Litre', uomUnitCode: 'L', uomEnteredQuantity: 0.3, packState: 'sealed', packCode: 'JAR', baseQtyPerPack: 100 };
    const text = render([attar(true)], { ...adjustment(), lines });

    expect(text).toContain('Entered 0.3 Litre');
    expect(text).toContain('Sealed JAR (100 ml each)');
  });

  it('adds nothing to a plain pcs line', () => {
    const text = render([]);

    expect(text).not.toContain('Entered');
    expect(text).not.toContain('Loose');
  });
});
