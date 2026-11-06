import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CompanyDateService } from '../../services/company-date.service';
import { ReportService } from '../../services/report.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { StockTransactionReportComponent } from './stock-transaction-report.component';

interface Access {
  outlets: { id: number; name: string }[];
  warehouses: { id: number; name: string }[];
  isBusinessOwner: boolean;
  defaultOutletId?: number | null;
  defaultLocationId?: number | null;
  defaultLocationType?: 'outlet' | 'warehouse' | null;
}

describe('Stock Transaction Report page', () => {
  const access = signal<Access | null>(null);

  function createPage(authorized: Access): StockTransactionReportComponent {
    access.set(authorized);
    const fixture = TestBed.createComponent(StockTransactionReportComponent);
    fixture.componentInstance.ngOnInit();
    return fixture.componentInstance;
  }

  const noAccess: Access = { outlets: [], warehouses: [], isBusinessOwner: false };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 00:30 on 1 April 2026 in Dhaka, while it is still 31 March in UTC.
    vi.setSystemTime(new Date('2026-03-31T18:30:00Z'));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: UserOutletAccessService, useValue: { load: () => of(null), authorized: access } },
        { provide: ReportService, useValue: { stockTransactionReport: signal(null) } },
      ],
    });
    TestBed.inject(CompanyDateService).setTimeZone('Asia/Dhaka');
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('defaults to this month by the Company calendar, not the UTC one', () => {
    const page = createPage(noAccess);

    expect([page.dateFrom(), page.dateTo()]).toEqual(['2026-04-01', '2026-04-01']);
  });

  it('shows movement times in the Company time zone', () => {
    const page = createPage(noAccess);
    TestBed.inject(CompanyDateService).setFormat('yyyy-MM-dd');

    expect(page.formatDate('2026-11-05T19:27:35Z')).toBe('2026-11-06 01:27:35');
  });

  it('shows the From and To as the calendar dates that were asked for', () => {
    const page = createPage(noAccess);
    TestBed.inject(CompanyDateService).setFormat('dd/MM/yyyy');

    expect(page.formatCalendarDate('2026-11-06T00:00:00')).toBe('06/11/2026');
    expect(page.formatCalendarDate(undefined)).toBe('—');
  });

  it('names every ledger movement type', () => {
    const page = createPage(noAccess);

    expect(page.transactionTypeLabel('return_transfer_in')).toBe('Return Transfer In');
    expect(page.transactionTypeLabel('return_transfer_out')).toBe('Return Transfer Out');
    expect(page.transactionTypeLabel('transfer_rejection')).toBe('Transfer Rejected');
    expect(page.transactionTypeBadgeClass('return_transfer_in')).toContain('green');
    expect(page.transactionTypeBadgeClass('return_transfer_out')).toContain('red');
  });

  it('links stock adjustments to the adjustment itself', () => {
    const page = createPage(noAccess);

    expect(page.referenceLink('stock_adjustment', 12)).toBe('/stock-adjustments/12');
    expect(page.referenceLink('stock_transfer', 3)).toBe('/stock-transfers/3');
  });

  it('selects the default warehouse for a user assigned to several warehouses only', () => {
    const page = createPage({
      outlets: [],
      warehouses: [{ id: 1, name: 'Central' }, { id: 2, name: 'North' }],
      isBusinessOwner: false,
      defaultLocationId: 2,
      defaultLocationType: 'warehouse',
    });

    expect(page.selectedLocationKey()).toBe('warehouse:2');
  });

  it('never leaves a user without "All Locations" on a blank location', () => {
    const page = createPage({
      outlets: [],
      warehouses: [{ id: 1, name: 'Central' }, { id: 2, name: 'North' }],
      isBusinessOwner: false,
    });

    expect(page.selectedLocationKey()).toBe('warehouse:1');
  });

  it('keeps "All Locations" as the default for a Business Owner without a home outlet', () => {
    const page = createPage({
      outlets: [{ id: 3, name: 'Basundhara 1' }],
      warehouses: [{ id: 1, name: 'Central' }],
      isBusinessOwner: true,
    });

    expect(page.selectedLocationKey()).toBe('');
  });
});
