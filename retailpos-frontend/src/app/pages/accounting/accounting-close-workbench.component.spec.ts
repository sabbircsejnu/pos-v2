import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AccountingCloseWorkbenchComponent } from './accounting-close-workbench.component';
import { AccountingService } from '../../services/accounting.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';

describe('AccountingCloseWorkbenchComponent', () => {
  const year = { id: 1, name: 'FY 2026', yearCode: 'FY26', startDate: '2026-01-01', endDate: '2026-12-31', isCurrent: true, isClosed: false };
  const period = { id: 10, financialYearId: 1, periodNumber: 12, name: 'December', startDate: '2026-12-01', endDate: '2026-12-31', status: 'Locked' };
  const periodRun = { id: 100, accountingPeriodId: 10, runNumber: 1, status: 'prepared', evaluationSequence: 1, preparedBy: 4, preparedAt: '2026-12-31T10:00:00Z', gates: [{ id: 8, evaluationSequence: 1, gateCode: 'inventory', status: 'blocking', actualAmount: 2, details: 'Difference', isHistoricalCutoverLimitation: true, evaluatedAt: '2026-12-31T10:00:00Z', hasApprovedException: false }, { id: 9, evaluationSequence: 1, gateCode: 'manual_journal_warning', status: 'warning', actualAmount: 1, details: 'Draft exists', isHistoricalCutoverLimitation: false, evaluatedAt: '2026-12-31T10:00:00Z', hasApprovedException: false }] };
  const yearRun = { id: 200, financialYearId: 1, finalAccountingPeriodId: 10, finalPeriodCloseRunId: 100, runNumber: 1, status: 'executed', preparedBy: 4, preparedAt: '2026-12-31T10:00:00Z', yearEndJournalEntryId: 77, yearEndJournalNumber: 'JR-FY-1', reversalJournalEntryId: 78, reopenedBy: 5, reopenedAt: '2027-01-02T10:00:00Z', reopenReason: 'Correction' };
  function setup(allowed = true) {
    const service = { getFinancialYears: vi.fn().mockReturnValue(of({ data: [year] })), getPeriods: vi.fn().mockReturnValue(of({ data: [period] })), getPeriodCloseRuns: vi.fn().mockReturnValue(of({ data: [periodRun] })), getFinancialYearCloseRuns: vi.fn().mockReturnValue(of({ data: [yearRun] })), preparePeriodClose: vi.fn().mockReturnValue(of({ data: periodRun })), reevaluatePeriodClose: vi.fn().mockReturnValue(of({ data: periodRun })), approveCloseException: vi.fn().mockReturnValue(of({ data: periodRun })), approvePeriodClose: vi.fn().mockReturnValue(of({ data: periodRun })), executePeriodClose: vi.fn().mockReturnValue(of({ data: periodRun })), reopenPeriodClose: vi.fn().mockReturnValue(of({ data: periodRun })), prepareFinancialYearClose: vi.fn().mockReturnValue(of({ data: yearRun })), approveFinancialYearClose: vi.fn().mockReturnValue(of({ data: yearRun })), executeFinancialYearClose: vi.fn().mockReturnValue(of({ data: yearRun })), reopenFinancialYearClose: vi.fn().mockReturnValue(of({ data: yearRun })), downloadPeriodClosePackage: vi.fn().mockReturnValue(of(new Blob(['{}']))), downloadFinancialYearClosePackage: vi.fn().mockReturnValue(of(new Blob(['{}']))) };
    return TestBed.configureTestingModule({ imports: [AccountingCloseWorkbenchComponent], providers: [{ provide: AccountingService, useValue: service }, { provide: AuthService, useValue: { hasPermission: vi.fn().mockReturnValue(allowed) } }, { provide: AlertService, useValue: { error: vi.fn() } }, { provide: ErrorHandlerService, useValue: { extractErrorMessage: vi.fn().mockReturnValue('Request failed') } }] }).compileComponents().then(() => ({ service }));
  }
  it('renders authoritative blocking and warning gates and only exposes an eligible exception action', async () => {
    await setup(); const fixture = TestBed.createComponent(AccountingCloseWorkbenchComponent); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.gate.blocking')).not.toBeNull(); expect(fixture.nativeElement.querySelector('.gate.warning')).not.toBeNull(); expect(fixture.nativeElement.textContent).toContain('Approve exception'); expect(fixture.nativeElement.textContent).toContain('JR-FY-1'); expect(fixture.nativeElement.textContent).toContain('Journal #78');
  });
  it('hides financial actions without permissions', async () => {
    await setup(false); const fixture = TestBed.createComponent(AccountingCloseWorkbenchComponent); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Approve exception'); expect(fixture.nativeElement.textContent).not.toContain('Reopen & reverse close');
  });
  it('refreshes backend state after a period mutation and invokes evidence package export', async () => {
    const { service } = await setup(); const fixture = TestBed.createComponent(AccountingCloseWorkbenchComponent); const component = fixture.componentInstance; fixture.detectChanges();
    component.reevaluatePeriod(); expect(service.reevaluatePeriodClose).toHaveBeenCalledWith(100); expect(service.getFinancialYears).toHaveBeenCalledTimes(2);
    const create = vi.spyOn(document, 'createElement'); component.downloadPeriodPackage(); expect(service.downloadPeriodClosePackage).toHaveBeenCalledWith(100); create.mockRestore();
  });
  it('requires a reopen reason before issuing a controlled fiscal reopen request', async () => {
    const { service } = await setup(); const fixture = TestBed.createComponent(AccountingCloseWorkbenchComponent); fixture.detectChanges(); const prompt = vi.spyOn(window, 'prompt').mockReturnValue(''); fixture.componentInstance.reopenYear(); expect(service.reopenFinancialYearClose).not.toHaveBeenCalled(); prompt.mockRestore();
  });
});