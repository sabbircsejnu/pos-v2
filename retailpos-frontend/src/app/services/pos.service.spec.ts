import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PosService } from './pos.service';
import { AlertService } from './alert.service';
import { errorInterceptor } from '../interceptors/error.interceptor';

describe('PosService held sales', () => {
  let service: PosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the list from the { heldSales, totalCount } envelope returned by the API', () => {
    service.loadHeldSales(1);
    http.expectOne(req => req.url.endsWith('/held-sales/outlet/1'))
      .flush({ success: true, data: { heldSales: [{ id: 5, items: [] }], totalCount: 1 } });

    expect(service.heldSales().length).toBe(1);
    expect(service.heldSales()[0].id).toBe(5);
  });

  it('still accepts a bare array and never stores a non-array', () => {
    service.loadHeldSales(1);
    http.expectOne(req => req.url.endsWith('/held-sales/outlet/1')).flush({ data: [{ id: 6, items: [] }] });
    expect(service.heldSales().map(h => h.id)).toEqual([6]);

    service.loadHeldSales(1);
    http.expectOne(req => req.url.endsWith('/held-sales/outlet/1')).flush({ data: { unexpected: true } });
    expect(service.heldSales()).toEqual([]);
  });
});

describe('PosService POS catalogue (Phase 4)', () => {
  let service: PosService;
  let http: HttpTestingController;
  const alert = { error: vi.fn(), config: () => null };

  beforeEach(() => {
    alert.error.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AlertService, useValue: alert },
      ]
    });
    service = TestBed.inject(PosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads products and categories from the POS endpoints, not Product/Category Management', () => {
    service.getCatalogue(3).subscribe();
    const catalogue = http.expectOne(req => req.url.endsWith('/pos/catalogue'));
    expect(catalogue.request.params.get('outletId')).toBe('3');
    expect(catalogue.request.params.get('limit')).toBe('200');
    catalogue.flush({ success: true, data: [] });

    service.getCategories().subscribe();
    http.expectOne(req => req.url.endsWith('/pos/categories')).flush({ success: true, data: [] });

    http.expectNone(req => /\/api\/(products|categories)(\/|\?|$)/.test(req.urlWithParams));
  });

  it('searches checkout customers through /api/pos/customers/search, not Customer Management', () => {
    service.searchCustomers('01700').subscribe();
    const req = http.expectOne(r => r.url.endsWith('/pos/customers/search'));
    expect(req.request.params.get('q')).toBe('01700');
    req.flush({ success: true, data: [] });

    http.expectNone(r => /\/api\/customers(\/|\?|$)/.test(r.urlWithParams));
  });

  it('creates a Quick Customer through POST /api/pos/customers with name and phone only', () => {
    service.createCustomer({ name: 'Rahim', phone: '01711000009' }).subscribe();
    const req = http.expectOne(r => r.url.endsWith('/pos/customers'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Rahim', phone: '01711000009' });
    req.flush({ success: true, data: { id: 1, name: 'Rahim', phone: '01711000009' } });
  });

  it('does not raise the global permission notice for a POS catalogue failure', () => {
    let failure: any;
    service.getCatalogue(3).subscribe({ error: e => (failure = e) });
    http.expectOne(req => req.url.endsWith('/pos/catalogue'))
      .flush({ success: false, message: 'You are not assigned to outlet 3.' }, { status: 403, statusText: 'Forbidden' });

    service.getCategories().subscribe({ error: () => {} });
    http.expectOne(req => req.url.endsWith('/pos/categories')).flush(null, { status: 403, statusText: 'Forbidden' });

    expect(alert.error).not.toHaveBeenCalled();
    expect(failure.status).toBe(403);
    expect(failure.originalError.error.message).toBe('You are not assigned to outlet 3.');
  });
});
