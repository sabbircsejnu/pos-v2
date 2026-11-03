import { CompanyDateService } from '../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';
import { PayrollPeriod } from '../../services/payroll-period.service';
import { PayrollPeriodControlPageComponent } from './payroll-period-control-page.component';

describe('Payroll period control page', () => {
  const base = `${environment.apiUrl}/payroll/v1/periods`;
  const permissions = new Set<string>();

  const period = (status: string): PayrollPeriod => ({
    id: 5, periodCode: '2026-10', periodStart: '2026-10-01', periodEnd: '2026-10-31',
    status, version: 3, closedReason: null
  });

  afterEach(() => {
    permissions.clear();
    TestBed.resetTestingModule();
  });

  async function create(...granted: string[]) {
    granted.forEach(permission => permissions.add(permission));
    await TestBed.configureTestingModule({
      imports: [PayrollPeriodControlPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.has(permission) } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollPeriodControlPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  async function settle(fixture: ComponentFixture<unknown>) {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function loadPeriod(
    fixture: ComponentFixture<PayrollPeriodControlPageComponent>,
    http: HttpTestingController,
    status: string
  ) {
    const input = fixture.nativeElement.querySelector('input[name="periodId"]') as HTMLInputElement;
    type(input, '5');
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http.expectOne(`${base}/5`).flush(period(status));
    await settle(fixture);
  }

  function type(element: HTMLInputElement | HTMLSelectElement, value: string) {
    element.value = element.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (element.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
    element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  function field(root: HTMLElement, label: string) {
    const target = Array.from(root.querySelectorAll('label'))
      .find(candidate => candidate.textContent?.trim().startsWith(label));
    expect(target, `label "${label}"`).toBeTruthy();
    const control = root.querySelector(`#${target!.htmlFor}`);
    expect(control, `control for "${label}"`).toBeTruthy();
    return control as HTMLInputElement | HTMLSelectElement;
  }

  function button(root: HTMLElement, text: string) {
    return Array.from(root.querySelectorAll('button'))
      .find(candidate => candidate.textContent?.replace(/\s+/g, ' ').trim() === text) as HTMLButtonElement | undefined;
  }

  describe('layout and kit rendering', () => {
    it('renders one h1 page header inside the main landmark with breadcrumbs and description', async () => {
      const { fixture } = await create();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelectorAll('main').length).toBe(1);
      const headings = root.querySelectorAll('h1');
      expect(headings.length).toBe(1);
      expect(headings[0].textContent).toContain('Payroll Period Control');
      expect(root.textContent).toContain('Lock, reopen, and assess close gates for a Payroll period.');
      const crumbs = root.querySelector('nav[aria-label="Breadcrumb"]');
      expect(crumbs?.textContent).toContain('Payroll');
      expect(crumbs?.querySelector('[aria-current="page"]')?.textContent).toContain('Period control');
    });

    it('ties the period ID label to a required numeric control and uses a kit submit button', async () => {
      const { fixture } = await create();
      const root = fixture.nativeElement as HTMLElement;
      const input = field(root, 'Payroll period ID') as HTMLInputElement;
      expect(input.name).toBe('periodId');
      expect(input.type).toBe('number');
      expect(input.required).toBe(true);
      expect(input.classList).toContain('ui-control');
      expect(root.querySelector('label')?.textContent).toContain('(required)');
      const load = button(root, 'Load')!;
      expect(load.type).toBe('submit');
      expect(load.hasAttribute('appUiButton')).toBe(true);
    });

    it('shows no period section before a period is loaded', async () => {
      const { fixture } = await create('payroll.period.lock', 'payroll.period.reopen', 'payroll.run.create');
      expect(fixture.nativeElement.textContent).not.toContain('Lock period');
      expect(fixture.nativeElement.textContent).not.toContain('Off-Cycle Payroll');
    });
  });

  describe('loading a period', () => {
    it('rejects an empty or invalid period ID with an alert and no request', async () => {
      const { fixture, http } = await create();
      (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      await settle(fixture);
      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Enter a valid Payroll period ID.');
      http.expectNone(request => request.url.startsWith(base));
    });

    it('shows a loading status while the request is in flight and disables Load', async () => {
      const { fixture, page, http } = await create();
      page.periodId = 5;
      page.load();
      await settle(fixture);
      const status = fixture.nativeElement.querySelector('[role="status"]') as HTMLElement;
      expect(status.textContent).toContain('Loading Payroll period...');
      expect(button(fixture.nativeElement, 'Load')!.disabled).toBe(true);
      http.expectOne(`${base}/5`).flush(period('Processing'));
      await settle(fixture);
      expect(fixture.nativeElement.textContent).not.toContain('Loading Payroll period...');
      expect(button(fixture.nativeElement, 'Load')!.disabled).toBe(false);
    });

    it('renders the loaded period as a titled card with a visible status badge and version', async () => {
      const { fixture, http } = await create();
      await loadPeriod(fixture, http, 'Approved');
      const root = fixture.nativeElement as HTMLElement;
      const card = Array.from(root.querySelectorAll('h2')).find(heading => heading.textContent?.includes('2026-10'));
      expect(card).toBeTruthy();
      const text = root.textContent!.replace(/\s+/g, ' ');
      expect(text).toContain("01/10/2026 – 31/10/2026");
      expect(root.querySelector('app-ui-badge')?.textContent).toContain('Approved');
      expect(text).toContain('Version 3');
      http.verify();
    });

    it('shows an error alert and hides any previous period when loading fails', async () => {
      const { fixture, http } = await create();
      await loadPeriod(fixture, http, 'Processing');
      expect(fixture.nativeElement.querySelector('h2')).toBeTruthy();
      const input = fixture.nativeElement.querySelector('input[name="periodId"]') as HTMLInputElement;
      type(input, '9');
      (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
      http.expectOne(`${base}/9`).flush({}, { status: 500, statusText: 'Server Error' });
      await settle(fixture);
      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Unable to load this Payroll period.');
      expect(fixture.nativeElement.textContent).not.toContain('2026-10-01');
    });
  });

  describe('permission-based actions', () => {
    it('offers no transition buttons and no reason field without the Payroll period permissions', async () => {
      const { fixture, http } = await create();
      await loadPeriod(fixture, http, 'Processing');
      const root = fixture.nativeElement as HTMLElement;
      expect(button(root, 'Lock period')).toBeUndefined();
      expect(button(root, 'Return unused period to Open')).toBeUndefined();
      expect(Array.from(root.querySelectorAll('label')).some(label => label.textContent?.startsWith('Reason'))).toBe(false);
    });

    it('shows Lock period for a Processing period and posts the expected version', async () => {
      const { fixture, http } = await create('payroll.period.lock');
      await loadPeriod(fixture, http, 'Processing');
      const lock = button(fixture.nativeElement, 'Lock period')!;
      expect(lock).toBeTruthy();
      expect(button(fixture.nativeElement, 'Reopen period')).toBeUndefined();
      lock.click();
      const request = http.expectOne(`${base}/5/lock`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ expectedVersion: 3 });
      request.flush(period('Locked'));
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('app-ui-badge')?.textContent).toContain('Locked');
      expect(button(fixture.nativeElement, 'Lock period')).toBeUndefined();
      http.verify();
    });

    it('hides Lock period when the period is not Processing', async () => {
      const { fixture, http } = await create('payroll.period.lock');
      await loadPeriod(fixture, http, 'Locked');
      expect(button(fixture.nativeElement, 'Lock period')).toBeUndefined();
    });

    it('requires a reason before reopen/return are enabled and sends the trimmed reason', async () => {
      const { fixture, http } = await create('payroll.period.reopen');
      await loadPeriod(fixture, http, 'Processing');
      const root = fixture.nativeElement as HTMLElement;
      const returnButton = button(root, 'Return unused period to Open')!;
      expect(returnButton.disabled).toBe(true);
      type(field(root, 'Reason') as HTMLInputElement, '   ');
      await settle(fixture);
      expect(button(root, 'Return unused period to Open')!.disabled).toBe(true);
      type(field(root, 'Reason') as HTMLInputElement, '  Opened by mistake ');
      await settle(fixture);
      expect(button(root, 'Return unused period to Open')!.disabled).toBe(false);
      button(root, 'Return unused period to Open')!.click();
      const request = http.expectOne(`${base}/5/return-to-open`);
      expect(request.request.body).toEqual({ expectedVersion: 3, reason: 'Opened by mistake' });
      request.flush(period('Open'));
      await settle(fixture);
      http.verify();
    });

    it('shows Reopen period for a Locked period but not Return to Open', async () => {
      const first = await create('payroll.period.reopen');
      await loadPeriod(first.fixture, first.http, 'Locked');
      expect(button(first.fixture.nativeElement, 'Reopen period')).toBeTruthy();
      expect(button(first.fixture.nativeElement, 'Return unused period to Open')).toBeUndefined();
    });

    it('does not show Reopen period for a Processing period', async () => {
      const { fixture, http } = await create('payroll.period.reopen');
      await loadPeriod(fixture, http, 'Processing');
      expect(button(fixture.nativeElement, 'Reopen period')).toBeUndefined();
    });

    it('requires the close permission and a reason for Assess and close on an Approved period', async () => {
      const withoutClose = await create('payroll.period.reopen');
      await loadPeriod(withoutClose.fixture, withoutClose.http, 'Approved');
      expect(button(withoutClose.fixture.nativeElement, 'Assess and close')).toBeUndefined();
      TestBed.resetTestingModule();
      permissions.clear();

      const { fixture, http } = await create('payroll.period.close', 'payroll.period.reopen');
      await loadPeriod(fixture, http, 'Approved');
      const root = fixture.nativeElement as HTMLElement;
      expect(button(root, 'Assess and close')!.disabled).toBe(true);
      type(field(root, 'Reason') as HTMLInputElement, 'Month end');
      await settle(fixture);
      expect(button(root, 'Assess and close')!.disabled).toBe(false);
    });

    it('shows the close gates in a named table region with visible Passed/Blocked text and the outcome heading', async () => {
      const { fixture, http } = await create('payroll.period.close', 'payroll.period.reopen');
      await loadPeriod(fixture, http, 'Approved');
      const root = fixture.nativeElement as HTMLElement;
      type(field(root, 'Reason') as HTMLInputElement, 'Month end');
      await settle(fixture);
      button(root, 'Assess and close')!.click();
      const close = http.expectOne(`${base}/5/close`);
      expect(close.request.body.reason).toBe('Month end');
      expect(close.request.body.expectedVersion).toBe(3);
      close.flush({
        payrollPeriodId: 5, status: 'Approved', version: 3, assessmentId: 1, policyVersion: 1,
        closed: false, isReplay: false,
        gates: [
          { code: 'RUNS_APPROVED', passed: true, blocking: true, message: 'All runs are approved.', referenceId: null },
          { code: 'PAYMENTS_RECONCILED', passed: false, blocking: true, message: 'Payments are not reconciled.', referenceId: null }
        ]
      }, { status: 409, statusText: 'Conflict' });
      await settle(fixture);

      const heading = Array.from(root.querySelectorAll('h2')).find(h => h.textContent?.includes('Close is blocked'));
      expect(heading).toBeTruthy();
      const region = root.querySelector('[role="region"][aria-label="Close gates"]') as HTMLElement;
      expect(region).toBeTruthy();
      const rows = Array.from(region.querySelectorAll('tbody tr')).map(row => row.textContent!.replace(/\s+/g, ' ').trim());
      expect(rows.length).toBe(2);
      expect(rows[0]).toContain('Passed');
      expect(rows[0]).toContain('RUNS_APPROVED');
      expect(rows[0]).toContain('All runs are approved.');
      expect(rows[1]).toContain('Blocked');
      expect(rows[1]).toContain('PAYMENTS_RECONCILED');
      expect(root.querySelector('[aria-live="polite"] [role="region"]')).toBeTruthy();
      expect(root.querySelector('.ui-table__footer')).toBeNull();
      http.verify();
    });

    it('announces a successful close with a "Period closed" heading', async () => {
      const { fixture, page, http } = await create('payroll.period.close');
      page.closeResult.set({
        payrollPeriodId: 5, status: 'Closed', version: 4, assessmentId: 2, policyVersion: 1,
        closed: true, isReplay: false,
        gates: [{ code: 'RUNS_APPROVED', passed: true, blocking: true, message: 'OK', referenceId: null }]
      });
      page.period.set({ ...period('Closed'), version: 4 });
      await settle(fixture);
      expect(Array.from(fixture.nativeElement.querySelectorAll('h2')).some(h => (h as HTMLElement).textContent?.includes('Period closed'))).toBe(true);
      http.verify();
    });

    it('shows a close failure alert when the response is not a close assessment', async () => {
      const { fixture, page, http } = await create('payroll.period.close', 'payroll.period.reopen');
      await loadPeriod(fixture, http, 'Approved');
      page.reason = 'Month end';
      page.close();
      http.expectOne(`${base}/5/close`).flush({ title: 'boom' }, { status: 500, statusText: 'Server Error' });
      await settle(fixture);
      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Close assessment failed. Reload the period and retry.');
    });
  });

  describe('correction and off-cycle runs', () => {
    it('shows the correction card only with both run permissions and a Locked/Approved/Posted/Closed period', async () => {
      const onlyCreate = await create('payroll.run.create');
      await loadPeriod(onlyCreate.fixture, onlyCreate.http, 'Locked');
      expect(onlyCreate.fixture.nativeElement.textContent).not.toContain('Payroll correction');
      TestBed.resetTestingModule();
      permissions.clear();

      const processing = await create('payroll.run.create', 'payroll.run.reopen');
      await loadPeriod(processing.fixture, processing.http, 'Processing');
      expect(processing.fixture.nativeElement.textContent).not.toContain('Payroll correction');
      TestBed.resetTestingModule();
      permissions.clear();

      const { fixture, http } = await create('payroll.run.create', 'payroll.run.reopen');
      await loadPeriod(fixture, http, 'Locked');
      const root = fixture.nativeElement as HTMLElement;
      expect(Array.from(root.querySelectorAll('h2')).some(h => h.textContent?.includes('Payroll correction'))).toBe(true);
      expect(root.textContent).toContain('Create a linked correction run; the approved run remains unchanged.');
      ['Original run ID', 'Original run version', 'Correction type', 'Affected employee IDs (comma-separated)',
        'Correction reference', 'Correction reason'].forEach(label => field(root, label));
    });

    it('keeps Create correction run disabled until every field is valid, then posts the request', async () => {
      const { fixture, http } = await create('payroll.run.create', 'payroll.run.reopen');
      await loadPeriod(fixture, http, 'Locked');
      const root = fixture.nativeElement as HTMLElement;
      expect(button(root, 'Create correction run')!.disabled).toBe(true);

      type(field(root, 'Original run ID') as HTMLInputElement, '12');
      type(field(root, 'Original run version') as HTMLInputElement, '2');
      type(field(root, 'Correction type') as HTMLSelectElement, 'Leave');
      type(field(root, 'Affected employee IDs') as HTMLInputElement, '3, 3');
      type(field(root, 'Correction reference') as HTMLInputElement, 'COR-1');
      type(field(root, 'Correction reason') as HTMLInputElement, 'Wrong leave days');
      await settle(fixture);
      // Duplicate employee IDs are invalid.
      expect(button(root, 'Create correction run')!.disabled).toBe(true);

      type(field(root, 'Affected employee IDs') as HTMLInputElement, '3, 4');
      await settle(fixture);
      const submit = button(root, 'Create correction run')!;
      expect(submit.disabled).toBe(false);
      submit.click();
      const request = http.expectOne(`${base}/5/runs/12/corrections`);
      expect(request.request.body).toEqual(expect.objectContaining({
        correctionReference: 'COR-1', correctionType: 'Leave', reason: 'Wrong leave days',
        employeeIds: [3, 4], expectedPeriodVersion: 3, expectedOriginalRunVersion: 2
      }));
      request.flush({ id: 77, runType: 'Correction', status: 'Draft', version: 1, downstreamReversalRequired: true });
      http.expectOne(`${base}/5`).flush(period('Locked'));
      await settle(fixture);
      const status = Array.from(root.querySelectorAll('[role="status"]'))
        .find(element => element.textContent?.includes('Correction run 77 created as'));
      expect(status).toBeTruthy();
      expect(status!.textContent).toContain('Downstream payment/Accounting correction is required separately.');
      http.verify();
    });

    it('shows Off-Cycle Payroll only for a Processing period with the run create permission', async () => {
      const locked = await create('payroll.run.create');
      await loadPeriod(locked.fixture, locked.http, 'Locked');
      expect(locked.fixture.nativeElement.textContent).not.toContain('Off-Cycle Payroll');
      TestBed.resetTestingModule();
      permissions.clear();

      const noPermission = await create();
      await loadPeriod(noPermission.fixture, noPermission.http, 'Processing');
      expect(noPermission.fixture.nativeElement.textContent).not.toContain('Off-Cycle Payroll');
      TestBed.resetTestingModule();
      permissions.clear();

      const { fixture, http } = await create('payroll.run.create');
      await loadPeriod(fixture, http, 'Processing');
      const root = fixture.nativeElement as HTMLElement;
      expect(Array.from(root.querySelectorAll('h2')).some(h =>       h.textContent?.includes('Off-Cycle Payroll'))).toBe(true);
            expect(button(root, 'Create Off-Cycle run')!.disabled).toBe(true);

      type(field(root, 'Off-Cycle reference') as HTMLInputElement, 'OFF-1');
      type(field(root, 'Off-Cycle reason') as HTMLInputElement, 'Bonus');
      await settle(fixture);
      // Employee IDs are shared with the correction form and still empty, which is invalid.
      expect(button(root, 'Create Off-Cycle run')!.disabled).toBe(true);
    });

    it('shows a failure alert when a special run cannot be created', async () => {
      const { fixture, page, http } = await create('payroll.run.create');
      await loadPeriod(fixture, http, 'Processing');
      page.offCycleReference = 'OFF-1';
      page.offCycleReason = 'Bonus';
      page.employeeIdsText = '8';
      page.createOffCycle();
      const request = http.expectOne(`${base}/5/runs/off-cycle`);
      expect(request.request.body).toEqual(expect.objectContaining({
        runReference: 'OFF-1', reason: 'Bonus', scopeType: 'Business', employeeIds: [8], expectedPeriodVersion: 3
      }));
      request.flush({}, { status: 500, statusText: 'Server Error' });
      await settle(fixture);
      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Payroll special run creation failed. Reload the period and retry.');
    });
  });
});
