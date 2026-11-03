import { CompanyDateService } from '../../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { environment } from '../../../../environments/environment';
import { HrLeavePageComponent } from './hr-leave-page.component';

describe('Phase 3B leave policy and ledger page', () => {
  const base = `${environment.apiUrl}/hr/v1/leave`;
  afterEach(() => TestBed.resetTestingModule());

  async function create(
    permissions: string[] = ['hr.leave.view'], hrEnabled = true, attendanceEnabled = false
  ) {
    await TestBed.configureTestingModule({
      imports: [HrLeavePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled, attendanceEnabled, payrollEnabled: false,
      deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrLeavePageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('does not request data without HR capability or leave permission', async () => {
    const { fixture, http } = await create([], false);
    http.expectNone(`${base}/types`);
    expect(fixture.nativeElement.textContent).toContain('HR capability unavailable');
    http.verify();
  });

  it('loads types and policies and shows empty states', async () => {
    const { fixture, http } = await create();
    http.expectOne(`${base}/types`).flush([]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No leave types are configured');
    expect(fixture.nativeElement.textContent).toContain('No policies match this filter');
    http.verify();
  });

  it('requires an explicit accrual method and posts the selected method', async () => {
    const { fixture, page, http } = await create(['hr.leave.view', 'hr.leave.admin']);
    http.expectOne(`${base}/types`).flush([{
      id: 4, code: 'ANNUAL', name: 'Annual', isPaid: true,
      requiresDocument: false, isActive: true, version: 1
    }]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    page.policyForm = {
      ...page.policyForm, leaveTypeId: 4, code: 'STANDARD', name: 'Standard',
      accrualMethod: 1, annualEntitlement: 12, effectiveFrom: '2026-01-01'
    };
    page.createPolicy();
    const request = http.expectOne(`${base}/policies`);
    expect(request.request.body.accrualMethod).toBe(1);
    expect(request.request.body.leaveTypeId).toBe(4);
    request.flush({ id: 12, leaveTypeId: 4, code: 'STANDARD', name: 'Standard',
      scopeType: 0, scopeReference: null, versionNo: 1, accrualMethod: 1,
      annualEntitlement: 12, carryForwardAllowed: false, carryForwardCap: null,
      carryForwardExpiryMonths: null, maximumBalance: null, negativeBalanceAllowed: false,
      payrollImpactCode: null, leaveYearStartMonth: 1, leaveYearStartDay: 1,
      effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true });
    expect(page.policies().length).toBe(1);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Monthly joining-date anniversary');
    http.verify();
  });

  it('loads balances and ledger and presents backend scope errors', async () => {
    const { fixture, page, http } = await create(['hr.leave.view', 'hr.leave_balance.view']);
    http.expectOne(`${base}/types`).flush([]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    page.balanceForm = { employeeId: 21, leaveTypeId: null };
    page.loadBalance();
    http.expectOne(`${base}/employees/21/balances`).flush([{
      employeeId: 21, leaveTypeId: 3, leaveTypeCode: 'ANNUAL', leaveTypeName: 'Annual',
      leaveYearStart: '2026-01-01', leaveYearEnd: '2026-12-31', policyVersionNo: 1,
      accrualMethod: 0, opening: 2, accrued: 4, used: 1, restored: 0, adjusted: 0,
      carriedForward: 0, expired: 0, remaining: 5, version: 2, status: 'Open',
      closeVersion: 0, closingBalance: null, closeCarryForward: null, closeExpired: null,
      carryForwardExpiresOn: null, lastLifecycleAtUtc: null,
      lastLifecycleAction: null, lastLifecycleReason: null
    }]);
    http.expectOne(`${base}/employees/21/ledger`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('5');
    expect(page.balanceLoaded()).toBe(true);
    page.balanceForm.employeeId = 22;
    page.loadBalance();
    http.expectOne(`${base}/employees/22/balances`).flush(
      { detail: "Employee is outside the caller's Primary location scope." },
      { status: 403, statusText: 'Forbidden' });
    expect(page.error()).toContain('Primary location scope');
    http.verify();
  });

  it('offers audited close actions only with both balance and close permissions', async () => {
    const { fixture, page, http } = await create([
      'hr.leave.view', 'hr.leave_balance.view', 'hr.leave_year.close'
    ]);
    http.expectOne(`${base}/types`).flush([]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    page.balanceForm = { employeeId: 21, leaveTypeId: null };
    page.loadBalance();
    const balance = {
      employeeId: 21, leaveTypeId: 3, leaveTypeCode: 'ANNUAL', leaveTypeName: 'Annual',
      leaveYearStart: '2026-01-01', leaveYearEnd: '2026-12-31', policyVersionNo: 1,
      accrualMethod: 0, opening: 2, accrued: 4, used: 1, restored: 0, adjusted: 0,
      carriedForward: 0, expired: 0, remaining: 5, version: 2, status: 'Open' as const,
      closeVersion: 0, closingBalance: null, closeCarryForward: null, closeExpired: null,
      carryForwardExpiresOn: null, lastLifecycleAtUtc: null,
      lastLifecycleAction: null, lastLifecycleReason: null
    };
    http.expectOne(`${base}/employees/21/balances`).flush([balance]);
    http.expectOne(`${base}/employees/21/ledger`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Review close');

    page.reviewLeaveYearClose(balance);
    const review = http.expectOne(`${base}/employees/21/leave-years/2026-01-01/review-close`);
    expect(review.request.body).toMatchObject({ leaveTypeId: 3, expectedVersion: 2 });
    review.flush({});
    http.expectOne(request =>
      request.url === `${base}/employees/21/balances` && request.params.get('leaveTypeId') === '3')
      .flush([]);
    http.expectOne(request =>
      request.url === `${base}/employees/21/ledger` && request.params.get('leaveTypeId') === '3')
      .flush([]);
    http.verify();
  });

  it('does not offer close actions without balance-view permission', async () => {
    const { fixture, http } = await create(['hr.leave.view', 'hr.leave_year.close']);
    http.expectOne(`${base}/types`).flush([]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Employee balance and ledger');
    http.verify();
  });

  it('keeps the busy indicator active while refreshing after a ledger adjustment', async () => {
    const { page, http } = await create([
      'hr.leave.view', 'hr.leave_balance.adjust', 'hr.leave_balance.view'
    ]);
    http.expectOne(`${base}/types`).flush([]);
    http.expectOne(request => request.url === `${base}/policies`).flush([]);
    page.adjustment = {
      employeeId: 21, leaveTypeId: 3, transactionType: 4, quantity: 1,
      effectiveDate: '2026-02-01', idempotencyKey: 'adjust-21', reason: 'Correction'
    };

    page.postLedgerEntry();
    http.expectOne(`${base}/employees/21/ledger`).flush({ entries: [], isReplay: false });
    const balances = http.expectOne(request =>
      request.url === `${base}/employees/21/balances` && request.params.get('leaveTypeId') === '3');
    expect(page.busy()).toBe(true);
    balances.flush([]);
    http.expectOne(request =>
      request.url === `${base}/employees/21/ledger` && request.params.get('leaveTypeId') === '3')
      .flush([]);
    expect(page.busy()).toBe(false);
    http.verify();
  });

  it('creates a document-required draft, uploads the private attachment, then refreshes requests', async () => {
    const { fixture, page, http } = await create(['hr.leave.request'], true, true);
    http.expectOne(`${base}/types`).flush([{
      id: 4, code: 'MEDICAL', name: 'Medical', isPaid: true,
      requiresDocument: true, isActive: true, version: 1
    }]);
    http.expectOne(`${base}/requests/mine`).flush([]);
    page.requestForm = {
      leaveTypeId: 4, startDate: '2026-03-10', endDate: '2026-03-10', reason: 'Appointment'
    };
    page.requestFile = new File(['evidence'], 'note.pdf', { type: 'application/pdf' });
    page.createRequest();

    const draftRequest = http.expectOne(`${base}/requests`);
    expect(draftRequest.request.body).toEqual({
      leaveTypeId: 4, startDate: '2026-03-10', endDate: '2026-03-10', reason: 'Appointment'
    });
    draftRequest.flush({
      id: 32, employeeId: 9, employeeName: 'Employee', leaveTypeId: 4,
      leaveTypeCode: 'MEDICAL', leaveTypeName: 'Medical',
      startDate: '2026-03-10', endDate: '2026-03-10', requestedUnits: 1,
      reason: 'Appointment', status: 'Draft', scopeLocationProjectionId: 12,
      requestedByUserId: 99, requestedAtUtc: '2026-03-01T00:00:00Z',
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null,
      approvalInstanceId: null, approvalStatus: null, currentApprovalStep: null,
      attachmentVersionId: null, version: 1, days: []
    });

    const upload = http.expectOne(`${base}/requests/32/attachments`);
    expect(upload.request.method).toBe('POST');
    expect((upload.request.body as FormData).get('file')).toBeInstanceOf(File);
    upload.flush({
      id: '4f36cbbc-75c9-4a2c-bf5b-9741df0a1d47', versionNo: 1, fileName: 'note.pdf',
      contentType: 'application/pdf', sizeBytes: 8, scanStatus: 1,
      uploadedByUserId: 99, uploadedAtUtc: '2026-03-01T00:00:00Z'
    });
    http.expectOne(`${base}/requests/mine`).flush([]);
    expect(page.error()).toBeNull();
    expect(page.busy()).toBe(false);
    fixture.detectChanges();
    http.verify();
  });
});

describe('HR leave page UI (shared kit)', () => {
  const base = `${environment.apiUrl}/hr/v1/leave`;
  afterEach(() => TestBed.resetTestingModule());

  const annual = { id: 4, code: 'ANNUAL', name: 'Annual', isPaid: true, requiresDocument: false, isActive: true, version: 1 };
  const medical = { id: 5, code: 'MEDICAL', name: 'Medical', isPaid: false, requiresDocument: true, isActive: true, version: 1 };
  const retired = { id: 6, code: 'OLD', name: 'Retired', isPaid: true, requiresDocument: false, isActive: false, version: 1 };

  const policy = {
    id: 12, leaveTypeId: 4, code: 'STANDARD', name: 'Standard', scopeType: 2, scopeReference: 'LOC-1',
    versionNo: 3, accrualMethod: 1, annualEntitlement: 12.5, carryForwardAllowed: false,
    carryForwardCap: null, carryForwardExpiryMonths: null, maximumBalance: null,
    negativeBalanceAllowed: false, payrollImpactCode: null, leaveYearStartMonth: 1, leaveYearStartDay: 1,
    effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true
  };

  function request(id: number, overrides: Record<string, unknown> = {}) {
    return {
      id, employeeId: 9, employeeName: 'Rahim Uddin', leaveTypeId: 4, leaveTypeCode: 'ANNUAL', leaveTypeName: 'Annual',
      startDate: '2026-03-10', endDate: '2026-03-11', requestedUnits: 2, reason: 'Family event', status: 'Draft',
      scopeLocationProjectionId: 12, requestedByUserId: 99, requestedAtUtc: '2026-03-01T00:00:00Z',
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null, approvalInstanceId: null,
      approvalStatus: null, currentApprovalStep: null, attachmentVersionId: null, version: 2,
      days: [
        { workDate: '2026-03-10', rosterId: 1, operationalLocationProjectionId: 12, dayClassification: 'Working', units: 1,
          policyVersionNo: 1, shiftSnapshotJson: '{}', calendarEvidenceJson: '{}' },
        { workDate: '2026-03-11', rosterId: 1, operationalLocationProjectionId: 12, dayClassification: 'Holiday', units: 1,
          policyVersionNo: 1, shiftSnapshotJson: '{}', calendarEvidenceJson: '{}' }
      ],
      ...overrides
    };
  }

  function balance(overrides: Record<string, unknown> = {}) {
    return {
      employeeId: 21, leaveTypeId: 4, leaveTypeCode: 'ANNUAL', leaveTypeName: 'Annual',
      leaveYearStart: '2026-01-01', leaveYearEnd: '2026-12-31', policyVersionNo: 1, accrualMethod: 0,
      opening: 2, accrued: 4.25, used: 1, restored: 0, adjusted: 0, carriedForward: 0, expired: 0,
      remaining: 5.25, version: 2, status: 'Open', closeVersion: 0, closingBalance: null,
      closeCarryForward: null, closeExpired: null, carryForwardExpiresOn: null, lastLifecycleAtUtc: null,
      lastLifecycleAction: null, lastLifecycleReason: null, ...overrides
    };
  }

  interface Boot {
    permissions?: string[]; attendance?: boolean; types?: unknown[]; policies?: unknown[];
    mine?: unknown[]; pending?: unknown[];
  }

  async function boot(options: Boot = {}) {
    const permissions = options.permissions ?? ['hr.leave.view'];
    const attendance = options.attendance ?? false;
    await TestBed.configureTestingModule({
      imports: [HrLeavePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled: true, attendanceEnabled: attendance, payrollEnabled: false,
      deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrLeavePageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`${base}/types`).flush(options.types ?? [annual, medical, retired]);
    const canView = permissions.includes('hr.leave.view') || permissions.includes('hr.leave.admin');
    if (canView) http.expectOne(req => req.url === `${base}/policies`).flush(options.policies ?? []);
    const request$ = permissions.includes('hr.leave.request') && attendance;
    const approve$ = permissions.includes('hr.leave.approve') && attendance;
    if (request$) http.expectOne(`${base}/requests/mine`).flush(options.mine ?? []);
    if (approve$) http.expectOne(req => req.url === `${base}/requests` && req.params.get('status') === 'PendingApproval')
      .flush(options.pending ?? []);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http, host: fixture.nativeElement as HTMLElement };
  }

  const cardByTitle = (host: HTMLElement, title: string) =>
    Array.from(host.querySelectorAll('app-ui-card')).find(card => card.querySelector('h2')?.textContent?.trim() === title) as HTMLElement | undefined;
  const regionLabels = (host: HTMLElement) =>
    Array.from(host.querySelectorAll('[role="region"]')).map(region => region.getAttribute('aria-label'));
  const labelsOf = (element: Element) =>
    Array.from(element.querySelectorAll('button:not(.company-date-trigger)')).map(button => button.textContent!.trim());

  it('renders a single page-header h1 inside the main landmark', async () => {
    const { host } = await boot();
    expect(host.querySelectorAll('h1').length).toBe(1);
    expect(host.querySelector('main h1')!.textContent!.trim()).toBe('Leave policies and balances');
    expect(host.querySelector('app-ui-page-header')!.textContent).toContain('append-only leave ledger');
  });

  it('shows a loading state while the leave types are loading and announces failures as an alert', async () => {
    await TestBed.configureTestingModule({
      imports: [HrLeavePageComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permission === 'hr.leave.view' } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled: true, attendanceEnabled: false, payrollEnabled: false, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrLeavePageComponent);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelector('app-ui-state[data-kind="loading"]')!.textContent).toContain('Loading or saving leave data...');
    TestBed.inject(HttpTestingController).expectOne(`${base}/types`)
      .flush({ detail: 'Leave data is outside your scope.' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    const alert = host.querySelector('app-ui-alert[role="alert"]')!;
    expect(alert.textContent).toContain('Leave data is outside your scope.');
    expect(alert.getAttribute('data-variant')).toBe('danger');
    expect(host.textContent).not.toContain('Loading or saving leave data...');
  });

  it('lists leave types in a named table with paid and active status badges', async () => {
    const { host } = await boot();
    const card = cardByTitle(host, 'Leave types')!;
    expect(card.querySelector('[role="region"]')!.getAttribute('aria-label')).toBe('Leave types');
    const rows = Array.from(card.querySelectorAll('tbody tr'))
      .map(row => Array.from(row.querySelectorAll('td')).map(cell => cell.textContent!.trim()));
    expect(rows).toEqual([['ANNUAL', 'Annual', 'Paid', 'Active'], ['MEDICAL', 'Medical', 'Unpaid', 'Active'], ['OLD', 'Retired', 'Paid', 'Inactive']]);
    const badges = Array.from(card.querySelectorAll('app-ui-badge')).map(badge => badge.getAttribute('data-variant'));
    expect(badges).toEqual(['success', 'success', 'neutral', 'success', 'success', 'neutral']);
    expect(card.querySelector('input[type="search"]')).toBeNull();
    expect(card.querySelector('.ui-table__footer')).toBeNull();
  });

  it('sorts leave types with aria-sort on the header', async () => {
    const { fixture, host } = await boot();
    const card = cardByTitle(host, 'Leave types')!;
    const header = Array.from(card.querySelectorAll('th')).find(th => th.textContent!.trim() === 'Name')!;
    expect(header.getAttribute('aria-sort')).toBe('none');
    header.querySelector('button')!.click();
    fixture.detectChanges();
    expect(header.getAttribute('aria-sort')).toBe('ascending');
    header.querySelector('button')!.click();
    fixture.detectChanges();
    expect(header.getAttribute('aria-sort')).toBe('descending');
    expect(card.querySelector('tbody tr')!.textContent).toContain('Retired');
  });

  it('adds a leave type from the labelled form with the unchanged payload and hides it without admin permission', async () => {
    const { fixture, page, http, host } = await boot({ permissions: ['hr.leave.view', 'hr.leave.admin'], types: [annual] });
    const card = cardByTitle(host, 'Add leave type')!;
    const code = card.querySelector<HTMLInputElement>('input[name="typeCode"]')!;
    expect(card.querySelector(`label[for="${code.id}"]`)!.textContent).toContain('Code');
    expect(code.getAttribute('aria-required')).toBe('true');
    code.value = code.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('SICK', (code.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'SICK';
    code.dispatchEvent(new Event('input'));
    const name = card.querySelector<HTMLInputElement>('input[name="typeName"]')!;
    name.value = name.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Sick leave', (name.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Sick leave';
    name.dispatchEvent(new Event('input'));
    card.querySelector<HTMLInputElement>('input[name="document"]')!.click();
    expect(page.typeForm).toEqual({ code: 'SICK', name: 'Sick leave', isPaid: true, requiresDocument: true });

    card.closest('form')!.dispatchEvent(new Event('submit'));
    const create = http.expectOne(`${base}/types`);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ code: 'SICK', name: 'Sick leave', isPaid: true, requiresDocument: true });
    create.flush({ id: 9, code: 'SICK', name: 'Sick leave', isPaid: true, requiresDocument: true, isActive: true, version: 1 });
    fixture.detectChanges();
    expect(cardByTitle(host, 'Leave types')!.textContent).toContain('SICK');
    expect(page.typeForm.code).toBe('');
    http.verify();

    TestBed.resetTestingModule();
    const viewer = await boot();
    expect(cardByTitle(viewer.host, 'Add leave type')).toBeUndefined();
  });

  it('offers the request form only with the request permission and explains the missing Attendance capability', async () => {
    const { host } = await boot({ permissions: ['hr.leave.request'], attendance: false });
    const card = cardByTitle(host, 'Request leave')!;
    expect(card.querySelector('app-ui-alert')!.textContent).toContain('Leave requests require the Attendance capability');
    expect(card.querySelector('app-ui-alert')!.getAttribute('role')).toBe('status');
    expect(cardByTitle(host, 'My leave requests')).toBeUndefined();
    TestBed.resetTestingModule();

    const none = await boot({ permissions: ['hr.leave.view'] });
    expect(cardByTitle(none.host, 'Request leave')).toBeUndefined();
  });

  it('binds the request form, lists only active types and shows the document field for document-required types', async () => {
    const { fixture, page, host } = await boot({ permissions: ['hr.leave.request'], attendance: true });
    const card = cardByTitle(host, 'Request leave')!;
    const type = card.querySelector<HTMLSelectElement>('select[name="requestType"]')!;
    expect(card.querySelector(`label[for="${type.id}"]`)!.textContent).toContain('Leave type');
    const options = Array.from(type.options).map(option => option.textContent!.trim());
    expect(options).toEqual(['Select type', 'ANNUAL — Annual', 'MEDICAL — Medical']);
    expect(page.requestForm.leaveTypeId).toBe(4);
    expect(card.querySelector('input[type="file"]')).toBeNull();

    const select = card.querySelector<HTMLSelectElement>('select[name="requestType"]')!;
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(page.requestForm.leaveTypeId).toBe(5);
    const file = card.querySelector<HTMLInputElement>('input[type="file"]')!;
    expect(file.required).toBe(true);
    expect(card.querySelector(`label[for="${file.id}"]`)!.textContent).toContain('Supporting document');

    const reason = card.querySelector<HTMLInputElement>('input[name="requestReason"]')!;
    reason.value = reason.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Checkup', (reason.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Checkup';
    reason.dispatchEvent(new Event('input'));
    expect(page.requestForm.reason).toBe('Checkup');
    const submit = card.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.textContent!.trim()).toBe('Create draft');
    expect(submit.disabled).toBe(false);
  });

  it('keeps request-draft validation and submits a draft without a document', async () => {
    const { fixture, page, http, host } = await boot({ permissions: ['hr.leave.request'], attendance: true });
    page.createRequest();
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('Select an active leave type, enter a reason, and attach the required document.');

    page.requestForm = { leaveTypeId: 4, startDate: '2026-04-01', endDate: '2026-04-02', reason: ' Trip ' };
    page.createRequest();
    const draft = http.expectOne(`${base}/requests`);
    expect(draft.request.body).toEqual({ leaveTypeId: 4, startDate: '2026-04-01', endDate: '2026-04-02', reason: 'Trip' });
    draft.flush(request(70));
    http.expectOne(`${base}/requests/mine`).flush([request(70)]);
    fixture.detectChanges();
    expect(cardByTitle(host, 'My leave requests')!.textContent).toContain('ANNUAL · 10/03/2026');
    http.verify();
  });

  it('shows my requests with status text, calendar evidence and approval info in a named table', async () => {
    const { host } = await boot({
      permissions: ['hr.leave.request'], attendance: true,
      mine: [request(1), request(2, { status: 'PendingApproval', approvalInstanceId: 31, currentApprovalStep: 2, decisionReason: 'Check roster' })]
    });
    const card = cardByTitle(host, 'My leave requests')!;
    expect(card.querySelector('[role="region"]')!.getAttribute('aria-label')).toBe('My leave requests');
    const rows = Array.from(card.querySelectorAll('tbody tr')).map(row => row.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows[0]).toContain('ANNUAL · 10/03/2026 – 11/03/2026');
    expect(rows[0]).toContain('(2 units)');
    expect(rows[0]).toContain('Draft');
    expect(rows[0]).toContain('version 2');
    expect(rows[0]).toContain('Family event');
    expect(rows[0]).toContain('10/03/2026 Working (1); 11/03/2026 Holiday (1)');
    expect(rows[1]).toContain('Approval instance 31 · step 2');
    expect(rows[1]).toContain('Decision: Check roster');
    const badges = Array.from(card.querySelectorAll('app-ui-badge')).map(badge => [badge.textContent!.trim(), badge.getAttribute('data-variant')]);
    expect(badges).toEqual([['Draft', 'info'], ['PendingApproval', 'warning']]);
  });

  it('shows request actions only for the statuses that allow them', async () => {
    const { host } = await boot({
      permissions: ['hr.leave.request'], attendance: true,
      mine: [request(1, { status: 'Draft' }), request(2, { status: 'Approved' }), request(3, { status: 'PendingApproval' }),
        request(4, { status: 'Rejected' })]
    });
    const rows = Array.from(cardByTitle(host, 'My leave requests')!.querySelectorAll('tbody tr'));
    expect(rows.map(row => labelsOf(row))).toEqual([
      ['Submit for approval', 'Cancel request'], ['Cancel request'], [], []
    ]);
  });

  it('submits and cancels a request through the unchanged endpoints and refreshes the list', async () => {
    const mine = [request(1, { status: 'Draft', version: 4 })];
    const { fixture, http, host } = await boot({ permissions: ['hr.leave.request'], attendance: true, mine });
    const card = cardByTitle(host, 'My leave requests')!;
    const click = (label: string) => Array.from(card.querySelectorAll('button')).find(b => b.textContent!.trim() === label)!.click();

    click('Submit for approval');
    const submit = http.expectOne(`${base}/requests/1/submit`);
    expect(submit.request.body).toEqual({ expectedVersion: 4 });
    submit.flush({});
    http.expectOne(`${base}/requests/mine`).flush(mine);
    fixture.detectChanges();

    click('Cancel request');
    const cancel = http.expectOne(`${base}/requests/1/cancel`);
    expect(cancel.request.body.reason).toBeNull();
    cancel.flush({});
    http.expectOne(`${base}/requests/mine`).flush(mine);

    fixture.detectChanges();
    click('Refresh');
    http.expectOne(`${base}/requests/mine`).flush([]);
    fixture.detectChanges();
    expect(card.querySelector('app-ui-state')!.textContent).toContain('No leave requests yet.');
    http.verify();
  });

  it('shows the pending approvals table only with the approve permission and Attendance capability', async () => {
    const { host } = await boot({
      permissions: ['hr.leave.approve'], attendance: true,
      pending: [request(7, { status: 'PendingApproval', approvalInstanceId: 51 })]
    });
    const card = cardByTitle(host, 'Pending leave approvals')!;
    expect(card.querySelector('[role="region"]')!.getAttribute('aria-label')).toBe('Pending leave approvals');
    const text = card.querySelector('tbody tr')!.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Rahim Uddin');
    expect(text).toContain('ANNUAL');
    expect(text).toContain("10/03/2026 – 11/03/2026");
    expect(text).toContain('Family event');
    expect(labelsOf(card.querySelector('tbody tr')!)).toEqual(['Approve', 'Reject']);
    TestBed.resetTestingModule();

    const noCapability = await boot({ permissions: ['hr.leave.approve', 'hr.leave.view'], attendance: false });
    expect(cardByTitle(noCapability.host, 'Pending leave approvals')).toBeUndefined();
  });

  it('records approval decisions with the trimmed reason from the labelled row input', async () => {
    const { fixture, http, host } = await boot({
      permissions: ['hr.leave.approve'], attendance: true,
      pending: [request(7, { status: 'PendingApproval', approvalInstanceId: 51 })]
    });
    const card = cardByTitle(host, 'Pending leave approvals')!;
    const input = card.querySelector<HTMLInputElement>('tbody input')!;
    expect(input.getAttribute('aria-label')).toBe('Decision reason for Rahim Uddin 10/03/2026 – 11/03/2026');
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('  Covered  ', (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : '  Covered  ';
    input.dispatchEvent(new Event('input'));
    const approve = Array.from(card.querySelectorAll('button')).find(b => b.textContent!.trim() === 'Approve')!;
    approve.click();
    const decision = http.expectOne(`${base}/approvals/51/approve`);
    expect(decision.request.body.reason).toBe('Covered');
    decision.flush({});
    http.expectOne(req => req.url === `${base}/requests` && req.params.get('status') === 'PendingApproval').flush([]);
    fixture.detectChanges();
    expect(card.querySelector('app-ui-state')!.textContent).toContain('No pending leave approvals.');
    http.verify();
  });

  it('renders policy versions with scope, accrual, entitlement and period, and filters by leave type', async () => {
    const { fixture, http, host } = await boot({ policies: [policy] });
    const card = cardByTitle(host, 'Policy versions')!;
    expect(card.querySelector('[role="region"]')!.getAttribute('aria-label')).toBe('Leave policy versions');
    const cells = Array.from(card.querySelectorAll('tbody td')).map(cell => cell.textContent!.trim());
    expect(cells).toEqual(['STANDARD v3 — Standard', 'Location: LOC-1', 'Monthly joining-date anniversary', '12.5', "01/01/2026 – Open"]);

    const select = card.querySelector<HTMLSelectElement>('select[name="policyType"]')!;
    expect(card.querySelector(`label[for="${select.id}"]`)!.textContent).toContain('Leave type');
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    const reload = http.expectOne(req => req.url === `${base}/policies`);
    expect(reload.request.params.get('leaveTypeId')).toBe('5');
    reload.flush([]);
    fixture.detectChanges();
    expect(card.querySelector('app-ui-state')!.textContent).toContain('No policies match this filter.');
    http.verify();
  });

  it('searches the policy table on the loaded rows only', async () => {
    const other = { ...policy, id: 13, code: 'NIGHT', name: 'Night', scopeType: 0, scopeReference: null };
    const { fixture, http, host } = await boot({ policies: [policy, other] });
    const card = cardByTitle(host, 'Policy versions')!;
    const search = card.querySelector<HTMLInputElement>('input[aria-label="Search policy versions"]')!;
    search.value = search.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('night', (search.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'night';
    search.dispatchEvent(new Event('input'));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    fixture.detectChanges();
    const rows = card.querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('NIGHT v3');
    http.verify();
  });

  it('shows the policy creation form to administrators with conditional scope and carry-forward fields', async () => {
    const { fixture, page, host } = await boot({ permissions: ['hr.leave.view', 'hr.leave.admin'], types: [annual] });
    const card = cardByTitle(host, 'Create policy version')!;
    expect(card.querySelector('input[name="scopeReference"]')).toBeNull();
    expect(card.querySelector('input[name="carryCap"]')).toBeNull();

    page.policyForm.scopeType = 2;
    card.querySelector<HTMLInputElement>('input[name="carryForward"]')!.click();
    fixture.detectChanges();
    expect(page.policyForm.carryForwardAllowed).toBe(true);
    expect(card.querySelector('input[name="scopeReference"]')).not.toBeNull();
    expect(card.querySelector('input[name="carryCap"]')).not.toBeNull();
    expect(card.querySelector('input[name="expiry"]')).not.toBeNull();

    for (const label of Array.from(card.querySelectorAll('label.ui-field__label'))) {
      expect(card.querySelector(`#${label.getAttribute('for')}`), label.textContent!).not.toBeNull();
    }
    const accrual = card.querySelector<HTMLSelectElement>('select[name="accrualMethod"]')!;
    expect(accrual.getAttribute('aria-required')).toBe('true');
    expect(Array.from(accrual.options).map(o => o.textContent!.trim())).toEqual([
      'Select an accrual method', 'Annual grant at leave-year start',
      'Monthly on joining-date anniversary', 'Annual on joining-date anniversary'
    ]);
  });

  it('requires an accrual method before posting a policy and shows the validation alert', async () => {
    const { fixture, http, host } = await boot({ permissions: ['hr.leave.view', 'hr.leave.admin'], types: [annual] });
    const card = cardByTitle(host, 'Create policy version')!;
    card.closest('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('Select a leave type, accrual method, and required policy scope reference.');
    http.expectNone(`${base}/policies`);
    http.verify();
  });

  it('hides policy management for users without leave view permission', async () => {
    const { host } = await boot({ permissions: ['hr.leave.request'], attendance: true });
    expect(cardByTitle(host, 'Policy versions')).toBeUndefined();
    expect(cardByTitle(host, 'Create policy version')).toBeUndefined();
  });

  it('shows balances with numbers as returned, status badge and permission-based close actions', async () => {
    const { fixture, page, http, host } = await boot({
      permissions: ['hr.leave.view', 'hr.leave_balance.view', 'hr.leave_year.close', 'hr.leave_year.reopen']
    });
    page.balanceForm = { employeeId: 21, leaveTypeId: null };
    page.loadBalance();
    http.expectOne(`${base}/employees/21/balances`).flush([
      balance(),
      balance({ leaveTypeId: 5, leaveYearStart: '2025-01-01', leaveYearEnd: '2025-12-31', status: 'ClosingReview' }),
      balance({
        leaveTypeId: 6, leaveYearStart: '2024-01-01', leaveYearEnd: '2024-12-31', status: 'Closed',
        closingBalance: 3, closeCarryForward: 2, closeExpired: 1, carryForwardExpiresOn: '2027-03-31',
        lastLifecycleAction: 'Closed', lastLifecycleReason: 'Year end'
      })
    ]);
    http.expectOne(`${base}/employees/21/ledger`).flush([]);
    fixture.detectChanges();

    const card = cardByTitle(host, 'Employee balance and ledger')!;
    const region = card.querySelector('[role="region"][aria-label="Leave balances"]')!;
    const rows = Array.from(region.querySelectorAll('tbody tr'));
    expect(rows.length).toBe(3);
    const first = rows[0].textContent!.replace(/\s+/g, ' ');
    expect(first).toContain('01/01/2026 – 31/12/2026');
    expect(first).toContain('ANNUAL — Annual');
    expect(first).toContain('4.25');
    expect(first).toContain('5.25');
    expect(first).toContain('Open');
    expect(first).toContain('version 2');
    expect(rows.map(row => labelsOf(row))).toEqual([['Review close'], ['Confirm close'], ['Reopen']]);
    expect(rows[2].textContent!.replace(/\s+/g, ' '))
      .toContain('Closed balance 3 · carry forward 2 · expired 1 · carried balance expires 2027-03-31');
    expect(rows[2].textContent).toContain('Closed: Year end');
    expect(Array.from(region.querySelectorAll('app-ui-badge')).map(b => b.getAttribute('data-variant')))
      .toEqual(['success', 'warning', 'neutral']);

    const reopen = rows[2].querySelector<HTMLButtonElement>('button')!;
    expect(reopen.disabled).toBe(true);
    const reason = card.querySelector<HTMLInputElement>('input[name="reopenReason"]')!;
    expect(card.querySelector(`label[for="${reason.id}"]`)!.textContent).toContain('Reason to reopen');
    reason.value = reason.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Correction', (reason.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Correction';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(reopen.disabled).toBe(false);
  });

  it('shows the ledger table with transaction names and source, and the empty states', async () => {
    const { fixture, page, http, host } = await boot({ permissions: ['hr.leave.view', 'hr.leave_balance.view'] });
    expect(cardByTitle(host, 'Employee balance and ledger')!.querySelector('[role="region"]')).toBeNull();
    page.balanceForm = { employeeId: 21, leaveTypeId: null };
    page.loadBalance();
    http.expectOne(`${base}/employees/21/balances`).flush([]);
    http.expectOne(`${base}/employees/21/ledger`).flush([]);
    fixture.detectChanges();
    const empties = Array.from(host.querySelectorAll('app-ui-state')).map(state => state.textContent!.trim());
    expect(empties.some(text => text.includes('No leave balances were found for this employee.'))).toBe(true);
    expect(empties.some(text => text.includes('No ledger entries are available for this filter.'))).toBe(true);

    page.loadBalance();
    http.expectOne(`${base}/employees/21/balances`).flush([balance()]);
    http.expectOne(`${base}/employees/21/ledger`).flush([{
      id: 1, employeeId: 21, leaveTypeId: 4, leaveYearStart: '2026-01-01', transactionType: 4, quantity: -1.5,
      effectiveDate: '2026-02-01', sourceType: 'Manual', sourceId: 'A-1', reason: 'Correction',
      actorRealUserId: 3, createdAtUtc: '2026-02-01T00:00:00Z'
    }]);
    fixture.detectChanges();
    const ledger = host.querySelector('[role="region"][aria-label="Leave ledger"]')!;
    const cells = Array.from(ledger.querySelectorAll('tbody td')).map(cell => cell.textContent!.trim());
    expect(cells).toEqual(["01/02/2026", 'Adjustment', '-1.5', 'Correction', 'Manual/A-1']);
    http.verify();
  });

  it('shows the adjustment and accrual cards only with their permissions and keeps their validation', async () => {
    const { fixture, page, http, host } = await boot({
      permissions: ['hr.leave.view', 'hr.leave_balance.adjust', 'hr.leave_balance.accrue'], types: [annual]
    });
    const adjust = cardByTitle(host, 'Opening balance / adjustment')!;
    const accrue = cardByTitle(host, 'Accrual')!;
    expect(labelsOf(adjust)).toEqual(['Post audited entry']);
    expect(labelsOf(accrue)).toEqual(['Run idempotent accrual']);
    for (const card of [adjust, accrue]) {
      for (const label of Array.from(card.querySelectorAll('label.ui-field__label'))) {
        expect(card.querySelector(`#${label.getAttribute('for')}`), label.textContent!).not.toBeNull();
      }
    }
    page.postLedgerEntry();
    page.accrue();
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent).toContain('Select an employee and leave type.');
    http.expectNone(`${base}/employees/0/ledger`);
    http.verify();

    TestBed.resetTestingModule();
    const viewer = await boot({ permissions: ['hr.leave.view'] });
    expect(cardByTitle(viewer.host, 'Opening balance / adjustment')).toBeUndefined();
    expect(cardByTitle(viewer.host, 'Accrual')).toBeUndefined();
    expect(cardByTitle(viewer.host, 'Employee balance and ledger')).toBeUndefined();
  });

  it('posts the accrual with the selected employee, leave type and date', async () => {
    const { fixture, page, http, host } = await boot({ permissions: ['hr.leave.view', 'hr.leave_balance.accrue'], types: [annual] });
    const card = cardByTitle(host, 'Accrual')!;
    const setInput = (name: string, value: string) => {
      const input = card.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
      input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
      input.dispatchEvent(new Event('input'));
    };
    setInput('accrueEmployee', '21');
    setInput('throughDate', '2026-06-30');
    const type = card.querySelector<HTMLSelectElement>('select[name="accrueType"]')!;
    type.value = type.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(type.options[1].value, (type.getAttribute('placeholder') ?? '').includes('HH:mm')) : type.options[1].value;
    type.dispatchEvent(new Event('change'));
    expect(page.accrual).toEqual({ employeeId: 21, leaveTypeId: 4, throughDate: '2026-06-30' });
    card.closest('form')!.dispatchEvent(new Event('submit'));
    const post = http.expectOne(`${base}/employees/21/accruals`);
    expect(post.request.body).toEqual({ leaveTypeId: 4, throughDate: '2026-06-30' });
    fixture.detectChanges();
    expect(Array.from(host.querySelectorAll<HTMLButtonElement>('button[type="submit"]')).every(b => b.disabled)).toBe(true);
    post.flush({ entries: [], isReplay: false });
    http.verify();
  });
});
