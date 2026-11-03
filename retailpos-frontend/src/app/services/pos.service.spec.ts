import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PosService } from './pos.service';

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
