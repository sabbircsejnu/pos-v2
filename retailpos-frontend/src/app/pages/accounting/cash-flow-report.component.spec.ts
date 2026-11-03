import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { CashFlowReportComponent } from './cash-flow-report.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { CashFlowReport } from '../../models/accounting.model';

describe('CashFlowReportComponent', () => {
  const currentReport = report({ operating: 117, investing: -15, financing: 40, unclassified: 3, total: 145, reconciliation: 0 });
  const comparisonReport = report({ operating: 70, investing: 0, financing: 10, unclassified: 9, total: 89, reconciliation: 0 });

  async function setup() {
    const accounting = {
      getCashFlow: vi.fn().mockReturnValue(of({ data: currentReport })),
      getComparativeCashFlow: vi.fn().mockReturnValue(of({ data: { current: currentReport, comparison: comparisonReport } })),
      exportCashFlow: vi.fn().mockReturnValue(of(new Blob(['cash-flow'], { type: 'text/csv' })))
    };

    await TestBed.configureTestingModule({
      imports: [CashFlowReportComponent],
      providers: [
        { provide: AccountingService, useValue: accounting },
        { provide: AlertService, useValue: { error: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
        { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(CashFlowReportComponent);
    fixture.detectChanges();
    return { fixture, accounting };
  }

  it('renders backend-calculated sections and highlights unclassified movement', async () => {
    const { fixture } = await setup();

    expect(fixture.nativeElement.textContent).toContain('Operating Activities');
    expect(fixture.nativeElement.textContent).toContain('Investing Activities');
    expect(fixture.nativeElement.textContent).toContain('Financing Activities');
    expect(fixture.nativeElement.textContent).toContain('Unclassified Activities');
    expect(fixture.nativeElement.textContent).toContain('Net Cash Movement');
    expect(fixture.nativeElement.textContent).toContain('145.00');
    expect(fixture.nativeElement.textContent).toContain('GL-derived movement difference');
    expect(fixture.nativeElement.querySelector('.text-amber-700')).not.toBeNull();
  });

  it('renders actual-only comparison values from the comparative API', async () => {
    const { fixture, accounting } = await setup();
    const component = fixture.componentInstance;

    component.comparisonFromDate.set('2026-06-01');
    component.comparisonToDate.set('2026-06-30');
    component.load();
    fixture.detectChanges();

    expect(accounting.getComparativeCashFlow).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Comparison');
    expect(fixture.nativeElement.textContent).toContain('Actual-only comparison');
    expect(fixture.nativeElement.textContent).toContain('89.00');
  });

  it('invokes CSV export with the current report filters', async () => {
    const { fixture, accounting } = await setup();
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:cash-flow');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    fixture.componentInstance.export();

    expect(accounting.exportCashFlow).toHaveBeenCalled();
    expect(createUrl).toHaveBeenCalled();
    expect(revokeUrl).toHaveBeenCalledWith('blob:cash-flow');
  });
});

function report(values: { operating: number; investing: number; financing: number; unclassified: number; total: number; reconciliation: number }): CashFlowReport {
  return {
    dateFrom: '2026-07-01',
    dateTo: '2026-07-31',
    method: 'Direct',
    openingLiquidity: 100,
    closingLiquidity: 245,
    operatingNetMovement: values.operating,
    investingNetMovement: values.investing,
    financingNetMovement: values.financing,
    unclassifiedNetMovement: values.unclassified,
    totalExternalNetMovement: values.total,
    glDerivedMovement: values.total,
    reconciliationDifference: values.reconciliation,
    internalTransferNetMovement: 0,
    cutoverAndNonCashNetMovement: 0,
    internalTransferRows: [],
    cutoverAndNonCashRows: [],
    sections: [
      section('Operating', values.operating),
      section('Investing', values.investing),
      section('Financing', values.financing),
      section('Unclassified', values.unclassified)
    ]
  };
}

function section(activityType: string, netMovement: number) {
  return {
    activityType,
    totalInflows: Math.max(netMovement, 0),
    totalOutflows: Math.max(-netMovement, 0),
    netMovement,
    rows: netMovement === 0 ? [] : [{
      journalEntryId: 1,
      journalEntryLineId: activityType.length,
      postingDate: '2026-07-10',
      journalNumber: 'JR-001',
      journalType: activityType === 'Unclassified' ? 'ManualJournal' : `${activityType}Posting`,
      referenceType: activityType === 'Unclassified' ? 'ManualJournal' : activityType,
      referenceId: 'source-1',
      accountSlot: 'cash',
      accountId: 1000,
      accountCode: '1000',
      accountName: 'Cash',
      inflow: Math.max(netMovement, 0),
      outflow: Math.max(-netMovement, 0),
      netMovement,
      classificationReason: `${activityType} reason`,
      counterpartAccounts: 'Counterpart',
      counterpartAccountIds: []
    }]
  };
}
