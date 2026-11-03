import { CompanyDateService } from '../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';
import { PayrollLoansPageComponent } from './payroll-loans-page.component';

describe('Payroll loans page', () => {
  const base = `${environment.apiUrl}/payroll/v1`;
  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[]) {
    await TestBed.configureTestingModule({
      imports: [PayrollLoansPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { hasPermission: (permission: string) => permissions.includes(permission) }
        }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollLoansPageComponent);
    fixture.detectChanges();
    return {
      fixture,
      page: fixture.componentInstance,
      http: TestBed.inject(HttpTestingController)
    };
  }

  it('hides confidential data and actions without loan view permission', async () => {
    const { fixture, page, http } = await create([]);
    expect(page.canView()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('You do not have employee loan view access.');
    expect(fixture.nativeElement.textContent).not.toContain('Create loan or advance request');
    http.expectNone(`${base}/loans`);
    http.verify();
  });

  it('loads and displays scoped history while exposing only permission-authorized actions', async () => {
    const { fixture, page, http } = await create(['payroll.loan.view']);
    page.employeeId = 1;
    page.loadLoans();
    expect(page.busy()).toBe(true);
    const request = http.expectOne(req =>
      req.url === `${base}/loans` && req.params.get('employeeId') === '1');
    expect(request.request.method).toBe('GET');
    request.flush([{
      id: 9,
      businessId: 1,
      employeeId: 1,
      operationalLocationProjectionId: 10,
      loanType: 'Loan',
      principalAmount: 1000,
      approvedAmount: 1000,
      disbursedDate: '2026-10-10',
      recoveryStartDate: '2026-11-01',
      recoveryMode: 'FixedMonthly',
      fixedInstallment: 500,
      outstandingAmount: 1000,
      status: 'Active',
      reason: 'Emergency',
      reference: null,
      approvalInstanceId: 4,
      createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z',
      submittedByUserId: 101,
      submittedAtUtc: '2026-10-10T00:00:00Z',
      decidedByUserId: 202,
      decidedAtUtc: '2026-10-10T00:00:00Z',
      decisionReason: 'Approved',
      version: 3,
      approvalSteps: [],
      approvalHistory: [],
      scheduleHistory: [],
      repayments: [],
      isReplay: false
    }]);
    fixture.detectChanges();
    expect(page.busy()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Loan #9 — Active');
    expect(fixture.nativeElement.textContent).toContain('Outstanding: 1000');
    expect(fixture.nativeElement.textContent).not.toContain('Approve & disburse');
    expect(fixture.nativeElement.textContent).not.toContain('Record repayment');
    http.verify();
  });

  it('shows a scoped access error without displaying confidential results', async () => {
    const { fixture, page, http } = await create(['payroll.loan.view']);
    page.employeeId = 1;
    page.loadLoans();
    http.expectOne(req => req.url === `${base}/loans`).flush(
      { message: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(page.busy()).toBe(false);
    expect(page.loans()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain(
      'Unable to load loan history. Verify access and try again.');
    http.verify();
  });
});

describe('Payroll loans page UI (shared kit)', () => {
  const base = `${environment.apiUrl}/payroll/v1`;
  afterEach(() => TestBed.resetTestingModule());

  function loan(overrides: Record<string, unknown> = {}) {
    return {
      id: 9, businessId: 1, employeeId: 1, operationalLocationProjectionId: 10, loanType: 'Loan',
      principalAmount: 1000, approvedAmount: null, disbursedDate: null, recoveryStartDate: '2026-11-01',
      recoveryMode: 'FixedMonthly', fixedInstallment: 500, outstandingAmount: 1000, status: 'Draft',
      reason: 'Emergency', reference: 'REF-1', approvalInstanceId: 4, createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z', submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null, version: 3,
      approvalSteps: [], approvalHistory: [], scheduleHistory: [], repayments: [], isReplay: false,
      ...overrides
    };
  }

  async function create(permissions: string[], loans?: ReturnType<typeof loan>[]) {
    await TestBed.configureTestingModule({
      imports: [PayrollLoansPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollLoansPageComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const host: HTMLElement = fixture.nativeElement;
    if (loans) {
      fixture.componentInstance.employeeId = 1;
      fixture.componentInstance.loadLoans();
      http.expectOne(request => request.url === `${base}/loans`).flush(loans);
      fixture.detectChanges();
    }
    return { fixture, page: fixture.componentInstance, http, host };
  }

  const buttonLabels = (host: HTMLElement) =>
    Array.from(host.querySelectorAll('button:not(.company-date-trigger)')).map(button => button.textContent!.trim());

  it('renders a single page-header h1 in the main landmark with the confidentiality notice', async () => {
    const { host } = await create(['payroll.loan.view']);
    expect(host.querySelectorAll('h1').length).toBe(1);
    expect(host.querySelector('main h1')!.textContent!.trim()).toBe('Employee Loans & Advances');
    expect(host.querySelector('app-ui-page-header')!.textContent)
      .toContain('Confidential loan records are limited to your authorized Business and location scope.');
  });

  it('shows only a named access alert and no forms without loan view permission', async () => {
    const { host } = await create([]);
    const alert = host.querySelector('app-ui-alert[role="alert"]')!;
    expect(alert.textContent).toContain('You do not have employee loan view access.');
    expect(host.querySelector('form')).toBeNull();
    expect(host.querySelector('input')).toBeNull();
    expect(host.querySelector('app-ui-card')).toBeNull();
  });

  it('ties the employee ID label to its control and loads the history on submit', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view']);
    const input = host.querySelector<HTMLInputElement>('input[name="employeeId"]')!;
    expect(host.querySelector(`label[for="${input.id}"]`)!.textContent).toContain('Employee ID');
    expect(input.getAttribute('aria-required')).toBe('true');
    await fixture.whenStable();
    input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('7', (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : '7';
    input.dispatchEvent(new Event('input'));
    expect(page.employeeId).toBe(7);
    host.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne(req => req.url === `${base}/loans` && req.params.get('employeeId') === '7');
    expect(request.request.method).toBe('GET');
    fixture.detectChanges();
    expect(host.querySelector('app-ui-state[data-kind="loading"]')!.textContent).toContain('Loading / saving...');
    expect(host.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    request.flush([]);
    fixture.detectChanges();
    expect(host.textContent).not.toContain('Loading / saving...');
    http.verify();
  });

  it('announces an invalid employee ID as an alert without calling the API', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view']);
    page.loadLoans();
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent).toContain('Enter a valid employee ID.');
    http.expectNone(`${base}/loans`);
    http.verify();
  });

  it('shows the empty state once an employee has no loans', async () => {
    const { host } = await create(['payroll.loan.view'], []);
    const state = host.querySelector('app-ui-state[data-kind="empty"]')!;
    expect(state.textContent).toContain('No loans or advances were found for this employee.');
  });

  it('shows the load error inside an alert and clears the loading state', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view']);
    page.employeeId = 1;
    page.loadLoans();
    http.expectOne(req => req.url === `${base}/loans`).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('Unable to load loan history. Verify access and try again.');
    expect(host.querySelector('app-ui-state[data-kind="loading"]')).toBeNull();
  });

  it('view-only users see loan facts and a status badge but no forms or workflow buttons', async () => {
    const { host } = await create(['payroll.loan.view'], [
      loan({ status: 'Active', approvedAmount: 900, disbursedDate: '2026-10-11', decisionReason: 'Approved',
        approvalSteps: [{ sequence: 1, decision: 'Approved' }] })
    ]);
    const cards = Array.from(host.querySelectorAll('app-ui-card'));
    const loanCard = cards.find(item => item.textContent!.includes('Loan #9 — Active'))!;
    expect(loanCard.querySelector('app-ui-badge')!.textContent!.trim()).toBe('Active');
    expect(loanCard.querySelector('app-ui-badge')!.getAttribute('data-variant')).toBe('success');
    const text = loanCard.textContent!.replace(/\s+/g, ' ');
    expect(text).toContain('Principal: 1000');
    expect(text).toContain('Approved: 900');
    expect(text).toContain('Outstanding: 1000');
    expect(text).toContain("Disbursed: 11/10/2026");
    expect(text).toContain('Recovery: FixedMonthly from 01/11/2026');
    expect(text).toContain('Emergency (REF-1)');
    expect(text).toContain('Decision: Approved');
    expect(text).toContain('Approval step 1: Approved');
    expect(buttonLabels(host)).toEqual(['Load loan history']);
    expect(host.querySelector('app-ui-alert[role="status"]')!.textContent)
      .toContain('Loan creation, schedule changes and repayment recording require loan manage permission.');
    expect(host.textContent).not.toContain('Create loan or advance request');
    expect(host.textContent).not.toContain('Update recovery schedule');
  });

  it('keeps the approval transitions tied to status and permission', async () => {
    const loans = [
      loan({ id: 1, status: 'Draft' }),
      loan({ id: 2, status: 'PendingApproval' }),
      loan({ id: 3, status: 'Active' }),
      loan({ id: 4, status: 'Settled' })
    ];
    const labelsFor = (host: HTMLElement, id: number) => {
      const card = Array.from(host.querySelectorAll('app-ui-card'))
        .find(item => item.querySelector('h2')?.textContent?.startsWith(`Loan #${id} `))!;
      return Array.from(card.querySelectorAll('button:not(.company-date-trigger)')).map(button => button.textContent!.trim());
    };

    const manager = await create(['payroll.loan.view', 'payroll.loan.manage'], loans);
    expect(labelsFor(manager.host, 1)).toEqual(['Submit for approval']);
    expect(labelsFor(manager.host, 2)).toEqual(['Cancel request']);
    expect(labelsFor(manager.host, 3)).toEqual(['Record repayment']);
    expect(labelsFor(manager.host, 4)).toEqual([]);
    TestBed.resetTestingModule();

    const approver = await create(['payroll.loan.view', 'payroll.loan.approve'], loans);
    expect(labelsFor(approver.host, 1)).toEqual([]);
    expect(labelsFor(approver.host, 2)).toEqual(['Approve & disburse', 'Reject']);
    expect(labelsFor(approver.host, 3)).toEqual([]);
    expect(approver.host.textContent).not.toContain('Record partial repayment or early settlement');
    TestBed.resetTestingModule();

    const full = await create(['payroll.loan.view', 'payroll.loan.manage', 'payroll.loan.approve'], loans);
    expect(labelsFor(full.host, 2)).toEqual(['Approve & disburse', 'Reject', 'Cancel request']);
  });

  it('submits, approves, rejects and cancels through the unchanged endpoints', async () => {
    const loans = [loan({ id: 1, status: 'Draft', version: 5 }), loan({ id: 2, status: 'PendingApproval', approvalInstanceId: 44 })];
    const { fixture, http, host } = await create(
      ['payroll.loan.view', 'payroll.loan.manage', 'payroll.loan.approve'], loans);
    const click = (label: string, index = 0) => Array.from(host.querySelectorAll('button'))
      .filter(button => button.textContent!.trim() === label)[index].click();
    const refresh = () => {
      http.expectOne(req => req.url === `${base}/loans`).flush(loans);
      fixture.detectChanges();
    };

    click('Submit for approval');
    const submit = http.expectOne(`${base}/loans/1/submit`);
    expect(submit.request.body).toEqual({ expectedVersion: 5 });
    submit.flush({});
    refresh();

    click('Approve & disburse');
    http.expectOne(`${base}/loans/approvals/44/approve`).flush({});
    refresh();
    click('Reject');
    http.expectOne(`${base}/loans/approvals/44/reject`).flush({});
    refresh();
    click('Cancel request');
    http.expectOne(`${base}/loans/approvals/44/cancel`).flush({});
    refresh();
    http.verify();
  });

  it('disables workflow buttons while saving and shows the failure alert', async () => {
    const { fixture, http, host } = await create(['payroll.loan.view', 'payroll.loan.manage'], [loan({ status: 'Draft' })]);
    const submit = Array.from(host.querySelectorAll('button')).find(b => b.textContent!.trim() === 'Submit for approval')!;
    submit.click();
    fixture.detectChanges();
    expect(Array.from(host.querySelectorAll<HTMLButtonElement>('button:not(.company-date-trigger)')).every(button => button.disabled)).toBe(true);
    http.expectOne(`${base}/loans/9/submit`).flush({}, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('The loan operation failed. Verify the request and your access, then try again.');
    expect(Array.from(host.querySelectorAll<HTMLButtonElement>('button')).some(button => button.disabled)).toBe(false);
  });

  it('binds the create form and shows the recovery-mode specific fields', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view', 'payroll.loan.manage'], []);
    await fixture.whenStable();
    const create$ = Array.from(host.querySelectorAll('app-ui-card'))
      .find(card => card.querySelector('h2')?.textContent === 'Create loan or advance request')!;
    expect(create$.querySelector('input[name="fixedInstallment"]')).not.toBeNull();
    expect(create$.querySelector('textarea')).toBeNull();

    const type = create$.querySelector<HTMLSelectElement>('select[name="loanType"]')!;
    type.value = type.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Advance', (type.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Advance';
    type.dispatchEvent(new Event('change'));
    const principal = create$.querySelector<HTMLInputElement>('input[name="principalAmount"]')!;
    principal.value = principal.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('250.5', (principal.getAttribute('placeholder') ?? '').includes('HH:mm')) : '250.5';
    principal.dispatchEvent(new Event('input'));
    expect(page.loanType).toBe('Advance');
    expect(page.principalAmount).toBe(250.5);

    const mode = create$.querySelector<HTMLSelectElement>('select[name="recoveryMode"]')!;
    mode.value = mode.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('CustomSchedule', (mode.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'CustomSchedule';
    mode.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(create$.querySelector('input[name="fixedInstallment"]')).toBeNull();
    const schedule = create$.querySelector<HTMLTextAreaElement>('textarea[name="customSchedule"]')!;
    expect(schedule.getAttribute('placeholder')).toBe('2026-11-01,500');
    expect(host.querySelector(`label[for="${schedule.id}"]`)!.textContent).toContain('Custom schedule');
    http.verify();
  });

  it('validates the create form exactly as before (required principal and schedule lines)', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view', 'payroll.loan.manage'], []);
    page.createLoan();
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('Employee ID and a positive principal amount are required.');

    page.newEmployeeId = 1;
    page.principalAmount = 100;
    page.recoveryMode = 'CustomSchedule';
    page.customScheduleText = 'bad line';
    page.createLoan();
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('Schedule line 1 must be YYYY-MM-DD,positive amount.');
    http.expectNone(`${base}/loans`);
    http.verify();
  });

  it('submits a fixed-monthly request with the unchanged payload', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view', 'payroll.loan.manage'], []);
    page.newEmployeeId = 3;
    page.principalAmount = 600;
    page.fixedInstallment = 100;
    page.reason = 'School fees';
    fixture.detectChanges();
    const createForm = Array.from(host.querySelectorAll('form'))
      .find(form => form.textContent!.includes('Create approval request'))!;
    createForm.dispatchEvent(new Event('submit'));
    const request = http.expectOne(`${base}/loans`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      employeeId: 3, loanType: 'Loan', principalAmount: 600, recoveryMode: 'FixedMonthly',
      fixedInstallment: 100, schedule: [], reason: 'School fees', reference: null
    });
    request.flush(loan({ employeeId: 3 }));
    http.expectOne(req => req.url === `${base}/loans` && req.params.get('employeeId') === '3').flush([]);
    http.verify();
  });

  it('shows the repayment form only for active loans and keeps its validation', async () => {
    const { fixture, page, http, host } = await create(['payroll.loan.view', 'payroll.loan.manage'], [
      loan({ id: 9, status: 'Active' }), loan({ id: 10, status: 'Draft' })
    ]);
    const repaymentForms = Array.from(host.querySelectorAll('form'))
      .filter(form => form.textContent!.includes('Record partial repayment or early settlement'));
    expect(repaymentForms.length).toBe(1);
    const amount = repaymentForms[0].querySelector<HTMLInputElement>('input[type="number"]')!;
    expect(host.querySelector(`label[for="${amount.id}"]`)!.textContent).toContain('Amount');
    page.recordRepayment(page.loans()[0]);
    fixture.detectChanges();
    expect(host.querySelector('app-ui-alert[role="alert"]')!.textContent)
      .toContain('A positive repayment amount and reason are required.');

    page.repaymentAmount = 50;
    page.repaymentReason = 'Cash';
    page.paymentDate = '2026-11-02';
    page.recordRepayment(page.loans()[0]);
    const request = http.expectOne(`${base}/loans/9/repayments`);
    expect(request.request.body).toMatchObject({ amount: 50, paymentDate: '2026-11-02', reason: 'Cash', reference: null });
    request.flush(loan());
    http.expectOne(req => req.url === `${base}/loans`).flush([]);
    http.verify();
  });

  it('offers the schedule update only to managers when an active loan is loaded', async () => {
    const manager = await create(['payroll.loan.view', 'payroll.loan.manage'], [loan({ status: 'Active' })]);
    const card = Array.from(manager.host.querySelectorAll('app-ui-card'))
      .find(item => item.querySelector('h2')?.textContent === 'Update recovery schedule')!;
    expect(card).toBeTruthy();
    const loanId = card.querySelector<HTMLInputElement>('input[name="scheduleLoanId"]')!;
    expect(card.querySelector(`label[for="${loanId.id}"]`)!.textContent).toContain('Active loan ID');
    expect(Array.from(card.querySelectorAll('button:not(.company-date-trigger)')).map(b => b.textContent!.trim())).toEqual(['Save schedule version']);
    TestBed.resetTestingModule();

    const viewer = await create(['payroll.loan.view'], [loan({ status: 'Active' })]);
    expect(viewer.host.textContent).not.toContain('Update recovery schedule');
    TestBed.resetTestingModule();

    const noActive = await create(['payroll.loan.view', 'payroll.loan.manage'], [loan({ status: 'Draft' })]);
    expect(noActive.host.textContent).not.toContain('Update recovery schedule');
  });

  it('presents schedule and repayment history as named tables inside a disclosure', async () => {
    const { host } = await create(['payroll.loan.view'], [loan({
      status: 'Active',
      scheduleHistory: [{
        id: 1, versionNo: 2, recoveryStartDate: '2026-11-01', recoveryMode: 'FixedMonthly',
        fixedInstallment: 500, status: 'Current', createdByUserId: 1, createdAtUtc: '2026-10-10T00:00:00Z',
        lines: [{ installmentNo: 1, dueDate: '2026-11-01', plannedAmount: 500, actualRecoveredAmount: 0, status: 'Planned' }]
      }],
      repayments: [{
        id: 5, amount: 75.5, paymentDate: '2026-10-20', reference: null, reason: 'Early payment',
        recordedByUserId: 1, recordedAtUtc: '2026-10-20T00:00:00Z', isReplay: false
      }]
    })]);
    const details = host.querySelector('details')!;
    expect(details.querySelector('summary')!.textContent).toContain('Schedule and repayment history');
    expect(details.querySelector('h4')!.textContent).toContain('Schedule v2 (Current) — FixedMonthly');
    const regions = Array.from(details.querySelectorAll('[role="region"]')).map(region => region.getAttribute('aria-label'));
    expect(regions).toEqual(['Schedule v2 lines for loan 9', 'Repayments for loan 9']);
    const cells = Array.from(details.querySelectorAll('tbody td')).map(cell => cell.textContent!.trim());
    expect(cells).toEqual(["01/11/2026", '500', '0', 'Planned', "20/10/2026", '75.5', 'Early payment']);
  });

  it('hides the history disclosure when there is nothing to show', async () => {
    const { host } = await create(['payroll.loan.view'], [loan()]);
    expect(host.querySelector('details')).toBeNull();
  });
});
