import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CompanyDateService } from '../../services/company-date.service';
import { ProductLedgerReportComponent } from './product-ledger-report.component';
import { StockMovementReportComponent } from './stock-movement-report.component';

interface DatePresets {
  dateFrom(): string;
  dateTo(): string;
  setToday(): void;
  setThisMonth(): void;
  setLastMonth(): void;
  setLast30Days(): void;
}

describe.each<[string, Type<unknown>]>([
  ['Stock Movement', StockMovementReportComponent],
  ['Product Ledger', ProductLedgerReportComponent],
])('%s report date presets follow the Company time zone', (_name, component) => {
  let page: DatePresets;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 00:30 on 1 April 2026 in Dhaka, while it is still 31 March in UTC.
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(CompanyDateService).setTimeZone('Asia/Dhaka');
    page = TestBed.createComponent(component).componentInstance as DatePresets;
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('today is the Company date', () => {
    page.setToday();

    expect([page.dateFrom(), page.dateTo()]).toEqual(['2026-04-01', '2026-04-01']);
  });

  it('this month starts on the first of the Company month', () => {
    page.setThisMonth();

    expect([page.dateFrom(), page.dateTo()]).toEqual(['2026-04-01', '2026-04-01']);
  });

  it('last month is the whole previous Company month', () => {
    page.setLastMonth();

    expect([page.dateFrom(), page.dateTo()]).toEqual(['2026-03-01', '2026-03-31']);
  });

  it('last 30 days counts back from the Company date', () => {
    page.setLast30Days();

    expect([page.dateFrom(), page.dateTo()]).toEqual(['2026-03-02', '2026-04-01']);
  });
});
