import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StockCountDto, StockCountLineDto } from '../../models/stock-count.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { StockCountService } from '../../services/stock-count.service';
import { StockCountDetailsComponent } from './stock-count-details.component';
import { PackStateView, VariantUomConfig } from '../../models/uom.model';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { UomService } from '../../services/uom.service';
import { StockCountUnitEntryComponent } from './stock-count-unit-entry.component';

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
    { id: 2, code: 'JAR500', label: 'Jar 500 ml', unit: unit('jar', 0), factorNumerator: 500, factorDenominator: 1, baseQuantityPerUnit: 500, isDefault: false, isSealedPack: false }
  ]
});

const line: StockCountLineDto = { id: 5, productId: 1, variantId: 11, productName: 'Attar', productCode: 'A', variantName: 'Oud', currentStock: 1200 };

const view: PackStateView = {
  variantId: 11, locationId: 1, locationType: 'outlet', baseUnitCode: 'ml', onHand: 1200, allocatedTotal: 1200, isReconciled: true,
  balances: [
    { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, packCount: 2, baseQuantity: 1000, capitalizedValue: 0 },
    { state: 'loose', packCode: '', baseQtyPerPack: 0, packCount: 0, baseQuantity: 200, capitalizedValue: 0 }
  ]
};

describe('StockCountUnitEntryComponent', () => {
  const saveCountEntries = vi.fn();
  const getPackState = vi.fn();
  let fixture: ComponentFixture<StockCountUnitEntryComponent>;

  async function create(config: VariantUomConfig): Promise<StockCountUnitEntryComponent> {
    await TestBed.configureTestingModule({
      imports: [StockCountUnitEntryComponent],
      providers: [
        { provide: UomService, useValue: { saveCountEntries, getPackState } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: Error) => error.message } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(StockCountUnitEntryComponent);
    fixture.componentRef.setInput('stockCountId', 9);
    fixture.componentRef.setInput('line', line);
    fixture.componentRef.setInput('config', config);
    fixture.componentRef.setInput('locationId', 1);
    fixture.componentRef.setInput('locationType', 'outlet');
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    vi.clearAllMocks();
    saveCountEntries.mockReturnValue(of({ lineId: 5, physicalStock: 1175, difference: -25 }));
    getPackState.mockReturnValue(of(view));
  });

  afterEach(() => TestBed.resetTestingModule());

  it('previews the base total and variance of mixed-unit rows', async () => {
    const entry = await create(attar());
    expect(entry.canSave()).toBe(false);

    entry.setRow(0, { unitOptionId: 2, quantity: 2 });
    entry.addRow();
    entry.setRow(1, { unitOptionId: null, quantity: 175 });
    fixture.detectChanges();

    expect(entry.total()).toBe(1175);
    expect(entry.variance()).toBe(-25);
    expect(fixture.nativeElement.textContent).toContain('1,175 ml');
    expect(fixture.nativeElement.textContent).toContain('-25 ml');
  });

  it('sends each row as entered and reports what the server returns', async () => {
    const entry = await create(attar());
    const saved = vi.fn();
    entry.saved.subscribe(saved);
    entry.setRow(0, { unitOptionId: 2, quantity: 2 });
    entry.addRow();
    entry.setRow(1, { unitOptionId: null, quantity: 175 });

    entry.save();

    expect(saveCountEntries).toHaveBeenCalledWith(9, 5, [
      { unitOptionId: 2, enteredQuantity: 2 },
      { unitOptionId: null, enteredQuantity: 175 }
    ]);
    expect(saved).toHaveBeenCalledWith({ lineId: 5, physicalStock: 1175, difference: -25 });
    expect(getPackState).not.toHaveBeenCalled();
  });

  it('accepts a count of zero but not an invalid row', async () => {
    const entry = await create(attar());

    entry.setRow(0, { unitOptionId: null, quantity: 0 });
    expect(entry.total()).toBe(0);
    expect(entry.variance()).toBe(-1200);

    entry.setRow(0, { unitOptionId: null, quantity: 1.234 });
    expect(entry.total()).toBeNull();
    expect(entry.canSave()).toBe(false);
  });

  it('starts sealed packs at the system count and tags every entry with its stock form', async () => {
    const entry = await create(attar(true));
    expect(entry.packs()).toEqual([
      { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, systemPacks: 2, counted: 2 }
    ]);

    entry.setRow(0, { unitOptionId: null, quantity: 175 });
    entry.save();

    expect(saveCountEntries.mock.calls[0][2]).toEqual([
      { unitOptionId: null, enteredQuantity: 1000, state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500 },
      { unitOptionId: null, enteredQuantity: 175, state: 'loose' }
    ]);
  });

  it('warns when a sealed count differs from the system, and keeps the input when the server refuses', async () => {
    const entry = await create(attar(true));
    saveCountEntries.mockReturnValue(throwError(() => new Error('The count shows 1 sealed JAR500 but the system holds 2.')));
    entry.setPack(0, 1);
    entry.setRow(0, { unitOptionId: null, quantity: 175 });
    fixture.detectChanges();
    expect(entry.packError(entry.packs()[0])).toContain('must equal the system (2)');

    entry.save();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('the system holds 2');
    expect(entry.saving()).toBe(false);
    expect(entry.packs()[0].counted).toBe(1);
    expect(entry.rows()[0].quantity).toBe(175);
  });

  it('does not submit twice', async () => {
    const entry = await create(attar());
    entry.setRow(0, { unitOptionId: null, quantity: 5 });
    entry.saving.set(true);

    entry.save();

    expect(saveCountEntries).not.toHaveBeenCalled();
  });

  it('offers a retry when sealed and loose stock cannot be read', async () => {
    getPackState.mockReturnValueOnce(throwError(() => new Error('down')));
    const entry = await create(attar(true));
    expect(entry.packStatus()).toBe('failed');

    entry.loadPacks();

    expect(entry.packStatus()).toBe('ready');
  });
});

describe('StockCountDetailsComponent mixed-unit entry', () => {
  const alerts = { success: vi.fn(), error: vi.fn(), confirm: vi.fn() };
  let permissions: string[];
  let status: string;

  function render() {
    const count = {
      id: 9, stockCountNo: 'SC-9', locationId: 1, locationType: 'outlet', locationName: 'A', status,
      lines: [line, { ...line, id: 6, variantId: 12, productName: 'Borka', currentStock: 4 }]
    } as unknown as StockCountDto;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: StockCountService, useValue: { getById: () => of({ data: count }) } },
        { provide: UomService, useValue: { getVariantConfigs: () => of([attar()]), saveCountEntries: vi.fn(), getPackState: vi.fn() } },
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } },
        { provide: AlertService, useValue: alerts },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '9' } } } },
        { provide: Router, useValue: {} },
        { provide: Location, useValue: { back: vi.fn() } }
      ]
    });
    const fixture = TestBed.createComponent(StockCountDetailsComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    vi.clearAllMocks();
    permissions = ['inventory.edit'];
    status = 'Draft';
  });

  afterEach(() => TestBed.resetTestingModule());

  const countButtons = (fixture: ComponentFixture<StockCountDetailsComponent>) =>
    Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('tbody button'))
      .filter(button => button.textContent?.includes('Count by unit'));

  it('offers it only on a configured line, and labels that line with its unit', () => {
    const fixture = render();
    const rows: HTMLTableRowElement[] = Array.from(fixture.nativeElement.querySelectorAll('tbody tr'));

    expect(countButtons(fixture)).toHaveLength(1);
    expect(rows[0].textContent).toContain('1200 ml');
    expect(rows[1].textContent).toContain('4 pcs');
  });

  it('needs a draft and permission', () => {
    permissions = [];
    expect(countButtons(render())).toHaveLength(0);
    TestBed.resetTestingModule();

    permissions = ['inventory.edit'];
    status = 'Submitted';
    expect(countButtons(render())).toHaveLength(0);
  });

  it('opens the entry and shows the figures the server returns', () => {
    const fixture = render();
    countButtons(fixture)[0].click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-stock-count-unit-entry')).not.toBeNull();

    fixture.componentInstance.onCountSaved({ lineId: 5, physicalStock: 1175, difference: -25 });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-stock-count-unit-entry')).toBeNull();
    expect(fixture.componentInstance.stockCount()?.lines[0]).toMatchObject({ physicalStock: 1175, difference: -25 });
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('1175 ml');
    expect(alerts.success).toHaveBeenCalled();
  });
});
