import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { UiTableQuery } from '../../components/shared/ui';
import { ExchangeListItem, ExchangeMetrics } from '../../models/exchange.model';
import { AlertService } from '../../services/alert.service';
import { AuthService } from '../../services/auth.service';
import { CurrencyService } from '../../services/currency.service';
import { ExchangeService } from '../../services/exchange.service';
import { ExchangeHistoryListComponent } from './exchange-history-list.component';

const item: ExchangeListItem = {
  id: 5, exchangeNumber: 'MX2611071', businessDate: '2026-11-07', completedAtUtc: '2026-11-07T09:00:00Z', receivingOutletId: 3,
  receivingOutletName: 'Main', customerName: 'Rana', originalInvoiceNumbers: ['INV-41', 'INV-42'], reasonCode: 'size_fitting',
  netAdjustmentGross: 50, settlementDirection: 'collect', settlementAmount: 50, status: 'completed', cashierName: 'C', salespersonName: 'S'
};

const metrics: ExchangeMetrics = {
  from: '2026-11-07', to: '2026-11-07', exchangeCount: 3, reversalCount: 1, replacementGross: 396, returnGross: 330,
  netAdjustmentGross: 66, replacementNet: 360, returnNet: 300, netAdjustmentNet: 60,
  byOutlet: [{ outletId: 3, outletName: 'Main', netAdjustmentNet: 60, netAdjustmentGross: 66, exchangeCount: 3 }],
  bySalesperson: [{ salespersonUserId: 12, salespersonName: 'Sami', positiveNet: 80, negativeNet: -20, signedNet: 60, targetEffectNet: 80 }]
};

const query = (over: Partial<UiTableQuery> = {}): UiTableQuery => ({
  search: '', searchColumns: [], filters: {}, sort: null, page: 1, pageSize: 25, sequence: 1, reason: 'init', ...over
});

describe('ExchangeHistoryListComponent', () => {
  let fixture: ComponentFixture<ExchangeHistoryListComponent>;
  let page: ExchangeHistoryListComponent;
  const exchanges = { list: vi.fn(), getMetrics: vi.fn() };

  beforeEach(async () => {
    vi.clearAllMocks();
    exchanges.list.mockReturnValue(of({ success: true, data: { items: [item], totalCount: 1, page: 1, pageSize: 25 } }));
    exchanges.getMetrics.mockReturnValue(of({ success: true, data: metrics }));
    await TestBed.configureTestingModule({
      imports: [ExchangeHistoryListComponent],
      providers: [
        provideRouter([]),
        { provide: ExchangeService, useValue: exchanges },
        { provide: AuthService, useValue: { hasPermission: () => false } },
        { provide: AlertService, useValue: { error: vi.fn(), config: () => null } },
        { provide: CurrencyService, useValue: { format: (value: number) => `$${value}`, formatNumber: (value: number) => `${value}` } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ExchangeHistoryListComponent);
    page = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('maps the table query and date filters to the API', () => {
    page.from.set('2026-11-01');
    page.onQueryChange(query({ search: 'INV-41', filters: { status: 'reversed' }, page: 2, pageSize: 50, sequence: 7 }));
    expect(exchanges.list).toHaveBeenLastCalledWith({
      search: 'INV-41', status: 'reversed', from: '2026-11-01', to: undefined, page: 2, pageSize: 50
    });
    expect(page.rows()).toEqual([item]);
    expect(page.total()).toBe(1);
    expect(page.responseSequence()).toBe(7);
  });

  it('restarts at page 1 when a date filter changes', () => {
    page.onQueryChange(query({ page: 3, sequence: 2 }));
    page.setTo('2026-11-07');
    expect(exchanges.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, to: '2026-11-07' }));
  });

  it('shows the error and keeps working after a failed load', () => {
    exchanges.list.mockReturnValueOnce(throwError(() => ({ status: 500, message: 'Server error' })));
    page.onQueryChange(query({ sequence: 3 }));
    expect(page.loadError()).toBe('Server error');
    page.reload();
    expect(page.loadError()).toBeNull();
    expect(page.rows()).toEqual([item]);
  });

  it('formats the invoice list and opens the detail on row click', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    expect(page.invoicesOf(item)).toBe('INV-41, INV-42');
    page.open(item);
    expect(navigate).toHaveBeenCalledWith(['/exchanges', 5]);
  });

  it('hides "New exchange" without pos.exchange.create', () => {
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('New exchange');
  });

  it('shows the metrics card for the list date range and reloads it when a date changes', () => {
    expect(exchanges.getMetrics).toHaveBeenCalledWith({ from: undefined, to: undefined });
    const card = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="exchange-metrics"]') as HTMLElement;
    expect(card.textContent).toContain('3');
    expect(card.textContent).toContain('$66');
    expect(card.textContent).toContain('$60');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sami');
    expect(page.metricsSubtitle()).toBe('Business date 2026-11-07');

    page.setFrom('2026-11-01');
    page.setTo('2026-11-07');
    expect(exchanges.getMetrics).toHaveBeenLastCalledWith({ from: '2026-11-01', to: '2026-11-07' });
  });

  it('shows a metrics error without breaking the list', () => {
    exchanges.getMetrics.mockReturnValueOnce(throwError(() => ({ status: 400, message: 'The start date must not be after the end date.' })));
    page.setFrom('2026-11-09');
    fixture.detectChanges();
    expect(page.metricsError()).toBe('The start date must not be after the end date.');
    expect(page.metrics()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Could not load exchange metrics');
  });
});
