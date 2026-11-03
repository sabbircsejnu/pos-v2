import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { PayrollDashboardService } from './payroll-dashboard.service';

describe('PayrollDashboardService', () => {
  let service: PayrollDashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PayrollDashboardService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(PayrollDashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the read-only aggregate with the active filters', () => {
    service.query({
      periodId: 7,
      runId: null,
      locationId: 12,
      fromDate: '2026-10-01',
      toDate: '2026-10-31',
      runType: 'Regular'
    }).subscribe();

    const request = http.expectOne(
      `${environment.apiUrl}/payroll/v1/dashboard?fromDate=2026-10-01&toDate=2026-10-31&periodId=7&locationId=12&runType=Regular`
    );
    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
