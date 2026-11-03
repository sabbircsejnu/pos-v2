import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BankReconciliationComponent } from './bank-reconciliation.component';
import { AccountingService } from '../../services/accounting.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

describe('BankReconciliationComponent', () => {
  const session = { id: 7, businessId: 1, bankAccountId: 5, periodStart: '2026-09-01', periodEnd: '2026-09-30', status: 'in_progress', statementOpeningBalance: 100, statementClosingBalance: 125, glOpeningBalance: 100 };
  const statement = { id: 10, bankAccountId: 5, bankStatementImportId: 2, sourceRowNumber: 1, transactionDate: '2026-09-10', description: 'Deposit', debitAmount: 0, creditAmount: 25, signedAmount: 25 };
  const gl = { journalEntryLineId: 20, date: '2026-09-10', signedAmount: 25, description: 'Deposit', journalNumber: 'JR-1' };

  function setup(permission = true) {
    const service = {
      getBankAccounts: vi.fn().mockReturnValue(of({ data: [{ id: 5, displayName: 'Main Bank', chartAccountId: 101, isActive: true }] })),
      getReconciliationSessions: vi.fn().mockReturnValue(of({ data: [session] })),
      getStatementImports: vi.fn().mockReturnValue(of({ data: [] })),
      getReconciliationCalculation: vi.fn().mockReturnValue(of({ data: { statementOpeningBalance: 100, statementMovement: 25, statementClosingBalance: 125, glOpeningBalance: 100, glMovement: 25, glClosingBalance: 125, matchedStatementAmount: 0, matchedStatementCount: 0, matchedGlAmount: 0, matchedGlCount: 0, unmatchedStatementAmount: 25, unmatchedStatementCount: 1, unmatchedGlAmount: 25, unmatchedGlCount: 1, reconciliationDifference: 0 } })),
      getStatementCandidates: vi.fn().mockReturnValue(of({ data: [{ bankStatementLineId: 10, date: '2026-09-10', signedAmount: 25, description: 'Deposit', externalReference: 'S-1' }] })),
      getGlCandidates: vi.fn().mockReturnValue(of({ data: [gl] })),
      getReconciliationMatches: vi.fn().mockReturnValue(of({ data: [] })),
      getStatementAdjustments: vi.fn().mockReturnValue(of({ data: [] })),
      getStatementExceptions: vi.fn().mockReturnValue(of({ data: [] })),
      getSettlements: vi.fn().mockReturnValue(of({ data: [] })),
      createReconciliationMatch: vi.fn().mockReturnValue(of({ data: { id: 1 } })),
      importBankStatement: vi.fn().mockReturnValue(of({ data: { sourceFileName: 'statement.csv', acceptedRowCount: 1 } }))
    };
    const auth = { hasPermission: vi.fn().mockReturnValue(permission) };
    return TestBed.configureTestingModule({ imports: [BankReconciliationComponent], providers: [
      { provide: AccountingService, useValue: service }, { provide: AuthService, useValue: auth },
      { provide: AlertService, useValue: { error: vi.fn() } }, { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('Duplicate import') } }
    ] }).compileComponents().then(() => ({ service, auth }));
  }

  it('renders backend reconciliation totals and never invents them locally', async () => {
    const { service } = await setup(); const fixture = TestBed.createComponent(BankReconciliationComponent); fixture.detectChanges();
    expect(service.getReconciliationCalculation).toHaveBeenCalledWith(7);
    expect(fixture.nativeElement.textContent).toContain('125.00');
    expect(fixture.nativeElement.textContent).toContain('Difference');
  });

  it('calculates exact selected totals and enables a valid match request', async () => {
    const { service } = await setup(); const fixture = TestBed.createComponent(BankReconciliationComponent); const component = fixture.componentInstance; fixture.detectChanges();
    component.toggleStatement(10); component.toggleGl(20); expect(component.selectedStatementTotal()).toBe(25); expect(component.selectedGlTotal()).toBe(25); component.match();
    expect(service.createReconciliationMatch).toHaveBeenCalledWith({ sessionId: 7, statementLineIds: [10], journalEntryLineIds: [20] });
  });

  it('does not expose write actions when permission is unavailable', async () => {
    await setup(false); const fixture = TestBed.createComponent(BankReconciliationComponent); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button.primary')).toBeNull();
  });

  it('surfaces duplicate import errors from the backend', async () => {
    const configured = await setup(); configured.service.importBankStatement.mockReturnValue(throwError(() => new Error('duplicate')));
    const fixture = TestBed.createComponent(BankReconciliationComponent); const component = fixture.componentInstance; fixture.detectChanges();
    component.bankAccountId = 5; component.selectedFile.set(new File(['csv'], 'statement.csv', { type: 'text/csv' })); component.importStatement();
    expect(component.error()).toBe('Duplicate import');
  });
});
