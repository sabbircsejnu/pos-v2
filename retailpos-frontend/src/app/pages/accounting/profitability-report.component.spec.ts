import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { ProfitabilityReportComponent } from './profitability-report.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { BusinessProfitabilityReport, OutletProfitabilityReport, ProductContributionReport } from '../../models/accounting.model';

describe('ProfitabilityReportComponent', () => {
  async function setup() {
    const accounting = {
      getBusinessProfitability: vi.fn().mockReturnValue(of({ data: businessReport(15) })),
      getComparativeBusinessProfitability: vi.fn().mockReturnValue(of({ data: { current: businessReport(15), comparison: businessReport(8) } })),
      getOutletProfitability: vi.fn().mockReturnValue(of({ data: outletReport() })),
      getComparativeOutletProfitability: vi.fn().mockReturnValue(of({ data: { current: outletReport(), comparison: outletReport(5) } })),
      getProductContribution: vi.fn().mockReturnValue(of({ data: productReport() })),
      getComparativeProductContribution: vi.fn().mockReturnValue(of({ data: { current: productReport(), comparison: productReport(20) } })),
      exportProfitability: vi.fn().mockReturnValue(of(new Blob(['profitability'], { type: 'text/csv' })))
    };

    await TestBed.configureTestingModule({
      imports: [ProfitabilityReportComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: { error: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
        { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(ProfitabilityReportComponent);
    fixture.detectChanges();
    return { fixture, accounting };
  }

  it('renders business profitability and comparative net profit change', async () => {
    const { fixture, accounting } = await setup();
    const component = fixture.componentInstance;

    component.comparisonFromDate.set('2026-06-01');
    component.comparisonToDate.set('2026-06-30');
    component.load();
    fixture.detectChanges();

    expect(accounting.getComparativeBusinessProfitability).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Net Profit');
    expect(fixture.nativeElement.textContent).toContain('Net profit change');
    expect(fixture.nativeElement.textContent).toContain('7.00');
  });

  it('renders outlet unallocated amounts explicitly', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.setView('outlet');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Outlet Profitability');
    expect(fixture.nativeElement.textContent).toContain('Unallocated');
    expect(fixture.nativeElement.textContent).toContain('lacks authoritative outlet attribution');
    expect(fixture.nativeElement.textContent).toContain('25.00');
  });

  it('uses contribution terminology for product/category reporting', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.setView('product');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Product / Category Contribution');
    expect(fixture.nativeElement.textContent).toContain('Contribution Basis');
    expect(fixture.nativeElement.textContent).toContain('shared expenses are not allocated');
    expect(fixture.nativeElement.textContent).toContain('Contribution Reconciliation');
    expect(fixture.nativeElement.textContent).not.toContain('Product Net Profit');
  });

  it('invokes export for the selected profitability view', async () => {
    const { fixture, accounting } = await setup();
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:profitability');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    fixture.componentInstance.setView('product');
    fixture.componentInstance.export();

    expect(accounting.exportProfitability).toHaveBeenCalledWith('product', expect.objectContaining({ dateFrom: expect.any(String), dateTo: expect.any(String) }));
    expect(createUrl).toHaveBeenCalled();
    expect(revokeUrl).toHaveBeenCalledWith('blob:profitability');
  });
});

function businessReport(netProfit: number): BusinessProfitabilityReport {
  return { dateFrom: '2026-07-01', dateTo: '2026-07-31', revenue: 100, costOfGoodsSold: 60, expenses: 25, netProfit, authoritativePnlNetProfit: netProfit, reconciliationDifference: 0, source: 'posted-journals' };
}

function outletReport(netProfit = 15): OutletProfitabilityReport {
  return {
    dateFrom: '2026-07-01', dateTo: '2026-07-31',
    outlets: [{ outletId: 11, outletName: 'Main Outlet', revenue: 100, costOfGoodsSold: 60, expenses: 0, netProfit: 40 }],
    unallocated: { outletName: 'Unallocated', revenue: 0, costOfGoodsSold: 0, expenses: 25, netProfit: -25, unallocatedReason: 'Posted accounting activity lacks authoritative outlet attribution.' },
    businessRevenue: 100, businessCostOfGoodsSold: 60, businessExpenses: 25, businessNetProfit: 15,
    attributedRevenue: 100, attributedCostOfGoodsSold: 60, attributedExpenses: 25, attributedNetProfit: netProfit, reconciliationDifference: 0
  };
}

function productReport(contribution = 40): ProductContributionReport {
  return {
    dateFrom: '2026-07-01', dateTo: '2026-07-31', contributionBasis: 'Revenue minus COGS only; shared expenses are not allocated.',
    products: [{ productId: 501, productName: 'Product', productCode: 'P-1', categoryId: 401, categoryName: 'Category', revenue: 100, costOfGoodsSold: 60, contribution, marginPercent: 40 }],
    categories: [{ categoryId: 401, categoryName: 'Category', revenue: 100, costOfGoodsSold: 60, contribution, marginPercent: 40 }],
    attributedRevenue: 100, attributedCostOfGoodsSold: 60, attributedContribution: contribution,
    authoritativeRevenue: 100, authoritativeCostOfGoodsSold: 60, unallocatedRevenue: 0, unallocatedCostOfGoodsSold: 0,
    revenueReconciliationDifference: 0, cogsReconciliationDifference: 0,
    historicalLimitation: 'Contribution uses SaleItem revenue and historical unit cost evidence only; shared expenses and journal lines without product/category source evidence are not allocated.'
  };
}
