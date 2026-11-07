import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { SaleItemDto } from '../../models/sale.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { SaleService } from '../../services/sale.service';
import { SaleDetailsComponent } from './sale-details.component';

const unitLine: SaleItemDto = {
  id: 1, variantId: 5, productName: 'Attar', variantSku: 'ATR', quantity: 24, unitPrice: 41.666666666667, discountAmount: 0, subtotal: 1000,
  unitCode: 'P12', unitLabel: '12 ml bottle', enteredQuantity: 2, enteredUnitPrice: 500, baseUnitCode: 'ml', packState: 'prepared', returnedQuantity: 0,
};
const plainLine: SaleItemDto = {
  id: 2, variantId: 6, productName: 'Shirt', variantSku: 'SHT', quantity: 3, unitPrice: 100, discountAmount: 0, subtotal: 300,
};
const saleOf = (items: SaleItemDto[], over: Record<string, unknown> = {}) => ({
  id: 9, saleNumber: 'S-9', status: 'completed', saleDate: '2020-01-01T00:00:00Z', outletName: 'Main', cashierName: 'C', paymentMethod: 'card', payments: [],
  totalAmount: 0, discount: 0, tax: 0, netTotal: 0, items, ...over,
});

describe('SaleDetailsComponent units and refund', () => {
  let fixture: ComponentFixture<SaleDetailsComponent>;
  let page: SaleDetailsComponent;
  const sales = { getSaleById: vi.fn(), refundSale: vi.fn(), voidSale: vi.fn(), reprintSaleReceipt: vi.fn(), getReceiptPdf: vi.fn() };
  const alerts = { error: vi.fn(), success: vi.fn() };
  const auth = { hasPermission: vi.fn() };

  async function setup(sale: unknown): Promise<void> {
    vi.clearAllMocks();
    auth.hasPermission.mockImplementation((key: string) => key === 'sales.refund');
    sales.getSaleById.mockReturnValue(of({ data: sale }));
    sales.refundSale.mockReturnValue(of({ data: {} }));
    await TestBed.configureTestingModule({
      imports: [SaleDetailsComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '9' } } } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        { provide: SaleService, useValue: sales },
        { provide: AlertService, useValue: alerts },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: () => 'server said no' } },
        { provide: AuthService, useValue: auth },
        { provide: CurrencyService, useValue: { format: (value: number) => `$${value}`, getCurrencySymbol: () => '$' } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(SaleDetailsComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const line = (index: number) => page.refundLines()[index];

  it('shows the entered unit, price and pack state instead of the base figures', async () => {
    await setup(saleOf([unitLine]));

    expect(text()).toContain('2 12 ml bottle');
    expect(text()).toContain('= 24 ml');
    expect(text()).toContain('per 12 ml bottle');
    expect(text()).toContain('prepared');
    expect(page.displayUnitPrice(unitLine)).toBe(500);
  });

  it('leaves a pcs line as it was and offers no refund on a pcs-only sale', async () => {
    await setup(saleOf([plainLine]));

    expect(text()).not.toContain('per ');
    expect(page.displayUnitPrice(plainLine)).toBe(100);
    expect(page.canRefund()).toBe(false);
  });

  it.each([
    ['without sales.refund', () => auth.hasPermission.mockReturnValue(false), {}],
    ['on a refunded sale', () => undefined, { status: 'refunded' }],
    ['on a voided sale', () => undefined, { status: 'voided' }],
  ])('offers no refund %s', async (_name, arrange, over) => {
    await setup(saleOf([unitLine], over));
    arrange();
    expect(page.canRefund()).toBe(false);
  });

  it('refunds a full sale with condition and packaging for the unit line, and nothing extra for the pcs line', async () => {
    await setup(saleOf([unitLine, plainLine]));
    expect(page.canRefund()).toBe(true);
    page.openRefundDialog();
    page.updateRefundLine(0, { condition: 'resellable', packagingReusable: true });
    page.refundReason.set(' Returned ');

    page.confirmRefund();

    expect(sales.refundSale).toHaveBeenCalledWith(9, {
      items: [
        { variantId: 5, quantity: 24, saleItemId: 1, condition: 'resellable', packagingReusable: true },
        { variantId: 6, quantity: 3, saleItemId: 2 },
      ],
      reason: 'Returned',
    });
    expect(alerts.success).toHaveBeenCalled();
    expect(sales.getSaleById).toHaveBeenCalledTimes(2);
    expect(page.showRefundDialog()).toBe(false);
  });

  it('sends a partial base quantity and leaves out lines at zero', async () => {
    await setup(saleOf([unitLine, plainLine]));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6.5, condition: 'non_resellable' });
    page.updateRefundLine(1, { quantity: 0 });
    page.refundReason.set('Leak');

    page.confirmRefund();

    expect(sales.refundSale.mock.calls[0][1].items).toEqual([
      { variantId: 5, quantity: 6.5, saleItemId: 1, condition: 'non_resellable', packagingReusable: false },
    ]);
  });

  it('never marks the packaging of a non-resellable line as reusable', async () => {
    await setup(saleOf([unitLine]));
    page.openRefundDialog();
    page.updateRefundLine(0, { packagingReusable: true });
    page.updateRefundLine(0, { condition: 'non_resellable' });
    expect(line(0).packagingReusable).toBe(false);
  });

  it('needs the cash amount only for a partial refund of a cash sale', async () => {
    await setup(saleOf([unitLine], { paymentMethod: 'cash' }));
    page.openRefundDialog();
    page.refundReason.set('Leak');
    expect(page.needsCashAmount()).toBe(false);

    page.updateRefundLine(0, { quantity: 12 });
    expect(page.needsCashAmount()).toBe(true);
    page.confirmRefund();
    expect(sales.refundSale).not.toHaveBeenCalled();
    expect(page.refundError()).toContain('cash paid back');

    page.refundCashAmount.set(500);
    page.confirmRefund();
    expect(sales.refundSale.mock.calls[0][1].cashRefundAmount).toBe(500);
  });

  it('sends no cash amount for a card sale', async () => {
    await setup(saleOf([unitLine]));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 12 });
    page.refundReason.set('Leak');
    expect(page.needsCashAmount()).toBe(false);
    page.confirmRefund();
    expect(Object.keys(sales.refundSale.mock.calls[0][1])).not.toContain('cashRefundAmount');
  });

  it.each([
    ['more than sold', 0, { quantity: 25 }, 'cannot exceed'],
    ['a negative quantity', 0, { quantity: -1 }, 'cannot be negative'],
    ['a fractional pcs quantity', 1, { quantity: 1.5 }, 'whole number'],
    ['too many decimals', 0, { quantity: 1.1234567 }, '6 decimal'],
  ])('refuses %s', async (_name, index, change, message) => {
    await setup(saleOf([unitLine, plainLine]));
    page.openRefundDialog();
    page.refundReason.set('Leak');
    page.updateRefundLine(index, change);
    page.confirmRefund();
    expect(sales.refundSale).not.toHaveBeenCalled();
    expect(page.refundError()).toContain(message);
  });

  it('requires a reason and at least one line', async () => {
    await setup(saleOf([unitLine]));
    page.openRefundDialog();
    page.confirmRefund();
    expect(page.refundError()).toContain('reason');

    page.refundReason.set('Leak');
    page.updateRefundLine(0, { quantity: 0 });
    page.confirmRefund();
    expect(page.refundError()).toContain('at least one line');
    expect(sales.refundSale).not.toHaveBeenCalled();
  });

  it('keeps the dialog and the input on a server error and shows the API message', async () => {
    await setup(saleOf([unitLine]));
    sales.refundSale.mockReturnValue(throwError(() => new Error('no')));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.refundReason.set('Leak');

    page.confirmRefund();

    expect(page.refundError()).toBe('server said no');
    expect(page.showRefundDialog()).toBe(true);
    expect(line(0).quantity).toBe(6);
    expect(page.isRefunding()).toBe(false);
  });

  it('prevents a double submit', async () => {
    await setup(saleOf([unitLine]));
    page.openRefundDialog();
    page.refundReason.set('Leak');
    page.isRefunding.set(true);
    page.confirmRefund();
    expect(sales.refundSale).not.toHaveBeenCalled();
  });

  it('renders the refund dialog with the unit line controls', async () => {
    await setup(saleOf([unitLine, plainLine]));
    page.openRefundDialog();
    fixture.detectChanges();

    const dialog = document.body.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(dialog).toContain('Refund Sale');
    expect(dialog).toContain('Packaging can be reused');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('fieldset select')).toHaveLength(1);
  });
});
