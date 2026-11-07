import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { UomCatalog, UomUnit } from '../../models/uom.model';
import { AuthService } from '../../services/auth.service';
import { UomService } from '../../services/uom.service';
import { UomUnitsPageComponent, formatConversion, toUnitRows } from './units-page.component';

const unit = (overrides: Partial<UomUnit>): UomUnit => ({
  id: 1, isGlobal: false, code: 'jar', name: 'Jar', symbol: 'jar', dimension: 'packaging',
  quantityPrecision: 0, isWholeUnit: true, isActive: true, isInUse: false, ...overrides,
});
const CATALOG: UomCatalog = {
  units: [
    unit({ id: 1, isGlobal: true, code: 'L', name: 'Litre', symbol: 'L', dimension: 'volume', quantityPrecision: 3, isWholeUnit: false }),
    unit({ id: 2, isGlobal: true, code: 'ml', name: 'Millilitre', symbol: 'ml', dimension: 'volume', quantityPrecision: 2, isWholeUnit: false }),
    unit({ id: 3, code: 'jar', name: 'Jar' }),
  ],
  conversions: [{ fromUnitId: 1, fromSymbol: 'L', toUnitId: 2, toSymbol: 'ml', numerator: 1000, denominator: 1 }],
};

describe('UomUnitsPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[], api: Partial<Record<keyof UomService, unknown>> = {}) {
    const service = {
      getCatalog: vi.fn().mockReturnValue(of(CATALOG)),
      createUnit: vi.fn(),
      updateUnit: vi.fn(),
      ...api,
    };
    TestBed.configureTestingModule({
      imports: [UomUnitsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: UomService, useValue: service },
        { provide: AuthService, useValue: { hasPermission: (key: string) => permissions.includes(key) } },
      ],
    });
    const fixture = TestBed.createComponent(UomUnitsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, service, element: fixture.nativeElement as HTMLElement };
  }

  it('formats a conversion from the larger unit, whichever way it is stored', () => {
    expect(formatConversion(CATALOG.conversions[0])).toBe('1 L = 1,000 ml');
    expect(formatConversion({ fromUnitId: 2, fromSymbol: 'ml', toUnitId: 1, toSymbol: 'L', numerator: 1, denominator: 1000 })).toBe('1 L = 1,000 ml');
  });

  it('shows dimension, precision and conversions, and says a packaging size belongs to the item', () => {
    const rows = toUnitRows(CATALOG);
    expect(rows[0]).toMatchObject({ dimensionLabel: 'Volume', precisionLabel: '3 decimal places', conversionText: '1 L = 1,000 ml', scope: 'global' });
    expect(rows[2]).toMatchObject({ precisionLabel: 'Whole numbers', conversionText: 'Size set per product', scope: 'business' });
  });

  it('lists units and hides every change for a view-only user', async () => {
    const { element } = await create(['uom.view']);
    expect(element.textContent).toContain('Millilitre');
    expect(element.textContent).not.toContain('Add unit');
    expect(element.querySelector('[aria-label="Edit unit jar"]')).toBeNull();
  });

  it('keeps global units read-only for a manager', async () => {
    const { element } = await create(['uom.view', 'uom.manage']);
    expect(element.querySelector('[aria-label="Edit unit jar"]')).not.toBeNull();
    expect(element.querySelector('[aria-label="Edit unit ml"]')).toBeNull();
    expect(element.textContent).toContain('Read-only');
  });

  it('shows a load failure with a retry and does not log out on 403', async () => {
    const getCatalog = vi.fn().mockReturnValueOnce(throwError(() => ({ status: 403 }))).mockReturnValue(of(CATALOG));
    const { fixture, element } = await create(['uom.view'], { getCatalog });
    expect(element.querySelector('app-ui-state')?.textContent).toContain('You do not have permission');
    (element.querySelector('app-ui-state button') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(getCatalog).toHaveBeenCalledTimes(2);
    expect(element.textContent).toContain('Millilitre');
  });

  it('does not submit an invalid form', async () => {
    const { page, service } = await create(['uom.manage']);
    page.openCreate();
    page.save();
    expect(service.createUnit).not.toHaveBeenCalled();
    page.form.patchValue({ code: 'box', name: 'Box', symbol: 'box', quantityPrecision: 2, isWholeUnit: true });
    page.save();
    expect(page.form.hasError('wholeUnitPrecision')).toBe(true);
    expect(service.createUnit).not.toHaveBeenCalled();
    page.form.patchValue({ quantityPrecision: 5, isWholeUnit: false });
    expect(page.form.controls.quantityPrecision.hasError('max')).toBe(true);
  });

  it('creates once, however often save is pressed, and refreshes the list', async () => {
    const response = new Subject<UomUnit>();
    const createUnit = vi.fn().mockReturnValue(response);
    const { page, service } = await create(['uom.manage'], { createUnit });
    page.openCreate();
    page.form.patchValue({ code: 'box', name: 'Box', symbol: 'bx', dimension: 'packaging' });
    page.save();
    page.save();
    expect(createUnit).toHaveBeenCalledTimes(1);
    expect(createUnit.mock.calls[0][0]).toEqual({
      code: 'box', name: 'Box', symbol: 'bx', dimension: 'packaging', quantityPrecision: 0, isWholeUnit: false, isActive: true,
    });
    response.next(unit({ code: 'box' }));
    response.complete();
    expect(page.dialogOpen()).toBe(false);
    expect(service.getCatalog).toHaveBeenCalledTimes(2);
  });

  it('keeps the input and shows the API message when saving fails', async () => {
    const createUnit = vi.fn().mockReturnValue(throwError(() => ({
      status: 409, originalError: { error: { message: "A unit with code 'box' already exists.", errorCode: 'UOM_DUPLICATE_UNIT' } },
    })));
    const { page } = await create(['uom.manage'], { createUnit });
    page.openCreate();
    page.form.patchValue({ code: 'box', name: 'Box', symbol: 'bx' });
    page.save();
    expect(page.saveError()).toBe("A unit with code 'box' already exists.");
    expect(page.dialogOpen()).toBe(true);
    expect(page.form.controls.code.value).toBe('box');
    expect(page.saving()).toBe(false);
  });

  it('locks code, dimension and lower precision for a unit in use', async () => {
    const { page } = await create(['uom.manage']);
    page.openEdit(toUnitRows({ units: [unit({ id: 3, isInUse: true, isWholeUnit: false, quantityPrecision: 2 })], conversions: [] })[0]);
    expect(page.form.controls.code.disabled).toBe(true);
    expect(page.form.controls.dimension.disabled).toBe(true);
    expect(page.form.controls.isWholeUnit.disabled).toBe(true);
    page.form.controls.quantityPrecision.setValue(1);
    expect(page.form.controls.quantityPrecision.hasError('min')).toBe(true);
    page.form.controls.quantityPrecision.setValue(3);
    expect(page.form.valid).toBe(true);
  });

  it('deactivates through update, keeping every other field', async () => {
    const updateUnit = vi.fn().mockReturnValue(of(unit({ id: 3, isActive: false })));
    const { page } = await create(['uom.manage'], { updateUnit });
    page.askToggle(toUnitRows(CATALOG)[2]);
    page.confirmToggle();
    expect(updateUnit).toHaveBeenCalledWith(3, {
      code: 'jar', name: 'Jar', symbol: 'jar', dimension: 'packaging', quantityPrecision: 0, isWholeUnit: true, isActive: false,
    });
    expect(page.confirmOpen()).toBe(false);
    expect(page.notice()).toContain('deactivated');
  });
});
