import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { VariantUomConfig } from '../../models/uom.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CompanyDateService } from '../../services/company-date.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { GrnService } from '../../services/grn.service';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { UomService } from '../../services/uom.service';
import { GrnCreateComponent } from './grn-create.component';
import { GrnDetailsComponent } from './grn-details.component';

const ml = { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 1, isWhole: false };
const config = (over: Partial<VariantUomConfig> = {}): VariantUomConfig => ({
  configId: 77, version: 1, variantId: 5, baseUnit: ml, packStateTrackingEnabled: true, repackingEnabled: false, sellingOptions: [],
  purchaseOptions: [{ id: 30, code: 'BOX', label: 'Box of 100', unit: ml, factorNumerator: 100, factorDenominator: 1, baseQuantityPerUnit: 100, isDefault: true, isSealedPack: true }],
  ...over,
});

const pendingPo = {
  id: 7, orderNumber: 'PO-000007', supplierName: 'S', warehouseName: '', locationType: 'outlet', locationId: 1, locationName: 'Main', orderDate: '2026-01-01T00:00:00Z', totalAmount: 600,
  items: [
    { id: 1, variantId: 5, productName: 'Attar', quantity: 300, unitPrice: 2 },
    { id: 2, variantId: 6, productName: 'Shirt', quantity: 4, unitPrice: 50 },
  ],
};
const poDetail = {
  items: [
    { id: 1, variantId: 5, quantity: 300, unitPrice: 2, uomUnitCode: 'BOX', uomLabel: 'Box of 100', uomEnteredQuantity: 3, uomEnteredUnitPrice: 200, uomOptionId: 30 },
    { id: 2, variantId: 6, quantity: 4, unitPrice: 50 },
  ],
};

describe('GRN create with units', () => {
  let fixture: ComponentFixture<GrnCreateComponent>;
  let page: GrnCreateComponent;
  const grn = { isLoading: signal(false), error: signal(null), getPendingPOs: vi.fn(), create: vi.fn() };
  const alerts = { error: vi.fn(), success: vi.fn() };
  const uom = { getVariantConfigs: vi.fn() };
  const purchaseOrders = { getById: vi.fn() };
  const auth = { hasPermission: vi.fn() };

  beforeEach(async () => {
    vi.clearAllMocks();
    grn.getPendingPOs.mockReturnValue(of({ data: [pendingPo] }));
    grn.create.mockReturnValue(of({ data: { id: 1 } }));
    uom.getVariantConfigs.mockReturnValue(of([config()]));
    purchaseOrders.getById.mockReturnValue(of({ data: poDetail }));
    auth.hasPermission.mockReturnValue(true);
    await TestBed.configureTestingModule({
      imports: [GrnCreateComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: GrnService, useValue: grn },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'server said no' } },
        { provide: CurrencyService, useValue: { format: (value: number) => `$${value}` } },
        { provide: UomService, useValue: uom },
        { provide: PurchaseOrderService, useValue: purchaseOrders },
        { provide: AuthService, useValue: auth },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(GrnCreateComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => TestBed.resetTestingModule());

  const open = () => {
    page.selectedPoId.set(7);
    page.onPoSelected(7);
    fixture.detectChanges();
  };
  const attar = () => page.lineItems()[0];
  const shirt = () => page.lineItems()[1];
  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const sent = () => grn.create.mock.calls[0][0].items;

  it('reads the ordered base quantity the pending endpoint sends and defaults acceptance to it', () => {
    open();
    expect(attar().orderedQty).toBe(300);
    expect(attar().receivedQty).toBe(300);
    expect(shirt().receivedQty).toBe(4);
  });

  it('shows the ordered purchase unit and its price next to the base quantity', () => {
    open();
    expect(text()).toContain('Ordered as 3 Box of 100');
    expect(text()).toContain('$200 per Box of 100');
    expect(page.baseQtyPerPack(attar())).toBe(100);
  });

  it('shows sealed and loose fields only for a pack-tracked line', () => {
    open();
    const root = fixture.nativeElement as HTMLElement;
    expect(page.isPackTracked(attar())).toBe(true);
    expect(page.isPackTracked(shirt())).toBe(false);
    expect(root.querySelectorAll('.grn-uom-split')).toHaveLength(1);
    expect(text()).toContain('Sealed packs (100 ml each)');
  });

  it('hides the sealed field when the line was not ordered in a sealed pack', () => {
    purchaseOrders.getById.mockReturnValue(of({ data: { items: [{ ...poDetail.items[0], uomOptionId: null }, poDetail.items[1]] } }));
    open();
    expect(page.baseQtyPerPack(attar())).toBe(0);
    expect(text()).not.toContain('Sealed packs');
    expect(text()).toContain('Loose (ml)');
  });

  it('sends a plain pcs line exactly as before', () => {
    open();
    page.onSubmit();
    expect(sent()[1]).toEqual({ poItemId: 2, receivedQty: 4 });
    expect(sent()[0]).toEqual({ poItemId: 1, receivedQty: 300 });
  });

  it('sends damaged, rejected and the sealed and loose split', () => {
    open();
    page.updateReceivedQty(0, 250.5);
    page.updateLine(0, 'damagedQty', 40);
    page.updateLine(0, 'rejectedQty', 9.5);
    page.updateLine(0, 'acceptedSealedPacks', 2);
    page.updateLine(0, 'acceptedLooseQty', 50.5);

    page.onSubmit();

    expect(sent()[0]).toEqual({ poItemId: 1, receivedQty: 250.5, damagedQty: 40, rejectedQty: 9.5, acceptedSealedPacks: 2, acceptedLooseQty: 50.5 });
  });

  it('blocks a split that does not add up, with a clear message, and keeps the input', () => {
    open();
    page.updateLine(0, 'acceptedSealedPacks', 2);
    fixture.detectChanges();

    expect(page.breakdownMessage(attar())).toContain('2 sealed × 100 + 0 loose = 200 ml, but the accepted quantity is 300 ml');
    expect(text()).toContain('the accepted quantity is 300 ml');
    page.onSubmit();

    expect(grn.create).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('Attar:'));
    expect(attar().acceptedSealedPacks).toBe(2);
  });

  it('explains a shortage without blocking a partial receipt', () => {
    open();
    page.updateReceivedQty(0, 250);
    page.updateLine(0, 'damagedQty', 10);
    fixture.detectChanges();

    expect(page.reconciliationMessage(attar())).toContain('= 260 ml, short by 40 ml');
    expect(text()).toContain('short by 40 ml');
    page.onSubmit();
    expect(grn.create).toHaveBeenCalledTimes(1);
  });

  it('allows fractions only for configured items', () => {
    open();
    expect(page.stepFor(attar())).toBe('0.1');
    expect(page.stepFor(shirt())).toBe('1');

    page.updateReceivedQty(1, 2.5);
    page.onSubmit();
    expect(alerts.error).toHaveBeenCalledWith('Shirt: quantities must be whole numbers');
    expect(grn.create).not.toHaveBeenCalled();

    page.updateReceivedQty(1, 2);
    page.updateReceivedQty(0, 10.25);
    page.onSubmit();
    expect(alerts.error).toHaveBeenCalledWith('Attar: quantities allow at most 1 decimal place(s)');
    expect(grn.create).not.toHaveBeenCalled();
  });

  it('leaves precision to the server when the units could not be loaded', () => {
    uom.getVariantConfigs.mockReturnValue(throwError(() => new Error('offline')));
    open();
    expect(page.uomUnavailable()).toBe(true);
    page.updateReceivedQty(0, 250.5);
    page.onSubmit();
    expect(grn.create).toHaveBeenCalledTimes(1);
  });

  it('does not open the PO without purchases.view, so the receiver sees no access notice', () => {
    auth.hasPermission.mockReturnValue(false);
    open();
    expect(purchaseOrders.getById).not.toHaveBeenCalled();
    expect(attar().uomConfig?.configId).toBe(77);
    expect(page.orderedAs(attar())).toBe('');
  });

  it('ignores a slow answer for a PO that is no longer selected', () => {
    open();
    page.selectedPO.set(null);
    page.lineItems.set([]);
    page.onPoSelected(null);
    expect(page.lineItems()).toEqual([]);
  });

  it('keeps the input and allows a retry after a server error, and prevents a double submit', () => {
    open();
    grn.create.mockReturnValueOnce(throwError(() => new Error('no')));
    page.updateLine(0, 'damagedQty', 5);

    page.onSubmit();
    expect(alerts.error).toHaveBeenCalledWith('server said no');
    expect(attar().damagedQty).toBe(5);
    expect(page.isSubmitting()).toBe(false);

    page.isSubmitting.set(true);
    page.onSubmit();
    expect(grn.create).toHaveBeenCalledTimes(1);
  });
});

describe('GRN details with units', () => {
  const grnOf = (items: unknown[]) => ({
    id: 1, poId: 7, poOrderNumber: 'PO-7', supplierName: 'S', locationType: 'outlet', locationId: 1, locationName: 'Main', receivedDate: '2026-01-01T00:00:00Z', status: 'full', items,
  });

  async function render(items: unknown[]): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [GrnDetailsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' } } } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: GrnService, useValue: { getById: () => of({ data: grnOf(items) }) } },
        { provide: CompanyDateService, useValue: { timeZone: () => 'UTC', format: () => 'date' } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(GrnDetailsComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows accepted, damaged, rejected and the pack split in base units', async () => {
    const root = await render([{
      id: 1, poItemId: 1, productName: 'Attar', orderedQty: 300, receivedQty: 250, damagedQty: 40, rejectedQty: 10,
      acceptedSealedPacks: 2, acceptedLooseQty: 50, uomLabel: 'Box of 100', uomBaseUnitCode: 'ml',
    }]);
    const text = root.textContent?.replace(/\s+/g, ' ') ?? '';

    expect(text).toContain('Accepted Qty');
    expect(text).toContain('250 ml');
    expect(text).toContain('40 ml');
    expect(text).toContain('10 ml');
    expect(text).toContain('2 sealed + 50 ml loose');
    expect(text).toContain('Ordered as Box of 100');
  });

  it('keeps the original table for a plain pcs GRN', async () => {
    const root = await render([{ id: 1, poItemId: 1, productName: 'Shirt', orderedQty: 4, receivedQty: 4, damagedQty: 0, rejectedQty: 0 }]);
    const text = root.textContent?.replace(/\s+/g, ' ') ?? '';

    expect(text).toContain('Received Qty');
    expect(text).not.toContain('Damaged');
    expect(text).not.toContain(' pcs');
  });
});
