import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CompanyDateService } from '../../services/company-date.service';
import { ReportService } from '../../services/report.service';
import { SalesReportComponent } from './sales-report.component';

describe('Sales report quick ranges follow the Company time zone', () => {
  let page: SalesReportComponent;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 00:30 on Wednesday 1 April 2026 in Dhaka, while it is still 31 March in UTC.
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    const reports = {
      getSalesSummary: () => of({}),
      getTopProducts: () => of({}),
      getSalesByPaymentMethod: () => of({}),
      getDailySalesTrend: () => of({}),
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ReportService, useValue: reports },
      ],
    });
    TestBed.inject(CompanyDateService).setTimeZone('Asia/Dhaka');
    page = TestBed.createComponent(SalesReportComponent).componentInstance;
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('today is the Company date', () => {
    page.setToday();

    expect([page.startDate(), page.endDate()]).toEqual(['2026-04-01', '2026-04-01']);
  });

  it('this week starts on the Company week Monday', () => {
    page.setThisWeek();

    expect([page.startDate(), page.endDate()]).toEqual(['2026-03-30', '2026-04-01']);
  });

  it('this month starts on the first of the Company month', () => {
    page.setThisMonth();

    expect([page.startDate(), page.endDate()]).toEqual(['2026-04-01', '2026-04-01']);
  });

  it('last month is the whole previous Company month', () => {
    page.setLastMonth();

    expect([page.startDate(), page.endDate()]).toEqual(['2026-03-01', '2026-03-31']);
  });
});
