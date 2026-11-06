import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { CompanyDateService } from './company-date.service';

describe('CompanyDateService today and Business switching', () => {
  let dates: CompanyDateService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    dates = TestBed.inject(CompanyDateService);
    http = TestBed.inject(HttpTestingController);
    vi.useFakeTimers({ toFake: ['Date'] });
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
    TestBed.resetTestingModule();
  });

  it.each([
    ['Asia/Dhaka', '2026-04-01'],
    ['America/New_York', '2026-03-31'],
  ])('uses the Company date in %s, not the UTC date', (zone, expected) => {
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    dates.setTimeZone(zone);

    expect(dates.todayIso()).toBe(expected);
  });

  it('offsets whole days across a month end', () => {
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    dates.setTimeZone('Asia/Dhaka');

    expect(dates.todayIso(-1)).toBe('2026-03-31');
    expect(dates.todayIso(30)).toBe('2026-05-01');
  });

  it('gives the first of the Company month', () => {
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    dates.setTimeZone('Asia/Dhaka');

    expect(dates.firstOfMonthIso()).toBe('2026-04-01');
  });

  it('reloads the zone when the Business changes, but not on the first value or a repeat', () => {
    const businesses = new Subject<number | null>();
    dates.reloadOnBusinessChange(businesses);

    businesses.next(null);
    http.expectNone(request => request.url.endsWith('/settings/formatting'));

    businesses.next(2);
    http.expectOne(request => request.url.endsWith('/settings/formatting'))
      .flush({ data: { dateFormat: 'dd/MM/yyyy', timeZone: 'Asia/Tokyo' } });
    expect(dates.timeZone()).toBe('Asia/Tokyo');

    businesses.next(2);
    http.expectNone(request => request.url.endsWith('/settings/formatting'));
  });
});
