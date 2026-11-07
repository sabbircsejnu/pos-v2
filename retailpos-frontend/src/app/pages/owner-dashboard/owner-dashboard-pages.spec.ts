import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Observable, map, of, throwError } from 'rxjs';
import { InTransitLine, InTransitStock, InventorySummary, MetricValue, OwnerDashboardMeta, SalesFinanceSummary, StockValuationCoverage } from '../../models/owner-dashboard.model';
import { AuthService } from '../../services/auth.service';
import { BusinessContextStore } from '../../services/business-context.store';
import { CurrencyService } from '../../services/currency.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { OwnerDashboardService } from '../../services/owner-dashboard.service';
import { InventoryIntelligencePageComponent } from './inventory-intelligence-page.component';
import { SalesFinancePageComponent } from './sales-finance-page.component';
import { OdFilterBarComponent } from './shared/od-filter-bar.component';
import { OdTimeChartComponent } from './shared/od-charts.component';

const META: OwnerDashboardMeta = {
  today: '2026-11-06',
  timeZoneId: 'Asia/Dhaka',
  showrooms: [{ id: 5, name: 'Outlet 5', type: 'outlet' }, { id: 1, name: 'Outlet 1', type: 'outlet' }],
  warehouses: [{ id: 9, name: 'Central', type: 'warehouse' }],
  categories: [{ id: 3, name: 'Borka' }],
  canCreateTransfer: true,
  thresholds: {
    minEligibleSellingDays: 14,
    slowVelocityPerDay: 0.2,
    minOnHandQty: 1,
    minDemandVelocityPerDay: 0.5,
    sourceExcessCoverageDays: 60,
    sourceRetainedCoverageDays: 30,
    destinationTargetCoverageDays: 21,
    minVelocityRatio: 2,
    minimumRetainedUnits: 1,
    minRecommendationQty: 1,
    maxSuggestedQty: null,
    highPriorityCoverDays: 7,
    mediumPriorityCoverDays: 14,
    profitEnabled: true,
    transferOpportunitiesEnabled: true
  },
  valuationMethod: 'Weighted average'
};

const CONTEXT = { from: '2026-11-06', to: '2026-11-06', timeZoneId: 'Asia/Dhaka', locationScope: 'all', dataAsOfUtc: '2026-11-06T04:00:00Z' };
const metric = (value: number | null, status: MetricValue['status'] = 'ok'): MetricValue => ({ value, status, note: null });

const coverage = (overrides: Partial<StockValuationCoverage> = {}): StockValuationCoverage => ({
  valuedSubtotal: 1000,
  status: 'valued',
  valuedRecords: 1,
  valuedQuantity: 20,
  zeroCostRecords: 0,
  zeroCostQuantity: 0,
  unvaluedRecords: 0,
  unvaluedQuantity: 0,
  ...overrides
});

const NO_STOCK = coverage({ valuedSubtotal: null, status: 'no_stock', valuedRecords: 0, valuedQuantity: 0 });
const NO_TRANSIT: InTransitStock = { quantityByUnit: [], valuation: NO_STOCK, transfers: 0, lines: 0 };

/** 200 m of fabric and 50 ml of attar on their way to a showroom, the attar without a reliable cost. */
const FABRIC_AND_ATTAR: InTransitStock = {
  quantityByUnit: [{ unitCode: 'm', quantity: 200 }, { unitCode: 'ml', quantity: 50 }],
  valuation: coverage({ valuedSubtotal: 2000, status: 'partial', valuedQuantity: 200, unvaluedRecords: 1, unvaluedQuantity: 50 }),
  transfers: 2,
  lines: 2
};

const SUMMARY: SalesFinanceSummary = {
  context: CONTEXT,
  netSales: metric(1000),
  netSalesChangePercent: 25,
  target: metric(null, 'not_configured'),
  achievementPercent: null,
  shortfall: metric(null, 'not_configured'),
  targetExceeded: metric(null, 'not_configured'),
  purchase: metric(500),
  expense: metric(100),
  profit: metric(null, 'unavailable'),
  dataQualityWarnings: ['1 refund has no usable amounts.'],
  targetCoverage: { showroomCount: 1, fullyConfiguredShowrooms: 0, partlyConfiguredShowrooms: 0, notConfiguredShowrooms: 1, scheduledOffShowrooms: 0, isComplete: false },
  today: { date: '2026-11-06', netSales: 1000, target: metric(null, 'not_configured'), achievementPercent: null, shortfall: metric(null, 'not_configured'), targetExceeded: metric(null, 'not_configured') }
};

function apiStub(meta: OwnerDashboardMeta = META) {
  return {
    meta: vi.fn(() => of(meta)),
    salesSummary: vi.fn(() => of(SUMMARY)),
    showrooms: vi.fn(() => of({
      context: CONTEXT,
      showrooms: [{
        outletId: 5, outletName: 'Outlet 5', netSales: 1000, target: null,
        achievementPercent: null, shortfall: null, targetExceeded: null, targetStatus: 'not_configured'
      }]
    })),
    cashFlow: vi.fn(() => of({
      context: CONTEXT, bucket: 'daily',
      points: [{ from: '2026-11-06', to: '2026-11-06', cashReceived: 100, expensePayments: 40 }],
      totalCashReceived: 100, totalExpensePayments: 40, difference: 60
    })),
    salesTrend: vi.fn(() => of({
      context: CONTEXT, bucket: 'daily',
      points: [{ from: '2026-11-06', to: '2026-11-06', netSales: 1000, target: null }],
      targetStatus: 'not_configured'
    })),
    inventorySummary: vi.fn(() => of({
      context: CONTEXT,
      totalStockValue: metric(5000, 'partial'),
      totalStockQuantity: 83,
      activeStockedProducts: 2,
      stockedVariants: 3,
      slowMovingStockValue: metric(1000),
      slowMovingSharePercent: 20,
      outOfStockVariants: 1,
      transferOpportunities: 1,
      highPriorityTransferOpportunities: 0,
      transferOpportunityStatus: 'ok',
      negativeStockRows: 0,
      unvaluedStockRows: 1,
      valuationMethod: 'Weighted average',
      dataQualityWarnings: [],
      valuation: coverage({ valuedSubtotal: 5000, status: 'partial', valuedQuantity: 80, unvaluedRecords: 1, unvaluedQuantity: 3 }),
      slowMovingValuation: coverage()
    })),
    stockByLocation: vi.fn(() => of({
      context: CONTEXT,
      locations: [{ locationId: 9, locationType: 'warehouse', locationName: 'Central', quantity: 20, stockValue: 1000, percentOfTotal: 20, valuation: coverage() }],
      totalStockValue: 5000,
      valueStatus: 'ok',
      valuation: coverage({ valuedSubtotal: 5000 })
    })),
    bestSellers: vi.fn(() => of({ items: [], totalCount: 0, page: 1, pageSize: 15 })),
    slowMovers: vi.fn(() => of({ context: CONTEXT, rows: { items: [], totalCount: 0, page: 1, pageSize: 15 }, insufficientEvidenceCount: 0 })),
    transferOpportunities: vi.fn(() => of({ context: CONTEXT, rows: { items: [], totalCount: 0, page: 1, pageSize: 10 }, status: 'ok', stockAsOfUtc: CONTEXT.dataAsOfUtc })),
    transferOpportunity: vi.fn(),
    productInsights: vi.fn(),
    inTransit: vi.fn(() => of({
      context: CONTEXT,
      rows: { items: [], totalCount: 0, page: 1, pageSize: 10 },
      summary: { incoming: NO_TRANSIT, outgoing: NO_TRANSIT, businessWide: true }
    }))
  };
}

async function setup<T>(
  component: new () => T,
  api: ReturnType<typeof apiStub>,
  query: Record<string, string> = {}
): Promise<{ fixture: ComponentFixture<T>; navigate: ReturnType<typeof vi.fn> }> {
  const navigate = vi.fn(() => Promise.resolve(true));
  await TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      { provide: OwnerDashboardService, useValue: api },
      { provide: Router, useValue: { navigate, createUrlTree: () => ({}), serializeUrl: () => '', events: of() } },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      { provide: AuthService, useValue: { currentUser$: new BehaviorSubject(null) } },
      { provide: BusinessContextStore, useValue: { selected$: new BehaviorSubject(null) } },
      { provide: CurrencyService, useValue: { format: (value: number | null) => `BDT ${value}`, formatNumber: (value: number | null) => `${value}` } },
      { provide: ErrorHandlerService, useValue: { extractErrorMessage: (error: Error) => error.message } }
    ]
  })
    .overrideComponent(component as never, { add: { schemas: [NO_ERRORS_SCHEMA] } })
    .compileComponents();
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, navigate };
}

describe('SalesFinancePageComponent', () => {
  it('defaults to Today for KPIs, Last 7 for cash and Last 30 for the trend', async () => {
    const api = apiStub();
    await setup(SalesFinancePageComponent, api);

    expect(api.salesSummary).toHaveBeenCalledWith({ from: '2026-11-06', to: '2026-11-06', outletId: null });
    expect(api.cashFlow).toHaveBeenCalledWith({ from: '2026-10-31', to: '2026-11-06', outletId: null });
    expect(api.salesTrend).toHaveBeenCalledWith({ from: '2026-10-08', to: '2026-11-06', outletId: null });
  });

  it('shows N/A for an unconfigured target and Unavailable for profit, never a number', async () => {
    const { fixture } = await setup(SalesFinancePageComponent, apiStub());
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('N/A');
    expect(text).toContain('Not configured');
    expect(text).toContain('Unavailable');
    expect(text).toContain('Target not configured');
    expect(text).toContain('1 refund has no usable amounts.');
  });

  it('shows a configured target with its achievement and shortfall', async () => {
    const api = apiStub();
    api.salesSummary = vi.fn(() => of({ ...SUMMARY, target: metric(2000), achievementPercent: 50, shortfall: metric(1000), targetExceeded: metric(0) }));
    const { fixture } = await setup(SalesFinancePageComponent, api);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('50% achieved');
    expect(text).toContain('Shortfall');
    expect(text).not.toContain('Target Exceeded');
  });

  it('labels an exceeded target as exceeded, never a negative shortfall', async () => {
    const api = apiStub();
    api.salesSummary = vi.fn(() => of({ ...SUMMARY, target: metric(500), achievementPercent: 200, shortfall: metric(0), targetExceeded: metric(500) }));
    const { fixture } = await setup(SalesFinancePageComponent, api);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Target Exceeded');
  });

  it('flags a total that only covers some showrooms', async () => {
    const api = apiStub();
    api.salesSummary = vi.fn(() => of({ ...SUMMARY, target: { value: 500, status: 'partial', note: 'Incomplete: 1 of 2 showroom(s)' } }));
    const { fixture } = await setup(SalesFinancePageComponent, api);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Partial');
    expect(text).toContain('Incomplete: 1 of 2 showroom(s)');
  });

  it('shows a scheduled off day instead of a shortfall', async () => {
    const api = apiStub();
    const offDay = metric(null, 'off_day');
    api.salesSummary = vi.fn(() => of({ ...SUMMARY, target: metric(0, 'off_day'), shortfall: offDay, targetExceeded: offDay }));
    api.showrooms = (vi.fn(() => of({
      context: CONTEXT,
      showrooms: [{ outletId: 5, outletName: 'Outlet 5', netSales: 1000, target: 0, achievementPercent: null, shortfall: null, targetExceeded: null, targetStatus: 'off_day' as const }]
    }))) as never;
    const { fixture } = await setup(SalesFinancePageComponent, api);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Scheduled Off Day');
  });

  it('draws a target series only when one exists, and leaves gaps as N/A', async () => {
    const api = apiStub();
    api.salesTrend = (vi.fn(() => of({
      context: CONTEXT,
      bucket: 'daily' as const,
      points: [
        { from: '2026-11-05', to: '2026-11-05', netSales: 900, target: 1000 },
        { from: '2026-11-06', to: '2026-11-06', netSales: 1000, target: null }
      ],
      targetStatus: 'partial' as const
    }))) as never;
    const { fixture } = await setup(SalesFinancePageComponent, api);
    const page = fixture.componentInstance as SalesFinancePageComponent;

    expect(page.trendPoints()[0].values).toEqual([900, 1000]);
    expect(page.trendPoints()[1].values).toEqual([1000]);
    expect(page.trendPoints()[1].texts[1]).toBe('N/A');
    expect((fixture.nativeElement as HTMLElement).querySelector('polyline.od-line--second')).not.toBeNull();
  });

  it('needs no permission beyond the dashboard: every figure is requested and shown', async () => {
    const api = apiStub();
    const { fixture } = await setup(SalesFinancePageComponent, api);

    expect(api.cashFlow).toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Restricted');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('BDT 500');
  });

  it('keeps the rest of the page working when one widget fails', async () => {
    const api = apiStub();
    api.salesTrend.mockReturnValue(throwError(() => new Error('Trend failed')) as never);
    const { fixture } = await setup(SalesFinancePageComponent, api);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Could not load the trend');
    expect(text).toContain('BDT 1000');
  });

  it('applies a global period to charts unless a chart has its own', async () => {
    const api = apiStub();
    const { fixture, navigate } = await setup(SalesFinancePageComponent, api);
    const page = fixture.componentInstance;
    page.setTrendPeriod('lastMonth');
    api.cashFlow.mockClear();
    api.salesTrend.mockClear();

    page.apply({ preset: 'custom', from: '2026-09-01', to: '2026-09-10', location: 'outlet:5', categoryId: null });

    expect(api.cashFlow).toHaveBeenCalledWith({ from: '2026-09-01', to: '2026-09-10', outletId: 5 });
    expect(api.salesTrend).toHaveBeenCalledWith({ from: '2026-10-01', to: '2026-10-31', outletId: 5 });
    expect(navigate).toHaveBeenCalled();

    page.setTrendPeriod('');
    expect(api.salesTrend).toHaveBeenLastCalledWith({ from: '2026-09-01', to: '2026-09-10', outletId: 5 });
  });

  it('narrows to a clicked showroom without changing any chart period', async () => {
    const api = apiStub();
    const { fixture } = await setup(SalesFinancePageComponent, api);

    fixture.componentInstance.filterToShowroom('5');

    expect(api.salesSummary).toHaveBeenLastCalledWith({ from: '2026-11-06', to: '2026-11-06', outletId: 5 });
    expect(api.cashFlow).toHaveBeenLastCalledWith({ from: '2026-10-31', to: '2026-11-06', outletId: 5 });
    expect(api.salesTrend).toHaveBeenLastCalledWith({ from: '2026-10-08', to: '2026-11-06', outletId: 5 });
  });

  it('labels the sales card Selected Period for any other range', async () => {
    const { fixture } = await setup(SalesFinancePageComponent, apiStub());
    expect(fixture.componentInstance.salesLabel()).toBe("Today's Sales");

    fixture.componentInstance.apply({ preset: 'yesterday', from: '2026-11-05', to: '2026-11-05', location: '', categoryId: null });
    expect(fixture.componentInstance.salesLabel()).toBe('Selected Period Sales');
  });

  it('honours a valid deep link and ignores an outlet of another business in it', async () => {
    const api = apiStub();
    await setup(SalesFinancePageComponent, api, { from: '2026-09-01', to: '2026-09-02', outletId: '999' });

    expect(api.salesSummary).toHaveBeenCalledWith({ from: '2026-09-01', to: '2026-09-02', outletId: null });
  });

  it('ignores an invalid deep link and uses the defaults', async () => {
    const api = apiStub();
    await setup(SalesFinancePageComponent, api, { from: '2026-09-10', to: '2026-09-01' });

    expect(api.salesSummary).toHaveBeenCalledWith({ from: '2026-11-06', to: '2026-11-06', outletId: null });
  });

  it('shows a retryable error when access is refused', async () => {
    const api = apiStub();
    api.meta.mockReturnValue(throwError(() => new Error('You do not have permission to perform this action.')) as never);
    const { fixture } = await setup(SalesFinancePageComponent, api);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('You do not have permission');
    expect(api.salesSummary).not.toHaveBeenCalled();
  });
});

describe('InventoryIntelligencePageComponent', () => {
  it('defaults the movement analysis to Last 30 Days', async () => {
    const api = apiStub();
    await setup(InventoryIntelligencePageComponent, api);

    expect(api.inventorySummary).toHaveBeenCalledWith({
      from: '2026-10-08', to: '2026-11-06', locationId: null, locationType: null, categoryId: null
    });
  });

  it('shows separate sales-analysis and stock-snapshot badges', async () => {
    const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Sales Analysis:');
    expect(text).toContain('Stock Snapshot:');
  });

  it('always shows stock value at cost', async () => {
    const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());
    const column = fixture.componentInstance.slowColumns().find(item => item.key === 'stockValue');

    expect(column).toBeDefined();
    expect(column?.visible).not.toBe(false);
    expect(fixture.componentInstance.valuationSubtitle()).toContain('At cost');
    expect(fixture.componentInstance.locationRows()[0].valueText).toBe('BDT 1000');
  });

  it('never shows a location with unknown cost as a zero amount', async () => {
    const api = apiStub();
    api.stockByLocation = vi.fn(() => of({
      context: CONTEXT,
      locations: [
        { locationId: 1, locationType: 'outlet', locationName: 'Partly', quantity: 30, stockValue: 1128, percentOfTotal: 100,
          valuation: coverage({ valuedSubtotal: 1128, status: 'partial', valuedQuantity: 10, unvaluedRecords: 2, unvaluedQuantity: 20 }) },
        { locationId: 2, locationType: 'outlet', locationName: 'Free', quantity: 3, stockValue: 0, percentOfTotal: 0,
          valuation: coverage({ valuedSubtotal: 0, status: 'zero_cost', valuedRecords: 0, valuedQuantity: 0, zeroCostRecords: 1, zeroCostQuantity: 3 }) },
        { locationId: 9, locationType: 'warehouse', locationName: 'Central', quantity: 2523, stockValue: null, percentOfTotal: null,
          valuation: coverage({ valuedSubtotal: null, status: 'not_valued', valuedRecords: 0, valuedQuantity: 0, unvaluedRecords: 23, unvaluedQuantity: 2523 }) }
      ],
      totalStockValue: 1128,
      valueStatus: 'partial',
      valuation: coverage({ valuedSubtotal: 1128, status: 'partial', valuedQuantity: 10, zeroCostRecords: 1, zeroCostQuantity: 3, unvaluedRecords: 25, unvaluedQuantity: 2543 })
    })) as never;

    const { fixture } = await setup(InventoryIntelligencePageComponent, api);
    const [partly, free, central] = fixture.componentInstance.locationRows();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(partly.valueText).toBe('BDT 1128 (Partial Value)');
    expect(partly.detail).toContain('Not valued: 20 units (2 records)');
    expect(free.valueText).toBe('BDT 0 (zero cost)');
    expect(central.valueText).toBe('Not Valued');
    expect(central.value).toBe(0);
    expect(central.detail).toContain('Not valued: 2,523 units (23 records)');
    expect(central.detail).not.toContain('% of');
    expect(text).toContain('Total: BDT 1128 (Partial Value)');
  });

  describe('with stock in transit', () => {
    const onHand = [{ unitCode: 'm', quantity: 300 }, { unitCode: 'ml', quantity: 450 }, { unitCode: 'pcs', quantity: 4560 }];
    const withTransit = (api: ReturnType<typeof apiStub>, businessWide: boolean) => {
      const summary = api.inventorySummary() as unknown as Observable<InventorySummary>;
      api.inventorySummary = vi.fn(() => summary.pipe(map(data => ({
        ...data,
        quantityByUnit: onHand,
        inTransit: businessWide
          ? { incoming: FABRIC_AND_ATTAR, outgoing: FABRIC_AND_ATTAR, businessWide }
          : { incoming: FABRIC_AND_ATTAR, outgoing: NO_TRANSIT, businessWide },
        totalAccountedByUnit: [{ unitCode: 'm', quantity: 500 }, { unitCode: 'ml', quantity: 500 }, { unitCode: 'pcs', quantity: 4560 }],
        totalAccountedValuation: coverage({ valuedSubtotal: 7000, status: 'partial', unvaluedRecords: 2 })
      })))) as never;
    };

    it('shows on hand, in transit and the accounted total separately, one figure per unit', async () => {
      const api = apiStub();
      withTransit(api, true);
      const { fixture } = await setup(InventoryIntelligencePageComponent, api);
      const page = fixture.componentInstance;
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

      expect(page.totalStockText()).toBe('300 m · 450 ml · 4,560 pcs');
      expect(page.inTransitText()).toBe('200 m · 50 ml');
      expect(page.accountedText()).toBe('500 m · 500 ml · 4,560 pcs');
      expect(page.inTransitStatus()).toBe('partial');
      expect(page.inTransitDetail()).toBe('BDT 2000 (Partial Value) · 2 transfer(s)');
      expect(text).toContain('On-hand Quantity');
      expect(text).toContain('In Transit');
      expect(text).toContain('Total Accounted Stock');
      // 200 m and 50 ml are never one number.
      expect(text).not.toContain('250');
    });

    it('splits a location into incoming and outgoing', async () => {
      const api = apiStub();
      withTransit(api, false);
      const { fixture } = await setup(InventoryIntelligencePageComponent, api, { from: '2026-11-01', to: '2026-11-06', location: 'outlet:5' });
      const page = fixture.componentInstance;

      expect(page.inTransitDetail()).toBe('Incoming: 200 m · 50 ml (BDT 2000 (Partial Value)) · Outgoing: None (no value)');
      expect(page.accountedNote()).toContain('Outgoing stock counts at its destination');
    });

    it('says when nothing is in transit', async () => {
      const api = apiStub();
      const summary = api.inventorySummary() as unknown as Observable<InventorySummary>;
      api.inventorySummary = vi.fn(() => summary.pipe(map(data => ({
        ...data, inTransit: { incoming: NO_TRANSIT, outgoing: NO_TRANSIT, businessWide: true }
      })))) as never;
      const { fixture } = await setup(InventoryIntelligencePageComponent, api);

      expect(fixture.componentInstance.inTransitText()).toBe('None');
      expect(fixture.componentInstance.inTransitDetail()).toBe('No dispatched transfer is awaiting receipt');
    });

    it('lists incoming and outgoing transit on each location', async () => {
      const api = apiStub();
      api.stockByLocation = vi.fn(() => of({
        context: CONTEXT,
        locations: [{
          locationId: 5, locationType: 'outlet', locationName: 'Outlet 5', quantity: 0, stockValue: null, percentOfTotal: null,
          valuation: NO_STOCK, quantityByUnit: [], incoming: FABRIC_AND_ATTAR, outgoing: NO_TRANSIT
        }],
        totalStockValue: null,
        valueStatus: 'ok',
        valuation: NO_STOCK,
        inTransit: FABRIC_AND_ATTAR
      })) as never;

      const { fixture } = await setup(InventoryIntelligencePageComponent, api);
      const [outlet] = fixture.componentInstance.locationRows();

      expect(outlet.detail).toContain('Incoming in transit: 200 m · 50 ml');
      expect(outlet.detail).not.toContain('Outgoing in transit');
    });

    it('lists the lines, marks direction for a location, and never shows unknown cost as zero', async () => {
      const line: InTransitLine = {
        transferId: 7, transferNo: 'TRF-2026-00007', isReturn: false, variantId: 70, productId: 700, mainCode: 'ATR', productName: 'Attar',
        sku: 'ATR-1', variantLabel: 'Default', categoryName: 'Perfume', fromLocationId: 9, fromLocationType: 'warehouse', fromLocationName: 'Central',
        toLocationId: 5, toLocationType: 'outlet', toLocationName: 'Outlet 5', quantity: 50, baseUnitCode: 'ml', value: null,
        dispatchedAtUtc: '2026-11-05T10:00:00Z'
      };
      const api = apiStub();
      api.inTransit = vi.fn(() => of({
        context: CONTEXT,
        rows: { items: [line], totalCount: 1, page: 1, pageSize: 10 },
        summary: { incoming: FABRIC_AND_ATTAR, outgoing: NO_TRANSIT, businessWide: false }
      })) as never;

      const { fixture } = await setup(InventoryIntelligencePageComponent, api, { from: '2026-11-01', to: '2026-11-06', location: 'outlet:5' });
      const page = fixture.componentInstance;
      page.onTransitQuery({ page: 1, pageSize: 10, search: '', sort: null, filters: { route: 'incoming' } } as never);

      expect(api.inTransit).toHaveBeenLastCalledWith(
        expect.objectContaining({ locationId: 5, locationType: 'outlet' }),
        expect.objectContaining({ direction: 'incoming' }));
      expect(page.direction(line)).toBe('Incoming');
      expect(page.transitColumns().find(column => column.key === 'route')?.filter).toBeDefined();
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('TRF-2026-00007');
      expect(text).toContain('Not Valued');
    });

    it('asks for no direction when all locations are shown', async () => {
      const api = apiStub();
      const { fixture } = await setup(InventoryIntelligencePageComponent, api);
      fixture.componentInstance.onTransitQuery({ page: 1, pageSize: 10, search: '', sort: null, filters: { route: 'incoming' } } as never);

      expect(api.inTransit).toHaveBeenLastCalledWith(
        expect.objectContaining({ locationId: null }),
        expect.objectContaining({ direction: undefined }));
      expect(fixture.componentInstance.transitColumns().find(column => column.key === 'route')?.filter).toBeUndefined();
    });
  });

  it('shows valued and unvalued quantities on the stock value card', async () => {
    const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());

    expect(fixture.componentInstance.coverageText(fixture.componentInstance.summary.data()?.valuation))
      .toBe('Valued: 80 units (1 record) · Not valued: 3 units (1 record)');
  });

  describe('with stock in more than one unit', () => {
    const byUnit = [{ unitCode: 'ml', quantity: 1250.5 }, { unitCode: 'pcs', quantity: 83 }];

    it('lists each unit instead of a total, and hides the quantities the API adds across units', async () => {
      const api = apiStub();
      const summary = api.inventorySummary() as unknown as Observable<InventorySummary>;
      api.inventorySummary = vi.fn(() => summary.pipe(map(data => ({ ...data, quantityByUnit: byUnit })))) as never;

      const { fixture } = await setup(InventoryIntelligencePageComponent, api);
      const page = fixture.componentInstance;
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

      expect(page.totalStockText()).toBe('1,250.5 ml · 83 pcs');
      expect(text).toContain('1,250.5 ml · 83 pcs');
      expect(page.coverageText(page.summary.data()?.valuation, page.summaryHasOtherUnits()))
        .toBe('Valued: 1 record · Not valued: 1 record');
      expect(text).not.toContain('80 units');
    });

    it('labels a location by unit', async () => {
      const api = apiStub();
      api.stockByLocation = vi.fn(() => of({
        context: CONTEXT,
        locations: [{
          locationId: 9, locationType: 'warehouse', locationName: 'Central', quantity: 83, stockValue: 1000, percentOfTotal: 100,
          valuation: coverage({ unvaluedRecords: 1, unvaluedQuantity: 1333 }), quantityByUnit: byUnit
        }],
        totalStockValue: 1000,
        valueStatus: 'ok',
        valuation: coverage()
      })) as never;

      const { fixture } = await setup(InventoryIntelligencePageComponent, api);
      const [central] = fixture.componentInstance.locationRows();

      expect(central.detail).toContain('1,250.5 ml · 83 pcs');
      expect(central.detail).not.toContain('83 units');
      expect(central.detail).not.toContain('1,333');
    });

    it('labels rows and sentences with the base unit, and leaves pcs as they were', async () => {
      const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());
      const page = fixture.componentInstance;

      expect(page.qty(1250.5, 'ml')).toBe('1,250.5 ml');
      expect(page.qty(28, 'pcs')).toBe('28');
      expect(page.qty(28, undefined)).toBe('28');
      expect(page.phrase(300, 'ml')).toBe('300 ml');
      expect(page.phrase(28, 'pcs')).toBe('28 unit(s)');
    });

    it('shows product totals per unit, and the API totals for pcs', async () => {
      const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());
      const page = fixture.componentInstance;
      const location = { netUnitsSold: 5, baseUnitCode: 'pcs' };
      const pcsOnly = { totalAvailableStock: 7, totalNetUnitsSold: 5, locations: [location], quantityByUnit: [{ unitCode: 'pcs', quantity: 7 }] } as never;
      const mixed = {
        totalAvailableStock: 7,
        totalNetUnitsSold: 5,
        locations: [location, { netUnitsSold: 250, baseUnitCode: 'ml' }],
        quantityByUnit: [{ unitCode: 'ml', quantity: 900 }, { unitCode: 'pcs', quantity: 7 }]
      } as never;

      expect(page.productStock(pcsOnly)).toBe(7);
      expect(page.productSold(pcsOnly)).toBe(5);
      expect(page.productStock(mixed)).toBe('900 ml · 7 pcs');
      expect(page.productSold(mixed)).toBe('5 pcs · 250 ml');
    });
  });

  it('labels a record with unknown cost Not Valued wherever it appears', async () => {
    const { fixture } = await setup(InventoryIntelligencePageComponent, apiStub());
    const page = fixture.componentInstance;

    expect(page.valueLabel(null, 5)).toBe('Not Valued');
    expect(page.valueLabel(0, 5)).toBe('BDT 0 (zero cost)');
    expect(page.valueLabel(0, 0)).toBe('BDT 0');
    expect(page.valueLabel(250, 5)).toBe('BDT 250');
  });

  it('requests a short preview, then full pages after View All', async () => {
    const api = apiStub();
    const { fixture } = await setup(InventoryIntelligencePageComponent, api);
    const page = fixture.componentInstance;

    expect(api.bestSellers.mock.lastCall?.[1 as never]).toMatchObject({ page: 1, pageSize: 15 });
    expect(api.slowMovers.mock.lastCall?.[1 as never]).toMatchObject({ page: 1, pageSize: 15 });
    expect(api.transferOpportunities.mock.lastCall?.[1 as never]).toMatchObject({ page: 1, pageSize: 10 });

    page.showAllBest();
    expect(api.bestSellers.mock.lastCall?.[1 as never]).toMatchObject({ pageSize: 25 });
  });

  it('maps table criteria to the API', async () => {
    const api = apiStub();
    const { fixture } = await setup(InventoryIntelligencePageComponent, api);
    const query = {
      search: 'BRK', searchColumns: [], filters: { source: '5', priority: 'high' },
      sort: { columnKey: 'netSales', direction: 'desc' as const },
      page: 1, pageSize: 25, sequence: 2, reason: 'filter' as const
    };

    fixture.componentInstance.onBestQuery(query);
    fixture.componentInstance.onTransferQuery(query);

    expect(api.bestSellers.mock.lastCall?.[1 as never]).toMatchObject({ search: 'BRK', sortBy: 'netSales', sortDirection: 'desc' });
    expect(api.transferOpportunities.mock.lastCall?.[1 as never]).toMatchObject({
      search: 'BRK', sourceLocationId: 5, priority: 'high'
    });
  });

  it('opens the existing transfer form prefilled, and creates nothing itself', async () => {
    const api = apiStub();
    const { fixture, navigate } = await setup(InventoryIntelligencePageComponent, api);

    fixture.componentInstance.initiate({
      recommendationKey: '11-5-1',
      variantId: 11,
      sku: 'SKU-11',
      suggestedQty: 28,
      source: { locationId: 5 },
      destination: { locationId: 1 }
    } as never);

    expect(navigate).toHaveBeenCalledWith(['/stock-transfers/create'], {
      queryParams: {
        fromLocationId: 5, fromLocationType: 'outlet', toLocationId: 1, toLocationType: 'outlet',
        variantId: 11, sku: 'SKU-11', quantity: 28
      }
    });
  });

  it('explains a stale recommendation in the details view', async () => {
    const api = apiStub();
    api.transferOpportunity.mockReturnValue(throwError(() => new Error('This recommendation is no longer available.')));
    const { fixture } = await setup(InventoryIntelligencePageComponent, api);

    fixture.componentInstance.openTransfer({ recommendationKey: '11-5-1' } as never);

    expect(fixture.componentInstance.transferDetail.error()).toContain('no longer available');
  });
});

describe('OdFilterBarComponent', () => {
  async function bar() {
    await TestBed.configureTestingModule({ imports: [OdFilterBarComponent] }).compileComponents();
    const fixture = TestBed.createComponent(OdFilterBarComponent);
    fixture.componentRef.setInput('today', '2026-11-06');
    fixture.componentRef.setInput('applied', { preset: 'today', from: '2026-11-06', to: '2026-11-06', location: '', categoryId: null });
    fixture.detectChanges();
    return fixture;
  }

  it('does not emit an inverted range', async () => {
    const fixture = await bar();
    const emitted = vi.fn();
    fixture.componentInstance.applyRequested.subscribe(emitted);

    fixture.componentInstance.form.patchValue({ from: '2026-11-07', to: '2026-11-06' });
    fixture.componentInstance.apply();

    expect(emitted).not.toHaveBeenCalled();
    expect(fixture.componentInstance.error()).toContain('on or before');
  });

  it('changes nothing until Apply, and a preset fills in its dates', async () => {
    const fixture = await bar();
    const emitted = vi.fn();
    fixture.componentInstance.applyRequested.subscribe(emitted);

    fixture.componentInstance.form.controls.preset.setValue('last7');
    fixture.componentInstance.onPresetChange();
    expect(emitted).not.toHaveBeenCalled();

    fixture.componentInstance.apply();
    expect(emitted).toHaveBeenCalledWith({
      preset: 'last7', from: '2026-10-31', to: '2026-11-06', location: '', categoryId: null
    });
  });

  it('marks the range custom when a date is edited by hand', async () => {
    const fixture = await bar();

    fixture.componentInstance.form.controls.from.setValue('2026-11-01');

    expect(fixture.componentInstance.form.controls.preset.value).toBe('custom');
  });
});

describe('OdTimeChartComponent', () => {
  async function chart() {
    await TestBed.configureTestingModule({ imports: [OdTimeChartComponent] }).compileComponents();
    const fixture = TestBed.createComponent(OdTimeChartComponent);
    fixture.componentRef.setInput('caption', 'Cash');
    fixture.componentRef.setInput('series', [{ key: 'a', label: 'Received', color: '--rp-color-primary' }]);
    fixture.componentRef.setInput('points', [
      { label: '01 Nov', values: [10], texts: ['BDT 10'] },
      { label: '02 Nov', values: [-5], texts: ['BDT -5'] }
    ]);
    fixture.detectChanges();
    return fixture;
  }

  it('reads points by keyboard', async () => {
    const fixture = await chart();
    const plot = (fixture.nativeElement as HTMLElement).querySelector('.od-plot') as HTMLElement;

    plot.dispatchEvent(new FocusEvent('focus'));
    plot.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();

    expect(fixture.componentInstance.active()).toBe(1);
    expect((fixture.nativeElement as HTMLElement).querySelector('.od-tip')?.textContent).toContain('BDT -5');
  });

  it('offers the same numbers as a table', async () => {
    const fixture = await chart();

    fixture.componentInstance.view.set('table');
    fixture.detectChanges();

    const table = (fixture.nativeElement as HTMLElement).querySelector('table');
    expect(table?.textContent).toContain('01 Nov');
    expect(table?.textContent).toContain('BDT 10');
  });
});
