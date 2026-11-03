import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { AuthorizedOutletsDto } from '../models/report.model';
import { UserOutletAccessService } from './user-outlet-access.service';

describe('UserOutletAccessService', () => {
  let service: UserOutletAccessService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/me/outlets`;
  const locations: AuthorizedOutletsDto = {
    outlets: [{ id: 12, name: 'Business Outlet', type: 'outlet' }],
    warehouses: [{ id: 21, name: 'Business Warehouse', type: 'warehouse' }],
    isBusinessOwner: true,
    isGlobalAccess: true,
    accessScope: 'all_locations'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(UserOutletAccessService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends no policy by default so the API keeps applying the Step 3 inventory scope', () => {
    service.load().subscribe();
    const request = http.expectOne(url);
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, data: locations });
  });

  it.each(['InventoryStockView', 'StockCountOperation', 'StockTransferSource', 'StockTransferDestination'] as const)(
    'requests the %s policy explicitly',
    policy => {
      service.load(policy).subscribe(response => expect(response.data).toEqual({ ...locations, policy }));
      const request = http.expectOne(`${url}?policy=${policy}`);
      expect(request.request.params.get('policy')).toBe(policy);
      request.flush({ success: true, data: { ...locations, policy } });
      expect(service.authorized()?.policy).toBe(policy);
    }
  );

  it('preserves the API envelope and maps the authorized source lists, not transfer destinations', () => {
    service.load().subscribe(response => {
      expect(response.data).toEqual(locations);
    });
    http.expectOne(url).flush({ success: true, data: locations });
    expect(service.authorized()).toEqual(locations);
  });

  it('reloads rather than reusing locations from a previous Business context', () => {
    service.load().subscribe();
    http.expectOne(url).flush({ success: true, data: locations });

    service.load().subscribe(response => {
      expect(response.data.outlets).toEqual([]);
      expect(response.data.warehouses[0].id).toBe(31);
    });
    expect(service.authorized()).toBeNull();
    http.expectOne(url).flush({
      success: true,
      data: { ...locations, outlets: [], warehouses: [{ id: 31, name: 'Other Business', type: 'warehouse' }] }
    });
  });

  it.each([403, 500])('propagates HTTP %s instead of returning an empty list', status => {
    let errorStatus: number | undefined;
    service.load().subscribe({
      next: () => expect.unreachable('An API failure must not become a successful empty list'),
      error: error => { errorStatus = error.status; }
    });
    http.expectOne(url).flush({ message: 'Cannot load locations' }, { status, statusText: 'Failure' });
    expect(errorStatus).toBe(status);
    expect(service.authorized()).toBeNull();
  });

  it.each([
    { success: true },
    { success: true, data: { outlets: [], warehouses: null } },
    { success: true, data: { outlets: null, warehouses: [] } }
  ])('rejects malformed responses rather than silently mapping missing lists to empty', response => {
    let message = '';
    service.load().subscribe({
      next: () => expect.unreachable('Malformed data must not become an empty list'),
      error: (error: Error) => { message = error.message; }
    });
    http.expectOne(url).flush(response);
    expect(message).toContain('invalid authorized locations response');
    expect(service.authorized()).toBeNull();
  });

  it('propagates an API failure envelope with its message', () => {
    let message = '';
    service.load().subscribe({
      next: () => expect.unreachable('Failure envelopes must not become empty lists'),
      error: (error: Error) => { message = error.message; }
    });
    http.expectOne(url).flush({ success: false, message: 'Location access denied' });
    expect(message).toBe('Location access denied');
    expect(service.authorized()).toBeNull();
  });
});
