import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CustomerService } from './customer.service';
import { environment } from '../../environments/environment';

describe('CustomerService loyalty capabilities', () => {
  const url = `${environment.apiUrl}/customers/loyalty-capabilities`;
  let service: CustomerService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CustomerService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('is disabled until the capabilities call confirms otherwise', () => {
    expect(service.legacyPointsMutationEnabled()).toBe(false);
  });

  it('reads legacyPointsMutationEnabled from the standard envelope and caches it for the session', () => {
    const results: boolean[] = [];
    service.getLoyaltyCapabilities().subscribe(c => results.push(c.legacyPointsMutationEnabled));
    http.expectOne(url).flush({ success: true, data: { legacyPointsMutationEnabled: true } });
    service.getLoyaltyCapabilities().subscribe(c => results.push(c.legacyPointsMutationEnabled));
    http.expectNone(url);

    expect(results).toEqual([true, true]);
    expect(service.legacyPointsMutationEnabled()).toBe(true);
  });

  it('treats an explicit false or a malformed body as disabled', () => {
    service.getLoyaltyCapabilities().subscribe();
    http.expectOne(url).flush({ success: true, data: { legacyPointsMutationEnabled: false } });
    expect(service.legacyPointsMutationEnabled()).toBe(false);
  });

  it('treats a malformed body as disabled', () => {
    service.getLoyaltyCapabilities().subscribe();
    http.expectOne(url).flush({ success: true, data: { legacyPointsMutationEnabled: 'yes' } });
    expect(service.legacyPointsMutationEnabled()).toBe(false);
  });

  it('defaults to disabled when the call fails and retries on the next request', () => {
    const results: boolean[] = [];
    service.getLoyaltyCapabilities().subscribe(c => results.push(c.legacyPointsMutationEnabled));
    http.expectOne(url).flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });
    expect(results).toEqual([false]);
    expect(service.legacyPointsMutationEnabled()).toBe(false);

    service.getLoyaltyCapabilities().subscribe(c => results.push(c.legacyPointsMutationEnabled));
    http.expectOne(url).flush({ success: true, data: { legacyPointsMutationEnabled: true } });
    expect(results).toEqual([false, true]);
    expect(service.legacyPointsMutationEnabled()).toBe(true);
  });
});
