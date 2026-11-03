import { CompanyDateService } from '../../services/company-date.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../services/hr-payroll-capability.store';
import {
  PayrollAdvancedReportPage,
  PayrollAdvancedReportService
} from '../../services/payroll-advanced-report.service';
import { PayrollAdvancedReportsPageComponent } from './payroll-advanced-reports-page.component';

const report: PayrollAdvancedReportPage = {
  kind: 'PayrollRegister',
  page: 1,
  pageSize: 50,
  totalCount: 1,
  items: [{
    employeeId: 12,
    employeeCode: 'EMP-12',
    employeeName: 'Report Employee',
    periodId: 4,
    periodCode: '2026-10',
    runId: 8,
    runRevision: 2,
    runType: 'Regular',
    originalRunId: null,
    category: 'PayrollRegister',
    componentCode: null,
    componentName: null,
    gross: 1200,
    deductions: 200,
    net: 1000,
    status: 'Approved',
    locationId: 10,
    locationName: 'Main location',
    effectiveDate: '2026-10-01',
    effectiveTo: null,
    salaryVersionId: 3,
    opening: null,
    recovered: null,
    outstanding: null,
    detail: null
  }]
};

describe('PayrollAdvancedReportsPageComponent', () => {
  let fixture: ComponentFixture<PayrollAdvancedReportsPageComponent>;
  let api: { query: ReturnType<typeof vi.fn>; export: ReturnType<typeof vi.fn> };
  let auth: { hasPermission: ReturnType<typeof vi.fn> };
  let capabilities: { isEnabled: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = { query: vi.fn().mockReturnValue(of(report)), export: vi.fn().mockReturnValue(of(new Blob())) };
    auth = { hasPermission: vi.fn().mockReturnValue(true) };
    capabilities = { isEnabled: vi.fn().mockReturnValue(true) };
    TestBed.configureTestingModule({
      imports: [PayrollAdvancedReportsPageComponent],
      providers: [
        { provide: PayrollAdvancedReportService, useValue: api },
        { provide: AuthService, useValue: auth },
        { provide: HrPayrollCapabilityStore, useValue: capabilities }
      ]
    });
  });

  it('loads and displays persisted report rows', () => {
    fixture = TestBed.createComponent(PayrollAdvancedReportsPageComponent);
    fixture.detectChanges();

    expect(api.query).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('Payroll reports');
    expect(fixture.nativeElement.textContent).toContain('EMP-12');
    expect(fixture.nativeElement.textContent).toContain('1000');
  });

  it('does not query confidential reports without salary-view permission', () => {
    auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.salary.view');
    fixture = TestBed.createComponent(PayrollAdvancedReportsPageComponent);
    fixture.detectChanges();

    expect(api.query).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('confidential report permissions');
  });

  it('requires a comparison run for Payroll variance', () => {
    fixture = TestBed.createComponent(PayrollAdvancedReportsPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.filters.kind = 'PayrollVariance';
    fixture.componentInstance.filters.compareRunId = null;
    api.query.mockClear();

    fixture.componentInstance.search();

    expect(api.query).not.toHaveBeenCalled();
    expect(fixture.componentInstance.error()).toContain('comparison run is required');
  });

  it('hides export actions without Payroll export permission', () => {
    auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.export');
    fixture = TestBed.createComponent(PayrollAdvancedReportsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Export CSV');
    expect(fixture.nativeElement.textContent).not.toContain('Export XLSX');
  });

  describe('shared UI kit presentation', () => {
    const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

    function render() {
      fixture = TestBed.createComponent(PayrollAdvancedReportsPageComponent);
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    const choose = (el: HTMLElement, name: string, value: string) => {
      const select = el.querySelector(`select[name="${name}"]`) as HTMLSelectElement;
      select.value = value;
      select.dispatchEvent(new Event('change'));
    };

    const setValue = (el: HTMLElement, name: string, value: string) => {
      const input = el.querySelector(`input[name="${name}"]`) as HTMLInputElement;
      input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
      input.dispatchEvent(new Event('input'));
    };

    const buttonLabels = (el: HTMLElement) => Array.from(el.querySelectorAll('button')).map(button => text(button));

    it('renders one page-header h1, a main landmark and a filters card', () => {
      const el = render();
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(text(el.querySelector('h1'))).toBe('Payroll reports');
      expect(el.querySelector('main')).not.toBeNull();
      expect(text(el.querySelector('app-ui-page-header'))).toContain('Reports use persisted Payroll evidence');
      expect(text(el.querySelector('app-ui-card h2'))).toBe('Filters');
    });

    it('ties every filter label to its control and only shows the comparison run for variance', async () => {
      const el = render();
      const labelFor = (labelText: string) => Array.from(el.querySelectorAll('form label'))
        .find(candidate => text(candidate).replace(/ \*.*$/, '') === labelText) as HTMLLabelElement | undefined;
      const expected: Record<string, string> = {
        Report: 'kind', From: 'from', To: 'to', 'Employee ID': 'employeeId', 'Location ID': 'locationId', 'Run ID': 'runId'
      };
      for (const [labelText, name] of Object.entries(expected)) {
        const label = labelFor(labelText);
        expect(label, labelText).toBeTruthy();
        expect(el.querySelector(`#${label!.htmlFor}`)?.getAttribute('name'), labelText).toBe(name);
      }
      expect(el.querySelector('input[name="from"]')?.getAttribute('aria-required')).toBe('true');
      expect(labelFor('Compare run ID')).toBeUndefined();

      await fixture.whenStable();
      choose(el, 'kind', 'PayrollVariance');
      fixture.detectChanges();
      const compare = labelFor('Compare run ID');
      expect(compare).toBeTruthy();
      const compareInput = el.querySelector(`#${compare!.htmlFor}`) as HTMLInputElement;
      expect(compareInput.name).toBe('compareRunId');
      expect(compareInput.getAttribute('aria-required')).toBe('true');

      await fixture.whenStable();
      setValue(el, 'compareRunId', '5');
      expect(fixture.componentInstance.filters.compareRunId).toBe(5);
      choose(el, 'kind', 'PayrollRegister');
      fixture.detectChanges();
      expect(fixture.componentInstance.filters.compareRunId).toBeNull();
      expect(labelFor('Compare run ID')).toBeUndefined();
    });

    it('keeps the ngModel bindings and queries with the edited filters from page 1', async () => {
      const calls: Record<string, unknown>[] = [];
      api.query.mockImplementation((filters: Record<string, unknown>) => {
        calls.push({ ...filters });
        return of(report);
      });
      const el = render();
      await fixture.whenStable();
      choose(el, 'kind', 'RunStatus');
      setValue(el, 'from', '2026-09-01');
      setValue(el, 'to', '2026-09-30');
      setValue(el, 'employeeId', '12');
      setValue(el, 'locationId', '10');
      setValue(el, 'runId', '8');
      fixture.componentInstance.filters.page = 3;

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();

      expect(calls).toHaveLength(2);
      expect(calls[1]).toEqual({
        kind: 'RunStatus', from: '2026-09-01', to: '2026-09-30', employeeId: 12, locationId: 10,
        runId: 8, compareRunId: null, page: 1, pageSize: 50
      });
    });

    it('blocks a variance search without a comparison run and announces it', async () => {
      const el = render();
      await fixture.whenStable();
      choose(el, 'kind', 'PayrollVariance');
      fixture.detectChanges();
      api.query.mockClear();

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(api.query).not.toHaveBeenCalled();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('A comparison run is required for Payroll variance.');
    });

    it('shows a loading status and disables Search while the request is pending', () => {
      const pending = new Subject<PayrollAdvancedReportPage>();
      api.query.mockReturnValue(pending);
      const el = render();
      const submit = el.querySelector('button[type="submit"]') as HTMLButtonElement;
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Loading Payroll report...');
      expect(submit.disabled).toBe(true);
      expect(submit.getAttribute('aria-busy')).toBe('true');

      pending.next(report);
      fixture.detectChanges();
      expect(el.querySelector('app-ui-state[role="status"]')).toBeNull();
      expect(submit.disabled).toBe(false);
    });

    it('renders rows in a named table region with every column and raw amounts', () => {
      const el = render();
      const region = el.querySelector('app-ui-data-table [role="region"]') as HTMLElement;
      expect(region.getAttribute('aria-label')).toBe('Payroll report results');
      expect(Array.from(el.querySelectorAll('thead th')).map(header => text(header))).toEqual([
        'Employee', 'Period / run', 'Category', 'Component', 'Gross', 'Deductions', 'Net', 'Status',
        'Location', 'Effective', 'Loan recovery', 'Details'
      ]);
      const cells = Array.from(el.querySelectorAll('tbody tr td')).map(cell => text(cell));
      expect(cells).toEqual([
        'EMP-12 — Report Employee (12)', '2026-10 / 8 · rev 2 · Regular', 'PayrollRegister', '—',
        '1200', '200', '1000', 'Approved', 'Main location', "01/10/2026", '—', '—'
      ]);
      expect(text(el.querySelector('tbody app-ui-badge'))).toContain('Approved');
      expect(text(el.querySelector('.pr-count'))).toBe('1 matching records');
      expect(el.querySelector('app-ui-data-table input[type="search"]')).toBeNull();
      expect(el.querySelector('app-ui-data-table nav[aria-label="Pagination"]')).toBeNull();
    });

    it('shows component, effective range, loan recovery and missing identifiers explicitly', () => {
      api.query.mockReturnValue(of({
        ...report,
        items: [{
          ...report.items[0], employeeCode: null, employeeName: null, employeeId: null, runRevision: null,
          runType: null, periodCode: null, periodId: 4, runId: null, componentCode: 'BASIC',
          componentName: 'Basic pay', gross: null, locationName: null, locationId: 10,
          effectiveTo: '2026-12-31', opening: 500, recovered: 100, outstanding: null, detail: 'Note'
        }]
      }));
      const el = render();
      const cells = Array.from(el.querySelectorAll('tbody tr td')).map(cell => text(cell));
      expect(cells).toEqual([
        '— — —', '4 / —', 'PayrollRegister', 'BASIC · Basic pay', '—', '200', '1000', 'Approved', '10',
        "01/10/2026 – 31/12/2026", 'Opening 500 · recovered 100 · outstanding —', 'Note'
      ]);
    });

    it('shows the empty state without export buttons or pagination', () => {
      api.query.mockReturnValue(of({ ...report, items: [], totalCount: 0 }));
      const el = render();
      expect(text(el.querySelector('app-ui-data-table app-ui-state'))).toContain('No records match these filters.');
      expect(buttonLabels(el)).not.toContain('Export CSV');
      expect(el.querySelector('nav[aria-label="Report pagination"]')).toBeNull();
    });

    it('pages with Previous / Next while keeping the page indicator accurate', () => {
      const pageOf = (page: number) => of({ ...report, page, totalCount: 120 });
      api.query.mockReturnValue(pageOf(1));
      const el = render();
      const nav = () => el.querySelector('nav[aria-label="Report pagination"]') as HTMLElement;
      const button = (label: string) =>
        Array.from(nav().querySelectorAll('button')).find(candidate => text(candidate) === label) as HTMLButtonElement;
      expect(text(nav())).toContain('Page 1 of 3');
      expect(button('Previous').disabled).toBe(true);

      api.query.mockReturnValue(pageOf(2));
      button('Next').click();
      fixture.detectChanges();
      expect(fixture.componentInstance.filters.page).toBe(2);
      expect(text(nav())).toContain('Page 2 of 3');
      expect(button('Previous').disabled).toBe(false);

      api.query.mockReturnValue(pageOf(3));
      button('Next').click();
      fixture.detectChanges();
      expect(button('Next').disabled).toBe(true);

      api.query.mockReturnValue(pageOf(2));
      button('Previous').click();
      fixture.detectChanges();
      expect(fixture.componentInstance.filters.page).toBe(2);
    });

    it('offers named export buttons only with export permission and downloads through the service', () => {
      const createObjectUrl = vi.fn(() => 'blob:test');
      Object.assign(URL, { createObjectURL: createObjectUrl, revokeObjectURL: vi.fn() });
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      const el = render();
      const actions = el.querySelector('.pr-actions') as HTMLElement;
      expect(buttonLabels(actions)).toEqual(['Export CSV', 'Export XLSX']);

      (actions.querySelectorAll('button')[1] as HTMLButtonElement).click();
      expect(api.export).toHaveBeenCalledWith(fixture.componentInstance.filters, 'xlsx');
      expect(createObjectUrl).toHaveBeenCalledOnce();
      expect(click).toHaveBeenCalledOnce();
      click.mockRestore();
    });

    it('hides the export actions container without export permission', () => {
      auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.export');
      const el = render();
      expect(el.querySelector('.pr-actions')).toBeNull();
      expect(text(el.querySelector('.pr-count'))).toBe('1 matching records');
    });

    it('announces export failures, query failures and missing permissions with role="alert"', () => {
      api.export.mockReturnValue(throwError(() => ({ error: {} })));
      let el = render();
      (el.querySelector('.pr-actions button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Unable to export the authorized Payroll report.');
      fixture.destroy();

      api.query.mockReturnValue(throwError(() => ({ error: { detail: 'Report is unavailable.' } })));
      el = render();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Report is unavailable.');
      fixture.destroy();

      api.query.mockClear();
      auth.hasPermission.mockImplementation((permission: string) => permission !== 'payroll.report.view');
      el = render();
      expect(text(el.querySelector('[role="alert"]')))
        .toBe('Payroll capability or confidential report permissions are unavailable.');
      expect(api.query).not.toHaveBeenCalled();
      expect(el.querySelector('app-ui-data-table')).toBeNull();
    });
  });
});
