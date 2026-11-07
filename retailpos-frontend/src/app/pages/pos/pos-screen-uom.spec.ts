import { HttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PosProduct } from '../../models/sale.model';
import { SellingOption, VariantUomConfig } from '../../models/uom.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { CustomerService } from '../../services/customer.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { PosService } from '../../services/pos.service';
import { ProductImageService } from '../../services/product-image.service';
import { SaleService } from '../../services/sale.service';
import { SettingsService } from '../../services/settings.service';
import { UomService } from '../../services/uom.service';
import { PosScreenComponent } from './pos-screen.component';

const ml = { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 1, isWhole: false };
const pack12: SellingOption = {
  id: 10, kind: 'pack', code: 'P12', label: '12 ml bottle', unit: null, baseQuantity: 12, factorNumerator: 1, factorDenominator: 1,
  price: 500, isDefault: true, sealedPackCode: null, isPreparedEligible: false, components: [],
};
const pack6: SellingOption = { ...pack12, id: 11, code: 'P6', label: '6 ml bottle', baseQuantity: 6, price: 260, isDefault: false };
const custom: SellingOption = {
  ...pack12, id: 12, kind: 'custom', code: 'CUSTOM', label: 'Custom', unit: ml, baseQuantity: null, price: 40, isDefault: false,
};
const config: VariantUomConfig = {
  configId: 77, version: 1, variantId: 5, baseUnit: ml, packStateTrackingEnabled: false, repackingEnabled: false,
  purchaseOptions: [], sellingOptions: [pack12, pack6, custom],
};

const product = (over: Partial<PosProduct> = {}): PosProduct => ({
  variantId: 5, productId: 1, productName: 'Attar', variantName: '', name: 'Attar', sku: 'ATR', price: 1, stockQty: 500, taxRate: 10, taxClassification: 'standard', ...over,
});

describe('PosScreenComponent units of measurement', () => {
  const posService = {
    heldSales: signal([]),
    isLoadingHeld: signal(false),
    loadHeldSales: vi.fn(),
    getTerminals: vi.fn(() => of({ data: [] })),
    getSalespersons: vi.fn(() => of({ data: { salespersons: [], defaultSalespersonUserId: null, defaultSource: 'none' } })),
    getActiveCashSession: vi.fn(() => of({ data: null })),
    getPosSettings: vi.fn(() => of({ data: null })),
    cartPrices: vi.fn(),
    lookup: vi.fn(() => of({ data: null })),
    getCatalogue: vi.fn(() => of({ data: [] })),
    getCategories: vi.fn(() => of({ data: [] })),
    searchCustomers: vi.fn(() => of({ data: [] })),
    stockHint: vi.fn(() => of({ data: { availableQty: 500 } })),
    reportDiscountAudit: vi.fn(() => of({ success: true })),
    holdSale: vi.fn(() => of({ data: {} })),
    recallHeldSale: vi.fn(),
    discardHeldSale: vi.fn(() => of({})),
  };
  const saleService = { createSale: vi.fn() };
  const alerts = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), confirm: vi.fn() };
  const uom = {
    getVariantConfigs: vi.fn(),
    quote: vi.fn(() => of({ baseQuantity: 12, priceBeforeDiscountAndVat: 500 })),
    getPackState: vi.fn(),
  };

  let component: PosScreenComponent;

  beforeEach(async () => {
    vi.clearAllMocks();
    uom.getVariantConfigs.mockImplementation((ids: number[]) => of(ids.includes(5) ? [config] : []));
    posService.cartPrices.mockImplementation((items: Array<{ productVariantId: number }>) => of({
      data: items.map(item => ({
        productVariantId: item.productVariantId, discountAmount: 0, finalPrice: 999, taxRate: 10, taxClassification: 'standard',
        taxAmount: 0, finalPriceWithTax: 999, pricesIncludeTax: false,
      })),
    }));

    await TestBed.configureTestingModule({
      imports: [PosScreenComponent],
      providers: [
        { provide: AuthService, useValue: { getUserValue: () => ({ id: 11, outletId: 3, outletName: 'Main' }), hasPermission: () => true } },
        { provide: SaleService, useValue: saleService },
        { provide: PosService, useValue: posService },
        { provide: CustomerService, useValue: { create: vi.fn() } },
        { provide: SettingsService, useValue: { getInventorySettings: () => of({ data: { lowStockThreshold: 5 } }), getReceiptSettings: () => of({ data: { autoPrintAfterSale: false, defaultReceiptSize: '80mm' } }) } },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'error' } },
        { provide: ProductImageService, useValue: { resolveUrl: (path?: string | null) => path ?? '' } },
        { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } },
        { provide: HttpClient, useValue: { get: vi.fn(() => of({ data: [] })) } },
        { provide: UomService, useValue: uom },
      ],
    }).compileComponents();
    component = TestBed.createComponent(PosScreenComponent).componentInstance;
  });

  const pick = (option: SellingOption, quantity = 1, extra: { fulfilmentState?: 'loose' | 'sealed' | 'prepared'; includePackaging?: boolean } = {}) => {
    component.addToCart(product());
    component.onUomPicked({ option, configId: 77, quantity, includePackaging: false, ...extra });
  };

  const readyForCheckout = () => {
    component.selectedTerminalId.set(8001);
    component.activeCashSession.set({ id: 1, openedAt: new Date().toISOString(), isOverdue: false } as never);
    component.setPaymentMethod('cash');
    component.cashTendered.set(100000);
  };

  it('opens the picker for a configured variant instead of adding a line', () => {
    component.addToCart(product());

    expect(component.uomPicker()?.config.configId).toBe(77);
    expect(component.cartItems()).toEqual([]);
  });

  it('adds a plain pcs variant straight away, with no unit fields', () => {
    component.addToCart(product({ variantId: 6, name: 'Shirt', price: 100 }));

    expect(component.uomPicker()).toBeNull();
    expect(component.cartItems()).toHaveLength(1);
    expect(component.cartItems()[0].sellingOptionId).toBeUndefined();
    expect(component.cartItems()[0].quantity).toBe(1);
  });

  it('asks for a variant once, then remembers the answer', () => {
    component.addToCart(product({ variantId: 6, price: 100 }));
    component.addToCart(product({ variantId: 6, price: 100 }));

    expect(uom.getVariantConfigs).toHaveBeenCalledTimes(1);
    expect(uom.getVariantConfigs).toHaveBeenCalledWith([6]);
    expect(component.cartItems()[0].quantity).toBe(2);
  });

  it('adds the variant as pcs when the configuration check fails, leaving the verdict to the server', () => {
    uom.getVariantConfigs.mockReturnValue(throwError(() => new Error('offline')));
    component.addToCart(product({ variantId: 6, price: 100 }));

    expect(component.cartItems()).toHaveLength(1);
    expect(component.cartItems()[0].sellingOptionId).toBeUndefined();
  });

  it('refuses an out-of-stock configured variant before opening the picker', () => {
    component.addToCart(product({ stockQty: 0 }));

    expect(alerts.error).toHaveBeenCalledWith('This product is out of stock');
    expect(component.uomPicker()).toBeNull();
  });

  it('keeps two options of one variant as separate lines and merges the same option', () => {
    pick(pack12);
    pick(pack6);
    pick(pack12, 2);

    expect(component.cartItems().map(line => [line.sellingOptionId, line.quantity, line.unitPrice, line.subtotal])).toEqual([
      [10, 3, 500, 1500],
      [11, 1, 260, 260],
    ]);
    expect(new Set(component.cartItems().map(line => component.cartLineKey(line))).size).toBe(2);
  });

  it('treats a measured quantity as the entered quantity at the option price', () => {
    pick(custom, 2.5);
    const [line] = component.cartItems();

    expect(line.quantity).toBe(2.5);
    expect(line.subtotal).toBe(100);
    expect(component.cartLineBaseText(line)).toBe('= 2.5 ml');
    expect(component.cartLineStep(line)).toBe('0.1');
  });

  it('accepts a measured quantity edit at the unit precision and refuses more', () => {
    pick(custom, 2.5);
    component.setQuantity(0, '3.5');
    expect(component.cartItems()[0].quantity).toBe(3.5);

    component.setQuantity(0, '3.55');
    expect(component.cartItems()[0].quantity).toBe(3.5);
    expect(alerts.warning).toHaveBeenCalled();
  });

  it('refuses a fractional pack count on the cart line', () => {
    pick(pack12, 2);
    component.setQuantity(0, '2.5');
    expect(component.cartItems()[0].quantity).toBe(2);
  });

  it('keeps a pcs quantity edit whole, exactly as before', () => {
    component.addToCart(product({ variantId: 6, price: 100 }));
    component.setQuantity(0, '2.9');
    expect(component.cartItems()[0].quantity).toBe(2);
  });

  it('warns about stock on a unit line without capping it', () => {
    component.outletStockByVariant.set({ 5: 20 });
    component.addToCart(product());
    component.onUomPicked({ option: pack12, configId: 77, quantity: 2, includePackaging: false });

    expect(component.cartItems()[0].quantity).toBe(2);
    expect(alerts.warning).toHaveBeenCalledWith(expect.stringContaining('20 ml'));
  });

  it('prices an option line by its option and takes only the tax when cart prices return no line amount', () => {
    pick(custom, 2.5);
    readyForCheckout();
    saleService.createSale.mockReturnValue(of({ data: { id: 0 } }));

    component.completeSale();

    expect(posService.cartPrices).toHaveBeenCalledWith([{ productVariantId: 5, quantity: 1, outletId: 3, sellingOptionId: 12, enteredQuantity: 2.5 }]);
    expect(component.cartItems()[0].unitPrice).toBe(40);
    expect(Object.keys(saleService.createSale.mock.calls[0][0].items[0])).not.toContain('expectedLineGross');
    expect(saleService.createSale.mock.calls[0][0].items[0]).toMatchObject({
      variantId: 5, quantity: 2.5, unitPrice: 40, sellingOptionId: 12, expectedConfigVersionId: 77, includePackaging: false,
    });
  });

  it('sends the chosen form and packaging at checkout', () => {
    pick(pack12, 1, { fulfilmentState: 'prepared', includePackaging: false });
    pick(pack6, 2, { fulfilmentState: 'loose', includePackaging: true });
    readyForCheckout();
    saleService.createSale.mockReturnValue(of({ data: { id: 0 } }));

    component.completeSale();

    expect(saleService.createSale.mock.calls[0][0].items.map((item: { fulfilmentState: string; includePackaging: boolean }) => [item.fulfilmentState, item.includePackaging])).toEqual([
      ['prepared', false],
      ['loose', true],
    ]);
  });

  it('sends a pcs line with no unit fields', () => {
    // The price cart-prices returns, so checkout goes straight through.
    component.addToCart(product({ variantId: 6, price: 999 }));
    readyForCheckout();
    saleService.createSale.mockReturnValue(of({ data: { id: 0 } }));

    component.completeSale();

    const line = saleService.createSale.mock.calls[0][0].items[0];
    expect(Object.keys(line)).not.toContain('sellingOptionId');
    expect(Object.keys(line)).not.toContain('includePackaging');
  });

  it('uses a new idempotency key when only the chosen option differs', () => {
    pick(pack12);
    readyForCheckout();
    saleService.createSale.mockReturnValue(throwError(() => ({ status: 0 })));
    component.completeSale();
    const firstKey = saleService.createSale.mock.calls[0][0].idempotencyKey;

    component.cartItems.update(lines => [{ ...lines[0], sellingOptionId: 99 }]);
    component.completeSale();

    expect(saleService.createSale.mock.calls[1][0].idempotencyKey).not.toBe(firstKey);
  });

  it('holds the unit fields and restores them on recall', () => {
    pick(pack12, 2, { includePackaging: true });
    component.holdSale();

    const held = posService.holdSale.mock.calls[0] as unknown as [{ items: Array<Record<string, unknown>> }];
    expect(held[0].items[0]).toMatchObject({
      quantity: 2, unitPrice: 500, sellingOptionId: 10, expectedConfigVersionId: 77, includePackaging: true, unitLabel: '12 ml bottle',
    });

    posService.recallHeldSale.mockReturnValue(of({ data: { items: held[0].items, discountPercent: 0, paymentMethod: 'cash' } }));
    component.recallHeldSale({ id: 3 } as never);

    expect(component.cartItems()[0]).toMatchObject({ quantity: 2, unitPrice: 500, sellingOptionId: 10, unitLabel: '12 ml bottle', includePackaging: true });
    expect(component.lineNeedsRepick(component.cartItems()[0])).toBe(false);
  });

  it('shows a stale recalled line as needing a re-pick, and re-picking replaces it', () => {
    posService.recallHeldSale.mockReturnValue(of({
      data: {
        items: [{ variantId: 5, productName: 'Attar', variantSku: 'ATR', quantity: 2, unitPrice: 450, discountAmount: 0, sellingOptionId: 10, expectedConfigVersionId: 70, unitLabel: 'old' }],
        discountPercent: 0,
        paymentMethod: 'cash',
      },
    }));
    component.recallHeldSale({ id: 3 } as never);
    expect(component.lineNeedsRepick(component.cartItems()[0])).toBe(true);

    component.repickLine(0);
    expect(component.uomPicker()?.replaceIndex).toBe(0);
    expect(component.uomPicker()?.preset).toEqual({ sellingOptionId: 10, quantity: 2 });

    component.onUomPicked({ option: pack12, configId: 77, quantity: 2, includePackaging: false });

    expect(component.cartItems()).toHaveLength(1);
    expect(component.cartItems()[0]).toMatchObject({ unitPrice: 500, expectedConfigVersionId: 77, subtotal: 1000 });
    expect(component.lineNeedsRepick(component.cartItems()[0])).toBe(false);
  });

  it('shows UOM_CONFIG_CHANGED at checkout, keeps the cart and flags the line for a re-pick', () => {
    pick(pack12);
    readyForCheckout();
    uom.getVariantConfigs.mockReturnValue(of([{ ...config, configId: 78 }]));
    saleService.createSale.mockReturnValue(throwError(() => ({ error: { errorCode: 'UOM_CONFIG_CHANGED', message: 'x' } })));

    component.completeSale();

    expect(component.checkoutNotice()?.text).toContain('have changed');
    expect(component.cartItems()).toHaveLength(1);
    expect(component.lineNeedsRepick(component.cartItems()[0])).toBe(true);
    expect(component.isProcessing()).toBe(false);
  });

  it('shows the friendly message for a stock error and keeps the cart', () => {
    pick(pack12);
    readyForCheckout();
    saleService.createSale.mockReturnValue(throwError(() => ({ error: { errorCode: 'UOM_INSUFFICIENT_LOOSE_STOCK', message: 'x' } })));

    component.completeSale();

    expect(component.checkoutNotice()?.text).toContain('not enough loose stock');
    expect(component.cartItems()).toHaveLength(1);
  });

  /** Cart prices that return a line amount for option lines, per request index. */
  const optionPrices = (rows: Array<{ finalPrice: number; lineGross?: number; appliedRuleName?: string; rulesNotApplied?: string[] | null }>) =>
    posService.cartPrices.mockImplementation((items: Array<{ productVariantId: number; sellingOptionId?: number }>) => of({
      data: items.map((item, index) => ({
        productVariantId: item.productVariantId, discountAmount: 0, taxRate: 10, taxClassification: 'standard',
        taxAmount: 0, finalPriceWithTax: 0, pricesIncludeTax: false, ...(item.sellingOptionId ? rows[index] : { finalPrice: 999 }),
      })),
    }));

  it('maps cart prices by index for two option lines of one variant and starts each subtotal from its line amount', () => {
    optionPrices([
      { finalPrice: 450, lineGross: 900, appliedRuleName: 'Pack promo' },
      { finalPrice: 40, lineGross: 90, rulesNotApplied: ['Buy 3 ml get 10%'] },
    ]);
    pick(pack12, 2);
    pick(custom, 2.5);

    const [packLine, customLine] = component.cartItems();
    expect(posService.cartPrices).toHaveBeenLastCalledWith([
      { productVariantId: 5, quantity: 1, outletId: 3, sellingOptionId: 10, enteredQuantity: 2 },
      { productVariantId: 5, quantity: 1, outletId: 3, sellingOptionId: 12, enteredQuantity: 2.5 },
    ]);
    expect([packLine.unitPrice, packLine.lineGross, packLine.subtotal, packLine.appliedRuleName]).toEqual([450, 900, 900, 'Pack promo']);
    expect([customLine.unitPrice, customLine.lineGross, customLine.subtotal]).toEqual([40, 90, 90]);
    expect(customLine.rulesNotApplied).toEqual(['Buy 3 ml get 10%']);
    expect(packLine.rulesNotApplied).toBeUndefined();
  });

  it('applies the manual discount on top of the line amount', () => {
    optionPrices([{ finalPrice: 450, lineGross: 900 }]);
    pick(pack12, 2);
    const line = (component as unknown as { repriceLine: (item: unknown, changes: unknown) => { discountAmount: number; subtotal: number } })
      .repriceLine(component.cartItems()[0], { discountPercent: 10 });
    expect(line.discountAmount).toBe(90);
    expect(line.subtotal).toBe(810);
  });

  it('drops the line amount when the quantity changes and asks the server again', () => {
    optionPrices([{ finalPrice: 450, lineGross: 900 }]);
    pick(pack12, 2);
    posService.cartPrices.mockReturnValue(of({ data: [] }));
    const calls = posService.cartPrices.mock.calls.length;

    component.setQuantity(0, '3');

    expect(component.cartItems()[0].lineGross).toBeUndefined();
    expect(component.cartItems()[0].subtotal).toBe(1350);
    expect(posService.cartPrices.mock.calls.length).toBe(calls + 1);
  });

  it('ignores a price result for a line that changed while the request was out', () => {
    pick(pack12, 2);
    const [requestItems] = posService.cartPrices.mock.calls.at(-1) as unknown as [unknown[]];
    component.cartItems.update(lines => [{ ...lines[0], quantity: 5 }]);
    (component as unknown as { applyCartPrices: (rows: unknown[], requested: unknown[]) => void }).applyCartPrices(
      [{ productVariantId: 5, finalPrice: 450, lineGross: 900, taxRate: 10, discountAmount: 0, taxAmount: 0, finalPriceWithTax: 0 }], requestItems);
    expect(component.cartItems()[0].lineGross).toBeUndefined();
  });

  it('sends expectedLineGross for an option line priced by the server', () => {
    optionPrices([{ finalPrice: 450, lineGross: 900 }, { finalPrice: 260, lineGross: 260 }]);
    pick(pack12, 2);
    pick(pack6, 1);
    readyForCheckout();
    saleService.createSale.mockReturnValue(of({ data: { id: 0 } }));

    component.completeSale();

    const items = saleService.createSale.mock.calls[0][0].items;
    expect(items[0]).toMatchObject({ sellingOptionId: 10, quantity: 2, unitPrice: 450, expectedLineGross: 900 });
    expect(items[1]).toMatchObject({ sellingOptionId: 11, quantity: 1, unitPrice: 260, expectedLineGross: 260 });
  });

  it('keeps the pcs cart-prices request and sale line exactly as before', () => {
    component.addToCart(product({ variantId: 6, price: 999 }));
    component.setQuantity(0, '2');
    readyForCheckout();
    saleService.createSale.mockReturnValue(of({ data: { id: 0 } }));

    component.completeSale();

    expect(posService.cartPrices).toHaveBeenLastCalledWith([{ productVariantId: 6, quantity: 2, outletId: 3 }]);
    expect(saleService.createSale.mock.calls[0][0].items[0]).toEqual({
      variantId: 6, quantity: 2, unitPrice: 999, discountAmount: 0, appliedRuleName: undefined,
    });
  });

  it('does not reprice an option line from a per-variant price change', () => {
    pick(pack12);
    pick(pack6);
    readyForCheckout();
    saleService.createSale.mockReturnValue(throwError(() => ({
      error: { errorCode: 'SALE_PRICE_CHANGED', data: [{ variantId: 5, currentUnitPrice: 1 }] },
    })));

    component.completeSale();

    expect(component.cartItems().map(line => line.unitPrice)).toEqual([500, 260]);
  });

  it('keeps shortcuts from reaching the screen behind the open picker', () => {
    readyForCheckout();
    component.addToCart(product());
    const completeSale = vi.spyOn(component, 'completeSale');

    component.handleKeyboardShortcuts(new KeyboardEvent('keydown', { key: 'F9' }));

    expect(completeSale).not.toHaveBeenCalled();
    component.onUomPickerCancelled();
    expect(component.uomPicker()).toBeNull();
    expect(component.cartItems()).toEqual([]);
  });

  it('loads the configurations of the catalogue up front, so a scan opens the picker without another request', () => {
    posService.getCatalogue.mockReturnValueOnce(of({
      data: [{ variantId: 5, productId: 1, productName: 'Attar', variantName: '', sku: 'ATR', categoryId: 1, listPrice: 1, stockQty: 500 }],
    }) as never);
    (component as unknown as { loadProducts(): void }).loadProducts();
    expect(uom.getVariantConfigs).toHaveBeenLastCalledWith([5]);
    uom.getVariantConfigs.mockClear();

    component.addToCart(product());

    expect(uom.getVariantConfigs).not.toHaveBeenCalled();
    expect(component.uomPicker()).not.toBeNull();
  });
});
