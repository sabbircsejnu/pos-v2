import { CompanyDateService } from '../../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrDashboardPageComponent } from './hr-dashboard-page.component';

describe('HR dashboard', () => {
  const url = `${environment.apiUrl}/hr/v1/dashboard`;
  let permissions = new Set(['hr.dashboard.view']);

  afterEach(() => TestBed.resetTestingModule());

  async function create(hrEnabled = true) {
    await TestBed.configureTestingModule({
      imports: [HrDashboardPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.has(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled, attendanceEnabled: false,
      payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrDashboardPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  beforeEach(() => { permissions = new Set(['hr.dashboard.view']); });

  it('loads and renders safe aggregate metrics', async () => {
    const { fixture, page, http } = await create();
    const request = http.expectOne(request => request.url === url && request.method === 'GET');
    expect(request.request.params.has('from')).toBe(true);
    expect(request.request.params.has('to')).toBe(true);
    request.flush({
      from: '2026-10-01', to: '2026-10-31', totalEmployees: 2,
      employmentStatuses: [{ label: 'Active', count: 2, minutes: null }],
      employmentTypes: [], locationTypes: [], locations: [], orgUnits: [], positions: [],
      joiners: 1, exits: 0, attendance: [], leaveRequests: [], overtime: [],
      documentsExpiring: 0, openExitCases: 0, pendingMandatoryClearanceItems: 0
    });
    fixture.detectChanges();
    expect(page.summary()?.totalEmployees).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('Active: 2');
    expect(Object.keys(page.summary()!).some(key => /salary|bank|payment/i.test(key))).toBe(false);
    http.verify();
  });

  it('does not load without HR capability or dashboard permission', async () => {
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

  it('reloads using changed date and location filters and surfaces errors', async () => {
    const { page, http } = await create();
    http.expectOne(request => request.url === url).flush({
      from: '2026-10-01', to: '2026-10-31', totalEmployees: 0,
      employmentStatuses: [], employmentTypes: [], locationTypes: [], locations: [], orgUnits: [], positions: [],
      joiners: 0, exits: 0, attendance: [], leaveRequests: [], overtime: [],
      documentsExpiring: 0, openExitCases: 0, pendingMandatoryClearanceItems: 0
    });
    page.from = '2026-09-01';
    page.to = '2026-09-30';
    page.locationProjectionId = 12;
    page.locationType = 'Outlet';
    page.search();
    const request = http.expectOne(candidate => candidate.url === url);
    expect(request.request.params.get('from')).toBe('2026-09-01');
    expect(request.request.params.get('locationProjectionId')).toBe('12');
    expect(request.request.params.get('locationType')).toBe('Outlet');
    request.flush({ detail: 'Dashboard is unavailable.' }, { status: 403, statusText: 'Forbidden' });
    expect(page.error()).toBe('Dashboard is unavailable.');
    http.verify();
  });

  const summaryBody = (overrides: Record<string, unknown> = {}) => ({
    from: '2026-10-01', to: '2026-10-31', totalEmployees: 5,
    employmentStatuses: [
      { label: 'Active', count: 4, minutes: null },
      { label: 'Probation', count: 1, minutes: null }
    ],
    employmentTypes: [], locationTypes: [], locations: [], orgUnits: [], positions: [],
    joiners: 3, exits: 1, attendance: [{ label: 'Worked', count: 2, minutes: 45 }],
    leaveRequests: [], overtime: [],
    documentsExpiring: 2, openExitCases: 1, pendingMandatoryClearanceItems: 4,
    ...overrides
  });

  const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

  describe('shared UI kit presentation', () => {
    it('renders one page-header h1, a main landmark and a filters card', async () => {
      const { fixture, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelectorAll('h1').length).toBe(1);
      expect(text(el.querySelector('h1'))).toBe('HR dashboard');
      expect(el.querySelector('main')).not.toBeNull();
      expect(el.querySelector('app-ui-page-header')?.contains(el.querySelector('h1'))).toBe(true);
      expect(text(el.querySelector('app-ui-page-header'))).toContain('No salary, bank, payment, or individual document data is shown.');
      expect(text(el.querySelector('app-ui-card'))).toContain('Filters');
      http.verify();
    });

    it('ties every filter label to its control and marks required dates', async () => {
      const { fixture, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      const expected: Record<string, string> = {
        From: 'from', To: 'to', 'Location projection ID': 'location', 'Location type': 'locationType'
      };
      for (const [labelText, name] of Object.entries(expected)) {
        const label = Array.from(el.querySelectorAll('label'))
          .find(candidate => text(candidate).startsWith(labelText)) as HTMLLabelElement;
        expect(label, labelText).toBeTruthy();
        const control = el.querySelector(`#${label.htmlFor}`);
        expect(control?.getAttribute('name'), labelText).toBe(name);
        expect(control?.classList.contains('ui-control')).toBe(true);
      }
      expect(el.querySelector('input[name="from"]')?.getAttribute('aria-required')).toBe('true');
      expect(el.querySelector('input[name="to"]')?.getAttribute('aria-required')).toBe('true');
      expect(el.querySelector('input[name="location"]')?.getAttribute('aria-required')).toBeNull();
      http.verify();
    });

    it('keeps the ngModel bindings and submits the edited filters', async () => {
      const { fixture, page, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      await fixture.whenStable();
      const el: HTMLElement = fixture.nativeElement;
      const from = el.querySelector('input[name="from"]') as HTMLInputElement;
      expect(from.value).toBe(TestBed.inject(CompanyDateService).format(page.from));

      const setValue = (name: string, value: string) => {
        const input = el.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
        input.dispatchEvent(new Event('input'));
      };
      setValue('from', '2026-09-01');
      setValue('to', '2026-09-30');
      setValue('location', '12');
      const select = el.querySelector('select[name="locationType"]') as HTMLSelectElement;
      select.selectedIndex = 2;
      select.dispatchEvent(new Event('change'));
      expect(page.from).toBe('2026-09-01');
      expect(page.to).toBe('2026-09-30');
      expect(page.locationProjectionId).toBe(12);
      expect(page.locationType).toBe('Warehouse');

      (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
      const request = http.expectOne(candidate => candidate.url === url);
      expect(request.request.params.get('from')).toBe('2026-09-01');
      expect(request.request.params.get('to')).toBe('2026-09-30');
      expect(request.request.params.get('locationProjectionId')).toBe('12');
      expect(request.request.params.get('locationType')).toBe('Warehouse');
      request.flush(summaryBody());
      http.verify();
    });

    it('shows a loading status and disables Refresh while the request is in flight', async () => {
      const { fixture, http } = await create();
      const el: HTMLElement = fixture.nativeElement;
      const refresh = el.querySelector('button[type="submit"]') as HTMLButtonElement;
      expect(text(el.querySelector('app-ui-state[role="status"]'))).toContain('Loading HR dashboard...');
      expect(refresh.disabled).toBe(true);
      expect(refresh.getAttribute('aria-busy')).toBe('true');
      expect(text(refresh)).toBe('Refresh');

      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      expect(el.querySelector('app-ui-state[role="status"]')).toBeNull();
      expect(refresh.disabled).toBe(false);
      expect(refresh.getAttribute('aria-busy')).toBeNull();
      http.verify();
    });

    it('renders the overview indicators as labelled values with their navigation links', async () => {
      const { fixture, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      const overview = el.querySelector('section[aria-label="HR overview"]') as HTMLElement;
      const pairs = Array.from(overview.querySelectorAll('dl')).map(list =>
        [text(list.querySelector('dt')), text(list.querySelector('dd'))]);
      expect(pairs).toEqual([
        ['Total employees', '5'], ['Joiners', '3'], ['Exits', '1'],
        ['Documents expiring', '2'], ['Open exit cases', '1'], ['Pending mandatory clearance', '4']
      ]);
      const links = Array.from(overview.querySelectorAll('a')).map(link => [text(link), link.getAttribute('href')]);
      expect(links).toEqual([['View employees', '/hr/employees'], ['View exits', '/hr/exits']]);
      expect(overview.querySelectorAll('a.ui-btn').length).toBe(2);
      http.verify();
    });

    it('lists section metrics with visible text, minutes badges, empty states and related links', async () => {
      const { fixture, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      const titles = Array.from(el.querySelectorAll('h2')).map(heading => text(heading));
      expect(titles).toEqual(expect.arrayContaining([
        'Employment status', 'Processed attendance', 'Leave requests', 'Overtime'
      ]));
      const status = Array.from(el.querySelectorAll('app-ui-card'))
        .find(card => text(card.querySelector('h2')) === 'Employment status') as HTMLElement;
      expect(Array.from(status.querySelectorAll('li')).map(item => text(item)))
        .toEqual(['Active: 4', 'Probation: 1']);
      const attendance = Array.from(el.querySelectorAll('app-ui-card'))
        .find(card => text(card.querySelector('h2')) === 'Processed attendance') as HTMLElement;
      expect(text(attendance.querySelector('li'))).toBe('Worked: 2 45 minutes');
      expect(text(attendance.querySelector('app-ui-badge'))).toContain('45 minutes');
      expect(attendance.querySelector('a')?.getAttribute('href')).toBe('/hr/attendance/reports');
      expect(text(attendance.querySelector('a'))).toBe('Open related view');
      // 9 sections, 2 with data
      expect(el.querySelectorAll('app-ui-state').length).toBe(7);
      expect(text(el)).toContain('No data for this period.');
      http.verify();
    });

    it('shows an informational status when no employees are in scope', async () => {
      const { fixture, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody({
        totalEmployees: 0, employmentStatuses: [], attendance: []
      }));
      fixture.detectChanges();
      const alert = fixture.nativeElement.querySelector('app-ui-alert[role="status"]');
      expect(text(alert)).toContain('No employees are in this scope.');
      http.verify();
    });

    it('announces API failures and permission problems with role="alert"', async () => {
      const { fixture, page, http } = await create();
      http.expectOne(request => request.url === url).flush(summaryBody());
      page.search();
      http.expectOne(request => request.url === url)
        .flush({ detail: 'Dashboard is unavailable.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();
      expect(text(fixture.nativeElement.querySelector('app-ui-alert[role="alert"]'))).toContain('Dashboard is unavailable.');
      http.verify();

      TestBed.resetTestingModule();
      permissions = new Set();
      const denied = await create();
      denied.fixture.detectChanges();
      expect(text(denied.fixture.nativeElement.querySelector('[role="alert"]')))
        .toContain('HR capability or dashboard permission unavailable.');
      expect(denied.fixture.nativeElement.querySelector('section[aria-label="HR overview"]')).toBeNull();
      denied.http.expectNone(url);
      denied.http.verify();
    });
  });
});
