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
  unitCode: 'P12', unitLabel: '12 ml bottle', enteredQuantity: 2, enteredUnitPrice: 500, baseUnitCode: 'ml', packState: 'prepared', returnedQuantity: 0, remainingReturnableQuantity: 24,
};
const plainLine: SaleItemDto = {
  id: 2, variantId: 6, productName: 'Shirt', variantSku: 'SHT', quantity: 3, unitPrice: 100, discountAmount: 0, subtotal: 300,
  returnedQuantity: 0, remainingReturnableQuantity: 3,
};
const saleOf = (items: SaleItemDto[], over: Record<string, unknown> = {}) => ({
  id: 9, saleNumber: 'S-9', status: 'completed', saleDate: '2020-01-01T00:00:00Z', outletName: 'Main', cashierName: 'C', paymentMethod: 'card', payments: [],
  totalAmount: 0, discount: 0, tax: 0, netTotal: 0, items, refunds: [], ...over,
});
const refundOf = (over: Record<string, unknown> = {}) => ({
  id: 70, refundNumber: 'R-9-1', sequence: 1, createdAtUtc: '2020-01-02T10:00:00Z', reason: 'Leak', actorUserId: 3, actorName: 'Rana',
  netAmount: 100, vatAmount: 0, totalAmount: 100, cashAmount: 60,
  lines: [{ saleItemId: 2, quantity: 1, condition: 'non_resellable', restockedQuantity: 0, packagingReusable: false, netAmount: 100, vatAmount: 0 }],
  payments: [{ method: 'cash', amount: 60 }, { method: 'card', amount: 40 }],
  ...over,
});
const codeError = (status: number, errorCode: string) => ({ status, message: 'server said no', originalError: { error: { errorCode } } });

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

  it('leaves a pcs line as it was and offers a refund on a completed pcs-only sale', async () => {
    await setup(saleOf([plainLine]));

    expect(text()).not.toContain('per ');
    expect(page.displayUnitPrice(plainLine)).toBe(100);
    expect(page.canRefund()).toBe(true);
    expect(text()).toContain('Refund');
  });

  it('offers a refund on a partially refunded sale with something left to return', async () => {
    await setup(saleOf([{ ...plainLine, returnedQuantity: 1, remainingReturnableQuantity: 2 }], { status: 'partially_refunded' }));
    expect(page.canRefund()).toBe(true);
    expect(text()).toContain('Partially refunded');
    expect((fixture.nativeElement as HTMLElement).querySelector('.status-partial')).not.toBeNull();
  });

  it.each([
    ['without sales.refund', () => auth.hasPermission.mockReturnValue(false), {}, [unitLine]],
    ['on a refunded sale', () => undefined, { status: 'refunded' }, [unitLine]],
    ['on a voided sale', () => undefined, { status: 'voided' }, [unitLine]],
    ['when nothing remains to return', () => undefined, { status: 'partially_refunded' },
      [{ ...plainLine, returnedQuantity: 3, remainingReturnableQuantity: 0 }]],
  ])('offers no refund %s', async (_name, arrange, over, items) => {
    await setup(saleOf(items as SaleItemDto[], over));
    arrange();
    expect(page.canRefund()).toBe(false);
  });

  it('refunds with a condition for every line, packaging only for the unit line, and an idempotency key', async () => {
    await setup(saleOf([unitLine, plainLine]));
    expect(page.canRefund()).toBe(true);
    page.openRefundDialog();
    expect(line(0).quantity).toBe(0);
    page.updateRefundLine(0, { quantity: 24, condition: 'resellable', packagingReusable: true });
    page.updateRefundLine(1, { quantity: 3, condition: 'non_resellable' });
    page.refundReason.set(' Returned ');

    page.confirmRefund();

    const [id, dto] = sales.refundSale.mock.calls[0];
    expect(id).toBe(9);
    expect(dto).toEqual({
      items: [
        { variantId: 5, quantity: 24, saleItemId: 1, condition: 'resellable', packagingReusable: true },
        { variantId: 6, quantity: 3, saleItemId: 2, condition: 'non_resellable' },
      ],
      reason: 'Returned',
      idempotencyKey: expect.any(String),
    });
    expect(dto.idempotencyKey.length).toBeLessThanOrEqual(120);
    expect(alerts.success).toHaveBeenCalled();
    expect(sales.getSaleById).toHaveBeenCalledTimes(2);
    expect(page.showRefundDialog()).toBe(false);
  });

  it('sends a partial base quantity and leaves out lines at zero', async () => {
    await setup(saleOf([unitLine, plainLine]));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6.5, condition: 'non_resellable' });
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

  it('sends no cash amount by default, even for a partial refund of a cash sale', async () => {
    await setup(saleOf([unitLine], { paymentMethod: 'cash' }));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 12 });
    page.refundReason.set('Leak');
    expect(page.needsCashAmount()).toBe(false);
    page.confirmRefund();
    expect(Object.keys(sales.refundSale.mock.calls[0][1])).not.toContain('cashRefundAmount');
  });

  it('asks for the cash paid back only after CASH_REFUND_POLICY_UNDEFINED and then sends it', async () => {
    await setup(saleOf([unitLine], { paymentMethod: 'cash' }));
    sales.refundSale.mockReturnValueOnce(throwError(() => codeError(400, 'CASH_REFUND_POLICY_UNDEFINED')));
    page.openRefundDialog();
    fixture.detectChanges();
    expect(document.body.textContent).not.toContain('Cash paid back');
    page.updateRefundLine(0, { quantity: 12 });
    page.refundReason.set('Leak');

    page.confirmRefund();
    expect(page.needsCashAmount()).toBe(true);
    expect(page.refundError()).toContain('cash paid back');
    fixture.detectChanges();
    expect(document.body.textContent).toContain('Cash paid back');

    page.confirmRefund();
    expect(sales.refundSale).toHaveBeenCalledTimes(1);
    expect(page.refundError()).toContain('cash paid back');

    page.refundCashAmount.set(500);
    page.confirmRefund();
    expect(sales.refundSale.mock.calls[1][1].cashRefundAmount).toBe(500);
  });

  it('reuses the idempotency key on a retry after an error and renews it after a success', async () => {
    await setup(saleOf([unitLine]));
    sales.refundSale.mockReturnValueOnce(throwError(() => codeError(500, 'X')));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.refundReason.set('Leak');

    page.confirmRefund();
    page.confirmRefund();
    const first = sales.refundSale.mock.calls[0][1].idempotencyKey;
    expect(first).toBeTruthy();
    expect(sales.refundSale.mock.calls[1][1].idempotencyKey).toBe(first);

    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.refundReason.set('Leak');
    page.confirmRefund();
    expect(sales.refundSale.mock.calls[2][1].idempotencyKey).not.toBe(first);
  });

  it('uses a new idempotency key when the request changes after an error', async () => {
    await setup(saleOf([unitLine]));
    sales.refundSale.mockReturnValueOnce(throwError(() => codeError(400, 'X')));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.refundReason.set('Leak');
    page.confirmRefund();

    page.updateRefundLine(0, { quantity: 5 });
    page.confirmRefund();
    expect(sales.refundSale.mock.calls[1][1].idempotencyKey).not.toBe(sales.refundSale.mock.calls[0][1].idempotencyKey);
  });

  it('shows access denied on 403 and keeps the dialog open', async () => {
    await setup(saleOf([unitLine]));
    sales.refundSale.mockReturnValue(throwError(() => ({ status: 403, message: 'forbidden' })));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.refundReason.set('Leak');
    page.confirmRefund();
    expect(page.refundError()).toContain('Access denied');
    expect(page.showRefundDialog()).toBe(true);
  });

  it('estimates each line pro rata and labels it as an estimate', async () => {
    await setup(saleOf([unitLine, plainLine]));
    page.openRefundDialog();
    page.updateRefundLine(0, { quantity: 6 });
    page.updateRefundLine(1, { quantity: 1 });
    expect(page.estimatedRefund(line(0))).toBe(250);
    expect(page.estimatedRefund(line(1))).toBe(100);
    expect(page.estimatedRefundTotal()).toBe(350);
    fixture.detectChanges();
    expect(document.body.textContent).toContain('estimate only');
  });

  it.each([
    ['more than sold', 0, { quantity: 25 }, 'cannot exceed'],
    ['more than remains', 1, { quantity: 3 }, 'cannot exceed'],
    ['a negative quantity', 0, { quantity: -1 }, 'cannot be negative'],
    ['a fractional pcs quantity', 1, { quantity: 1.5 }, 'whole number'],
    ['too many decimals', 0, { quantity: 1.1234567 }, '6 decimal'],
  ])('refuses %s', async (_name, index, change, message) => {
    await setup(saleOf([unitLine, { ...plainLine, returnedQuantity: 1, remainingReturnableQuantity: 2 }], { status: 'partially_refunded' }));
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
    page.updateRefundLine(0, { quantity: 1 });
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
    page.updateRefundLine(0, { quantity: 1 });
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
    expect(dialog).toContain('several parts');
    expect(dialog).not.toContain('only once');
    expect(document.body.querySelectorAll('fieldset select')).toHaveLength(2);
    expect(document.body.querySelectorAll('fieldset input[type="checkbox"]')).toHaveLength(1);
  });

  it('shows a line with nothing left as read-only', async () => {
    await setup(saleOf([unitLine, { ...plainLine, returnedQuantity: 3, remainingReturnableQuantity: 0 }], { status: 'partially_refunded' }));
    page.openRefundDialog();
    fixture.detectChanges();
    expect(document.body.textContent).toContain('Nothing left to return on this line.');
    expect(document.body.querySelectorAll('fieldset input[type="number"]')).toHaveLength(1);
  });

  it('renders the refund history with lines, condition, restock and payment split', async () => {
    await setup(saleOf([unitLine, { ...plainLine, returnedQuantity: 1, remainingReturnableQuantity: 2 }],
      { status: 'partially_refunded', refunds: [refundOf()] }));

    const page_ = text();
    expect(page_).toContain('R-9-1');
    expect(page_).toContain('Rana');
    expect(page_).toContain('Reason: Leak');
    expect(page_).toContain('Shirt');
    expect(page_).toContain('Returned 1 pcs · Non-resellable · restocked 0 pcs');
    expect(page_).toContain('$100');
    expect(page_).toContain('cash: $60');
    expect(page_).toContain('card: $40');
  });

  it('notes a refunded sale recorded before refund history', async () => {
    await setup(saleOf([{ ...plainLine, remainingReturnableQuantity: 0 }], { status: 'refunded', refunds: [] }));
    expect(text()).toContain('refunded before refund history was recorded');
    expect(page.canRefund()).toBe(false);
  });
});
