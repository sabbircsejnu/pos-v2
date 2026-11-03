import { CompanyDateService } from '../../services/company-date.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../services/hr-payroll-capability.store';
import { PayrollDashboardService, PayrollDashboardSummary } from '../../services/payroll-dashboard.service';
import { PayrollDashboardPageComponent } from './payroll-dashboard-page.component';

const dashboard: PayrollDashboardSummary = {
  period: { id: 4, code: '2026-10', status: 'Closed', start: '2026-10-01', end: '2026-10-31' },
  run: {
    id: 8, status: 'Completed', runType: 'Regular', employeeCount: 1,
    gross: 1200, totalDeductions: 200, netPayable: 1000, approvedAmount: 1000,
    processingStates: [], runTypes: [], unresolvedCorrectionLineageCount: 0
  },
  payments: {
    totalPayable: 1000, totalPaid: 700, totalOutstanding: 300,
    states: [{ status: 'PartiallyPaid', count: 1, amount: 300 }],
    heldCount: 0, processingCount: 0, failedCount: 0, returnedCount: 0, completedCount: 0,
    methods: [{ method: 'Cash', settledAmount: 400 }, { method: 'Bank', settledAmount: 300 }]
  },
  reconciliation: {
    applies: true, reconciledCount: 0, partiallyReconciledCount: 1, unreconciledCount: 0,
    exceptionCount: 0
  },
  accounting: { enabled: false, expensePostings: [], settlementPostings: [], blockerCount: 0 },
  finalSettlements: {
    approvedPositiveCount: 0, approvedPositiveAmount: 0, paidAmount: 0, outstandingAmount: 0,
    negativeReceivableCount: 0, negativeReceivableAmount: 0,
    financiallyCompletedCount: 0, unresolvedFinancialCount: 0
  },
  blockers: {
    unresolvedPaymentCount: 1, partialPaymentCount: 1, reconciliationPendingCount: 1,
    accountingPendingOrFailedCount: 0, correctionPendingCount: 0
  }
};

describe('PayrollDashboardPageComponent', () => {
  let fixture: ComponentFixture<PayrollDashboardPageComponent>;
  let api: { query: ReturnType<typeof vi.fn> };
  let auth: { hasPermission: ReturnType<typeof vi.fn> };
  let capabilities: { isEnabled: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = { query: vi.fn().mockReturnValue(of(dashboard)) };
    auth = { hasPermission: vi.fn().mockReturnValue(true) };
    capabilities = { isEnabled: vi.fn().mockReturnValue(true) };
    TestBed.configureTestingModule({
      imports: [PayrollDashboardPageComponent],
      providers: [
        provideRouter([]),
        { provide: PayrollDashboardService, useValue: api },
        { provide: AuthService, useValue: auth },
        { provide: HrPayrollCapabilityStore, useValue: capabilities }
      ]
    });
  });

  it('loads and renders persisted Payroll summary figures', () => {
    fixture = TestBed.createComponent(PayrollDashboardPageComponent);
    fixture.detectChanges();

    expect(api.query).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('Payroll dashboard');
    expect(fixture.nativeElement.textContent).toContain('1200');
    expect(fixture.nativeElement.textContent).toContain('700');
    expect(fixture.nativeElement.textContent).toContain('300');
  });

  it('does not request confidential aggregates without salary-view permission', () => {
    auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.salary.view');
    fixture = TestBed.createComponent(PayrollDashboardPageComponent);
    fixture.detectChanges();

    expect(api.query).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('confidential dashboard permissions');
  });

  describe('shared UI kit presentation', () => {
    const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

    function render() {
      fixture = TestBed.createComponent(PayrollDashboardPageComponent);
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    function card(el: HTMLElement, title: string) {
      return Array.from(el.querySelectorAll('app-ui-card'))
        .find(candidate => text(candidate.querySelector('h2')) === title) as HTMLElement;
    }

    it('renders one page-header h1, a main landmark and a filters card', () => {
      const el = render();
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(text(el.querySelector('h1'))).toBe('Payroll dashboard');
      expect(el.querySelector('main')).not.toBeNull();
      expect(text(el.querySelector('app-ui-page-header'))).toContain('Read-only summary of persisted Payroll');
      expect(card(el, 'Filters')).toBeTruthy();
    });

    it('ties every filter label to its control and marks the required dates', () => {
      const el = render();
      const expected: Record<string, string> = {
        From: 'fromDate', To: 'toDate', 'Payroll period ID': 'periodId',
        'Payroll run ID': 'runId', 'Location ID': 'locationId', 'Run type': 'runType'
      };
      for (const [labelText, name] of Object.entries(expected)) {
        const label = Array.from(el.querySelectorAll('label'))
          .find(candidate => text(candidate).startsWith(labelText)) as HTMLLabelElement;
        expect(label, labelText).toBeTruthy();
        expect(el.querySelector(`#${label.htmlFor}`)?.getAttribute('name'), labelText).toBe(name);
      }
      expect(el.querySelector('input[name="fromDate"]')?.getAttribute('aria-required')).toBe('true');
      expect(el.querySelector('input[name="toDate"]')?.getAttribute('aria-required')).toBe('true');
      expect(el.querySelector('input[name="runId"]')?.getAttribute('aria-required')).toBeNull();
    });

    it('keeps the ngModel bindings and queries with the edited filters', async () => {
      const el = render();
      await fixture.whenStable();
      const setValue = (name: string, value: string) => {
        const input = el.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
        input.dispatchEvent(new Event('input'));
      };
      setValue('fromDate', '2026-09-01');
      setValue('toDate', '2026-09-30');
      setValue('periodId', '4');
      setValue('runId', '8');
      setValue('locationId', '2');
      const select = el.querySelector('select[name="runType"]') as HTMLSelectElement;
      select.selectedIndex = 2;
      select.dispatchEvent(new Event('change'));
      api.query.mockClear();

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();

      expect(api.query).toHaveBeenCalledOnce();
      expect(api.query).toHaveBeenCalledWith({
        periodId: 4, runId: 8, locationId: 2, fromDate: '2026-09-01', toDate: '2026-09-30', runType: 'Correction'
      });
    });

    it('shows a loading status and disables Refresh while the request is pending', () => {
      const pending = new Subject<PayrollDashboardSummary>();
      api.query.mockReturnValue(pending);
      const el = render();
      const refresh = el.querySelector('button[type="submit"]') as HTMLButtonElement;
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Loading Payroll dashboard...');
      expect(refresh.disabled).toBe(true);
      expect(refresh.getAttribute('aria-busy')).toBe('true');

      pending.next(dashboard);
      fixture.detectChanges();
      expect(el.querySelector('app-ui-state[role="status"]')).toBeNull();
      expect(refresh.disabled).toBe(false);
    });

    it('renders the totals as labelled raw values', () => {
      const el = render();
      const totals = el.querySelector('section[aria-label="Payroll totals"]') as HTMLElement;
      const pairs = Array.from(totals.querySelectorAll('dl')).map(list =>
        [text(list.querySelector('dt')), text(list.querySelector('dd'))]);
      expect(pairs).toEqual([
        ['Gross earnings', '1200'], ['Deductions', '200'], ['Net payable', '1000'],
        ['Paid', '700'], ['Outstanding', '300'], ['Employees in run', '1']
      ]);
    });

    it('shows payment states in a named, scrollable table with a visible status badge', () => {
      const el = render();
      const states = card(el, 'Payment states');
      const region = states.querySelector('[role="region"]') as HTMLElement;
      expect(region.getAttribute('aria-label')).toBe('Payment states');
      expect(Array.from(states.querySelectorAll('th')).map(header => text(header)))
        .toEqual(['Status', 'Count', 'Outstanding']);
      const cells = Array.from(states.querySelectorAll('tbody tr td')).map(cell => text(cell));
      expect(cells).toEqual(['PartiallyPaid', '1', '300']);
      expect(text(states.querySelector('app-ui-badge'))).toContain('PartiallyPaid');
      expect(states.querySelector('input[type="search"]')).toBeNull();
      expect(states.querySelector('nav[aria-label="Pagination"]')).toBeNull();
      expect(text(states)).not.toContain('No payment data for this run.');
    });

    it('shows reconciliation, settlement and blocker figures with navigation links', () => {
      const el = render();
      const facts = (title: string) => Array.from(card(el, title).querySelectorAll('dl > dt'))
        .map(term => [text(term), text(term.nextElementSibling)]);
      expect(facts('Reconciliation')).toEqual([
        ['Reconciled', '0'], ['Partial', '1'], ['Unreconciled', '0'], ['Exceptions', '0']
      ]);
      expect(facts('Final Settlements')).toEqual([
        ['Approved positive', '0 · 0'], ['Paid', '0'], ['Outstanding', '0'],
        ['Receivables', '0 · 0'], ['Unresolved financial items', '0']
      ]);
      expect(facts('Operational blockers')).toEqual([
        ['Unresolved payments', '1'], ['Partial payments', '1'], ['Pending reconciliation', '1'],
        ['Accounting pending or failed', '0'], ['Correction runs pending', '0']
      ]);
      expect(text(card(el, 'Accounting postings'))).toContain('Accounting is not enabled for this Business.');
      const links = Array.from(el.querySelectorAll('a.ui-btn')).map(link => [text(link), link.getAttribute('href')]);
      expect(links).toEqual([
        ['Open payments', '/payroll/payments'], ['Review payment evidence', '/payroll/payments'],
        ['Open exit cases', '/hr/exits'], ['Open Payroll periods', '/payroll/periods']
      ]);
    });

    it('explains empty scopes with status messages and not-applicable cards', () => {
      api.query.mockReturnValue(of({
        ...dashboard,
        period: { ...dashboard.period, id: 0 },
        run: { ...dashboard.run, id: 0 },
        payments: { ...dashboard.payments, states: [{ status: 'Held', count: 0, amount: 0 }] },
        reconciliation: { ...dashboard.reconciliation, applies: false },
        accounting: { ...dashboard.accounting, enabled: true, blockerCount: 3 }
      }));
      const el = render();
      const statuses = Array.from(el.querySelectorAll('app-ui-alert[role="status"]')).map(alert => text(alert));
      expect(statuses).toEqual([
        'No Payroll period is available in this scope.', 'No Payroll run matches these filters.'
      ]);
      expect(text(card(el, 'Payment states'))).toContain('No payment data for this run.');
      expect(text(card(el, 'Reconciliation'))).toContain('Reconciliation is not applicable for this run.');
      expect(card(el, 'Reconciliation').querySelector('dl')).toBeNull();
      expect(text(card(el, 'Accounting postings'))).toContain('Pending, failed, or conflicting postings');
      expect(text(card(el, 'Accounting postings').querySelector('dd'))).toBe('3');
    });

    it('announces API failures and missing permissions with role="alert" and hides the figures', () => {
      api.query.mockReturnValue(throwError(() => ({ error: { detail: 'Dashboard is unavailable.' } })));
      let el = render();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Dashboard is unavailable.');
      expect(el.querySelector('section[aria-label="Payroll totals"]')).toBeNull();
      fixture.destroy();

      api.query.mockClear();
      auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.dashboard.view');
      el = render();
      expect(text(el.querySelector('[role="alert"]')))
        .toContain('Payroll capability or confidential dashboard permissions are unavailable.');
      expect(api.query).not.toHaveBeenCalled();
      expect(el.querySelector('section[aria-label="Payroll totals"]')).toBeNull();
    });
  });
});
