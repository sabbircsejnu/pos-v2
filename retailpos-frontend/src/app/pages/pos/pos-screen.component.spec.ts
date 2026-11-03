import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { signal } from '@angular/core';

import { PosScreenComponent } from './pos-screen.component';
import { AuthService } from '../../services/auth.service';
import { SaleService } from '../../services/sale.service';
import { PosService } from '../../services/pos.service';
import { CustomerService } from '../../services/customer.service';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { CurrencyService } from '../../services/currency.service';
import { HttpClient } from '@angular/common/http';
import { PosBusinessSettingsDto, PosProduct } from '../../models/sale.model';

describe('PosScreenComponent dropdown behavior', () => {
  const authServiceMock = {
    getUserValue: vi.fn().mockReturnValue({ id: 11, outletId: 3, outletName: 'Main Outlet' }),
    hasPermission: vi.fn().mockReturnValue(true),
  };

  const posSettings: PosBusinessSettingsDto = {
    maxCashSessionDurationHours: 15,
    defaultTimeZoneId: 'UTC',
    taxEnabled: true,
    defaultTaxRate: 10,
    taxName: 'VAT',
    pricesIncludeTax: false,
    maxManualDiscountPercent: 50,
    version: 1,
    updatedAt: null,
    isPersisted: true,
    canApplyDiscount: true,
    canOverrideDiscountLimit: false,
  };

  const pricedRow = (overrides: Record<string, unknown> = {}) => ({
    productVariantId: 7,
    discountAmount: 0,
    finalPrice: 100,
    taxRate: 10,
    taxClassification: 'standard',
    taxAmount: 10,
    finalPriceWithTax: 110,
    pricesIncludeTax: false,
    ...overrides,
  });

  const posServiceMock = {
    heldSales: signal([]),
    isLoadingHeld: signal(false),
    loadHeldSales: vi.fn(),
    getTerminals: vi.fn().mockReturnValue(of({ data: [] })),
    getSalespersons: vi.fn().mockReturnValue(of({ data: { salespersons: [], defaultSalespersonUserId: null, defaultSource: 'none' } })),
    getActiveCashSession: vi.fn().mockReturnValue(of({ data: null })),
    getPosSettings: vi.fn().mockReturnValue(of({ data: posSettings })),
    cartPrices: vi.fn().mockReturnValue(of({ data: [pricedRow()] })),
    lookup: vi.fn().mockReturnValue(of({ data: null })),
    stockHint: vi.fn().mockReturnValue(of({ data: { availableQty: 7 } })),
    reportDiscountAudit: vi.fn().mockReturnValue(of({ success: true })),
    holdSale: vi.fn(),
    recallHeldSale: vi.fn(),
    discardHeldSale: vi.fn(),
  };

  const saleServiceMock = {
    createSale: vi.fn(),
  };

  const customerServiceMock = {
    create: vi.fn(),
  };

  const settingsServiceMock = {
    getInventorySettings: vi.fn().mockReturnValue(of({ data: { lowStockThreshold: 5 } })),
    getReceiptSettings: vi.fn().mockReturnValue(of({ data: { autoPrintAfterSale: false, defaultReceiptSize: '80mm' } })),
  };

  const alertServiceMock = {
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn(),
    confirm: vi.fn(),
  };

  const errorHandlerServiceMock = {
    extractErrorMessage: vi.fn().mockReturnValue('error'),
  };

  const productImageServiceMock = {
    resolveUrl: vi.fn().mockImplementation((p?: string | null) => p || ''),
  };

  const currencyServiceMock = {
    format: vi.fn().mockImplementation((value: number) => value.toFixed(2)),
  };

  const httpClientMock = {
    get: vi.fn().mockReturnValue(of({ data: [] })),
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    posServiceMock.getTerminals.mockReturnValue(of({ data: [] }));
    posServiceMock.getActiveCashSession.mockReturnValue(of({ data: null }));
    posServiceMock.getSalespersons.mockReturnValue(of({ data: { salespersons: [], defaultSalespersonUserId: null, defaultSource: 'none' } }));
    posServiceMock.getPosSettings.mockReturnValue(of({ data: posSettings }));
    posServiceMock.cartPrices.mockReturnValue(of({ data: [pricedRow()] }));
    authServiceMock.hasPermission.mockReturnValue(true);

    await TestBed.configureTestingModule({
      imports: [PosScreenComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: SaleService, useValue: saleServiceMock },
        { provide: PosService, useValue: posServiceMock },
        { provide: CustomerService, useValue: customerServiceMock },
        { provide: SettingsService, useValue: settingsServiceMock },
        { provide: AlertService, useValue: alertServiceMock },
        { provide: ErrorHandlerService, useValue: errorHandlerServiceMock },
        { provide: ProductImageService, useValue: productImageServiceMock },
        { provide: CurrencyService, useValue: currencyServiceMock },
        { provide: HttpClient, useValue: httpClientMock },
      ]
    }).compileComponents();
  });

  function buildProduct(overrides: Partial<PosProduct> = {}): PosProduct {
    return {
      variantId: 1001,
      productId: 9001,
      productName: 'Test Product 9001',
      variantName: 'Blue 42',
      name: 'Test Product 9001 - Blue 42',
      productCode: 'ABA9001',
      sku: 'ABA9001-42-BLU-2',
      barcode: '8800120000964',
      attributes: '[{"attributeName":"Color","value":"Blue","isActive":true},{"attributeName":"Size","value":"42","isActive":true}]',
      price: 999,
      stockQty: 0,
      categoryId: 1,
      categoryName: 'Shirts',
      primaryImageThumb: '',
      ...overrides,
    };
  }

  it('matches smart search by barcode, main code, SKU, product name and variant values', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    component.allProducts.set([
      buildProduct(),
      buildProduct({
        variantId: 1002,
        productId: 9002,
        productName: 'Other Product',
        name: 'Other Product - Red',
        productCode: 'XYZ100',
        sku: 'XYZ100-RED',
        barcode: '11111111',
        attributes: '[{"attributeName":"Color","value":"Red","isActive":true}]'
      })
    ]);

    component.smartSearchQuery.set('8800120000964');
    expect(component.smartSearchResults().map(x => x.variantId)).toContain(1001);

    component.smartSearchQuery.set('aba9001');
    expect(component.smartSearchResults().map(x => x.variantId)).toContain(1001);

    component.smartSearchQuery.set('aba9001-42-blu-2');
    expect(component.smartSearchResults().map(x => x.variantId)).toContain(1001);

    component.smartSearchQuery.set('test product 9001');
    expect(component.smartSearchResults().map(x => x.variantId)).toContain(1001);

    component.smartSearchQuery.set('blue');
    expect(component.smartSearchResults().map(x => x.variantId)).toContain(1001);
  });

  it('formats variant details from active attributes and excludes inactive/deleted values', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    const product = buildProduct({
      attributes: JSON.stringify([
        { attributeName: 'Color', value: 'Blue', isActive: true },
        { attributeName: 'Size', value: '42', isActive: true },
        { attributeName: 'Fabric', value: 'Cotton', active: true },
        { attributeName: 'OldFlag', value: 'Deleted', isActive: false },
        { attributeName: 'Removed', value: 'X', deleted: true }
      ])
    });

    const details = component.getVariantDetails(product);
    expect(details).toContain('Color: Blue');
    expect(details).toContain('Size: 42');
    expect(details).toContain('Fabric: Cotton');
    expect(details).not.toContain('OldFlag');
    expect(details).not.toContain('Removed');
  });

  it('maps stock status class/text based on selected outlet stock hint values', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;
    component.lowStockThreshold.set(5);

    const product = buildProduct({ variantId: 2001, stockQty: 99 });

    expect(component.getStockStatusClass(product)).toBe('stock-unknown');
    expect(component.getStockStatusText(product)).toContain('selected outlet');

    component.outletStockByVariant.set({ 2001: 12 });
    expect(component.getStockStatusClass(product)).toBe('stock-in');
    expect(component.getStockStatusText(product)).toBe('Current Stock: 12');

    component.outletStockByVariant.set({ 2001: 2 });
    expect(component.getStockStatusClass(product)).toBe('stock-low');
    expect(component.getStockStatusText(product)).toBe('Current Stock: 2 (Low Stock)');

    component.outletStockByVariant.set({ 2001: 0 });
    expect(component.getStockStatusClass(product)).toBe('stock-out');
    expect(component.getStockStatusText(product)).toBe('Out of Stock');
  });

  it('refreshes outlet stock hints when smart-search input is focused with query', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    component.currentUser.set({ id: 11, outletId: 3 });
    component.allProducts.set([buildProduct({ variantId: 3001 })]);
    component.smartSearchQuery.set('test');

    component.onSmartSearchFocus();

    expect(posServiceMock.stockHint).toHaveBeenCalledWith(3001, 3, 1);
    expect(component.outletStockByVariant()[3001]).toBe(7);
  });

  it('renders dropdown row with product, variant, code, SKU, barcode and stock details', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();

    component.allProducts.set([buildProduct({ variantId: 4001 })]);
    posServiceMock.stockHint.mockReturnValueOnce(of({ data: { availableQty: 12 } }));

    component.onSmartSearchChange('test product');
    fixture.detectChanges();

    const dropdown = fixture.nativeElement.querySelector('.smart-search-dropdown') as HTMLElement;
    expect(dropdown).toBeTruthy();
    expect(dropdown.textContent || '').toContain('Test Product 9001');
    expect(dropdown.textContent || '').toContain('Variant: Color: Blue | Size: 42');
    expect(dropdown.textContent || '').toContain('Main Code: ABA9001');
    expect(dropdown.textContent || '').toContain('SKU: ABA9001-42-BLU-2');
    expect(dropdown.textContent || '').toContain('Barcode: 8800120000964');
    expect(dropdown.textContent || '').toContain('Current Stock: 12');
  });

  it('shows low-stock visual class in dropdown when outlet stock is at threshold', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();
    component.lowStockThreshold.set(5);

    component.allProducts.set([buildProduct({ variantId: 5001 })]);
    posServiceMock.stockHint.mockReturnValueOnce(of({ data: { availableQty: 2 } }));

    component.onSmartSearchChange('test');
    fixture.detectChanges();

    const stockPill = fixture.nativeElement.querySelector('.smart-stock-pill') as HTMLElement;
    expect(stockPill).toBeTruthy();
    expect(stockPill.className).toContain('stock-low');
    expect(stockPill.textContent || '').toContain('Current Stock: 2 (Low Stock)');
  });

  it('asks confirmation before switching from overall discount to product-wise discount and keeps values if not confirmed', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    component.cartItems.set([{
      variantId: 1,
      productName: 'Item A',
      variantSku: 'SKU-A',
      quantity: 2,
      unitPrice: 100,
      discountPercent: 0,
      discountAmount: 0,
      subtotal: 200,
    }]);
    component.discountPercent.set(10);

    component.onItemDiscountChange(0, '15');

    expect(alertServiceMock.confirm).toHaveBeenCalled();
    expect(component.discountPercent()).toBe(10);
    expect(component.cartItems()[0].discountAmount).toBe(0);
    expect(component.cartItems()[0].subtotal).toBe(200);
  });

  it('switches to product-wise discount after confirmation and clears overall discount', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    let onConfirm: (() => void) | undefined;
    alertServiceMock.confirm.mockImplementation((_: string, cb: () => void) => {
      onConfirm = cb;
    });

    component.cartItems.set([{
      variantId: 2,
      productName: 'Item B',
      variantSku: 'SKU-B',
      quantity: 2,
      unitPrice: 100,
      discountPercent: 0,
      discountAmount: 0,
      subtotal: 200,
    }]);
    component.discountPercent.set(12);

    component.onItemDiscountChange(0, '15');
    expect(onConfirm).toBeTruthy();

    onConfirm?.();

    expect(component.discountPercent()).toBe(0);
    expect(component.cartItems()[0].discountPercent).toBe(15);
    expect(component.cartItems()[0].discountAmount).toBe(30);
    expect(component.cartItems()[0].subtotal).toBe(170);
  });

  it('asks confirmation before switching from product-wise discount to overall discount and keeps values if not confirmed', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    component.cartItems.set([{
      variantId: 3,
      productName: 'Item C',
      variantSku: 'SKU-C',
      quantity: 2,
      unitPrice: 100,
      discountPercent: 10,
      discountAmount: 20,
      subtotal: 180,
    }]);
    component.discountPercent.set(0);

    component.onDiscountChange('5');

    expect(alertServiceMock.confirm).toHaveBeenCalled();
    expect(component.discountPercent()).toBe(0);
    expect(component.cartItems()[0].discountAmount).toBe(20);
    expect(component.cartItems()[0].subtotal).toBe(180);
  });

  it('switches to overall discount after confirmation and clears product-wise discounts', () => {
    const fixture = TestBed.createComponent(PosScreenComponent);
    const component = fixture.componentInstance;

    let onConfirm: (() => void) | undefined;
    alertServiceMock.confirm.mockImplementation((_: string, cb: () => void) => {
      onConfirm = cb;
    });

    component.cartItems.set([{
      variantId: 4,
      productName: 'Item D',
      variantSku: 'SKU-D',
      quantity: 2,
      unitPrice: 100,
      discountPercent: 10,
      discountAmount: 20,
      subtotal: 180,
    }]);
    component.discountPercent.set(0);

    component.onDiscountChange('5');
    expect(onConfirm).toBeTruthy();

    onConfirm?.();

    expect(component.discountPercent()).toBe(5);
    expect(component.cartItems()[0].discountPercent).toBe(0);
    expect(component.cartItems()[0].discountAmount).toBe(0);
    expect(component.cartItems()[0].subtotal).toBe(200);
  });

  describe('server-authoritative checkout', () => {
    function prepareCheckout(component: PosScreenComponent): void {
      component.posSettings.set(posSettings);
      component.cartItems.set([{
        variantId: 7,
        productName: 'Item G',
        variantSku: 'SKU-G',
        quantity: 2,
        unitPrice: 100,
        discountPercent: 10,
        discountAmount: 20,
        subtotal: 180,
        taxRate: 10,
        taxClassification: 'standard',
      }]);
      component.selectedTerminalId.set(8001);
      component.activeCashSession.set({ id: 1, openedAt: new Date().toISOString(), isOverdue: false } as any);
      component.paymentMethod.set('cash');
      component.cashTendered.set(500);
    }

    it('does not send a cashier identity and sends the cash tendered for a single cash sale', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(of({ data: { id: 0 } }));

      component.completeSale();

      const dto = saleServiceMock.createSale.mock.calls[0][0];
      expect(dto.cashierId).toBeUndefined();
      expect(dto.cashTendered).toBe(500);
      expect(dto.idempotencyKey).toBeTruthy();
    });

    it('re-prices the cart before submitting and sends the tax computed by the server algorithm', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(of({ data: { id: 0 } }));

      component.completeSale();

      expect(posServiceMock.cartPrices).toHaveBeenCalledWith([{ productVariantId: 7, quantity: 2, outletId: 3 }]);
      const dto = saleServiceMock.createSale.mock.calls[0][0];
      // 2 x 100 less a 20.00 line discount = 180.00 at 10% exclusive.
      expect(dto.tax).toBe(18);
      expect(dto.discount).toBe(0);
      expect(dto.items[0].discountAmount).toBe(20);
      expect(component.grandTotal()).toBe(198);
    });

    it('does not submit when the refreshed prices differ from what the cashier saw', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      posServiceMock.cartPrices.mockReturnValue(of({ data: [pricedRow({ finalPrice: 95 })] }));

      component.completeSale();

      expect(saleServiceMock.createSale).not.toHaveBeenCalled();
      expect(component.cartItems()[0].unitPrice).toBe(95);
      expect(component.checkoutNotice()?.variant).toBe('warning');
      expect(alertServiceMock.warning).toHaveBeenCalled();
      expect(component.isProcessing()).toBe(false);
    });

    it('stops with an error when prices cannot be refreshed', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      posServiceMock.cartPrices.mockReturnValue(throwError(() => ({ status: 0 })));

      component.completeSale();

      expect(saleServiceMock.createSale).not.toHaveBeenCalled();
      expect(alertServiceMock.error).toHaveBeenCalled();
      expect(component.isProcessing()).toBe(false);
    });

    it('reuses the idempotency key when the same cart is retried and issues a new one once the cart changes', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({ status: 0, message: 'offline' })));

      component.completeSale();
      component.completeSale();
      component.increaseQty(0);
      posServiceMock.cartPrices.mockReturnValue(of({ data: [pricedRow()] }));
      component.completeSale();

      const keys = saleServiceMock.createSale.mock.calls.map(call => call[0].idempotencyKey);
      expect(keys[0]).toBe(keys[1]);
      expect(keys[2]).not.toBe(keys[0]);
    });

    it('adopts the current server price when checkout is rejected with SALE_PRICE_CHANGED', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({
        status: 409,
        message: 'Prices have changed',
        originalError: { error: { errorCode: 'SALE_PRICE_CHANGED', data: [{ variantId: 7, submittedUnitPrice: 100, currentUnitPrice: 90 }] } },
      })));
      posServiceMock.cartPrices
        .mockReturnValueOnce(of({ data: [pricedRow()] }))
        .mockReturnValue(of({ data: [pricedRow({ finalPrice: 90 })] }));

      component.completeSale();

      const line = component.cartItems()[0];
      expect(line.unitPrice).toBe(90);
      expect(line.discountAmount).toBe(18);
      expect(line.subtotal).toBe(162);
      expect(alertServiceMock.warning).toHaveBeenCalled();
      expect(component.checkoutNotice()?.variant).toBe('warning');
      expect(component.isProcessing()).toBe(false);
    });

    it('refreshes settings and prices and adopts the new tax when checkout is rejected with SALE_TAX_MISMATCH', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({
        status: 409,
        message: 'Tax mismatch',
        originalError: { error: { errorCode: 'SALE_TAX_MISMATCH', data: { serverTax: 9 } } },
      })));
      posServiceMock.cartPrices
        .mockReturnValueOnce(of({ data: [pricedRow()] }))
        .mockReturnValue(of({ data: [pricedRow({ taxRate: 5 })] }));
      posServiceMock.getPosSettings.mockReturnValue(of({ data: { ...posSettings, pricesIncludeTax: false, defaultTaxRate: 5 } }));

      component.completeSale();

      expect(posServiceMock.getPosSettings).toHaveBeenCalled();
      expect(component.cartItems()[0].taxRate).toBe(5);
      expect(component.taxAmount()).toBe(9);
      expect(component.checkoutNotice()?.variant).toBe('warning');
      expect(alertServiceMock.warning).toHaveBeenCalled();
      expect(component.isProcessing()).toBe(false);
    });

    it('maps checkout error codes to inline messages instead of the raw server text', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({
        status: 409,
        message: 'raw message',
        originalError: { error: { errorCode: 'SALE_OUTLET_INVOICE_PREFIX_MISSING' } },
      })));

      component.completeSale();

      expect(component.checkoutNotice()?.variant).toBe('danger');
      expect(component.checkoutNotice()?.text).toContain('Outlet settings');
      expect(alertServiceMock.error).not.toHaveBeenCalled();
    });

    it('reloads terminals and shows the blocking message on SALE_NO_ACTIVE_TERMINAL', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      prepareCheckout(component);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({
        status: 409,
        message: 'raw message',
        originalError: { error: { errorCode: 'SALE_NO_ACTIVE_TERMINAL' } },
      })));

      component.completeSale();

      expect(posServiceMock.getTerminals).toHaveBeenCalledWith(3);
      expect(component.terminalsState()).toBe('none');
      expect(component.selectedTerminalId()).toBeNull();
      expect(component.checkoutNotice()?.text).toBe(
        'No active terminal is configured for this outlet. Add or activate one in Outlet settings.');
    });
  });

  describe('tax display', () => {
    it('uses the server algorithm with per-line rates and classifications', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set(posSettings);
      component.cartItems.set([
        { variantId: 1, productName: 'A', variantSku: 'A', quantity: 1, unitPrice: 600, discountAmount: 0, subtotal: 600, taxRate: 10, taxClassification: 'standard' },
        { variantId: 2, productName: 'B', variantSku: 'B', quantity: 1, unitPrice: 400, discountAmount: 0, subtotal: 400, taxRate: 10, taxClassification: 'zero_rated' },
      ]);
      component.discountPercent.set(10);

      expect(component.discountAmount()).toBe(100);
      expect(component.taxAmount()).toBe(54);
      expect(component.grandTotal()).toBe(954);
      expect(component.taxLabel()).toBe('VAT');
    });

    it('treats prices as tax-inclusive and labels the tax line "incl. VAT"', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, pricesIncludeTax: true });
      component.pricesIncludeTax.set(true);
      component.cartItems.set([
        { variantId: 1, productName: 'A', variantSku: 'A', quantity: 1, unitPrice: 110, discountAmount: 0, subtotal: 110, taxRate: 10, taxClassification: 'standard' },
      ]);

      expect(component.taxAmount()).toBe(10);
      expect(component.grandTotal()).toBe(110);
      expect(component.taxLabel()).toBe('incl. VAT');
    });

    it('charges no tax when the Business tax switch is off', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, taxEnabled: false });
      component.cartItems.set([
        { variantId: 1, productName: 'A', variantSku: 'A', quantity: 1, unitPrice: 100, discountAmount: 0, subtotal: 100, taxRate: 10, taxClassification: 'standard' },
      ]);

      expect(component.taxAmount()).toBe(0);
      expect(component.grandTotal()).toBe(100);
    });

    it('fetches tax details from cart-prices for a catalogue product and adopts the server price', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      posServiceMock.cartPrices.mockReturnValue(of({ data: [pricedRow({ productVariantId: 1001, finalPrice: 950, taxRate: 7.5 })] }));

      component.addToCart(buildProduct({ variantId: 1001, price: 999, stockQty: 5 }));

      expect(posServiceMock.cartPrices).toHaveBeenCalledWith([{ productVariantId: 1001, quantity: 1, outletId: 3 }]);
      const line = component.cartItems()[0];
      expect(line.unitPrice).toBe(950);
      expect(line.taxRate).toBe(7.5);
      expect(line.subtotal).toBe(950);
    });
  });

  describe('manual discount permissions and limit', () => {
    function cartWithLineDiscount(component: PosScreenComponent, percent: number): void {
      component.cartItems.set([{
        variantId: 7, productName: 'Item G', variantSku: 'SKU-G', quantity: 1, unitPrice: 100,
        discountPercent: 0, discountAmount: 0, subtotal: 100, taxRate: 10, taxClassification: 'standard',
      }]);
      component.selectedTerminalId.set(8001);
      component.activeCashSession.set({ id: 1, openedAt: new Date().toISOString(), isOverdue: false } as any);
      component.cashTendered.set(1000);
      component.onItemDiscountChange(0, String(percent));
    }

    it('shows the limit and allows a discount within it', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, maxManualDiscountPercent: 10 });
      cartWithLineDiscount(component, 10);

      expect(component.discountLimitText()).toBe('Manual discount limit: 10%');
      expect(component.discountBlockMessage()).toBe('');
      expect(component.canCompleteSale()).toBe(true);
    });

    it('blocks a discount over the limit without override and does not submit', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, maxManualDiscountPercent: 10 });
      cartWithLineDiscount(component, 15);

      expect(component.discountBlockMessage()).toContain('10%');
      expect(component.canCompleteSale()).toBe(false);

      component.completeSale();

      expect(saleServiceMock.createSale).not.toHaveBeenCalled();
      expect(posServiceMock.cartPrices).not.toHaveBeenCalled();
      expect(component.checkoutNotice()?.variant).toBe('danger');
    });

    it('allows a discount over the limit with the override permission and shows a notice', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, maxManualDiscountPercent: 10, canOverrideDiscountLimit: true });
      saleServiceMock.createSale.mockReturnValue(of({ data: { id: 0 } }));
      cartWithLineDiscount(component, 15);

      expect(component.discountBlockMessage()).toBe('');
      expect(component.discountOverrideNotice()).toContain('override');

      component.completeSale();

      expect(saleServiceMock.createSale).toHaveBeenCalledTimes(1);
      expect(saleServiceMock.createSale.mock.calls[0][0].items[0].discountAmount).toBe(15);
    });

    it('says manual discounts are not enabled when the limit is 0 and blocks them', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, maxManualDiscountPercent: 0 });
      cartWithLineDiscount(component, 5);

      expect(component.discountLimitText()).toBe('Manual discounts are not enabled');
      expect(component.discountBlockMessage()).toBe('Manual discounts are not enabled for this business.');
      expect(component.canCompleteSale()).toBe(false);
    });

    it('refuses to apply a manual discount without sales.discount.apply', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, canApplyDiscount: false });
      component.cartItems.set([{
        variantId: 7, productName: 'Item G', variantSku: 'SKU-G', quantity: 1, unitPrice: 100,
        discountPercent: 0, discountAmount: 0, subtotal: 100, taxRate: 10, taxClassification: 'standard',
      }]);

      expect(component.canApplyDiscount()).toBe(false);
      component.onItemDiscountChange(0, '5');
      component.onDiscountChange('5');

      expect(alertServiceMock.error).toHaveBeenCalled();
      expect(component.cartItems()[0].discountAmount).toBe(0);
      expect(component.discountPercent()).toBe(0);
    });

    it('falls back to the permission when the settings are not available', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      authServiceMock.hasPermission.mockImplementation((permission: string) => permission !== 'sales.discount.apply');

      expect(component.canApplyDiscount()).toBe(false);
      expect(component.canOverrideDiscountLimit()).toBe(true);
    });

    it('maps SALE_DISCOUNT_LIMIT_EXCEEDED from the server to an inline message', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.posSettings.set({ ...posSettings, maxManualDiscountPercent: 20 });
      cartWithLineDiscount(component, 15);
      saleServiceMock.createSale.mockReturnValue(throwError(() => ({
        status: 409,
        message: 'raw',
        originalError: { error: { errorCode: 'SALE_DISCOUNT_LIMIT_EXCEEDED', data: { limitPercent: 10, effectivePercent: 15, scope: 'line' } } },
      })));

      component.completeSale();

      expect(component.checkoutNotice()?.variant).toBe('danger');
      expect(component.checkoutNotice()?.text).toContain('15%');
      expect(component.checkoutNotice()?.text).toContain('10%');
    });
  });

  describe('terminal selection', () => {
    function startScreen(terminals: Array<{ id: number; name: string; code: string; isDefault?: boolean }>) {
      posServiceMock.getTerminals.mockReturnValue(of({ data: terminals }));
      const fixture = TestBed.createComponent(PosScreenComponent);
      fixture.detectChanges();
      return fixture;
    }

    function putItemInCart(component: PosScreenComponent): void {
      component.cartItems.set([{
        variantId: 7, productName: 'Item G', variantSku: 'SKU-G', quantity: 1, unitPrice: 100,
        discountPercent: 0, discountAmount: 0, subtotal: 100, taxRate: 10, taxClassification: 'standard',
      }]);
      component.activeCashSession.set({ id: 1, openedAt: new Date().toISOString(), isOverdue: false } as any);
      component.cashTendered.set(1000);
    }

    it('blocks checkout with a clear message when the outlet has no active terminal', () => {
      const fixture = startScreen([]);
      const component = fixture.componentInstance;
      putItemInCart(component);
      fixture.detectChanges();

      expect(component.terminalsState()).toBe('none');
      expect(component.selectedTerminalId()).toBeNull();
      expect(component.canCompleteSale()).toBe(false);
      expect(fixture.nativeElement.textContent).toContain(
        'No active terminal is configured for this outlet. Add or activate one in Outlet settings.');
    });

    it('auto-selects the only active terminal', () => {
      const component = startScreen([{ id: 21, name: 'Till 1', code: 'T1' }]).componentInstance;
      putItemInCart(component);

      expect(component.terminalsState()).toBe('single');
      expect(component.selectedTerminalId()).toBe(21);
      expect(component.terminalPrompt()).toBe('');
      expect(component.canCompleteSale()).toBe(true);
    });

    it('requires an explicit choice when several terminals exist, ignoring isDefault', () => {
      const fixture = startScreen([
        { id: 21, name: 'Till 1', code: 'T1', isDefault: true },
        { id: 22, name: 'Till 2', code: 'T2' },
      ]);
      const component = fixture.componentInstance;
      putItemInCart(component);
      fixture.detectChanges();

      expect(component.terminalsState()).toBe('multiple');
      expect(component.selectedTerminalId()).toBeNull();
      expect(component.canCompleteSale()).toBe(false);
      expect(component.terminalPrompt()).toContain('Select the POS terminal');
      expect(fixture.nativeElement.textContent).toContain('Select the POS terminal');

      component.completeSale();
      expect(saleServiceMock.createSale).not.toHaveBeenCalled();

      component.onTerminalChange(22);
      expect(component.selectedTerminalId()).toBe(22);
      expect(component.terminalPrompt()).toBe('');
    });

    it('treats a failed terminal load as blocking without claiming none are configured', () => {
      posServiceMock.getTerminals.mockReturnValue(throwError(() => ({ status: 500 })));
      const fixture = TestBed.createComponent(PosScreenComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance.terminalsState()).toBe('error');
      expect(fixture.componentInstance.terminalPrompt()).not.toContain('Outlet settings');
      expect(fixture.componentInstance.canCompleteSale()).toBe(false);
    });
  });

  describe('Phase 4 checkout UX', () => {
    function cartLine(overrides: Record<string, unknown> = {}): any {
      return {
        variantId: 7,
        productName: 'Item G',
        variantSku: 'SKU-G',
        quantity: 1,
        unitPrice: 100,
        discountPercent: 0,
        discountAmount: 0,
        subtotal: 100,
        taxRate: 10,
        taxClassification: 'standard',
        ...overrides,
      };
    }

    function readyForCheckout(component: PosScreenComponent): void {
      component.posSettings.set(posSettings);
      component.cartItems.set([cartLine()]);
      component.selectedTerminalId.set(8001);
      component.activeCashSession.set({ id: 1, openedAt: new Date().toISOString(), isOverdue: false } as any);
    }

    it('preselects the server default salesperson and sends the chosen salesperson with the sale', () => {
      posServiceMock.getTerminals.mockReturnValue(of({ data: [{ id: 8001, name: 'T1', code: 'T1' }] }));
      posServiceMock.getActiveCashSession.mockReturnValue(of({ data: { id: 1, openedAt: new Date().toISOString(), businessDate: '2026-11-03', isOverdue: false } }));
      posServiceMock.getSalespersons.mockReturnValue(of({ data: {
        salespersons: [{ userId: 11, name: 'Cashier', primaryOutlet: true }, { userId: 12, name: 'Seller', primaryOutlet: true }],
        defaultSalespersonUserId: 11,
        defaultSource: 'session_opener',
      } }));
      const fixture = TestBed.createComponent(PosScreenComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      expect(posServiceMock.getSalespersons).toHaveBeenCalledWith(3, 8001);
      expect(component.selectedSalespersonId()).toBe(11);

      component.onSalespersonChange('12');
      readyForCheckout(component);
      component.cashTendered.set(200);
      saleServiceMock.createSale.mockReturnValue(of({ data: { id: 0 } }));
      component.completeSale();

      expect(saleServiceMock.createSale.mock.calls[0][0].salespersonUserId).toBe(12);
      expect(saleServiceMock.createSale.mock.calls[0][0].cashierId).toBeUndefined();
    });

    it('accepts a typed whole quantity, rejects zero and caps at the known outlet stock', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.cartItems.set([cartLine({ knownStock: 5 })]);

      component.setQuantity(0, '3');
      expect(component.cartItems()[0].quantity).toBe(3);
      expect(component.cartItems()[0].subtotal).toBe(300);

      component.setQuantity(0, '0');
      expect(component.cartItems()[0].quantity).toBe(3);
      expect(alertServiceMock.warning).toHaveBeenCalled();

      component.setQuantity(0, '9');
      expect(component.cartItems()[0].quantity).toBe(5);
    });

    it('asks before clearing the cart and before a recall replaces a cart in progress', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.cartItems.set([cartLine()]);

      component.requestClearCart();
      expect(alertServiceMock.confirm).toHaveBeenCalledTimes(1);
      expect(component.cartItems().length).toBe(1);

      component.recallHeldSale({ id: 5, items: [], discountPercent: 0, paymentMethod: 'cash' } as any);
      expect(alertServiceMock.confirm).toHaveBeenCalledTimes(2);
      expect(posServiceMock.recallHeldSale).not.toHaveBeenCalled();

      component.discardHeldSale({ id: 5, items: [] } as any);
      expect(alertServiceMock.confirm).toHaveBeenCalledTimes(3);
      expect(posServiceMock.discardHeldSale).not.toHaveBeenCalled();
    });

    it('shows change and keeps only the cash allocation as net cash retained in a split payment', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      readyForCheckout(component);
      component.useSplitPayment.set(true);
      component.splitPayments.set([
        { method: 'cash', amount: 60, tendered: 100 },
        { method: 'card', amount: 50 },
      ]);

      expect(component.payableAmount()).toBe(110);
      expect(component.change()).toBe(40);
      expect(component.netCashRetained()).toBe(60);
      expect(component.cashTenderedTotal()).toBe(100);
      expect(component.canCompleteSale()).toBe(true);

      component.splitPayments.set([
        { method: 'cash', amount: 60, tendered: 50 },
        { method: 'card', amount: 50 },
      ]);
      expect(component.splitTenderError()).toContain('cannot be less');
      expect(component.canCompleteSale()).toBe(false);
    });

    it('records the first receipt print as Printed and later prints as Reprinted', () => {
      const print = vi.spyOn(window, 'print').mockImplementation(() => {});
      (saleServiceMock as any).registerReceiptPrintAttempt = vi.fn().mockReturnValue(of({}));
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      component.completedSale.set({ id: 42 });

      component.printReceipt();
      component.printReceipt();

      const actions = (saleServiceMock as any).registerReceiptPrintAttempt.mock.calls.map((call: any[]) => call[1].action);
      expect(actions).toEqual(['Printed', 'Reprinted']);
      print.mockRestore();
    });

    it('never shows a browser-derived business date when no session is open', () => {
      const component = TestBed.createComponent(PosScreenComponent).componentInstance;
      expect(component.headerBusinessDate()).toBeNull();
    });
  });
});
