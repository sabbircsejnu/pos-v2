import { CompanyDateService } from '../../../services/company-date.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HrAttendanceReportsPageComponent } from './hr-attendance-reports-page.component';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { environment } from '../../../../environments/environment';

describe('Phase 2G attendance reports', () => {
  const base = `${environment.apiUrl}/hr/v1/attendance/reports`;
  afterEach(() => TestBed.resetTestingModule());
  async function create(permission = true, attendance = true) {
    await TestBed.configureTestingModule({
      imports: [HrAttendanceReportsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (value: string) =>
          permission && value === 'hr.attendance.report' } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled: true, attendanceEnabled: attendance,
      payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrAttendanceReportsPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('gates requests on capability and dedicated report permission', async () => {
    const { fixture, http } = await create(false);
    http.expectNone(base);
    expect(fixture.nativeElement.textContent).toContain('attendance report permission unavailable');
    http.verify();
  });

  it('queries backend filters and shows one cross-midnight row with correction evidence', async () => {
    const { fixture, page, http } = await create();
    http.expectOne(req => req.url === base).flush({ items: [], page: 1, pageSize: 50, totalCount: 0,
      lateCount: 0, earlyLeaveCount: 0, absentCount: 0, exceptionCount: 0 });
    page.filters = { ...page.filters, from: '2027-01-01', to: '2027-01-02',
      employeeId: 17, locationProjectionId: 10, status: 'Late', exceptionsOnly: true };
    page.search();
    const request = http.expectOne(req => req.url === base);
    expect(request.request.params.get('employeeId')).toBe('17');
    expect(request.request.params.get('locationProjectionId')).toBe('10');
    expect(request.request.params.get('status')).toBe('Late');
    expect(request.request.params.get('exceptionsOnly')).toBe('true');
    request.flush({ items: [{
      id: 5, employeeId: 17, employeeCode: 'E17', employeeName: 'Test Employee',
      workDate: '2027-01-01', locationProjectionId: 10, locationName: 'Outlet',
      shiftId: 2, shiftCode: 'NIGHT', shiftName: 'Night shift', shiftStartTimeLocal: '10:00',
      shiftEndTimeLocal: '02:00', shiftCrossesMidnight: true, dayClassification: 'Holiday',
      holidayEvidence: [{ code: 'H', name: 'Holiday', holidayType: 'Public' }],
      weeklyOffEvidence: { versionNo: 2, days: ['Friday'] },
      firstInAtLocal: '2027-01-01T10:00:00', lastOutAtLocal: '2027-01-02T02:00:00',
      workedMinutes: 960, breakMinutes: 0, lateMinutes: 0, earlyLeaveMinutes: 0,
      status: 'Present', isCorrected: true, processingVersion: 3,
      lastProcessedAtUtc: '2027-01-02T03:00:00Z', correctionCount: 1,
      latestCorrectionSequence: 1, latestCorrectionStatus: 'Approved',
      punches: [{ punchTimeLocal: '2027-01-01T10:00:00', punchTimeUtc: null,
        directionCode: 'IN', sourceType: 'Manual' },
      { punchTimeLocal: '2027-01-02T02:00:00', punchTimeUtc: null,
        directionCode: 'OUT', sourceType: 'Manual' }]
    }], page: 1, pageSize: 50, totalCount: 1, lateCount: 0, earlyLeaveCount: 0,
    absentCount: 0, exceptionCount: 0 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('next day; one WorkDate');
    expect(fixture.nativeElement.textContent).toContain('Corrected');
    expect(fixture.nativeElement.textContent).toContain('Approved');
    expect(fixture.nativeElement.textContent).toContain('Weekly off v2');
    http.verify();
  });

  it('shows backend error and does not hide empty results', async () => {
    const { fixture, http } = await create();
    http.expectOne(req => req.url === base).flush({ detail: 'Location scope denied.' }, { status: 403, statusText: 'Forbidden' });
    expect(fixture.componentInstance.error()).toContain('Location scope denied');
    fixture.componentInstance.error.set(null);
    fixture.componentInstance.report.set({ items: [], page: 1, pageSize: 50, totalCount: 0,
      lateCount: 0, earlyLeaveCount: 0, absentCount: 0, exceptionCount: 0 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No attendance rows match');
    http.verify();
  });

  describe('shared UI kit presentation', () => {
    const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

    const counts = { lateCount: 1, earlyLeaveCount: 2, absentCount: 3, exceptionCount: 4 };

    const row = (overrides: Record<string, unknown> = {}) => ({
      id: 5, employeeId: 17, employeeCode: 'E17', employeeName: 'Test Employee',
      workDate: '2027-01-01', locationProjectionId: 10, locationName: 'Outlet',
      shiftId: 2, shiftCode: 'DAY', shiftName: 'Day shift', shiftStartTimeLocal: '09:00',
      shiftEndTimeLocal: '17:00', shiftCrossesMidnight: false, dayClassification: 'Working',
      holidayEvidence: [], weeklyOffEvidence: null,
      firstInAtLocal: null, lastOutAtLocal: null, workedMinutes: 480, breakMinutes: 30,
      lateMinutes: 5, earlyLeaveMinutes: 0, status: 'Late', isCorrected: false, processingVersion: 2,
      lastProcessedAtUtc: '2027-01-02T03:00:00Z', correctionCount: 0, latestCorrectionSequence: null,
      latestCorrectionStatus: null, punches: [],
      ...overrides
    });

    const pageBody = (items: unknown[], extra: Record<string, unknown> = {}) => ({
      items, page: 1, pageSize: 50, totalCount: items.length, ...counts, ...extra
    });

    async function loaded(items: unknown[] = [row()], extra: Record<string, unknown> = {}) {
      const created = await create();
      created.http.expectOne(req => req.url === base).flush(pageBody(items, extra));
      created.fixture.detectChanges();
      return { ...created, el: created.fixture.nativeElement as HTMLElement };
    }

    it('renders one page-header h1, a main landmark and a filters card', async () => {
      const { el, http } = await loaded();
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(text(el.querySelector('h1'))).toBe('Attendance reports');
      expect(el.querySelector('main')).not.toBeNull();
      expect(text(el.querySelector('app-ui-page-header'))).toContain('WorkDate is the shift start date.');
      expect(text(el.querySelector('app-ui-card h2'))).toBe('Filters');
      http.verify();
    });

    it('ties every filter label to its control, including the wrapped checkbox', async () => {
      const { el, http } = await loaded();
      const expected: Record<string, string> = {
        From: 'from', To: 'to', 'Employee ID': 'employee', 'Location projection ID': 'location', Status: 'status'
      };
      const labels = Array.from(el.querySelectorAll('form label')) as HTMLLabelElement[];
      for (const [labelText, name] of Object.entries(expected)) {
        const label = labels.find(candidate => text(candidate).replace(/ \*.*$/, '') === labelText);
        expect(label, labelText).toBeTruthy();
        expect(el.querySelector(`#${label!.htmlFor}`)?.getAttribute('name'), labelText).toBe(name);
      }
      const exceptions = labels.find(candidate => text(candidate) === 'Exceptions only')!;
      expect(exceptions.control?.getAttribute('name')).toBe('exceptionsOnly');
      expect((exceptions.control as HTMLInputElement).type).toBe('checkbox');
      expect(el.querySelector('input[name="from"]')?.getAttribute('aria-required')).toBe('true');
      http.verify();
    });

    it('keeps the ngModel bindings and requests the edited filters from page 1', async () => {
      const { fixture, page, el, http } = await loaded();
      await fixture.whenStable();
      const setValue = (name: string, value: string) => {
        const input = el.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
        input.dispatchEvent(new Event('input'));
      };
      setValue('from', '2027-01-01');
      setValue('to', '2027-01-02');
      setValue('employee', '17');
      setValue('location', '10');
      const status = el.querySelector('select[name="status"]') as HTMLSelectElement;
      status.value = status.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Late', (status.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Late';
      status.dispatchEvent(new Event('change'));
      const exceptions = el.querySelector('input[name="exceptionsOnly"]') as HTMLInputElement;
      exceptions.click();
      expect(page.filters).toMatchObject({
        from: '2027-01-01', to: '2027-01-02', employeeId: 17, locationProjectionId: 10,
        status: 'Late', exceptionsOnly: true
      });
      page.filters.page = 4;

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      const request = http.expectOne(req => req.url === base);
      expect(request.request.params.get('employeeId')).toBe('17');
      expect(request.request.params.get('locationProjectionId')).toBe('10');
      expect(request.request.params.get('status')).toBe('Late');
      expect(request.request.params.get('exceptionsOnly')).toBe('true');
      expect(request.request.params.get('page')).toBe('1');
      request.flush(pageBody([]));
      http.verify();
    });

    it('shows a loading status and disables Search while the request is pending', async () => {
      const { fixture, http } = await create();
      const el: HTMLElement = fixture.nativeElement;
      const submit = el.querySelector('button[type="submit"]') as HTMLButtonElement;
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Loading attendance report...');
      expect(submit.disabled).toBe(true);
      expect(submit.getAttribute('aria-busy')).toBe('true');
      http.expectOne(req => req.url === base).flush(pageBody([]));
      fixture.detectChanges();
      expect(el.querySelector('app-ui-state[role="status"]')).toBeNull();
      expect(submit.disabled).toBe(false);
      http.verify();
    });

    it('renders evidence in a named table region with the result summary line', async () => {
      const { el, http } = await loaded([
        row({
          shiftCrossesMidnight: true, status: 'Present', isCorrected: true, correctionCount: 2,
          latestCorrectionStatus: 'Approved', dayClassification: 'Holiday',
          holidayEvidence: [{ code: 'H1', name: 'National day', holidayType: 'Public' }],
          weeklyOffEvidence: { versionNo: 3, days: ['Friday', 'Saturday'] },
          punches: [
            { punchTimeLocal: '2027-01-01T09:00:00', punchTimeUtc: null, directionCode: 'IN', sourceType: 'Manual' },
            { punchTimeLocal: '2027-01-01T17:00:00', punchTimeUtc: null, directionCode: null, sourceType: 'Device' }
          ]
        }),
        row({ id: 6, employeeId: 18, employeeCode: 'E18', status: 'Absent', punches: [] })
      ]);
      expect(text(el.querySelector('.ar-count')))
        .toBe('2 matching rows · 4 exceptions · 1 late · 2 early leave · 3 absent');
      const region = el.querySelector('app-ui-data-table [role="region"]') as HTMLElement;
      expect(region.getAttribute('aria-label')).toBe('Attendance report results');
      expect(Array.from(el.querySelectorAll('thead th')).map(header => text(header))).toEqual([
        'Employee / WorkDate', 'Shift / Location', 'Result', 'Calendar evidence', 'Processed evidence', 'Punches'
      ]);
      const rows = Array.from(el.querySelectorAll('tbody tr'));
      const lines = (cell: Element) =>
        Array.from(cell.querySelectorAll('.ar-stack > *')).map(line => text(line)).join(' | ');
      const first = Array.from(rows[0].querySelectorAll('td')).map(cell => lines(cell));
      expect(first[0]).toBe("E17 — Test Employee (17) | 01/01/2027");
      expect(first[1]).toBe('DAY — Day shift | 09:00–17:00 (next day; one WorkDate) | Outlet');
      expect(first[2]).toBe('Present Corrected | Late 5m · Early leave 0m | Worked 480m · Break 30m');
      expect(first[3]).toBe('Holiday | H1 National day | Weekly off v3 (Friday, Saturday)');
      expect(first[4]).toBe('Processing v2 · 02/01/2027 03:00:00 | 2 correction(s), latest Approved');
      expect(first[5]).toBe('IN 2027-01-01T09:00:00 (Manual) | Punch 2027-01-01T17:00:00 (Device)');
      const second = Array.from(rows[1].querySelectorAll('td')).map(cell => lines(cell));
      expect(second[1]).not.toContain('next day');
      expect(second[2]).not.toContain('Corrected');
      expect(second[5]).toBe('No linked punch');
      expect(el.querySelector('app-ui-data-table input[type="search"]')).toBeNull();
      expect(el.querySelector('app-ui-data-table nav[aria-label="Pagination"]')).toBeNull();
      http.verify();
    });

    it('shows the status as visible badge text with a distinct variant per outcome', async () => {
      const { el, page, http } = await loaded([
        row({ id: 1, status: 'Present' }), row({ id: 2, status: 'Late' }),
        row({ id: 3, status: 'Absent' }), row({ id: 4, status: 'WeeklyOff' }), row({ id: 5, status: 'Other' })
      ]);
      const badges = Array.from(el.querySelectorAll('tbody tr td:nth-child(3) app-ui-badge'))
        .map(badge => [text(badge), badge.getAttribute('data-variant')]);
      expect(badges).toEqual([
        ['Present', 'success'], ['Late', 'warning'], ['Absent', 'danger'], ['WeeklyOff', 'info'], ['Other', 'neutral']
      ]);
      expect(page.statusVariant('MissingPunch')).toBe('danger');
      expect(page.statusVariant('HalfDay')).toBe('warning');
      expect(page.statusVariant('Holiday')).toBe('info');
      http.verify();
    });

    it('shows the empty state without pagination', async () => {
      const { el, http } = await loaded([]);
      expect(text(el.querySelector('app-ui-data-table app-ui-state')))
        .toContain('No attendance rows match these filters.');
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

      button('Next').click();
      const next = http.expectOne(req => req.url === base);
      expect(next.request.params.get('page')).toBe('2');
      next.flush(pageBody([row({ employeeCode: 'E51' })], { totalCount: 120, page: 2 }));
      fixture.detectChanges();
      expect(text(nav())).toContain('Page 2 of 3');
      expect(button('Previous').disabled).toBe(false);
      expect(text(el.querySelector('tbody td'))).toContain('E51');

      button('Next').click();
      http.expectOne(req => req.url === base).flush(pageBody([row()], { totalCount: 120, page: 3 }));
      fixture.detectChanges();
      expect(button('Next').disabled).toBe(true);
      http.verify();
    });

    it('announces backend errors and missing permission with role="alert"', async () => {
      const { fixture, el, http } = await loaded();
      fixture.componentInstance.search();
      http.expectOne(req => req.url === base)
        .flush({ detail: 'Location scope denied.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(text(el.querySelector('app-ui-alert[role="alert"]'))).toBe('Location scope denied.');
      http.verify();

      TestBed.resetTestingModule();
      const denied = await create(false);
      denied.fixture.detectChanges();
      expect(text(denied.fixture.nativeElement.querySelector('[role="alert"]')))
        .toBe('HR/Attendance capability or attendance report permission unavailable.');
      denied.http.expectNone(base);
      denied.http.verify();
    });
  });
});
