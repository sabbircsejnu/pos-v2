import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../environments/environment';
import { ExchangeRequest } from '../models/exchange.model';
import { ExchangeService } from './exchange.service';
import { SUPPRESS_ERROR_NOTICE } from '../interceptors/error.interceptor';

const base = `${environment.apiUrl}/pos/exchanges`;

const request: ExchangeRequest = {
  receivingOutletId: 3,
  terminalId: 7,
  sourceSaleIds: [41],
  returns: [{ sourceKind: 'sale_item', sourceLineId: 501, quantity: 1, conditionSellable: true, packagingReusable: false }],
  replacements: [{ variantId: 88, quantity: 1, discountAmount: 0, includePackaging: false }],
  invoiceDiscount: 0,
  salespersonUserId: 12,
  reasonCode: 'size_fitting',
  reasonOtherText: null,
  internalNote: null
};

describe('ExchangeService', () => {
  let service: ExchangeService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ExchangeService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('reads and saves the policy', () => {
    service.getPolicy().subscribe();
    expect(http.expectOne(`${base}/policy`).request.method).toBe('GET');

    const body = {
      expectedVersionNo: 2, isEnabled: true, allowLowerPriceExchange: false, exchangeWindowDays: 7, allowReplacementReExchange: false,
      replacementWindowDays: null, maxExchangeCountPerLineage: null, phoneVerificationMode: 'optional' as const,
      highValueApprovalThreshold: null, negativeTargetAdjustmentMode: 'track_only' as const
    };
    service.savePolicy(body).subscribe();
    const put = http.expectOne(`${base}/policy`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual(body);
  });

  it('looks up a source invoice by its exact number', () => {
    service.findSourceInvoice(' INV-1 ').subscribe();
    const req = http.expectOne(r => r.url === `${base}/source-invoice`);
    expect(req.request.params.get('invoiceNumber')).toBe('INV-1');
  });

  it('posts preview, approvals and confirm with the given bodies', () => {
    service.preview(request).subscribe();
    const preview = http.expectOne(`${base}/preview`);
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual(request);

    const approvals = { preview: request, previewHash: 'h1', approvals: [{ type: 'window' as const, reason: 'Late' }], supervisorLogin: 'boss@x.test', supervisorPassword: 'pw' };
    service.grantApprovals(approvals).subscribe();
    expect(http.expectOne(`${base}/approvals`).request.body).toEqual(approvals);

    const confirm = { ...request, previewHash: 'h1', idempotencyKey: 'k1', settlements: [], approvalTokens: ['t1'] };
    service.confirm(confirm).subscribe();
    const post = http.expectOne(base);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(confirm);
  });

  it('lists with only the filters given', () => {
    service.list({ search: 'EX', from: '2026-11-01', status: 'completed', page: 2, pageSize: 25 }).subscribe();
    const req = http.expectOne(r => r.url === base && r.method === 'GET');
    expect(req.request.params.get('search')).toBe('EX');
    expect(req.request.params.get('from')).toBe('2026-11-01');
    expect(req.request.params.has('to')).toBe(false);
    expect(req.request.params.has('outletId')).toBe(false);
    expect(req.request.params.get('status')).toBe('completed');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
  });

  it('reads metrics with only the filters given', () => {
    service.getMetrics({ from: '2026-11-01', to: '2026-11-07' }).subscribe();
    const req = http.expectOne(r => r.url === `${base}/metrics`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-11-01');
    expect(req.request.params.get('to')).toBe('2026-11-07');
    expect(req.request.params.has('outletId')).toBe(false);

    service.getMetrics().subscribe();
    const bare = http.expectOne(r => r.url === `${base}/metrics`);
    expect(bare.request.params.keys()).toEqual([]);
  });

  it('uses the detail, by-sale, receipt and reversal routes', () => {
    service.getById(5).subscribe();
    http.expectOne(`${base}/5`);
    service.listBySale(41).subscribe();
    http.expectOne(`${base}/by-sale/41`);
    service.getReceiptDocument(5).subscribe();
    http.expectOne(`${base}/5/receipt-document`);
    service.getReceiptPdf(5, 'a4').subscribe();
    const pdf = http.expectOne(r => r.url === `${base}/5/receipt-pdf`);
    expect(pdf.request.params.get('mode')).toBe('a4');
    expect(pdf.request.responseType).toBe('blob');
    service.recordReprint(5).subscribe();
    expect(http.expectOne(`${base}/5/reprint`).request.method).toBe('POST');
    service.recordPrintAttempt(5, { terminalId: 7, succeeded: true }).subscribe();
    expect(http.expectOne(`${base}/5/receipt-print-attempt`).request.body).toEqual({ terminalId: 7, succeeded: true });
    service.getReversalEligibility(5).subscribe();
    http.expectOne(`${base}/5/reversal-eligibility`);
    const reverse = { reason: 'Wrong', terminalId: 7, idempotencyKey: 'r1', settlements: [] };
    service.reverse(5, reverse).subscribe();
    expect(http.expectOne(`${base}/5/reverse`).request.body).toEqual(reverse);
  });

  // Browser UAT DEF-03: these screens show the specific exchange error themselves, so the global
  // "You do not have permission" notice must not cover it (e.g. a supervisor without outlet access).
  it('suppresses the global error notice for approvals, confirm and reverse', () => {
    service.grantApprovals({ preview: request, previewHash: 'h', approvals: [], supervisorLogin: 'a@b.test', supervisorPassword: 'p' }).subscribe();
    expect(http.expectOne(`${base}/approvals`).request.context.get(SUPPRESS_ERROR_NOTICE)).toBe(true);
    service.confirm({ ...request, previewHash: 'h', idempotencyKey: 'k', settlements: [], approvalTokens: [] }).subscribe();
    expect(http.expectOne(base).request.context.get(SUPPRESS_ERROR_NOTICE)).toBe(true);
    service.reverse(5, { reason: 'r', terminalId: 7, idempotencyKey: 'r1', settlements: [] }).subscribe();
    expect(http.expectOne(`${base}/5/reverse`).request.context.get(SUPPRESS_ERROR_NOTICE)).toBe(true);
    service.preview(request).subscribe();
    expect(http.expectOne(`${base}/preview`).request.context.get(SUPPRESS_ERROR_NOTICE)).toBe(false);
  });
});
