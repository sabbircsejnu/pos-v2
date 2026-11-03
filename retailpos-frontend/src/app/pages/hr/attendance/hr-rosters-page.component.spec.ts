import { CompanyDateService } from '../../../services/company-date.service';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { HrRostersPageComponent } from './hr-rosters-page.component';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { RosterRow } from '../../../services/hr-roster.service';
import { environment } from '../../../../environments/environment';

describe('Phase 2B roster page', () => {
  const base = `${environment.apiUrl}/hr/v1/rosters`;
  afterEach(() => TestBed.resetTestingModule());
  async function create(manage = true, enabled = true) {
    await TestBed.configureTestingModule({ imports: [HrRostersPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => p === 'hr.roster.view' || manage && p === 'hr.roster.manage' } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: enabled,
      payrollEnabled: false, deviceIntegrationEnabled: false });
    const fixture = TestBed.createComponent(HrRostersPageComponent); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    if (enabled) http.expectOne(r => r.url === base && r.method === 'GET').flush([]);
    return { fixture, page: fixture.componentInstance, http };
  }
  it('gates capability and programmatic writes', async () => {
    const { page, http, fixture } = await create(true, false);
    page.save(); http.expectNone(base); expect(fixture.nativeElement.textContent).not.toContain('Save draft'); http.verify();
  });
  it('hides writes for view-only callers', async () => {
    const { page, http, fixture } = await create(false);
    page.bulk(); expect(page.error()).toContain('manage access'); expect(fixture.nativeElement.textContent).not.toContain('Save draft'); http.verify();
  });
  it('creates one WorkDate draft and displays next-day evidence', async () => {
    const { page, http } = await create();
    page.data = { ...page.data, employeeId: 1, shiftId: 2, operationalLocationProjectionId: 10, workDate: '2027-01-01', workKind: 'Extended' };
    page.save(); const request = http.expectOne(base); expect(request.request.body.workDate).toBe('2027-01-01');
    expect(request.request.body.workKind).toBe('Extended'); request.flush({ id: 1 }); http.expectOne(r => r.url === base).flush([]); http.verify();
  });
  it('publishes with expected version and surfaces stale errors', async () => {
    const { page, http } = await create();
    const row: RosterRow = { id: 7, businessId: 1, data: page.data, status: 'Draft', version: 3,
      publishedAtUtc: null, publishedShift: null, publishedLocation: null, publishedClassification: null };
    page.transition(row, 'publish'); const request = http.expectOne(`${base}/7/publish`);
    expect(request.request.body.expectedVersion).toBe(3);
    request.flush({ detail: 'Roster changed since it was read.' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toContain('changed since'); http.verify();
  });
  it('requires distinct bulk employee IDs and sends atomic draft rows', async () => {
    const { page, http } = await create();
    page.bulkEmployees = '1,1'; page.bulk(); http.expectNone(r => r.method === 'POST');
    page.bulkEmployees = '1,2'; page.bulk(); const request = http.expectOne(`${base}/bulk`);
    expect(request.request.body.rows.map((r: { employeeId: number }) => r.employeeId)).toEqual([1, 2]);
    request.flush([]); http.expectOne(r => r.url === base).flush([]); http.verify();
  });
});

describe('Roster page (UI kit)', () => {
  const base = `${environment.apiUrl}/hr/v1/rosters`;
  afterEach(() => TestBed.resetTestingModule());
  const future = '2999-01-01';
  const row = (id: number, over: Partial<RosterRow> = {}, workDate = future): RosterRow => ({ id, businessId: 1,
    data: { employeeId: 5, shiftId: 2, operationalLocationProjectionId: 10, workDate, source: 'Manual', workKind: 'Normal',
      dayClassification: 'NormalWorkingDay' }, status: 'Draft', version: 3, publishedAtUtc: null, publishedShift: null,
    publishedLocation: null, publishedClassification: null, ...over });

  async function open(opts: { manage?: boolean; view?: boolean; enabled?: boolean } = {}) {
    const manage = opts.manage ?? true;
    const view = opts.view ?? true;
    await TestBed.configureTestingModule({ imports: [HrRostersPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: { hasPermission: (p: string) => view && p === 'hr.roster.view' || manage && p === 'hr.roster.manage' } }]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({ businessId: 1, hrEnabled: true, attendanceEnabled: opts.enabled ?? true,
      payrollEnabled: false, deviceIntegrationEnabled: false });
    const fixture = TestBed.createComponent(HrRostersPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };
    const flushList = async (rows: RosterRow[] = []) => {
      http.expectOne(r => r.url === base && r.method === 'GET').flush(rows); await settle();
    };
    return { fixture, http, el, settle, flushList, page: fixture.componentInstance };
  }

  const bodyRows = (root: ParentNode) => Array.from(root.querySelectorAll('tbody tr'));
  const buttonByText = (el: ParentNode, text: string) =>
    Array.from(el.querySelectorAll('button')).find(b => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
  const control = (el: ParentNode, name: string) => el.querySelector(`[name="${name}"]`) as HTMLInputElement & HTMLSelectElement;
  async function type(settle: () => Promise<void>, input: HTMLInputElement, value: string) {
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value; input.dispatchEvent(new Event('input')); await settle();
  }

  it('renders a single h1, landmark, breadcrumb, explanation and named table region', async () => {
    const { el, flushList } = await open();
    await flushList([row(1)]);
    expect(el.querySelector('main')).not.toBeNull();
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Employee rosters');
    expect(el.textContent).toContain('WorkDate is the shift START date, including overnight shifts.');
    expect(el.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe('/dashboard');
    expect(el.querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Employee rosters');
    expect(Array.from(el.querySelectorAll('thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Employee / WorkDate', 'Shift / Location', 'Status', 'Version', 'Published (UTC)', 'Frozen context', 'Actions']);
  });

  it('labels the period filter and keeps the default period bound to the loaded range', async () => {
    const { el, http, settle, flushList, page } = await open();
    await flushList();
    expect(control(el, 'from').value).toBe(TestBed.inject(CompanyDateService).format(page.from));
    expect(control(el, 'to').value).toBe(TestBed.inject(CompanyDateService).format(page.to));
    for (const label of Array.from(el.querySelectorAll('label.ui-field__label'))) {
      expect(el.querySelector(`#${label.getAttribute('for')}`)).not.toBeNull();
    }
    await type(settle, control(el, 'from'), '2027-01-01');
    await type(settle, control(el, 'to'), '2027-01-31');
    buttonByText(el, 'Load')!.click();
    const request = http.expectOne(r => r.url === base && r.method === 'GET');
    expect(request.request.params.get('from')).toBe('2027-01-01');
    expect(request.request.params.get('to')).toBe('2027-01-31');
    request.flush([]);
  });

  it('shows rosters with status badges, version, published text and frozen evidence', async () => {
    const { el, flushList } = await open();
    await flushList([
      row(1),
      row(2, { status: 'Published', version: 5, publishedAtUtc: '2026-10-01T00:00:00Z',
        publishedShift: { startTimeLocal: '22:00', endTimeLocal: '06:00', crossesMidnight: true } as RosterRow['publishedShift'],
        publishedLocation: { projectionId: 10, name: 'Main outlet', sourceEntityType: 'Outlet' },
        publishedClassification: { workDate: future, classification: 'Holiday', source: 'Calendar', isHoliday: true, isWeeklyOff: false,
          holidays: [], weeklyOffPolicy: { id: 1 } as never } })
    ]);
    const rows = bodyRows(el);
    const texts = (r: Element) => Array.from(r.querySelectorAll('td')).map(td => td.textContent?.replace(/\s+/g, ' ').trim());
    expect(texts(rows[0]).slice(0, 5)).toEqual([`5 / ${TestBed.inject(CompanyDateService).format(future)} / Normal`, '2 / 10', 'Draft', '3', 'Not published']);
    expect(rows[0].querySelector('app-ui-badge')?.textContent?.trim()).toBe('Draft');
    expect(texts(rows[1])[2]).toBe('Published');
    expect(texts(rows[1])[4]).toBe(TestBed.inject(CompanyDateService).format('2026-10-01T00:00:00Z', true));
    const frozen = texts(rows[1])[5];
    expect(frozen).toContain('22:00 - 06:00 (next day)');
    expect(frozen).toContain('Main outlet Holiday');
    expect(frozen).toContain('Holiday evidence retained');
    expect(frozen).toContain('Weekly-off policy evidence retained');
  });

  it('shows named row actions according to status, date and permission', async () => {
    const { el, flushList } = await open();
    await flushList([row(1), row(2, { status: 'Published' }), row(3, { status: 'Cancelled' }), row(4, {}, '2000-01-01')]);
    const names = (i: number) => Array.from(bodyRows(el)[i].querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
    const displayDate = TestBed.inject(CompanyDateService).format(future);
    expect(names(0)).toEqual(['Edit roster 5 / ' + displayDate + ' / Normal', 'Publish roster 5 / ' + displayDate + ' / Normal',
      'Cancel future roster 5 / ' + displayDate + ' / Normal']);
    expect(names(1)).toEqual(['Cancel future roster 5 / ' + displayDate + ' / Normal']);
    expect(names(2)).toEqual([]);
    expect(names(3).length).toBe(2);
    expect(Array.from(bodyRows(el)[3].querySelectorAll('button')).map(b => b.textContent?.trim())).toEqual(['Edit', 'Publish']);
  });

  it('hides the forms and every row action for view-only callers', async () => {
    const { el, flushList } = await open({ manage: false });
    await flushList([row(1)]);
    expect(el.querySelector('form[aria-label="Roster draft"]')).toBeNull();
    expect(el.textContent).not.toContain('Bulk draft assignment');
    expect(bodyRows(el)[0].querySelectorAll('button').length).toBe(0);
  });

  it('edits a draft from its row action, then publishes with the expected version', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList([row(1)]);
    (bodyRows(el)[0].querySelector('button[aria-label^="Edit roster"]') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('app-ui-card h2')?.textContent).toContain('Roster period');
    expect(Array.from(el.querySelectorAll('app-ui-card h2')).some(h => h.textContent?.includes('Edit draft'))).toBe(true);
    expect(control(el, 'employee').value).toBe('5');
    (bodyRows(el)[0].querySelector('button[aria-label^="Publish roster"]') as HTMLButtonElement).click();
    const request = http.expectOne(`${base}/1/publish`);
    expect(request.request.body.expectedVersion).toBe(3);
    request.flush({});
    http.expectOne(r => r.url === base && r.method === 'GET').flush([]);
  });

  it('cancels a future roster from the row action with the expected version', async () => {
    const { el, http, flushList } = await open();
    await flushList([row(1, { status: 'Published' })]);
    (bodyRows(el)[0].querySelector('button[aria-label^="Cancel future roster"]') as HTMLButtonElement).click();
    const request = http.expectOne(`${base}/1/cancel`);
    expect(request.request.body.expectedVersion).toBe(3);
    request.flush({});
    http.expectOne(r => r.url === base && r.method === 'GET').flush([]);
  });

  it('creates a draft from the labelled form and shows API errors as an alert', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList();
    const labels = Array.from(el.querySelectorAll('form[aria-label="Roster draft"] label.ui-field__label'));
    expect(labels.length).toBe(6);
    for (const label of labels) expect(el.querySelector(`#${label.getAttribute('for')}`)).not.toBeNull();
    await type(settle, control(el, 'employee'), '9');
    await type(settle, control(el, 'shift'), '2');
    await type(settle, control(el, 'location'), '10');
    buttonByText(el, 'Save draft')!.click();
    const request = http.expectOne(r => r.url === base && r.method === 'POST');
    expect(request.request.body).toMatchObject({ employeeId: 9, shiftId: 2, operationalLocationProjectionId: 10, workKind: 'Normal' });
    request.flush({ detail: 'Shift is inactive.' }, { status: 400, statusText: 'Bad' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Shift is inactive.');
  });

  it('keeps the bulk-assignment input named and shows its validation as an alert', async () => {
    const { el, http, settle, flushList } = await open();
    await flushList();
    const input = el.querySelector('input[aria-label="Bulk employee IDs"]') as HTMLInputElement;
    expect(input).not.toBeNull();
    await type(settle, input, '1,1');
    buttonByText(el, 'Create drafts')!.click();
    await settle();
    http.expectNone(r => r.method === 'POST');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Provide 1-100 unique positive employee IDs.');
  });

  it('shows loading while empty, then the empty state', async () => {
    const { el, settle, flushList } = await open();
    await settle();
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Loading / saving...');
    await flushList([]);
    expect(el.textContent).toContain('No rosters found.');
  });

  it('shows the capability message as an alert and offers no write forms', async () => {
    const { el, settle } = await open({ enabled: false });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('HR/Attendance capability or roster view permission unavailable.');
    expect(el.querySelector('form[aria-label="Roster draft"]')).toBeNull();
  });

  it('shows a load failure from the API as an alert', async () => {
    const { el, http, settle } = await open();
    http.expectOne(r => r.url === base && r.method === 'GET').flush({ detail: 'Not allowed.' }, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Not allowed.');
  });

  it('searches and sorts the loaded rosters', async () => {
    const { el, settle, flushList } = await open();
    await flushList([row(1, { data: { ...row(1).data, employeeId: 7 } }), row(2, { data: { ...row(2).data, employeeId: 3 } })]);
    const header = Array.from(el.querySelectorAll('thead th')).find(th => th.textContent?.includes('Employee / WorkDate'))!;
    header.querySelector('button')!.click();
    await settle();
    expect(header.getAttribute('aria-sort')).toBe('ascending');
    const displayDate = TestBed.inject(CompanyDateService).format(future);
    expect(bodyRows(el)[0].textContent).toContain(`3 / ${displayDate}`);
    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = `7 / ${displayDate}`; input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(bodyRows(el).length).toBe(1);
    expect(bodyRows(el)[0].textContent).toContain(`7 / ${displayDate}`);
  });
});
