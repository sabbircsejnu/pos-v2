import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PackStateView, SellingOption, VariantUomConfig } from '../../models/uom.model';
import { CurrencyService } from '../../services/currency.service';
import { UomService } from '../../services/uom.service';
import { PosUomPickerComponent } from './pos-uom-picker.component';
import { PosUomSelection } from './pos-uom';

const ml = { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 1, isWhole: false };

const pack: SellingOption = {
  id: 10, kind: 'pack', code: 'P12', label: '12 ml bottle', unit: null, baseQuantity: 12,
  factorNumerator: 1, factorDenominator: 1, price: 500, isDefault: true, sealedPackCode: null,
  isPreparedEligible: true, components: [{ variantId: 9, quantity: 1, isRequired: false }],
};
const custom: SellingOption = {
  id: 11, kind: 'custom', code: 'CUSTOM', label: 'Custom', unit: ml, baseQuantity: null,
  factorNumerator: 1, factorDenominator: 1, price: 40, isDefault: false, sealedPackCode: null,
  isPreparedEligible: false, components: [],
};

const config = (over: Partial<VariantUomConfig> = {}): VariantUomConfig => ({
  configId: 77, version: 1, variantId: 5, baseUnit: ml, packStateTrackingEnabled: false, repackingEnabled: false,
  purchaseOptions: [], sellingOptions: [pack, custom], ...over,
});

const packState: PackStateView = {
  variantId: 5, locationId: 3, locationType: 'outlet', baseUnitCode: 'ml', onHand: 500, allocatedTotal: 500, isReconciled: true,
  balances: [
    { state: 'loose', packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 476, capitalizedValue: 0 },
    { state: 'prepared', packCode: 'P12', baseQtyPerPack: 12, packCount: 2, baseQuantity: 24, capitalizedValue: 40 },
  ],
};

describe('PosUomPickerComponent', () => {
  let fixture: ComponentFixture<PosUomPickerComponent>;
  let picker: PosUomPickerComponent;
  let uom: { quote: ReturnType<typeof vi.fn>; getPackState: ReturnType<typeof vi.fn>; getVariantConfigs: ReturnType<typeof vi.fn> };
  let picked: PosUomSelection[];
  const root = () => fixture.nativeElement as HTMLElement;

  async function setup(current: VariantUomConfig = config()): Promise<void> {
    vi.useFakeTimers();
    uom = {
      quote: vi.fn((_variantId: number, _optionId: number, quantity: number) => of({
        selectedUnit: 'x', enteredQuantity: quantity, baseUnit: 'ml', baseQuantity: quantity * 12,
        priceBeforeDiscountAndVat: quantity * 500, effectiveConfigVersionId: 77, isPreviewOnly: true,
      })),
      getPackState: vi.fn(() => of(packState)),
      getVariantConfigs: vi.fn(() => of([current])),
    };
    await TestBed.configureTestingModule({
      imports: [PosUomPickerComponent],
      providers: [
        { provide: UomService, useValue: uom },
        { provide: CurrencyService, useValue: { format: (value: number) => `${value}` } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PosUomPickerComponent);
    fixture.componentRef.setInput('config', current);
    fixture.componentRef.setInput('productName', 'Attar');
    fixture.componentRef.setInput('outletId', 3);
    fixture.componentRef.setInput('knownStock', 500);
    picker = fixture.componentInstance;
    picked = [];
    picker.picked.subscribe(selection => picked.push(selection));
    fixture.detectChanges();
  }

  const settle = () => {
    vi.advanceTimersByTime(200);
    fixture.detectChanges();
  };

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('starts on the default pack with one pack and adds it on confirm', async () => {
    await setup();
    expect(picker.option()?.id).toBe(10);
    expect(picker.quantityText()).toBe('1');

    picker.confirm();

    expect(picked[0]).toMatchObject({ configId: 77, quantity: 1, includePackaging: false });
    expect(picked[0].option.id).toBe(10);
  });

  it('previews the price and base stock from the server quote', async () => {
    await setup();
    picker.setQuantity('2');
    settle();

    expect(uom.quote).toHaveBeenLastCalledWith(5, 10, 2, 77);
    expect(picker.quote()?.baseQuantity).toBe(24);
    expect(picker.stockAfter()).toBe(476);
    expect(root().textContent).toContain('Uses 24 ml');
  });

  it('refuses a fractional pack count and a custom quantity beyond the unit precision', async () => {
    await setup();
    picker.setQuantity('1.5');
    expect(picker.quantityError()).toContain('whole number');
    picker.confirm();
    expect(picked).toEqual([]);

    picker.selectOption(custom);
    expect(picker.quantityText()).toBe('');
    picker.setQuantity('2.55');
    expect(picker.quantityError()).toContain('1 decimal');
    picker.setQuantity('2.5');
    expect(picker.quantityError()).toBe('');
    picker.confirm();
    expect(picked[0]).toMatchObject({ quantity: 2.5 });
  });

  it('does not ask for a quote while the quantity is invalid', async () => {
    await setup();
    settle();
    uom.quote.mockClear();
    picker.setQuantity('1.5');
    settle();
    expect(uom.quote).not.toHaveBeenCalled();
    expect(picker.quote()).toBeNull();
  });

  it('chooses options with the digit keys, but digits typed in the quantity field stay there', async () => {
    await setup();
    const panel = root().querySelector('.uom-picker') as HTMLElement;

    panel.dispatchEvent(new KeyboardEvent('keydown', { key: '2', bubbles: true }));
    expect(picker.option()?.id).toBe(11);

    picker.selectOption(pack);
    fixture.detectChanges();
    const input = root().querySelector('#pos-uom-quantity') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: '2', bubbles: true }));
    expect(picker.option()?.id).toBe(10);
  });

  it('moves between options with the arrow keys and adds the card on Enter', async () => {
    await setup();
    const panel = root().querySelector('.uom-picker') as HTMLElement;
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(picker.option()?.id).toBe(11);
    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(picker.option()?.id).toBe(10);
    fixture.detectChanges();

    const card = root().querySelector('[data-option-id="10"]') as HTMLButtonElement;
    card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(picked).toHaveLength(1);
  });

  it('offers packaging for a counter-filled pack and sends the choice', async () => {
    await setup();
    expect(picker.showPackagingToggle()).toBe(true);
    picker.includePackaging.set(true);
    picker.confirm();
    expect(picked[0].includePackaging).toBe(true);
  });

  it('shows stock forms with availability for a pack-tracked variant', async () => {
    await setup(config({ packStateTrackingEnabled: true }));
    fixture.detectChanges();

    expect(picker.forms()).toEqual(['loose', 'prepared']);
    expect(picker.form()).toBe('loose');
    expect(picker.formAvailability('prepared')).toBe('2 pack(s) available');
    expect(picker.formAvailability('loose')).toBe('476 ml available');
    expect(uom.getPackState).toHaveBeenCalledWith(5, 3, 'outlet');

    picker.includePackaging.set(true);
    picker.setForm('prepared');
    expect(picker.showPackagingToggle()).toBe(false);
    picker.confirm();
    expect(picked[0]).toMatchObject({ fulfilmentState: 'prepared', includePackaging: false });
  });

  it('never reads pack state for a variant that does not track it, and sends no form', async () => {
    await setup();
    expect(uom.getPackState).not.toHaveBeenCalled();
    picker.confirm();
    expect(picked[0].fulfilmentState).toBeUndefined();
  });

  it('adopts the new units and keeps the picker open when the quote reports a changed configuration', async () => {
    const fresh = config({ configId: 78, sellingOptions: [{ ...pack, id: 20, price: 550 }] });
    await setup();
    uom.getVariantConfigs.mockReturnValue(of([fresh]));
    const changed: Array<VariantUomConfig | null> = [];
    picker.configChanged.subscribe(value => changed.push(value));
    uom.quote.mockReturnValueOnce(throwError(() => ({ error: { errorCode: 'UOM_CONFIG_CHANGED', message: 'x' } })));

    picker.setQuantity('3');
    settle();

    expect(changed).toEqual([fresh]);
    expect(picker.option()?.id).toBe(20);
    expect(picker.notice()).toContain('have changed');
    picker.confirm();
    expect(picked[0]).toMatchObject({ configId: 78 });
  });

  it('keeps working when pack state cannot be loaded', async () => {
    await setup(config({ packStateTrackingEnabled: true }));
    TestBed.resetTestingModule();
    uom.getPackState.mockReturnValue(throwError(() => new Error('offline')));
    await TestBed.configureTestingModule({
      imports: [PosUomPickerComponent],
      providers: [
        { provide: UomService, useValue: uom },
        { provide: CurrencyService, useValue: { format: (value: number) => `${value}` } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PosUomPickerComponent);
    fixture.componentRef.setInput('config', config({ packStateTrackingEnabled: true }));
    fixture.componentRef.setInput('productName', 'Attar');
    fixture.componentRef.setInput('outletId', 3);
    fixture.detectChanges();

    expect(fixture.componentInstance.packStateFailed()).toBe(true);
    expect(fixture.componentInstance.option()?.id).toBe(10);
  });
});
