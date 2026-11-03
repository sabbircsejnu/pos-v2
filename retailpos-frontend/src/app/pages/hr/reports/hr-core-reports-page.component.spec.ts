import { CompanyDateService } from '../../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrCoreReportsPageComponent } from './hr-core-reports-page.component';

describe('Core HR reports', () => {
  const url = `${environment.apiUrl}/hr/v1/reports/core`;
  let permissions = new Set(['hr.report.view']);

  afterEach(() => TestBed.resetTestingModule());

  async function create(hrEnabled = true) {
    await TestBed.configureTestingModule({
      imports: [HrCoreReportsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.has(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled, attendanceEnabled: false,
      payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrCoreReportsPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  beforeEach(() => { permissions = new Set(['hr.report.view']); });

  it('loads a filtered employee report and shows safe report fields', async () => {
    const { fixture, http, page } = await create();
    const request = http.expectOne(request => request.url === url && request.method === 'GET');
    expect(request.request.params.get('kind')).toBe('EmployeeRoster');
    request.flush({
      kind: 'EmployeeRoster', page: 1, pageSize: 50, totalCount: 1,
      items: [{
        employeeId: 1, employeeCode: 'E-1', employeeName: 'Employee', category: 'Employee',
        effectiveDate: '2026-10-10', status: 'Active', locationName: 'Outlet',
        detail: 'POS-1 — Cashier', metric: null
      }]
    });
    fixture.detectChanges();
    expect(page.report()?.totalCount).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Employee');
    expect(fixture.nativeElement.textContent).toContain('POS-1');
    http.verify();
  });

  it('blocks requests when HR capability or report permission is missing', async () => {
    const disabled = await create(false);
    disabled.http.expectNone(url);
    expect(disabled.page.error()).toContain('unavailable');
    disabled.http.verify();

    TestBed.resetTestingModule();
    permissions = new Set();
    const denied = await create();
    denied.http.expectNone(url);
    expect(denied.page.error()).toContain('unavailable');
    denied.http.verify();
  });

  it('shows report errors and empty results explicitly', async () => {
    const { fixture, page, http } = await create();
    http.expectOne(request => request.url === url).flush({
      kind: 'EmployeeRoster', page: 1, pageSize: 50, totalCount: 0, items: []
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No records match');

    page.search();
    http.expectOne(request => request.url === url)
      .flush({ detail: 'Report is unavailable.' }, { status: 403, statusText: 'Forbidden' });
    expect(page.error()).toBe('Report is unavailable.');
    http.verify();
  });

  describe('shared UI kit presentation', () => {
    const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

    const row = (overrides: Record<string, unknown> = {}) => ({
      employeeId: 1, employeeCode: 'E-1', employeeName: 'Employee One', category: 'Roster',
      effectiveDate: '2026-10-10', status: 'Active', locationName: 'Outlet', detail: null, metric: null,
      employmentType: null, department: null, position: null, locationType: null, effectiveTo: null,
      isCurrent: null, actorUserId: null, occurredAtUtc: null, quantity: null, completionMode: null,
      payrollEnabled: null, measures: null,
      ...overrides
    });

    const pageBody = (items: unknown[], extra: Record<string, unknown> = {}) => ({
      kind: 'EmployeeRoster', page: 1, pageSize: 50, totalCount: items.length, items, ...extra
    });

    async function loaded(items: unknown[] = [row()], extra: Record<string, unknown> = {}) {
      const created = await create();
      created.http.expectOne(request => request.url === url).flush(pageBody(items, extra));
      created.fixture.detectChanges();
      return { ...created, el: created.fixture.nativeElement as HTMLElement };
    }

    it('renders one page-header h1, a main landmark and a filters card', async () => {
      const { el, http } = await loaded();
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(text(el.querySelector('h1'))).toBe('Core HR reports');
      expect(el.querySelector('main')).not.toBeNull();
      expect(text(el.querySelector('app-ui-page-header'))).toContain('Personal identifiers, contact details');
      expect(text(el.querySelector('app-ui-card h2'))).toBe('Filters');
      http.verify();
    });

    it('ties every filter label to its control', async () => {
      const { el, http } = await loaded();
      const expected: Record<string, string> = {
        Report: 'kind', From: 'from', To: 'to', 'Employee ID': 'employeeId',
        'Location projection ID': 'locationId', Status: 'status', 'As of date': 'asOfDate',
        'Employment type ID': 'employmentTypeId', 'Employment status': 'employmentStatus',
        'Position ID': 'positionId', 'Department ID': 'departmentId', 'Sort by': 'sortBy', Order: 'descending'
      };
      const labels = Array.from(el.querySelectorAll('form label')) as HTMLLabelElement[];
      expect(labels.length).toBe(Object.keys(expected).length);
      for (const [labelText, name] of Object.entries(expected)) {
        const label = labels.find(candidate => text(candidate).replace(/ \*.*$/, '').replace(/ \(required\)$/, '') === labelText);
        expect(label, labelText).toBeTruthy();
        expect(el.querySelector(`#${label!.htmlFor}`)?.getAttribute('name'), labelText).toBe(name);
      }
      expect(el.querySelector('input[name="from"]')?.getAttribute('aria-required')).toBe('true');
      expect(el.querySelector('input[name="employeeId"]')?.getAttribute('aria-required')).toBeNull();
      http.verify();
    });

    it('keeps the ngModel bindings and requests with the edited filters', async () => {
      const { fixture, page, el, http } = await loaded();
      await fixture.whenStable();
      const setValue = (name: string, value: string) => {
        const input = el.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
        input.dispatchEvent(new Event('input'));
      };
      const choose = (name: string, index: number) => {
        const select = el.querySelector(`select[name="${name}"]`) as HTMLSelectElement;
        select.selectedIndex = index;
        select.dispatchEvent(new Event('change'));
      };
      choose('kind', 8);
      setValue('from', '2026-09-01');
      setValue('to', '2026-09-30');
      setValue('employeeId', '7');
      setValue('locationId', '12');
      setValue('status', 'Open');
      setValue('asOfDate', '2026-09-15');
      setValue('employmentTypeId', '3');
      setValue('employmentStatus', 'Confirmed');
      setValue('positionId', '4');
      setValue('departmentId', '5');
      choose('sortBy', 3);
      choose('descending', 1);
      expect(page.filters.kind).toBe('LeaveSummary');
      expect(page.filters.sortBy).toBe('effectiveDate');
      expect(page.filters.descending).toBe(false);

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      const request = http.expectOne(candidate => candidate.url === url);
      const expectedParams: Record<string, string> = {
        kind: 'LeaveSummary', from: '2026-09-01', to: '2026-09-30', employeeId: '7',
        locationProjectionId: '12', status: 'Open', asOfDate: '2026-09-15', employmentTypeId: '3',
        employmentStatus: 'Confirmed', positionId: '4', departmentId: '5', sortBy: 'effectiveDate',
        descending: 'false', page: '1', pageSize: '50'
      };
      for (const [key, value] of Object.entries(expectedParams)) {
        expect(request.request.params.get(key), key).toBe(value);
      }
      request.flush(pageBody([]));
      http.verify();
    });

    it('shows a loading status and disables Search while the request is pending', async () => {
      const { fixture, http } = await create();
      const el: HTMLElement = fixture.nativeElement;
      const submit = el.querySelector('button[type="submit"]') as HTMLButtonElement;
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Loading report...');
      expect(submit.disabled).toBe(true);
      expect(submit.getAttribute('aria-busy')).toBe('true');
      http.expectOne(request => request.url === url).flush(pageBody([]));
      fixture.detectChanges();
      expect(el.querySelector('app-ui-state[role="status"]')).toBeNull();
      expect(submit.disabled).toBe(false);
      http.verify();
    });

    it('renders results in a named table region with headers, safe fields and a status badge', async () => {
      const { el, http } = await loaded([
        row({ detail: 'POS-1 — Cashier' }),
        row({ employeeId: 2, employeeCode: 'E-2', employeeName: 'Employee Two', effectiveDate: null,
          status: 'Probation', locationName: null })
      ], { totalCount: 2 });
      const region = el.querySelector('app-ui-data-table [role="region"]') as HTMLElement;
      expect(region.getAttribute('aria-label')).toBe('Core HR report results');
      expect(Array.from(el.querySelectorAll('thead th')).map(header => text(header)))
        .toEqual(['Employee', 'Report item', 'Date', 'Status', 'Location', 'Details']);
      const rows = Array.from(el.querySelectorAll('tbody tr'));
      expect(rows.length).toBe(2);
      const first = Array.from(rows[0].querySelectorAll('td')).map(cell => text(cell));
      expect(first.slice(0, 5)).toEqual(['E-1 — Employee One (1)', 'Roster', "10/10/2026", 'Active', 'Outlet']);
      expect(first[5]).toContain('POS-1 — Cashier');
      const second = Array.from(rows[1].querySelectorAll('td')).map(cell => text(cell));
      expect(second.slice(0, 5)).toEqual(['E-2 — Employee Two (2)', 'Roster', '—', 'Probation', '—']);
      expect(text(rows[1].querySelector('app-ui-badge'))).toContain('Probation');
      expect(text(el.querySelector('.cr-count'))).toBe('2 matching records');
      expect(el.querySelector('app-ui-data-table input[type="search"]')).toBeNull();
      expect(el.querySelector('app-ui-data-table nav[aria-label="Pagination"]')).toBeNull();
      http.verify();
    });

    it('composes the details cell from every optional field and falls back to a dash', async () => {
      const { el, http } = await loaded([
        row({
          employmentType: 'Full time', department: 'Sales', position: 'Cashier', locationType: 'Outlet',
          payrollEnabled: false, effectiveTo: '2026-12-31', quantity: 2, isCurrent: true, actorUserId: 9,
          occurredAtUtc: '2026-10-01T00:00:00Z', metric: 3,
          measures: {
            scheduledDays: 20, presentDays: null, absentDays: null, lateDays: null, holidayDays: null,
            weeklyOffDays: null, approvedLeaveUnits: null, workedMinutes: 90, approvedOvertimeEntries: null,
            opening: null, accrued: null, used: null, restored: null, adjusted: null, carriedForward: null,
            expired: null, balance: 4, mandatoryClearanceItems: null, completedMandatoryClearanceItems: null
          }
        }),
        row({ employeeId: 2, employeeCode: 'E-2', isCurrent: null })
      ]);
      const details = Array.from(el.querySelectorAll('tbody tr')).map(item => text(item.querySelectorAll('td')[5]));
      for (const expected of [
        'Full time Sales Cashier Outlet', ' · Payroll disabled', ' · through 31/12/2026', ' · quantity 2',
        ' · Current', ' · Actor 9', ` · ${TestBed.inject(CompanyDateService).format('2026-10-01T00:00:00Z', true)}`,
        ' · Scheduled days: 20 · Worked minutes: 90 · Balance: 4', ' · 3 pending mandatory items'
      ]) {
        expect(details[0], expected).toContain(expected);
      }
      expect(details[1]).toBe('—');
      http.verify();
    });

    it('shows the empty state without export buttons or pagination', async () => {
      const { el, http } = await loaded([]);
      expect(text(el.querySelector('app-ui-data-table app-ui-state'))).toContain('No records match these filters.');
      expect(text(el.querySelector('.cr-count'))).toBe('0 matching records');
      expect(Array.from(el.querySelectorAll('button')).map(button => text(button))).not.toContain('Export CSV');
      expect(el.querySelector('nav[aria-label="Report pagination"]')).toBeNull();
      http.verify();
    });

    it('pages through the server results with Previous / Next and a page indicator', async () => {
      const { fixture, el, http } = await loaded([row()], { totalCount: 120 });
      const nav = () => el.querySelector('nav[aria-label="Report pagination"]') as HTMLElement;
      const button = (label: string) =>
        Array.from(nav().querySelectorAll('button')).find(candidate => text(candidate) === label) as HTMLButtonElement;
      expect(text(nav())).toContain('Page 1 of 3');
      expect(button('Previous').disabled).toBe(true);
      expect(button('Next').disabled).toBe(false);

      button('Next').click();
      const next = http.expectOne(request => request.url === url);
      expect(next.request.params.get('page')).toBe('2');
      next.flush(pageBody([row({ employeeCode: 'E-51' })], { totalCount: 120, page: 2 }));
      fixture.detectChanges();
      expect(text(nav())).toContain('Page 2 of 3');
      expect(button('Previous').disabled).toBe(false);
      expect(text(el.querySelector('tbody tr td'))).toContain('E-51');

      button('Next').click();
      http.expectOne(request => request.url === url)
        .flush(pageBody([row()], { totalCount: 120, page: 3 }));
      fixture.detectChanges();
      expect(button('Next').disabled).toBe(true);
      button('Previous').click();
      expect(http.expectOne(request => request.url === url).request.params.get('page')).toBe('2');
      http.verify();
    });

    it('exports through named buttons, blocks them while preparing and reports failures', async () => {
      const { fixture, el, http } = await loaded();
      const createObjectUrl = vi.fn(() => 'blob:test');
      const revokeObjectUrl = vi.fn();
      Object.assign(URL, { createObjectURL: createObjectUrl, revokeObjectURL: revokeObjectUrl });
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      const buttons = () => Array.from(el.querySelectorAll('.cr-actions button')) as HTMLButtonElement[];
      expect(buttons().map(button => text(button))).toEqual(['Export CSV', 'Export XLSX']);

      buttons()[0].click();
      fixture.detectChanges();
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Preparing export...');
      expect(buttons().every(button => button.disabled)).toBe(true);
      http.expectOne(request => request.url === `${url}/export/csv`).flush(new Blob(['x']));
      fixture.detectChanges();
      expect(createObjectUrl).toHaveBeenCalledOnce();
      expect(click).toHaveBeenCalledOnce();
      expect(buttons().every(button => !button.disabled)).toBe(true);

      buttons()[1].click();
      http.expectOne(request => request.url === `${url}/export/xlsx`)
        .flush(new Blob(), { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Unable to export the authorized HR report.');
      click.mockRestore();
      http.verify();
    });

    it('announces failures and missing permissions with role="alert"', async () => {
      const { fixture, page, el, http } = await loaded();
      page.search();
      http.expectOne(request => request.url === url)
        .flush({ detail: 'Report is unavailable.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Report is unavailable.');
      http.verify();

      TestBed.resetTestingModule();
      permissions = new Set();
      const denied = await create();
      denied.fixture.detectChanges();
      expect(text(denied.fixture.nativeElement.querySelector('[role="alert"]')))
        .toBe('HR capability or report permission unavailable.');
      denied.http.expectNone(url);
      denied.http.verify();
    });
  });
});
