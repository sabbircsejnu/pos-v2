import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { BalanceSheetReportComponent } from './balance-sheet-report.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';
import { Router } from '@angular/router';

describe('BalanceSheetReportComponent', () => {
  it('renders the journal cutover limitation and a non-zero reconciliation difference', async () => {
    await TestBed.configureTestingModule({
      imports: [BalanceSheetReportComponent],
      providers: [
        { provide: AccountingService, useValue: { getBalanceSheet: vi.fn().mockReturnValue(of({ data: {
          asOfDate: '2026-01-31', isManagementView: true, cutoverWarning: 'Opening balance is unavailable.',
          assets: { name: 'Assets', total: 100, accounts: [] }, liabilities: { name: 'Liabilities', total: 40, accounts: [] }, equity: { name: 'Equity', total: 50, accounts: [] },
          currentYearEarnings: 0, unclosedPriorYearEarnings: 0, totalAssets: 100, totalLiabilitiesAndEquity: 90, reconciliationDifference: 10
        } })), getComparativeBalanceSheet: vi.fn(), getChartAccounts: vi.fn() } },
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: AlertService, useValue: { error: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
        { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(BalanceSheetReportComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Opening balance is unavailable.');
    expect(fixture.nativeElement.textContent).toContain('Management view');
    expect(fixture.nativeElement.textContent).toContain('Reconciliation Difference');
    expect(fixture.nativeElement.textContent).toContain('10.00');
  });
});