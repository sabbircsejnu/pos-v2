import { CompanyDateService } from '../../../services/company-date.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { HrCalendarsPageComponent } from './hr-calendars-page.component';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { environment } from '../../../../environments/environment';

describe('Phase 2A calendar pages', () => {
  const base = `${environment.apiUrl}/hr/v1`;
  afterEach(() => TestBed.resetTestingModule());
  async function create(mode: 'shifts' | 'holidays' | 'weekly-off', manage = true, attendance = true) {
    const resource = mode === 'shifts' ? 'shift' : mode === 'holidays' ? 'holiday' : 'weekly_off';
    const permissions = [`hr.${resource}.view`, ...(manage ? [`hr.${resource}.manage`] : [])];
    await TestBed.configureTestingModule({
      imports: [HrCalendarsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { calendarMode: mode } } } },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: attendance,
      payrollEnabled: false, deviceIntegrationEnabled: false });
    const fixture = TestBed.createComponent(HrCalendarsPageComponent); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    if (attendance) http.expectOne(`${base}/${mode === 'holidays' ? 'holiday-calendars' : mode === 'weekly-off' ? 'weekly-off-policies' : 'shifts'}`).flush([]);
    return { fixture, http, page: fixture.componentInstance };
  }
  it('requires Attendance and HR capability before loading', async () => {
    const { fixture, http } = await create('shifts', true, false);
    http.expectNone(`${base}/shifts`); expect(fixture.nativeElement.textContent).toContain('capability or view permission');
    expect(fixture.nativeElement.textContent).not.toContain('Save shift'); http.verify();
  });
  it('hides writes for view-only callers and rejects programmatic write', async () => {
    const { fixture, page, http } = await create('shifts', false);
    expect(fixture.nativeElement.textContent).not.toContain('Save shift');
    page.saveShift(); http.expectNone(r => r.method === 'POST'); expect(page.error()).toContain('Manage access');
    http.verify();
  });
  it('saves a generic cross-midnight shift with break rules at the exact API URL', async () => {
    const { page, http } = await create('shifts');
    page.shift = { ...page.shift, code: 'EXT', name: 'Extended', startTimeLocal: '10:00', endTimeLocal: '02:00',
      crossesMidnight: true, minimumWorkMinutes: 900, breaks: [{ sequence: 1, durationMinutes: 60, isPaid: false }] };
    page.saveShift();
    const req = http.expectOne(`${base}/shifts`);
    expect(req.request.body.crossesMidnight).toBe(true); expect(req.request.body.breaks[0].isPaid).toBe(false);
    req.flush({ id: 1, data: page.shift, version: 1, isActive: true }); http.expectOne(`${base}/shifts`).flush([]); http.verify();
  });
  it('updates and deactivates using expected shift concurrency versions', async () => {
    const { page, http } = await create('shifts');
    const row = { id: 3, data: page.shift, version: 7, isActive: true };
    page.editShift(row); page.saveShift();
    const req = http.expectOne(`${base}/shifts/3`); expect(req.request.method).toBe('PUT');
    expect(req.request.body.expectedVersion).toBe(7);
    req.flush({ detail: 'Configuration changed since it was read.' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toContain('changed since');
    page.disableShift(row);
    const deactivate = http.expectOne(`${base}/shifts/3/deactivate`); expect(deactivate.request.body.expectedVersion).toBe(7);
    deactivate.flush(row); http.expectOne(`${base}/shifts`).flush([]); http.verify();
  });
  it('versions weekly-off policy rather than overwriting historical days', async () => {
    const { page, http, fixture } = await create('weekly-off');
    const previous = { id: 5, data: { scope: page.scope, effectiveFrom: '2026-11-01', effectiveTo: null, days: [5] },
      version: 2, versionNo: 1, isActive: true };
    page.editWeekly(previous); page.effectiveFrom = '2026-12-01'; page.toggleDay(1, true); page.saveScoped();
    const req = http.expectOne(`${base}/weekly-off-policies/5/versions`);
    expect(req.request.body.expectedVersion).toBe(2); expect(req.request.body.data.days).toEqual([5, 1]);
    req.flush(previous); http.expectOne(`${base}/weekly-off-policies`).flush([previous]);
    fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain("01/11/2026"); http.verify();
  });
  it('requires explicit future closure date and retains version in request', async () => {
    const { page, http } = await create('weekly-off');
    const row = { id: 5, data: { scope: page.scope, effectiveFrom: '2026-11-01', effectiveTo: null, days: [5] },
      version: 2, versionNo: 1, isActive: true };
    page.disableWeekly(row); http.expectNone(`${base}/weekly-off-policies/5/deactivate`);
    page.closeDate = '2026-12-31'; page.disableWeekly(row);
    const req = http.expectOne(`${base}/weekly-off-policies/5/deactivate`);
    expect(req.request.body).toEqual({ expectedVersion: 2, effectiveTo: '2026-12-31' });
    req.flush(row); http.expectOne(`${base}/weekly-off-policies`).flush([]); http.verify();
  });
  it('creates specific projection calendar and future holiday with visible errors', async () => {
    const { page, http } = await create('holidays');
    page.scope = { scopeType: 'Location', locationType: null, operationalLocationProjectionId: 10 };
    page.calendar.name = 'Outlet'; page.effectiveFrom = '2026-11-01'; page.saveScoped();
    const req = http.expectOne(`${base}/holiday-calendars`);
    expect(req.request.body.scope.operationalLocationProjectionId).toBe(10);
    const row = { id: 7, data: req.request.body, isActive: true, version: 1 };
    req.flush(row); http.expectOne(`${base}/holiday-calendars`).flush([row]);
    page.selectCalendar(row); http.expectOne(`${base}/holiday-calendars/7/holidays`).flush([]);
    page.holiday = { holidayDate: '2026-11-02', code: 'H', name: 'Public', holidayType: 'Public', isPaid: true, notes: null };
    page.saveHoliday();
    http.expectOne(`${base}/holiday-calendars/7/holidays`).flush({ detail: 'Holiday date is invalid.' }, { status: 400, statusText: 'Bad request' });
    expect(page.error()).toContain('date is invalid'); http.verify();
  });
});

describe('Calendar pages (UI kit)', () => {
  const base = `${environment.apiUrl}/hr/v1`;
  afterEach(() => TestBed.resetTestingModule());
  type Mode = 'shifts' | 'holidays' | 'weekly-off';
  const scope = { scopeType: 'Company', locationType: null, operationalLocationProjectionId: null };
  const shiftRow = (id: number, over: Record<string, unknown> = {}, isActive = true) => ({ id, version: 4, isActive,
    data: { code: 'DAY', name: 'Day shift', startTimeLocal: '09:00', endTimeLocal: '17:00', crossesMidnight: false,
      minimumWorkMinutes: 420, maximumAllowedWorkMinutes: null, allowedEarlyPunchMinutes: 0, allowedLatePunchMinutes: 0,
      minimumRestBeforeNextShiftMinutes: null, graceInMinutes: 0, graceOutMinutes: 0, overtimeEligible: false, breaks: [], ...over } });
  const weeklyRow = (id: number, isActive = true, days = [5]) => ({ id, version: 2, versionNo: 1, isActive,
    data: { scope, effectiveFrom: '2026-11-01', effectiveTo: null, days } });
  const calendarRow = (id: number, name: string, isActive = true) => ({ id, version: 1, isActive,
    data: { name, scope, effectiveFrom: '2026-11-01', effectiveTo: null } });
  const holidayRow = (id: number, calendarId: number, isActive = true) => ({ id, calendarId, version: 2, isActive,
    data: { holidayDate: '2026-11-02', code: 'H', name: 'Public', holidayType: 'Public', isPaid: true, notes: null } });
  const outlet = { id: 10, businessId: 1, sourceSystem: 'Retail', sourceEntityType: 'Outlet', sourceEntityId: '1', name: 'Main outlet',
    address: 'x', sourceVersion: 1, isSourceActive: true, isSourceDeleted: false, syncStatus: 'Current' };

  async function open(mode: Mode, opts: { manage?: boolean; classify?: boolean; rows?: unknown[]; attendance?: boolean } = {}) {
    const manage = opts.manage ?? true;
    const attendance = opts.attendance ?? true;
    const resource = mode === 'shifts' ? 'shift' : mode === 'holidays' ? 'holiday' : 'weekly_off';
    const permissions = [`hr.${resource}.view`, ...(manage ? [`hr.${resource}.manage`] : []),
      ...(opts.classify ? ['hr.holiday.view', 'hr.weekly_off.view', 'hr.location.view'] : [])];
    await TestBed.configureTestingModule({
      imports: [HrCalendarsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { calendarMode: mode } } } },
        { provide: AuthService, useValue: { hasPermission: (p: string) => permissions.includes(p) } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: attendance,
      payrollEnabled: false, deviceIntegrationEnabled: false });
    const fixture = TestBed.createComponent(HrCalendarsPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    const listUrl = `${base}/${mode === 'holidays' ? 'holiday-calendars' : mode === 'weekly-off' ? 'weekly-off-policies' : 'shifts'}`;
    const flushList = async (rows: unknown[] = opts.rows ?? []) => { http.expectOne(listUrl).flush(rows); await settle(); };
    return { fixture, http, el, settle, flushList, listUrl, page: fixture.componentInstance };
  }

  const bodyRows = (root: ParentNode) => Array.from(root.querySelectorAll('tbody tr'));
  const cellTexts = (row: Element) => Array.from(row.querySelectorAll('td')).map(td => td.textContent?.replace(/\s+/g, ' ').trim());
  const buttonByText = (el: ParentNode, text: string) =>
    Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  const byAria = (el: ParentNode, label: string) => el.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement | null;
  const control = (el: ParentNode, name: string) => el.querySelector(`[name="${name}"]`) as HTMLInputElement & HTMLSelectElement;
  function controlByLabel(el: ParentNode, text: string) {
    const label = Array.from(el.querySelectorAll('label.ui-field__label')).find(l => l.textContent?.includes(text));
    return el.querySelector(`#${label?.getAttribute('for')}`) as HTMLInputElement & HTMLSelectElement;
  }
  async function type(settle: () => Promise<void>, input: HTMLInputElement, value: string) {
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value; input.dispatchEvent(new Event('input')); await settle();
  }

  describe('shifts', () => {
    it('renders the page header, landmark, breadcrumb and description once', async () => {
      const { el, flushList } = await open('shifts');
      await flushList();
      expect(el.querySelector('main')).not.toBeNull();
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(el.querySelector('h1')?.textContent).toContain('Shift management');
      expect(el.querySelector('nav[aria-label="Breadcrumb"] [aria-current="page"]')?.textContent).toContain('Shift management');
      expect(el.textContent).toContain('Calendar changes are future-only (UTC).');
    });

    it('ties every form label to a control and uses checkbox labels for flags', async () => {
      const { el, flushList } = await open('shifts');
      await flushList();
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Create shift');
      const labels = Array.from(el.querySelectorAll('form label.ui-field__label'));
      expect(labels.length).toBe(11);
      for (const label of labels) expect(el.querySelector(`#${label.getAttribute('for')}`)).not.toBeNull();
      expect(control(el, 'code').getAttribute('aria-required')).toBe('true');
      expect(control(el, 'max').getAttribute('aria-required')).toBeNull();
      const flags = Array.from(el.querySelectorAll('label.ui-check')).map(l => l.textContent?.trim());
      expect(flags).toEqual(['Crosses midnight (WorkDate = start date)', 'Overtime eligible (no rates)']);
      expect(el.querySelector('fieldset legend')?.textContent).toContain('Ordered duration-based breaks');
    });

    it('lists shifts in a named table with status badge and version', async () => {
      const { el, flushList } = await open('shifts');
      await flushList([shiftRow(1, { crossesMidnight: true, endTimeLocal: '02:00', maximumAllowedWorkMinutes: 600 }),
        shiftRow(2, { code: 'OLD', name: 'Old' }, false)]);
      expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Shifts');
      expect(Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim()))
        .toEqual(['Shift', 'Local window', 'Minimum / maximum', 'Status', 'Version', 'Actions']);
      const rows = bodyRows(el);
      expect(cellTexts(rows[0]).slice(0, 5)).toEqual(['DAY - Day shift', '09:00 - 02:00 (next day)', '420 / 600', 'Active', '4']);
      expect(cellTexts(rows[1])[2]).toBe('420 / unspecified');
      expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Inactive');
    });

    it('offers named Edit and Deactivate actions only to managers for active shifts', async () => {
      const { el, flushList } = await open('shifts');
      await flushList([shiftRow(1), shiftRow(2, { code: 'OLD', name: 'Old' }, false)]);
      const rows = bodyRows(el);
      expect(byAria(rows[0], 'Edit shift DAY - Day shift')).not.toBeNull();
      expect(byAria(rows[0], 'Deactivate shift DAY - Day shift')).not.toBeNull();
      expect(rows[1].querySelectorAll('button').length).toBe(0);
    });

    it('hides the form and row actions for view-only callers', async () => {
      const { el, flushList } = await open('shifts', { manage: false });
      await flushList([shiftRow(1)]);
      expect(el.querySelector('form')).toBeNull();
      expect(buttonByText(el, 'Edit')).toBeUndefined();
      expect(el.querySelector('tbody')?.textContent).toContain('DAY - Day shift');
    });

    it('edits a row through the form and sends the expected version', async () => {
      const { el, http, settle, flushList } = await open('shifts');
      await flushList([shiftRow(1)]);
      byAria(el, 'Edit shift DAY - Day shift')!.click();
      await settle();
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Edit shift');
      expect(control(el, 'code').value).toBe('DAY');
      expect(control(el, 'min').value).toBe('420');
      buttonByText(el, 'Save shift')!.click();
      const request = http.expectOne(`${base}/shifts/1`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body.expectedVersion).toBe(4);
      request.flush(shiftRow(1)); http.expectOne(`${base}/shifts`).flush([]);
      http.verify();
    });

    it('deactivates a shift from its row action with the expected version', async () => {
      const { el, http, flushList } = await open('shifts');
      await flushList([shiftRow(1)]);
      byAria(el, 'Deactivate shift DAY - Day shift')!.click();
      const request = http.expectOne(`${base}/shifts/1/deactivate`);
      expect(request.request.body).toEqual({ expectedVersion: 4 });
      request.flush(shiftRow(1)); http.expectOne(`${base}/shifts`).flush([]);
    });

    it('adds and removes ordered breaks with named controls', async () => {
      const { el, settle, flushList, page } = await open('shifts');
      await flushList();
      buttonByText(el, 'Add break')!.click();
      await settle();
      const minutes = () => el.querySelector('.cal-break input[type="number"]') as HTMLInputElement | null;
      expect(minutes()?.value).toBe('30');
      expect(page.shift.breaks.length).toBe(1);
      byAria(el, 'Remove break 1')!.click();
      await settle();
      expect(minutes()).toBeNull();
      expect(page.shift.breaks.length).toBe(0);
    });

    it('submits the typed values and shows a conflict as an alert', async () => {
      const { el, http, settle, flushList } = await open('shifts');
      await flushList();
      await type(settle, control(el, 'code'), 'NEW');
      await type(settle, control(el, 'name'), 'New shift');
      buttonByText(el, 'Save shift')!.click();
      const request = http.expectOne(`${base}/shifts`);
      expect(request.request.body.code).toBe('NEW');
      expect(request.request.body.name).toBe('New shift');
      request.flush({ detail: 'Shift code already exists.' }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Shift code already exists.');
    });

    it('shows the capability message as an alert and no table rows when attendance is disabled', async () => {
      const { el, http, settle } = await open('shifts', { attendance: false });
      await settle();
      http.expectNone(`${base}/shifts`);
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('HR/Attendance capability or view permission is unavailable.');
      expect(el.querySelector('form')).toBeNull();
    });

    it('shows loading, then the empty state', async () => {
      const { el, settle, flushList } = await open('shifts');
      await settle();
      expect(el.querySelector('[role="status"]')?.textContent).toContain('Loading / saving...');
      await flushList([]);
      expect(el.textContent).toContain('No shifts found.');
    });

    it('searches and sorts the loaded shifts', async () => {
      const { el, settle, flushList } = await open('shifts');
      await flushList([shiftRow(1, { code: 'ZED', name: 'Zed' }), shiftRow(2, { code: 'ALP', name: 'Alpha' })]);
      const header = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Shift'))!;
      header.querySelector('button')!.click();
      await settle();
      expect(header.getAttribute('aria-sort')).toBe('ascending');
      expect(cellTexts(bodyRows(el)[0])[0]).toContain('ALP');
      const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
      input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('zed', (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'zed'; input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      await settle();
      expect(bodyRows(el).length).toBe(1);
      expect(cellTexts(bodyRows(el)[0])[0]).toContain('ZED');
    });
  });

  describe('weekly-off policies', () => {
    it('renders scope, days, period, version and state badges', async () => {
      const { el, flushList } = await open('weekly-off');
      await flushList([weeklyRow(5), weeklyRow(6, false, [0, 6])]);
      expect(el.querySelector('h1')?.textContent).toContain('Weekly-off policies');
      expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Weekly-off policies');
      const rows = bodyRows(el);
      expect(cellTexts(rows[0]).slice(0, 5)).toEqual(['Company', 'Friday', '01/11/2026 - open', '1', 'Open']);
      expect(cellTexts(rows[1])[1]).toBe('Sunday, Saturday');
      expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Closed (history retained)');
      expect(rows[1].querySelectorAll('button').length).toBe(0);
    });

    it('starts a new version from the row, locks the scope and keeps the version request', async () => {
      const { el, http, settle, flushList } = await open('weekly-off');
      await flushList([weeklyRow(5)]);
      byAria(el, 'New version of Company')!.click();
      await settle();
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('New policy version');
      const day = (name: string) => Array.from(el.querySelectorAll('fieldset label.ui-check'))
        .find(l => l.textContent?.trim() === name)!.querySelector('input') as HTMLInputElement;
      expect(control(el, 'scope').disabled).toBe(true);
      expect(day('Friday').checked).toBe(true);
      expect(day('Monday').checked).toBe(false);
      day('Monday').click();
      await settle();
      await type(settle, control(el, 'from'), '2026-12-01');
      buttonByText(el, 'Save new version')!.click();
      const request = http.expectOne(`${base}/weekly-off-policies/5/versions`);
      expect(request.request.body.expectedVersion).toBe(2);
      expect(request.request.body.data.days).toEqual([5, 1]);
      request.flush(weeklyRow(5)); http.expectOne(`${base}/weekly-off-policies`).flush([]);
    });

    it('asks for a closure date through an alert and then closes at that date', async () => {
      const { el, http, settle, flushList } = await open('weekly-off');
      await flushList([weeklyRow(5)]);
      byAria(el, 'Close Company at date')!.click();
      await settle();
      http.expectNone(`${base}/weekly-off-policies/5/deactivate`);
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Select a future closure date.');
      await type(settle, controlByLabel(el, 'Closure effective end'), '2026-12-31');
      byAria(el, 'Close Company at date')!.click();
      const request = http.expectOne(`${base}/weekly-off-policies/5/deactivate`);
      expect(request.request.body).toEqual({ expectedVersion: 2, effectiveTo: '2026-12-31' });
      request.flush(weeklyRow(5)); http.expectOne(`${base}/weekly-off-policies`).flush([]);
    });

    it('labels the weekly-off day checkboxes and the creation form for managers only', async () => {
      const { el, flushList } = await open('weekly-off');
      await flushList();
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Create weekly-off policy');
      const days = Array.from(el.querySelectorAll('fieldset label.ui-check')).map(l => l.textContent?.trim());
      expect(days).toEqual(['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']);
      expect(buttonByText(el, 'Save configuration')).toBeDefined();
    });

    it('hides forms, closure date and row actions for view-only callers', async () => {
      const { el, flushList } = await open('weekly-off', { manage: false });
      await flushList([weeklyRow(5)]);
      expect(el.querySelector('form')).toBeNull();
      expect(el.textContent).not.toContain('Closure effective end');
      expect(buttonByText(el, 'New version')).toBeUndefined();
      expect(bodyRows(el).length).toBe(1);
    });
  });

  describe('holiday calendars', () => {
    it('lists calendars with scope, period, version and state', async () => {
      const { el, flushList } = await open('holidays');
      await flushList([calendarRow(7, 'Outlet'), calendarRow(8, 'Old', false)]);
      expect(el.querySelector('h1')?.textContent).toContain('Holiday calendars');
      expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Holiday calendars');
      const rows = bodyRows(el);
      expect(cellTexts(rows[0]).slice(0, 5)).toEqual(['Outlet', 'Company', '01/11/2026 - open', '1', 'Open']);
      expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Closed');
      expect(byAria(rows[1], 'View holidays of Old')).not.toBeNull();
      expect(byAria(rows[1], 'Edit future calendar Old')).toBeNull();
    });

    it('opens the holidays of a calendar with a holiday form for managers and row actions', async () => {
      const { el, http, settle, flushList } = await open('holidays');
      await flushList([calendarRow(7, 'Outlet')]);
      byAria(el, 'View holidays of Outlet')!.click();
      http.expectOne(`${base}/holiday-calendars/7/holidays`).flush([holidayRow(11, 7)]);
      await settle();
      const heading = Array.from(el.querySelectorAll('app-ui-card h2')).find(h => h.textContent?.includes('Holidays: Outlet'));
      expect(heading).toBeDefined();
      expect(buttonByText(el, 'Save holiday')).toBeDefined();
      const holidays = el.querySelector('[role="region"][aria-label="Holidays"]')!;
      expect(cellTexts(bodyRows(holidays)[0]).slice(0, 5)).toEqual(["02/11/2026", 'H - Public', 'Public / Paid', 'Active', '2']);

      byAria(el, 'Deactivate holiday H - Public')!.click();
      const request = http.expectOne(`${base}/holiday-calendars/7/holidays/11/deactivate`);
      expect(request.request.body).toEqual({ expectedVersion: 2 });
      request.flush(holidayRow(11, 7)); http.expectOne(`${base}/holiday-calendars`).flush([calendarRow(7, 'Outlet')]);
      http.expectOne(`${base}/holiday-calendars/7/holidays`).flush([]);
    });

    it('hides the holiday form and actions for view-only callers but still shows the holidays', async () => {
      const { el, http, settle, flushList } = await open('holidays', { manage: false });
      await flushList([calendarRow(7, 'Outlet')]);
      expect(byAria(el, 'Edit future calendar Outlet')).toBeNull();
      byAria(el, 'View holidays of Outlet')!.click();
      http.expectOne(`${base}/holiday-calendars/7/holidays`).flush([holidayRow(11, 7)]);
      await settle();
      expect(el.querySelector('form')).toBeNull();
      expect(buttonByText(el, 'Deactivate')).toBeUndefined();
      expect(el.querySelector('[role="region"][aria-label="Holidays"]')?.textContent).toContain('H - Public');
    });

    it('does not offer a holiday form for a closed calendar', async () => {
      const { el, http, settle, flushList } = await open('holidays');
      await flushList([calendarRow(8, 'Old', false)]);
      byAria(el, 'View holidays of Old')!.click();
      http.expectOne(`${base}/holiday-calendars/8/holidays`).flush([]);
      await settle();
      expect(buttonByText(el, 'Save holiday')).toBeUndefined();
      expect(el.textContent).toContain('No holidays found.');
    });

    it('edits a calendar from its row and shows the manager forms with labelled fields', async () => {
      const { el, settle, flushList } = await open('holidays');
      await flushList([calendarRow(7, 'Outlet')]);
      expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Holiday calendar');
      expect(controlByLabel(el, 'Calendar name')).not.toBeNull();
      byAria(el, 'Edit future calendar Outlet')!.click();
      await settle();
      expect(control(el, 'calendarName').value).toBe('Outlet');
      expect(control(el, 'scope').disabled).toBe(true);
    });
  });

  describe('effective day classification', () => {
    async function openWithLocations() {
      const ctx = await open('holidays', { classify: true });
      await ctx.flushList([]);
      ctx.http.expectOne(`${base}/operational-locations`).flush([outlet]);
      await ctx.settle();
      return ctx;
    }

    it('is a labelled card shown only with both view permissions', async () => {
      const { el } = await openWithLocations();
      const card = Array.from(el.querySelectorAll('app-ui-card')).find(c => c.querySelector('h2')?.textContent?.includes('Effective day classification'));
      expect(card).toBeDefined();
      expect(controlByLabel(card!, 'Date')).not.toBeNull();
      expect(controlByLabel(card!, 'Location').options.length).toBe(2);
    });

    it('is absent without the classification permissions', async () => {
      const { el, flushList } = await open('holidays');
      await flushList([]);
      expect(el.textContent).not.toContain('Effective day classification');
    });

    it('shows the existing validation alert, then the resolved classification text', async () => {
      const { el, http, settle } = await openWithLocations();
      buttonByText(el, 'Resolve')!.click();
      await settle();
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Select a date and location with holiday and weekly-off view access.');

      await type(settle, controlByLabel(el, 'Date'), '2026-11-02');
      const select = controlByLabel(el, 'Location');
      select.selectedIndex = 1; select.dispatchEvent(new Event('change')); await settle();
      buttonByText(el, 'Resolve')!.click();
      const request = http.expectOne(r => r.url === `${base}/day-classification`);
      expect(request.request.params.get('workDate')).toBe('2026-11-02');
      expect(request.request.params.get('locationId')).toBe('10');
      request.flush({ workDate: '2026-11-02', classification: 'Holiday', source: 'Calendar', isHoliday: true, isWeeklyOff: false,
        holidays: [holidayRow(11, 7)], weeklyOffPolicy: null });
      await settle();
      const text = el.querySelector('app-ui-alert[role="status"]')?.textContent?.replace(/\s+/g, ' ');
      expect(text).toContain('02/11/2026: Holiday (Calendar).');
      expect(text).toContain('Holiday: true; Weekly off: false; policy version: none.');
      expect(text).toContain('1 holiday evidence row(s). No benefits are calculated.');
    });
  });
});
