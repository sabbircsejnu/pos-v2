import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { AlertService } from './alert.service';
import { CompanyDateService, normalizeDateFormat } from './company-date.service';
import { environment } from '../../environments/environment';

describe('company date formatting and parsing', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });

  it.each([
    ['dd/MM/yyyy', '05/04/2026'], ['DD/MM/YYYY', '05/04/2026'],
    ['MM/DD/YYYY', '04/05/2026'], ['yyyy-MM-dd', '2026-04-05']
  ])('uses configured %s for input and display without ambiguous browser parsing', (pattern, display) => {
    const dates = TestBed.inject(CompanyDateService);
    dates.setFormat(pattern);
    expect(dates.format('2026-04-05')).toBe(display);
    expect(dates.parse(display)).toBe('2026-04-05');
    expect(dates.inputText('2026-04-05T09:30', true)).toBe(`${display} 09:30`);
    expect(dates.parse(`${display} 09:30`, true)).toBe('2026-04-05T09:30');
  });

  it.each(['31/04/2026', '29/02/2025', '00/01/2026', '01/13/2026', '02/30/2026', '2026-04-05', '5/4/2026'])(
    'rejects impossible or wrongly formatted entry %s', text => {
      expect(TestBed.inject(CompanyDateService).parse(text)).toBeNull();
    });

  it('handles leap years, empty values, date-only values without timezone shifting and timestamp time', () => {
    const dates = TestBed.inject(CompanyDateService);
    expect(dates.parse('29/02/2024')).toBe('2024-02-29');
    expect(dates.parse('29/02/1900')).toBeNull();
    expect(dates.parse('29/02/2000')).toBe('2000-02-29');
    expect(dates.parse('05/04/2026 24:01', true)).toBeNull();
    expect(dates.parse('05/04/2026 23:59:60', true)).toBeNull();
    expect(dates.format(null)).toBe('');
    expect(dates.format('2026-04-05', false, '-1200')).toBe('05/04/2026');
    expect(dates.format('2026-04-05T09:30:00Z', true, 'UTC')).toBe('05/04/2026 09:30:00');
    expect(dates.format('2026-04-05T22:30:00Z', true, 'Asia/Dhaka')).toBe('06/04/2026 04:30:00');
  });

  it('normalizes legacy tokens without Angular week-year/day-of-year interpretation', () => {
    expect(normalizeDateFormat('DD/MM/YYYY')).toBe('dd/MM/yyyy');
    expect(() => normalizeDateFormat('not-a-date-format')).toThrow();
  });

  it('loads for login, not anonymous, reloads for identity changes, and resets after logout', () => {
    const users = new BehaviorSubject<{ id: number } | null>(null);
    const dates = TestBed.inject(CompanyDateService);
    const http = TestBed.inject(HttpTestingController);
    const url = `${environment.apiUrl}/settings/formatting`;
    dates.connect(users);
    http.expectNone(url);
    users.next({ id: 1 });
    http.expectOne(url).flush({ data: { dateFormat: 'MM/DD/YYYY' } });
    expect(dates.dateFormat()).toBe('MM/dd/yyyy');
    users.next({ id: 1 });
    http.expectNone(url);
    users.next({ id: 2 });
    http.expectOne(url).flush({ data: { dateFormat: 'DD/MM/YYYY' } });
    expect(dates.dateFormat()).toBe('dd/MM/yyyy');
    users.next(null);
    expect(dates.dateFormat()).toBe('dd/MM/yyyy');
  });

  it('notifies explicitly if settings fail to load', () => {
    const users = new BehaviorSubject<{ id: number } | null>({ id: 1 });
    TestBed.inject(CompanyDateService).connect(users);
    TestBed.inject(HttpTestingController).expectOne(`${environment.apiUrl}/settings/formatting`)
      .flush({}, { status: 503, statusText: 'Unavailable' });
    expect(TestBed.inject(AlertService).config()?.message).toContain('could not be loaded');
  });
});
