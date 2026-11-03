import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { CashBookReportComponent } from './cash-book-report.component';
import { AccountingService } from '../../services/accounting.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CurrencyService } from '../../services/currency.service';

describe('CashBookReportComponent', () => {
  it('renders a non-zero reconciliation difference returned by the report API', async () => {
    await TestBed.configureTestingModule({
      imports: [CashBookReportComponent],
      providers: [
        { provide: AccountingService, useValue: { getCashBookReport: vi.fn().mockReturnValue(of({ data: {
          accounts: [{ accountId: 1, accountSlot: 'cash', accountCode: '1000', accountName: 'Cash', openingBalance: 10, totalInflows: 0, totalOutflows: 0, closingBalance: 10, generalLedgerBalance: 5, reconciliationDifference: 5, rows: [] }]
        } })) } },
        { provide: AlertService, useValue: { error: vi.fn() } },
        { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('error') } },
        { provide: CurrencyService, useValue: { format: (value: number) => value.toFixed(2) } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(CashBookReportComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('GL reconciliation difference');
    expect(fixture.nativeElement.textContent).toContain('5.00');
    expect(fixture.nativeElement.querySelector('.text-red-700')).not.toBeNull();
  });
});