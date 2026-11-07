import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { VariantUomConfig } from '../../models/uom.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { SupplierService } from '../../services/supplier.service';
import { UomService } from '../../services/uom.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { VariationService } from '../../services/variation.service';
import { PoFormComponent } from './po-form.component';

const ml = { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 1, isWhole: false };
const box = { ...ml, id: 2, code: 'box', symbol: 'box', dimension: 'packaging' as const, precision: 0, isWhole: true };
const config: VariantUomConfig = {
  configId: 77, version: 1, variantId: 5, baseUnit: ml, packStateTrackingEnabled: true, repackingEnabled: false, sellingOptions: [],
  purchaseOptions: [
    { id: 30, code: 'BOX', label: 'Box of 100', unit: box, factorNumerator: 100, factorDenominator: 1, baseQuantityPerUnit: 100, isDefault: true, isSealedPack: true },
    { id: 31, code: 'L', label: 'Litre', unit: { ...ml, id: 3, code: 'l', symbol: 'L', precision: 3 }, factorNumerator: 1000, factorDenominator: 1, baseQuantityPerUnit: 1000, isDefault: false, isSealedPack: false },
  ],
};
const variant = { id: 5, productId: 1, productName: 'Attar', name: 'Default', finalPrice: 9, costPrice: 2 };
const plainVariant = { id: 6, productId: 2, productName: 'Shirt', name: 'Blue', finalPrice: 9, costPrice: 50 };

describe('PoFormComponent purchase units', () => {
  let fixture: ComponentFixture<PoFormComponent>;
  let component: PoFormComponent;
  let poService: { create: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn>; getById: ReturnType<typeof vi.fn>; purchaseAndReceive: ReturnType<typeof vi.fn> };
  let alerts: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn>; warning: ReturnType<typeof vi.fn>; confirm: ReturnType<typeof vi.fn> };
  let uom: { getVariantConfigs: ReturnType<typeof vi.fn> };

  async function setup(options: { routeId?: string; purchaseOrder?: Record<string, unknown> } = {}): Promise<void> {
    vi.useFakeTimers();
    poService = {
      create: vi.fn(() => of({ data: { id: 7 } })),
      update: vi.fn(() => of({ data: { id: 7 } })),
      getById: vi.fn(() => of({ data: options.purchaseOrder })),
      purchaseAndReceive: vi.fn(() => of({ data: { purchaseOrder: { id: 7 }, grnId: 9 } })),
    };
    alerts = { error: vi.fn(), success: vi.fn(), warning: vi.fn(), confirm: vi.fn((_m: string, ok: () => void) => ok()) };
    uom = { getVariantConfigs: vi.fn((ids: number[]) => of(ids.includes(5) ? [config] : [])) };
    await TestBed.configureTestingModule({
      imports: [PoFormComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => options.routeId ?? null } } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: PurchaseOrderService, useValue: poService },
        { provide: SupplierService, useValue: { getAll: () => of({ data: [] }) } },
        { provide: ProductService, useValue: { searchVariants: () => of({ data: [] }) } },
        { provide: VariationService, useValue: { getAllVariations: () => of([]) } },
        { provide: UserOutletAccessService, useValue: { load: () => of({ data: { outlets: [{ id: 12, name: 'Main', type: 'outlet' }], warehouses: [] } }) } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: unknown) => String(error) } },
        { provide: CurrencyService, useValue: { format: (value: number) => `${value}` } },
        { provide: AuthService, useValue: { hasPermission: () => true } },
        { provide: ProductImageService, useValue: { resolveUrl: (path?: string | null) => path ?? '' } },
        { provide: UomService, useValue: uom },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PoFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.supplierId.set(1);
  }

  afterEach(() => {
    TestBed.resetTestingModule();
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  const line = (index = 0) => component.items()[index];

  it('starts a configured line in the default purchase unit, with the cost converted', async () => {
    await setup();
    component.selectVariant(variant, 0);

    expect(line().purchaseOptionId).toBe(30);
    expect(line().unitPrice).toBe(200);
    expect(component.purchaseUnitLabel(line())).toBe('Box of 100');
    expect(component.baseQuantityText(line())).toBe('100 ml');
  });

  it('shows a live base quantity preview and the unit the price is for', async () => {
    await setup();
    component.selectVariant(variant, 0);
    component.updateItemQuantity(0, 3);
    fixture.detectChanges();

    expect(component.baseQuantityPreview(line())).toBe(300);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('= 300 ml');
    expect(text).toContain('per Box of 100');
  });

  it('keeps the per-base cost when the purchase unit changes', async () => {
    await setup();
    component.selectVariant(variant, 0);

    component.onPurchaseOptionChange(0, 31);
    expect(line().unitPrice).toBe(2000);

    component.onPurchaseOptionChange(0, null);
    expect(line().unitPrice).toBe(2);
    expect(line().purchaseOptionId).toBeNull();
    expect(component.purchaseUnitLabel(line())).toBe('ml');
  });

  it('sends the purchase unit and the expected configuration', async () => {
    await setup();
    component.selectVariant(variant, 0);
    component.locationType.set('outlet');
    component.locationId.set(12);
    component.updateItemQuantity(0, 3);

    component.save();

    expect(poService.create.mock.calls[0][0].items[0]).toMatchObject({
      variantId: 5, quantity: 3, unitPrice: 200, purchaseOptionId: 30, expectedConfigVersionId: 77,
    });
  });

  it('leaves a pcs line and its payload exactly as they were', async () => {
    await setup();
    component.selectVariant(plainVariant, 0);
    component.locationType.set('outlet');
    component.locationId.set(12);
    fixture.detectChanges();

    expect(component.uomConfigFor(line())).toBeNull();
    expect(line().unitPrice).toBe(50);
    component.save();

    const sent = poService.create.mock.calls[0][0].items[0];
    expect(Object.keys(sent)).not.toContain('purchaseOptionId');
    expect(Object.keys(sent)).not.toContain('expectedConfigVersionId');
  });

  it('refuses a fractional sealed pack and a quantity beyond the unit precision, keeping the input', async () => {
    await setup();
    component.selectVariant(variant, 0);
    component.locationType.set('outlet');
    component.locationId.set(12);
    component.updateItemQuantity(0, 1.5);

    component.save();
    expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('whole number'));
    expect(poService.create).not.toHaveBeenCalled();
    expect(line().quantity).toBe(1.5);

    component.onPurchaseOptionChange(0, null);
    component.updateItemQuantity(0, 1.25);
    component.save();
    expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('1 decimal'));

    component.updateItemQuantity(0, 1.5);
    component.save();
    expect(poService.create).toHaveBeenCalledTimes(1);
  });

  it('restores the entered unit of a saved line when editing', async () => {
    await setup({
      routeId: '7',
      purchaseOrder: {
        supplierId: 1, locationType: 'outlet', locationId: 12, orderDate: '2026-01-01T00:00:00Z',
        items: [{ variantId: 5, quantity: 300, unitPrice: 2, uomOptionId: 30, uomEnteredQuantity: 3, uomEnteredUnitPrice: 200, productName: 'Attar' }],
      },
    });

    expect(line()).toMatchObject({ purchaseOptionId: 30, quantity: 3, unitPrice: 200 });
  });

  it('never sends entered figures as base: a saved line falls back to base units when its unit is gone', async () => {
    await setup({
      routeId: '7',
      purchaseOrder: {
        supplierId: 1, locationType: 'outlet', locationId: 12, orderDate: '2026-01-01T00:00:00Z',
        items: [{ variantId: 5, quantity: 300, unitPrice: 2, uomOptionId: 99, uomEnteredQuantity: 3, uomEnteredUnitPrice: 200, productName: 'Attar' }],
      },
    });

    expect(line()).toMatchObject({ purchaseOptionId: null, quantity: 300, unitPrice: 2 });
    expect(alerts.warning).toHaveBeenCalledWith(expect.stringContaining('no longer available'));
  });

  it('keeps a saved line in base units when the configuration cannot be loaded', async () => {
    vi.useFakeTimers();
    await setup({
      routeId: '7',
      purchaseOrder: { supplierId: 1, locationType: 'outlet', locationId: 12, orderDate: '2026-01-01T00:00:00Z', items: [] },
    });
    uom.getVariantConfigs.mockReturnValue(throwError(() => new Error('offline')));
    poService.getById.mockReturnValue(of({
      data: {
        supplierId: 1, locationType: 'outlet', locationId: 12, orderDate: '2026-01-01T00:00:00Z',
        items: [{ variantId: 5, quantity: 300, unitPrice: 2, uomOptionId: 30, uomEnteredQuantity: 3, uomEnteredUnitPrice: 200, productName: 'Attar' }],
      },
    }));

    component.loadPurchaseOrder(7);

    expect(line()).toMatchObject({ quantity: 300, unitPrice: 2 });
    expect(alerts.warning).toHaveBeenCalled();
  });

  it('sends the sealed and loose split with Purchase & Receive, but not with a draft', async () => {
    await setup();
    component.selectVariant(variant, 0);
    component.locationType.set('outlet');
    component.locationId.set(12);
    component.updateItemQuantity(0, 3);
    component.updateItemSplit(0, 'acceptedSealedPacks', '2');
    component.updateItemSplit(0, 'acceptedLooseQuantity', '100');

    component.purchaseAndReceive();
    expect(poService.purchaseAndReceive.mock.calls[0][0].items[0]).toMatchObject({ acceptedSealedPacks: 2, acceptedLooseQuantity: 100 });

    component.save();
    expect(Object.keys(poService.create.mock.calls[0][0].items[0])).not.toContain('acceptedSealedPacks');
  });

  it('stops Purchase & Receive when the split does not add up', async () => {
    await setup();
    component.selectVariant(variant, 0);
    component.locationType.set('outlet');
    component.locationId.set(12);
    component.updateItemQuantity(0, 3);
    component.updateItemSplit(0, 'acceptedSealedPacks', '2');

    component.purchaseAndReceive();

    expect(alerts.error).toHaveBeenCalledWith(expect.stringContaining('accepted quantity is 300 ml'));
    expect(poService.purchaseAndReceive).not.toHaveBeenCalled();
  });
});
