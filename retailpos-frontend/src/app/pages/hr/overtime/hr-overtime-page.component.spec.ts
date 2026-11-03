import { CompanyDateService } from '../../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrOvertimePageComponent } from './hr-overtime-page.component';

describe('HR overtime workbench', () => {
  const base = `${environment.apiUrl}/hr/v1/overtime`;
  const permissions = new Set([
    'hr.overtime.view', 'hr.overtime.calculate', 'hr.overtime.submit',
    'hr.overtime.approve', 'hr.overtime_policy.manage'
  ]);
  afterEach(() => TestBed.resetTestingModule());

  async function create(capabilitiesEnabled = true) {
    await TestBed.configureTestingModule({
      imports: [HrOvertimePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.has(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled: true, attendanceEnabled: capabilitiesEnabled,
      payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrOvertimePageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('blocks all backend requests when Attendance capability is disabled', async () => {
    const { fixture, http } = await create(false);
    http.expectNone(`${base}/entries`);
    http.expectNone(`${base}/policies`);
    expect(fixture.nativeElement.textContent).toContain('HR or Attendance capability is disabled');
    http.verify();
  });

  it('creates a policy, calculates attendance-backed overtime, and displays immutable evidence versions', async () => {
    const { fixture, page, http } = await create();
    http.expectOne(`${base}/policies`).flush([]);
    http.expectOne(request => request.url === `${base}/entries`).flush([]);

    page.policy = {
      code: 'NIGHT', name: 'Night shift', scopeType: 'Business', scopeReference: null,
      versionNo: 1, effectiveFrom: '2026-10-01', effectiveTo: null,
      minimumEligibleMinutes: 15, maximumEligibleMinutes: 240, roundingUnitMinutes: 15,
      roundingMethod: 'Down', includePreShift: false, includePostShift: true,
      allowNormalWorkingDay: true, allowWeeklyOff: true, allowHoliday: true, allowSpecialDay: false,
      rateType: 'Multiplier', normalDayRate: 1.5, weeklyOffRate: 2,
      holidayRate: 2, specialDayRate: 2, isActive: true
    };
    page.createPolicy();
    const createRequest = http.expectOne(`${base}/policies`);
    expect(createRequest.request.method).toBe('POST');
    createRequest.flush({ id: 1, ...page.policy, scopeReference: null });
    http.expectOne(`${base}/policies`).flush([]);

    page.calculate();
    const calculate = http.expectOne(`${base}/calculate`);
    expect(calculate.request.body).toEqual({
      from: page.filters.from, to: page.filters.to,
      employeeId: null, locationProjectionId: null
    });
    calculate.flush([{
      id: 9, employeeId: 17, employeeCode: 'E17', employeeName: 'Test employee',
      workDate: '2026-10-10', dailyAttendanceId: 20, operationalLocationProjectionId: 10,
      dayCategory: 'NormalWorkingDay', attendanceProcessingVersion: 2, policyVersionId: 1,
      policyVersionNo: 1, eligibleMinutes: 30, calculatedMinutes: 30, approvedMinutes: null,
      status: 'Calculated', calculationVersion: 2, approvalInstanceId: null,
      approvalStatus: null, currentApprovalStep: null, version: 2,
      evidence: [{
        calculationVersion: 1, attendanceProcessingVersion: 1, policyVersionId: 1,
        eligibleMinutes: 15, calculatedMinutes: 15, dayCategory: 'NormalWorkingDay',
        calculationFingerprint: 'A', policySnapshotJson: '{}', attendanceSnapshotJson: '{}',
        calculationInputsJson: '{}', createdAtUtc: '2026-10-10T03:00:00Z', createdByUserId: 5
      }, {
        calculationVersion: 2, attendanceProcessingVersion: 2, policyVersionId: 1,
        eligibleMinutes: 30, calculatedMinutes: 30, dayCategory: 'NormalWorkingDay',
        calculationFingerprint: 'B', policySnapshotJson: '{}', attendanceSnapshotJson: '{}',
        calculationInputsJson: '{}', createdAtUtc: '2026-10-10T04:00:00Z', createdByUserId: 5
      }]
    }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Calculation v2');
    expect(fixture.nativeElement.textContent).toContain('Evidence versions v1, v2');
    http.verify();
  });
});

describe('HR overtime workbench UI (shared kit)', () => {
  const base = `${environment.apiUrl}/hr/v1/overtime`;
  const all = [
    'hr.overtime.view', 'hr.overtime.calculate', 'hr.overtime.submit',
    'hr.overtime.approve', 'hr.overtime_policy.manage'
  ];
  afterEach(() => TestBed.resetTestingModule());

  function entry(id: number, overrides: Record<string, unknown> = {}) {
    return {
      id, employeeId: 100 + id, employeeCode: `E${id}`, employeeName: `Employee ${id}`,
      workDate: '2026-10-10', dailyAttendanceId: 20, operationalLocationProjectionId: 10,
      dayCategory: 'NormalWorkingDay', attendanceProcessingVersion: 2, policyVersionId: 1,
      policyVersionNo: 1, eligibleMinutes: 30, calculatedMinutes: 30, approvedMinutes: null,
      status: 'Calculated', calculationVersion: 2, approvalInstanceId: null,
      approvalStatus: null, currentApprovalStep: null, version: 2, evidence: [], ...overrides
    };
  }

  const policyRow = {
    id: 3, code: 'NIGHT', name: 'Night shift', scopeType: 'Location', scopeReference: 'LOC-1',
    versionNo: 2, effectiveFrom: '2026-10-01', effectiveTo: null, minimumEligibleMinutes: 15,
    maximumEligibleMinutes: null, roundingUnitMinutes: 15, roundingMethod: 'Down',
    includePreShift: false, includePostShift: true, allowNormalWorkingDay: true,
    allowWeeklyOff: true, allowHoliday: true, allowSpecialDay: false, rateType: 'Multiplier',
    normalDayRate: 1.5, weeklyOffRate: 2, holidayRate: 2, specialDayRate: 2, isActive: true
  };

  async function create(permissions: string[] = all, load = true) {
    await TestBed.configureTestingModule({
      imports: [HrOvertimePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled: true, attendanceEnabled: true,
      payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrOvertimePageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const host: HTMLElement = fixture.nativeElement;
    const result = { fixture, page: fixture.componentInstance, http, host };
    if (load) {
      if (permissions.includes('hr.overtime_policy.manage')) http.expectOne(`${base}/policies`).flush([policyRow]);
      http.expectOne(request => request.url === `${base}/entries`).flush([]);
      fixture.detectChanges();
    }
    return result;
  }

  async function loadEntries(
    permissions: string[], rows: ReturnType<typeof entry>[]
  ) {
    const ctx = await create(permissions, false);
    if (permissions.includes('hr.overtime_policy.manage')) ctx.http.expectOne(`${base}/policies`).flush([]);
    ctx.http.expectOne(request => request.url === `${base}/entries`).flush(rows);
    ctx.fixture.detectChanges();
    return ctx;
  }

  // The entries table is the last data table on the page (the policy table precedes it).
  const entriesTable = (host: HTMLElement) => {
    const tables = host.querySelectorAll('app-ui-data-table');
    return tables[tables.length - 1] as HTMLElement;
  };

  const buttons = (host: HTMLElement) =>
    Array.from(host.querySelectorAll('button')).map(button => button.textContent!.trim());

  it('renders a single page-header h1 inside the main landmark', async () => {
    const { host } = await create();
    expect(host.querySelectorAll('h1').length).toBe(1);
    expect(host.querySelector('main h1')!.textContent!.trim()).toBe('Overtime');
    expect(host.querySelector('app-ui-page-header')!.textContent).toContain('no pay amount is calculated here');
    expect(host.querySelectorAll('app-ui-card').length).toBeGreaterThanOrEqual(3);
  });

  it('exposes both tables as named scroll regions', async () => {
    const { host } = await create();
    const regions = Array.from(host.querySelectorAll('[role="region"]')).map(region => region.getAttribute('aria-label'));
    expect(regions).toEqual(['Overtime policy versions', 'Overtime entries']);
    expect(Array.from(host.querySelectorAll('app-ui-data-table table')).length).toBe(2);
  });

  it('ties every form label to its control and marks required fields', async () => {
    const { host } = await create();
    const labels = Array.from(host.querySelectorAll('label.ui-field__label'));
    expect(labels.length).toBeGreaterThan(15);
    for (const label of labels) {
      const control = host.querySelector(`#${label.getAttribute('for')}`);
      expect(control, label.textContent!).not.toBeNull();
    }
    const code = host.querySelector<HTMLInputElement>('input[name="code"]')!;
    expect(code.getAttribute('aria-required')).toBe('true');
    expect(host.querySelector(`label[for="${code.id}"]`)!.textContent).toContain('Code');
    expect(host.querySelector(`label[for="${code.id}"]`)!.textContent).toContain('(required)');
    expect(host.querySelector<HTMLInputElement>('input[name="effectiveTo"]')!.hasAttribute('aria-required')).toBe(false);
  });

  it('keeps ngModel bindings on the policy form and the scope reference rules', async () => {
    const { fixture, page, host } = await create();
    const code = host.querySelector<HTMLInputElement>('input[name="code"]')!;
    code.value = code.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('WEEKEND', (code.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'WEEKEND';
    code.dispatchEvent(new Event('input'));
    expect(page.policy.code).toBe('WEEKEND');

    expect(host.querySelector('input[name="scopeReference"]')).toBeNull();
    const scope = host.querySelector<HTMLSelectElement>('select[name="scopeType"]')!;
    scope.value = scope.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Location', (scope.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Location';
    scope.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(page.policy.scopeType).toBe('Location');
    const reference = host.querySelector<HTMLInputElement>('input[name="scopeReference"]')!;
    reference.value = reference.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(' LOC-9 ', (reference.getAttribute('placeholder') ?? '').includes('HH:mm')) : ' LOC-9 ';
    reference.dispatchEvent(new Event('input'));
    expect(page.policy.scopeReference).toBe(' LOC-9 ');

    const pre = host.querySelector<HTMLInputElement>('input[name="includePre"]')!;
    expect(pre.type).toBe('checkbox');
    pre.click();
    expect(page.policy.includePreShift).toBe(true);

    scope.value = scope.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Business', (scope.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Business';
    scope.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(page.policy.scopeReference).toBeNull();
    expect(host.querySelector('input[name="scopeReference"]')).toBeNull();
  });

  it('submits the policy form through the Create version button with the unchanged payload', async () => {
    const { fixture, page, http, host } = await create();
    page.policy = { ...page.policy, code: 'X1', name: 'Extra', scopeType: 'Location', scopeReference: ' LOC-9 ' };
    fixture.detectChanges();
    const submit = Array.from(host.querySelectorAll<HTMLButtonElement>('button[type="submit"]'))
      .find(button => button.textContent!.trim() === 'Create version')!;
    expect(submit.classList.contains('ui-btn')).toBe(true);
    host.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne(`${base}/policies`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({ code: 'X1', name: 'Extra', scopeType: 'Location', scopeReference: 'LOC-9' });
    request.flush({ ...policyRow });
    http.expectOne(`${base}/policies`).flush([policyRow]);
    fixture.detectChanges();
    expect(host.querySelector('[role="status"]')!.textContent).toContain('Overtime policy version created.');
    expect(host.textContent).toContain('NIGHT v2 — Night shift');
    http.verify();
  });

  it('shows policy versions in a table with scope, period, eligibility and an active badge', async () => {
    const { host } = await create();
    const row = host.querySelector('app-ui-data-table tbody tr')!;
    const text = row.textContent!;
    expect(text).toContain('NIGHT v2 — Night shift');
    expect(text).toContain('Location · LOC-1');
    expect(text).toContain("01/10/2026 – open");
    expect(text).toContain('Min 15m · cap nonem · round Down/15m');
    expect(row.querySelector('app-ui-badge')!.textContent!.trim()).toBe('Active');
  });

  it('hides policy management and does not request policies without the manage permission', async () => {
    const { host, http } = await create(['hr.overtime.view'], false);
    http.expectNone(`${base}/policies`);
    http.expectOne(request => request.url === `${base}/entries`).flush([]);
    expect(host.textContent).not.toContain('Create policy version');
    expect(host.textContent).not.toContain('Policy versions');
    expect(host.querySelector('input[name="code"]')).toBeNull();
    http.verify();
  });

  it('offers Calculate / reprocess only with the calculate permission', async () => {
    const withPermission = await create(all);
    expect(buttons(withPermission.host)).toContain('Calculate / reprocess');
    TestBed.resetTestingModule();
    const without = await create(['hr.overtime.view']);
    expect(buttons(without.host)).not.toContain('Calculate / reprocess');
    expect(buttons(without.host)).toContain('Search');
  });

  it('shows the empty state and a loading state for the entries table', async () => {
    const { fixture, http, host } = await create(['hr.overtime.view'], false);
    fixture.detectChanges();
    expect(host.textContent).toContain('Loading overtime...');
    expect(host.querySelector('app-ui-data-table app-ui-state[data-kind="loading"]')).not.toBeNull();
    http.expectOne(request => request.url === `${base}/entries`).flush([]);
    fixture.detectChanges();
    expect(host.textContent).not.toContain('Loading overtime...');
    expect(host.querySelector('app-ui-data-table app-ui-state')!.textContent)
      .toContain('No overtime entries match these filters.');
  });

  it('announces a failed load as an alert with the server detail', async () => {
    const { fixture, http, host } = await create(['hr.overtime.view'], false);
    http.expectOne(request => request.url === `${base}/entries`)
      .flush({ detail: 'Outside your location scope.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    const alert = host.querySelector('app-ui-alert[role="alert"]')!;
    expect(alert.textContent).toContain('Outside your location scope.');
    expect(alert.getAttribute('data-variant')).toBe('danger');
  });

  it('presents the calculation outcome as a status message after calculating', async () => {
    const { fixture, page, http, host } = await create();
    page.calculate();
    http.expectOne(`${base}/calculate`).flush([entry(1)]);
    fixture.detectChanges();
    const status = host.querySelector('app-ui-alert[role="status"]')!;
    expect(status.textContent).toContain('Overtime calculation completed.');
    expect(entriesTable(host).querySelector('tbody tr')!.textContent).toContain('E1 — Employee 1 (101)');
  });

  it('renders entry rows with employee, date, context, calculation and a visible workflow status badge', async () => {
    const { host } = await loadEntries(all, [
      entry(1, { status: 'PendingApproval', approvalInstanceId: 5, approvalStatus: 'Pending', currentApprovalStep: 1,
        approvedMinutes: 20, evidence: [{ calculationVersion: 1 }, { calculationVersion: 2 }] })
    ]);
    const cells = Array.from(host.querySelectorAll('app-ui-data-table tbody tr td')).map(cell => cell.textContent!.replace(/\s+/g, ' ').trim());
    expect(cells[0]).toContain('E1 — Employee 1 (101)');
    expect(cells[1]).toBe("10/10/2026");
    expect(cells[2]).toContain('NormalWorkingDay · location 10');
    expect(cells[2]).toContain('Attendance 20 / v2');
    expect(cells[3]).toContain('30 eligible · 30 calculated · 20 approved');
    expect(cells[4]).toContain('PendingApproval');
    expect(cells[4]).toContain('approval Pending step 1');
    expect(cells[4]).toContain('Evidence versions v1, v2');
    const badge = host.querySelector('app-ui-data-table app-ui-badge')!;
    expect(badge.textContent!.trim()).toBe('PendingApproval');
    expect(badge.getAttribute('data-variant')).toBe('warning');
  });

  it('shows row actions only for the workflow state and the permission that allows them', async () => {
    const rows = [
      entry(1),
      entry(2, { status: 'PendingApproval', approvalInstanceId: 7 }),
      entry(3, { status: 'Calculated', calculatedMinutes: 0 }),
      entry(4, { status: 'PendingApproval', approvalInstanceId: null })
    ];
    const rowButtons = (host: HTMLElement) => Array.from(host.querySelectorAll('app-ui-data-table tbody tr'))
      .map(row => Array.from(row.querySelectorAll('button')).map(button => button.textContent!.trim()));

    const full = await loadEntries(all, rows);
    expect(rowButtons(full.host)).toEqual([
      ['Submit'], ['Cancel', 'Approve', 'Reject'], [], ['Cancel']
    ]);
    TestBed.resetTestingModule();

    const approver = await loadEntries(['hr.overtime.approve'], rows);
    expect(rowButtons(approver.host)).toEqual([[], ['Approve', 'Reject'], [], []]);
    TestBed.resetTestingModule();

    const submitter = await loadEntries(['hr.overtime.view', 'hr.overtime.submit'], rows);
    expect(rowButtons(submitter.host)).toEqual([['Submit'], ['Cancel'], [], ['Cancel']]);
    TestBed.resetTestingModule();

    const viewer = await loadEntries(['hr.overtime.view'], rows);
    expect(rowButtons(viewer.host)).toEqual([[], [], [], []]);
  });

  it('submits, cancels, approves and rejects through the unchanged endpoints', async () => {
    const rows = [entry(1), entry(2, { status: 'PendingApproval', approvalInstanceId: 7 })];
    const { fixture, http, host } = await loadEntries(all, rows);
    const click = (rowIndex: number, name: string) => {
      const button = Array.from(host.querySelectorAll('app-ui-data-table tbody tr')[rowIndex].querySelectorAll('button'))
        .find(item => item.textContent!.trim() === name)!;
      button.click();
    };
    const refresh = () => {
      http.expectOne(request => request.url === `${base}/entries`).flush(rows);
      fixture.detectChanges();
    };

    click(0, 'Submit');
    const submit = http.expectOne(`${base}/entries/1/submit`);
    expect(submit.request.body).toEqual({ expectedVersion: 2 });
    submit.flush({});
    refresh();

    click(1, 'Cancel');
    const cancel = http.expectOne(`${base}/entries/2/cancel`);
    expect(cancel.request.body.reason).toBe('Cancelled from the overtime workbench.');
    cancel.flush({});
    refresh();

    click(1, 'Approve');
    const approve = http.expectOne(`${base}/approvals/7/approve`);
    expect(approve.request.body.reason).toBe('');
    approve.flush({});
    refresh();

    click(1, 'Reject');
    http.expectNone(`${base}/approvals/7/reject`);
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent).toContain('A reason is required to reject overtime.');

    const reason = host.querySelector<HTMLInputElement>('input[name="decisionReason"]')!;
    reason.value = reason.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(' Not worked ', (reason.getAttribute('placeholder') ?? '').includes('HH:mm')) : ' Not worked ';
    reason.dispatchEvent(new Event('input'));
    click(1, 'Reject');
    const reject = http.expectOne(`${base}/approvals/7/reject`);
    expect(reject.request.body.reason).toBe('Not worked');
    reject.flush({});
    refresh();
    http.verify();
  });

  it('disables the workflow buttons while a request is in flight', async () => {
    const { fixture, page, http, host } = await loadEntries(all, [entry(1)]);
    page.search();
    fixture.detectChanges();
    const rowButton = host.querySelector<HTMLButtonElement>('app-ui-data-table tbody tr button')!;
    expect(rowButton.disabled).toBe(true);
    expect(host.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    http.expectOne(request => request.url === `${base}/entries`).flush([entry(1)]);
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('app-ui-data-table tbody tr button')!.disabled).toBe(false);
  });

  it('searches the loaded entries without a new request and shows a no-results state', async () => {
    const rows = [entry(1), entry(2, { employeeName: 'Zed Wong' })];
    const { fixture, http, host } = await loadEntries(all, rows);
    const search = host.querySelector<HTMLInputElement>('input[aria-label="Search overtime entries"]')!;
    const bodyRows = () => entriesTable(host).querySelectorAll('tbody tr').length;
    expect(bodyRows()).toBe(2);
    search.value = search.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('zed', (search.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'zed';
    search.dispatchEvent(new Event('input'));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(bodyRows()).toBe(1);
    expect(entriesTable(host).querySelector('tbody')!.textContent).toContain('Zed Wong');
    search.value = search.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('nobody', (search.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'nobody';
    search.dispatchEvent(new Event('input'));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    expect(entriesTable(host).querySelector('app-ui-state')!.textContent).toContain('No matching results');
    http.verify();
  });

  it('sorts by employee with aria-sort and paginates large result sets', async () => {
    const rows = Array.from({ length: 12 }, (_, index) => entry(index + 1));
    const { fixture, host } = await loadEntries(all, rows);
    const table = entriesTable(host);
    const header = Array.from(table.querySelectorAll('th'))
      .find(th => th.textContent!.trim() === 'Employee')!;
    expect(header.getAttribute('aria-sort')).toBe('none');
    header.querySelector('button')!.click();
    fixture.detectChanges();
    expect(header.getAttribute('aria-sort')).toBe('ascending');
    header.querySelector('button')!.click();
    fixture.detectChanges();
    expect(header.getAttribute('aria-sort')).toBe('descending');
    expect(table.querySelector('tbody tr td')!.textContent).toContain('E12 —');

    expect(table.querySelectorAll('tbody tr').length).toBe(10);
    table.querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click();
    fixture.detectChanges();
    expect(table.querySelectorAll('tbody tr').length).toBe(2);
  });

  it('keeps the actions column header and status text visible for assistive technology', async () => {
    const { host } = await loadEntries(all, [entry(1)]);
    const headers = Array.from(entriesTable(host).querySelectorAll('th')).map(th => th.textContent!.trim());
    expect(headers).toEqual(['Employee', 'Work date', 'Attendance / context', 'Calculation', 'Workflow', 'Actions']);
    expect(host.querySelector('app-ui-data-table app-ui-badge')!.textContent!.trim()).toBe('Calculated');
  });
});
