import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ExchangePreview, ExchangeSourceInvoice } from '../../models/exchange.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { ConnectivityService } from '../../services/connectivity.service';
import { CurrencyService } from '../../services/currency.service';
import { ExchangeService } from '../../services/exchange.service';
import { PosService } from '../../services/pos.service';
import { SaleService } from '../../services/sale.service';
import { UomService } from '../../services/uom.service';
import { CONDITION_REQUIRED_MESSAGE, ExchangeWorkflowComponent, WALK_IN_COMBINE_MESSAGE } from './exchange-workflow.component';

const invoiceOf = (over: Partial<ExchangeSourceInvoice> = {}): ExchangeSourceInvoice => ({
  saleId: 41, invoiceNumber: 'INV-41', purchaseLocalDate: '2026-11-05', outletId: 3, outletName: 'Main', customerId: 9,
  customerName: 'Rana', customerPhoneMasked: '01*****89', isWalkIn: false, status: 'completed', eligible: true,
  windowExpired: false, lastEligibleDate: '2026-11-12',
  lines: [{
    saleItemId: 501, variantId: 70, productName: 'Shirt', variantName: 'M', sku: 'SHT-M', barcode: '111', quantity: 2,
    returnedQuantity: 0, remainingQuantity: 2, unitLabel: null, enteredQuantity: null, paidGross: 200, evidenceAvailable: true
  }],
  ...over
});

const previewOf = (over: Partial<ExchangePreview> = {}): ExchangePreview => ({
  previewHash: 'hash-1', eligibilityLocalDate: '2026-11-07', receivingOutletId: 3, terminalId: 7, customerId: 9,
  phoneVerificationResult: 'not_required',
  returns: [{ sourceKind: 'sale_item', sourceLineId: 501, variantId: 70, quantity: 1, creditNet: 87, creditVat: 13, creditGross: 100, anchorDate: '2026-11-05', windowExpired: false, chainExceeded: false }],
  replacements: [{ variantId: 80, sellingOptionId: null, baseQuantity: 1, unitPrice: 100, lineDiscount: 0, invoiceDiscountShare: 0, taxableAmount: 87, vatRate: 15, vatAmount: 13, gross: 100, availableStock: 5 }],
  returnNet: 87, returnVat: 13, returnGross: 100, replacementNet: 87, replacementVat: 13, replacementGross: 100, replacementDiscount: 0,
  netAdjustmentGross: 0, settlementDirection: 'none', settlementAmount: 0, highValueAmount: 100, requiredApprovals: [],
  policyVersionNo: 1, loyaltyStatus: 'module_inactive',
  ...over
});

const apiError = (status: number, errorCode: string, data: unknown = null) =>
  ({ status, message: 'server said no', originalError: { error: { errorCode, data } } });

describe('ExchangeWorkflowComponent', () => {
  let fixture: ComponentFixture<ExchangeWorkflowComponent>;
  let page: ExchangeWorkflowComponent;
  const online = signal(true);
  const exchanges = {
    findSourceInvoice: vi.fn(), preview: vi.fn(), grantApprovals: vi.fn(), confirm: vi.fn(),
    getReceiptDocument: vi.fn(), recordPrintAttempt: vi.fn(), getReceiptPdf: vi.fn()
  };
  const pos = {
    getTerminals: vi.fn(() => of({ data: [{ id: 7, name: 'T1', code: 'T1' }] })),
    getActiveCashSession: vi.fn(() => of({ data: { id: 1, outletId: 3, terminalId: 7, businessDate: '2026-11-07' } })),
    getSalespersons: vi.fn(() => of({ data: { salespersons: [{ userId: 12, name: 'Sami', primaryOutlet: true }], defaultSalespersonUserId: 12, defaultSource: 'current_user' } })),
    lookup: vi.fn()
  };

  async function setup(): Promise<void> {
    vi.clearAllMocks();
    online.set(true);
    exchanges.preview.mockReturnValue(of({ success: true, data: previewOf() }));
    exchanges.getReceiptDocument.mockReturnValue(of({ success: true, data: null }));
    await TestBed.configureTestingModule({
      imports: [ExchangeWorkflowComponent],
      providers: [
        { provide: AuthService, useValue: { getUserValue: () => ({ id: 5, name: 'Cashier', outletId: 3, outletName: 'Main' }), hasPermission: () => true } },
        { provide: ExchangeService, useValue: exchanges },
        { provide: PosService, useValue: pos },
        { provide: SaleService, useValue: { getSaleById: vi.fn() } },
        { provide: UomService, useValue: { getVariantConfigs: () => of([]) } },
        { provide: ConnectivityService, useValue: { online } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        provideRouter([]),
        { provide: AlertService, useValue: { error: vi.fn(), success: vi.fn(), config: () => null } },
        { provide: CurrencyService, useValue: { format: (value: number) => `$${value}`, formatNumber: (value: number) => `${value}` } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExchangeWorkflowComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  /** A complete, valid exchange: one invoice, one sellable returned line, one replacement, a reason. */
  function fillValidExchange(): void {
    const invoice = invoiceOf();
    page.invoices.set([invoice]);
    page.addReturn(invoice, invoice.lines[0]);
    page.updateReturn('41:501', { conditionSellable: true });
    page.replacements.set([{
      key: '80', variantId: 80, productName: 'Shirt', variantName: 'L', sku: 'SHT-L', listPrice: 100, stockQty: 5,
      quantity: 1, discount: null, includePackaging: false
    }]);
    page.reasonForm.patchValue({ reasonCode: 'size_fitting' });
    page.onReasonChanged();
  }

  function reviewed(over: Partial<ExchangePreview> = {}): void {
    exchanges.preview.mockReturnValue(of({ success: true, data: previewOf(over) }));
    fillValidExchange();
    page.runPreview();
  }

  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');

  it('loads the terminal, the active session and the default salesperson', async () => {
    await setup();
    expect(page.terminalId()).toBe(7);
    expect(page.sessionState()).toBe('active');
    expect(page.salespersonId()).toBe(12);
    expect(page.blockReason()).toBe('');
  });

  it('lays out the invoice lookup as one aligned input + button row with the guidance below it', async () => {
    await setup();
    const form = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="invoice-lookup"]')!;
    const bar = form.querySelector('.ex-lookup__bar')!;
    expect(bar.querySelector('input#exchange-invoice-number')).not.toBeNull();
    expect(bar.querySelector('button[type="submit"]')).not.toBeNull();
    // Guidance and errors live outside the input row, so they can never push the button out of line.
    expect(bar.querySelector('#exchange-invoice-help')).toBeNull();
    expect(form.querySelector('#exchange-invoice-help')).not.toBeNull();
    expect(form.querySelector('label[for="exchange-invoice-number"]')?.textContent).toContain('Invoice number');
    const button = bar.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true); // nothing typed yet
  });

  it('disables Next while the current step is incomplete and says what is missing', async () => {
    await setup();
    fixture.detectChanges();
    const next = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Next') as HTMLButtonElement;
    expect(next.disabled).toBe(true);
    expect(text()).toContain('Add the original invoice to continue.');
  });

  it('adds a searched replacement with its code and variant, and a second pick of it adds one more', async () => {
    await setup();
    const pick = {
      variantId: 31, productName: 'Ari Wasenat Premium Borka', variantName: 'Default', productCode: 'ABA852P', sku: 'ABA852P-BLK-M',
      attributes: '{"Colour":"Black","Size":"M"}', effectivePrice: 2450, stockQty: 4, inStock: true
    };
    page.onReplacementPicked(pick);
    page.onReplacementPicked(pick);
    expect(page.replacements().length).toBe(1);
    expect(page.replacements()[0]).toMatchObject({ variantId: 31, quantity: 2, productCode: 'ABA852P', variantLabel: 'Colour: Black | Size: M', listPrice: 2450, stockQty: 4 });
  });

  it('shows the session business date as a date, not a raw timestamp (browser UAT DEF-02)', async () => {
    await setup();
    fixture.detectChanges();
    expect(text()).toContain('Open · 07/11/2026');
    expect(text()).not.toContain('2026-11-07T');
  });

  it('refuses to combine a walk-in invoice with another invoice (D-03)', async () => {
    await setup();
    page.invoices.set([invoiceOf()]);
    expect(page.invoiceProblem(invoiceOf({ saleId: 42, invoiceNumber: 'INV-42', isWalkIn: true, customerId: 1 }))).toBe(WALK_IN_COMBINE_MESSAGE);
    expect(page.invoiceProblem(invoiceOf({ saleId: 43, invoiceNumber: 'INV-43', customerId: 10 }))).toContain('different customer');
    expect(page.invoiceProblem(invoiceOf({ saleId: 44, invoiceNumber: 'INV-44' }))).toBe('');
  });

  it('adds an invoice found by its exact number', async () => {
    await setup();
    exchanges.findSourceInvoice.mockReturnValue(of({ success: true, data: invoiceOf() }));
    page.invoiceInput.set('INV-41');
    page.addInvoice();
    expect(exchanges.findSourceInvoice).toHaveBeenCalledWith('INV-41');
    expect(page.invoices().length).toBe(1);
    expect(page.invoiceInput()).toBe('');
  });

  it('asks which line when a scanned item matches several invoice lines', async () => {
    await setup();
    const invoice = invoiceOf();
    invoice.lines.push({ ...invoice.lines[0], saleItemId: 502 });
    page.invoices.set([invoice]);
    page.returnScan.set('111');
    page.scanReturn();
    expect(page.lineChoiceOpen()).toBe(true);
    expect(page.lineChoices().length).toBe(2);
    page.chooseLine(invoice, invoice.lines[1]);
    expect(page.returns()[0].line.saleItemId).toBe(502);
  });

  it('blocks the returned-items step until every line is confirmed sellable (EX-05)', async () => {
    await setup();
    const invoice = invoiceOf();
    page.invoices.set([invoice]);
    page.addReturn(invoice, invoice.lines[0]);

    expect(page.canLeaveStep(1)).toBe(false);
    expect(page.stepMessage()).toBe(CONDITION_REQUIRED_MESSAGE);

    page.updateReturn('41:501', { quantity: 3, conditionSellable: true });
    expect(page.canLeaveStep(1)).toBe(false);
    expect(page.stepMessage()).toContain('exceeds the remaining 2');

    page.updateReturn('41:501', { quantity: 2 });
    expect(page.canLeaveStep(1)).toBe(true);
  });

  it('requires details when the reason is Other', async () => {
    await setup();
    page.reasonForm.patchValue({ reasonCode: 'other' });
    page.onReasonChanged();
    expect(page.canLeaveStep(3)).toBe(false);
    expect(page.stepMessage()).toContain('Describe the reason');

    page.reasonForm.patchValue({ reasonOtherText: 'Gift for a friend' });
    page.onReasonChanged();
    expect(page.canLeaveStep(3)).toBe(true);
    expect(page.buildRequest()?.reasonOtherText).toBe('Gift for a friend');
  });

  it('sends the request to preview and shows the server totals with "Loyalty: not active"', async () => {
    await setup();
    reviewed();
    const sent = exchanges.preview.mock.calls[0][0];
    expect(sent.returns).toEqual([{ sourceKind: 'sale_item', sourceLineId: 501, quantity: 1, conditionSellable: true, packagingReusable: false }]);
    expect(sent.replacements).toEqual([{ variantId: 80, quantity: 1, discountAmount: 0, includePackaging: false }]);
    expect(sent.receivingOutletId).toBe(3);
    expect(sent.terminalId).toBe(7);
    expect(page.previewIsCurrent()).toBe(true);

    page.activeStep.set(4);
    fixture.detectChanges();
    expect(text()).toContain('Loyalty: not active');
    expect(text()).not.toMatch(/points/i);
  });

  it('re-exchanges an earlier exchange replacement line without listing the exchange as a source sale (EX-04)', async () => {
    await setup();
    const exchange = invoiceOf({
      saleId: 0, kind: 'exchange', invoiceNumber: 'MX2611071', customerId: 9,
      lines: [{
        saleItemId: 9001, variantId: 80, productName: 'Jacket', variantName: 'L', sku: 'JKT-L', barcode: '222', quantity: 1,
        returnedQuantity: 0, remainingQuantity: 1, unitLabel: null, enteredQuantity: null, paidGross: 110, evidenceAvailable: true,
        sourceKind: 'exchange_replacement_line'
      }]
    });
    exchanges.findSourceInvoice.mockReturnValue(of({ success: true, data: exchange }));
    page.invoiceInput.set('MX2611071');
    page.addInvoice();
    expect(page.invoices()).toEqual([exchange]);

    // A second exchange (also saleId 0) is a different source; the same one is refused.
    expect(page.invoiceProblem(invoiceOf({ ...exchange, invoiceNumber: 'MX2611072' }))).toBe('');
    expect(page.invoiceProblem(exchange)).toContain('already added');

    // Combined with the same customer's sale: only the sale is a source sale id.
    page.invoices.update(list => [...list, invoiceOf()]);
    page.addReturn(exchange, exchange.lines[0]);
    page.addReturn(page.invoices()[1], page.invoices()[1].lines[0]);
    expect(page.isReturned(exchange, exchange.lines[0])).toBe(true);
    page.returns.update(list => list.map(draft => ({ ...draft, conditionSellable: true })));
    page.replacements.set([{
      key: '81', variantId: 81, productName: 'Coat', variantName: 'L', sku: 'COT-L', listPrice: 100, stockQty: 5,
      quantity: 2, discount: null, includePackaging: false
    }]);
    page.reasonForm.patchValue({ reasonCode: 'size_fitting' });
    page.onReasonChanged();

    const request = page.buildRequest()!;
    expect(request.sourceSaleIds).toEqual([41]);
    expect(request.returns.map(line => [line.sourceKind, line.sourceLineId])).toEqual([
      ['exchange_replacement_line', 9001],
      ['sale_item', 501]
    ]);
    expect(page.returnInvoice(9001, 'exchange_replacement_line')).toBe('MX2611071');

    page.removeInvoice(exchange);
    expect(page.invoices().map(invoice => invoice.invoiceNumber)).toEqual(['INV-41']);
    expect(page.returns().map(draft => draft.line.saleItemId)).toEqual([501]);
  });

  it('never sends a product discount and an invoice discount together (EX-27)', async () => {
    await setup();
    fillValidExchange();
    page.setDiscountMode('line');
    page.updateReplacement('80', { discount: 5 });
    page.setDiscountMode('invoice');
    page.setInvoiceDiscount(10);
    const request = page.buildRequest()!;
    expect(request.invoiceDiscount).toBe(10);
    expect(request.replacements[0].discountAmount).toBe(0);
  });

  it('disables Confirm while offline and never sends the request', async () => {
    await setup();
    reviewed();
    expect(page.canConfirm()).toBe(true);

    online.set(false);
    page.activeStep.set(5);
    fixture.detectChanges();
    expect(page.canConfirm()).toBe(false);
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="confirm-exchange"]')!;
    expect(button.disabled).toBe(true);
    page.confirm();
    expect(exchanges.confirm).not.toHaveBeenCalled();
  });

  it('re-runs the preview when Confirm reports a stale preview', async () => {
    await setup();
    reviewed();
    exchanges.confirm.mockReturnValue(throwError(() => apiError(409, 'EXCHANGE_PREVIEW_STALE')));
    exchanges.preview.mockReturnValue(of({ success: true, data: previewOf({ previewHash: 'hash-2' }) }));

    page.confirm();

    expect(exchanges.preview).toHaveBeenCalledTimes(2);
    expect(page.activeStep()).toBe(4);
    expect(page.preview()?.previewHash).toBe('hash-2');
    expect(page.confirmError()).toContain('changed since the review');
  });

  it('reuses the idempotency key when the same confirm is retried', async () => {
    await setup();
    reviewed();
    exchanges.confirm.mockReturnValueOnce(throwError(() => apiError(409, 'EXCHANGE_CONCURRENCY_CONFLICT')));
    exchanges.confirm.mockReturnValueOnce(of({ success: true, data: { exchangeId: 5, exchangeNumber: 'MX2611071', replayed: false } }));

    page.confirm();
    expect(page.confirmError()).toContain('Try Confirm again');
    page.confirm();

    const first = exchanges.confirm.mock.calls[0][0];
    const second = exchanges.confirm.mock.calls[1][0];
    expect(first.idempotencyKey).toBeTruthy();
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
    expect(first.previewHash).toBe('hash-1');
    expect(page.result()?.exchangeNumber).toBe('MX2611071');
  });

  it('prevents a double submit while Confirm is in flight', async () => {
    await setup();
    reviewed();
    exchanges.confirm.mockReturnValue(new (await import('rxjs')).Subject());
    page.confirm();
    page.confirm();
    expect(exchanges.confirm).toHaveBeenCalledTimes(1);
  });

  it('clears the supervisor password after the approval request, on success and on failure', async () => {
    await setup();
    reviewed({ requiredApprovals: ['window'] });
    expect(page.canConfirm()).toBe(false);

    exchanges.grantApprovals.mockReturnValueOnce(throwError(() => apiError(403, 'EXCHANGE_APPROVAL_NOT_PERMITTED')));
    page.openApprovals();
    page.supervisorLogin.set('boss@shop.test');
    page.supervisorPassword.set('secret-1');
    page.setApprovalReason('window', 'Customer was travelling');
    page.submitApprovals();
    expect(exchanges.grantApprovals.mock.calls[0][0].supervisorPassword).toBe('secret-1');
    expect(page.supervisorPassword()).toBe('');
    expect(page.approvalError()).toContain('does not have the required permission');

    exchanges.grantApprovals.mockReturnValueOnce(of({ success: true, data: [{ approvalId: 1, approvalToken: 'tok-w', type: 'window', expiresAtUtc: '2026-11-07T10:05:00Z', supervisorName: 'Boss' }] }));
    page.supervisorPassword.set('secret-2');
    page.submitApprovals();
    const body = exchanges.grantApprovals.mock.calls[1][0];
    expect(body.approvals).toEqual([{ type: 'window', reason: 'Customer was travelling' }]);
    expect(body.previewHash).toBe('hash-1');
    expect(page.supervisorPassword()).toBe('');
    expect(page.approvalOpen()).toBe(false);
    expect(page.missingApprovals()).toEqual([]);
    expect(page.canConfirm()).toBe(true);
  });

  it('requires a reference and an external-completion confirmation for a card refund (D-09)', async () => {
    await setup();
    reviewed({ settlementDirection: 'refund', settlementAmount: 50, netAdjustmentGross: -50 });

    page.settlement.update(draft => ({ ...draft, refundMethod: 'card' }));
    expect(page.settlementProblem()).toContain('reference');
    expect(page.canConfirm()).toBe(false);

    page.settlement.update(draft => ({ ...draft, reference: 'APPR-7781' }));
    expect(page.settlementProblem()).toContain('completed outside the system');

    page.settlement.update(draft => ({ ...draft, refundConfirmed: true }));
    expect(page.settlementProblem()).toBe('');

    exchanges.confirm.mockReturnValue(of({ success: true, data: { exchangeId: 6, exchangeNumber: 'MX2611072', replayed: false } }));
    page.confirm();
    expect(exchanges.confirm.mock.calls[0][0].settlements).toEqual([
      { direction: 'refund', method: 'card', amount: 50, externalReference: 'APPR-7781', externalRefundConfirmed: true }
    ]);
  });

  it('asks for the refund method before requesting a refund approval and binds it to that method', async () => {
    await setup();
    reviewed({ settlementDirection: 'refund', settlementAmount: 50, requiredApprovals: ['refund'] });
    expect(page.approvalTypesToRequest()).toEqual([]);

    page.settlement.update(draft => ({ ...draft, refundMethod: 'cash' }));
    expect(page.approvalTypesToRequest()).toEqual(['refund']);

    exchanges.grantApprovals.mockReturnValue(of({ success: true, data: [{ approvalId: 2, approvalToken: 'tok-r', type: 'refund', expiresAtUtc: '2026-11-07T10:05:00Z', supervisorName: 'Boss' }] }));
    page.openApprovals();
    page.supervisorLogin.set('boss@shop.test');
    page.supervisorPassword.set('pw');
    page.setApprovalReason('refund', 'Customer prefers cash');
    page.submitApprovals();
    expect(exchanges.grantApprovals.mock.calls[0][0]).toMatchObject({ refundAmount: 50, refundMethod: 'cash' });
    expect(page.missingApprovals()).toEqual([]);

    page.settlement.update(draft => ({ ...draft, refundMethod: 'mobile' }));
    expect(page.missingApprovals()).toEqual(['refund']);
  });

  it('offers another refund method when the drawer cash is insufficient', async () => {
    await setup();
    reviewed({ settlementDirection: 'refund', settlementAmount: 50 });
    page.settlement.update(draft => ({ ...draft, refundMethod: 'cash' }));
    exchanges.confirm.mockReturnValue(throwError(() => apiError(422, 'EXCHANGE_CASH_INSUFFICIENT')));
    page.confirm();
    expect(page.confirmError()).toContain('Choose card or mobile refund');
    expect(page.settlement().refundMethod).toBeNull();
  });

  it('invalidates the review when the exchange changes after the preview', async () => {
    await setup();
    reviewed();
    page.updateReplacement('80', { quantity: 2 });
    expect(page.previewIsCurrent()).toBe(false);
    expect(page.canConfirm()).toBe(false);
  });
});
