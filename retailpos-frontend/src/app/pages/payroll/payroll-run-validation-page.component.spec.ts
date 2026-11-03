import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';
import { PayrollRunValidationPageComponent } from './payroll-run-validation-page.component';

describe('Payroll run validation page', () => {
  const base = `${environment.apiUrl}/payroll/v1/periods`;

  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[]) {
    await TestBed.configureTestingModule({
      imports: [PayrollRunValidationPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { hasPermission: (permission: string) => permissions.includes(permission) }
        }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollRunValidationPageComponent);
    fixture.detectChanges();
    return {
      fixture,
      page: fixture.componentInstance,
      http: TestBed.inject(HttpTestingController)
    };
  }

  it('does not request confidential findings without both view permissions', async () => {
    const { fixture, page, http } = await create(['payroll.run.view']);

    expect(page.canView()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain(
      'Payroll run and salary view permissions are required');
    page.periodId = 4;
    page.runId = 8;
    page.load();

    http.expectNone(`${base}/4/runs/8/validation`);
    http.verify();
  });

  it('loads paginated findings without rendering salary amounts', async () => {
    const { fixture, page, http } = await create([
      'payroll.run.view',
      'payroll.salary.view'
    ]);
    page.periodId = 4;
    page.runId = 8;
    page.load();

    http.expectOne(`${base}/4/runs/8`).flush({
      id: 8, status: 'Draft', version: 1, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });
    const request = http.expectOne(req =>
      req.url === `${base}/4/runs/8/validation`
      && req.params.get('page') === '1'
      && req.params.get('pageSize') === '50');
    expect(request.request.method).toBe('GET');
    request.flush({
      payrollRunId: 8,
      payrollPeriodId: 4,
      runStatus: 'Draft',
      calculationVersion: 1,
      isCalculated: true,
      canSubmit: false,
      blockingErrorCount: 1,
      warningCount: 1,
      page: 1,
      pageSize: 50,
      totalFindings: 2,
      findings: [
        {
          code: 'MissingPaymentDestination',
          category: 'PaymentDetails',
          severity: 'BlockingError',
          employeeId: 15,
          relatedRunId: null,
          message: 'No active payment destination is effective for this employee by the Payroll period end date.'
        },
        {
          code: 'LeavePendingApproval',
          category: 'Leave',
          severity: 'Warning',
          employeeId: 15,
          relatedRunId: null,
          message: 'A leave request pending approval overlaps the Payroll period.'
        }
      ]
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('1 blocking errors');
    expect(fixture.nativeElement.textContent).toContain('Employee #15');
    expect(fixture.nativeElement.textContent).toContain('not ready for submission');
    expect(fixture.nativeElement.textContent).not.toContain('GrossEarnings');
    expect(fixture.nativeElement.textContent).not.toContain('NetPay');
    http.verify();
  });

  it('clears prior findings when the request fails', async () => {
    const { fixture, page, http } = await create([
      'payroll.run.view',
      'payroll.salary.view'
    ]);
    page.periodId = 4;
    page.runId = 8;
    page.load();
    http.expectOne(`${base}/4/runs/8`).flush({
      id: 8, status: 'Draft', version: 1, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });
    http.expectOne(req =>
      req.url === `${base}/4/runs/8/validation`
      && req.params.get('page') === '1').flush({
      message: 'forbidden'
    }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(page.result()).toBeNull();
    expect(page.busy()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain(
      'Unable to load Payroll validation. Verify run access and try again.');
    http.verify();
  });

  it('submits a ready Draft run and reloads its status', async () => {
    const { page, http } = await create([
      'payroll.run.view', 'payroll.salary.view', 'payroll.run.submit'
    ]);
    page.periodId = 4;
    page.runId = 8;
    page.load();
    http.expectOne(`${base}/4/runs/8`).flush({
      id: 8, status: 'Draft', version: 4, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });
    http.expectOne(req => req.url === `${base}/4/runs/8/validation`).flush({
      payrollRunId: 8, payrollPeriodId: 4, runStatus: 'Draft',
      calculationVersion: 1, isCalculated: true, canSubmit: true,
      blockingErrorCount: 0, warningCount: 0, page: 1, pageSize: 50,
      totalFindings: 0, findings: []
    });

    page.submit();
    const submission = http.expectOne(`${base}/4/runs/8/submit`);
    expect(submission.request.method).toBe('POST');
    expect(submission.request.body).toEqual({ expectedVersion: 4 });
    submission.flush({
      instanceId: 22, status: 'Pending', currentStepSequence: 1,
      isReplay: false, targetStatus: null
    });
    http.expectOne(`${base}/4/runs/8`).flush({
      id: 8, status: 'PendingApproval', version: 5, approvalInstanceId: 22,
      submittedByUserId: 10, submittedAtUtc: '2026-10-01T00:00:00Z',
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });
    http.expectOne(req => req.url === `${base}/4/runs/8/validation`).flush({
      payrollRunId: 8, payrollPeriodId: 4, runStatus: 'PendingApproval',
      calculationVersion: 1, isCalculated: true, canSubmit: false,
      blockingErrorCount: 0, warningCount: 0, page: 1, pageSize: 50,
      totalFindings: 0, findings: []
    });
    expect(page.run()?.approvalInstanceId).toBe(22);
    http.verify();
  });

  it('generates an immutable Payslip only for an approved run with confidential view permissions', async () => {
    const { fixture, page, http } = await create([
      'payroll.run.view', 'payroll.salary.view', 'payroll.payslip.view'
    ]);
    page.runId = 8;
    page.payslipEmployeeId = 15;
    page.run.set({
      id: 8, status: 'Approved', version: 5, approvalInstanceId: 22,
      submittedByUserId: 10, submittedAtUtc: '2026-10-01T00:00:00Z',
      decidedByUserId: 11, decidedAtUtc: '2026-10-02T00:00:00Z', decisionReason: null
    });

    page.generatePayslip();
    const request = http.expectOne(`${environment.apiUrl}/payroll/v1/runs/8/payslips/15`);
    expect(request.request.method).toBe('POST');
    request.flush({
      id: 'artifact-1', runId: 8, runRevision: 2, employeeResultId: 91,
      versionNo: 1, artifactType: 'Payslip', format: 'PDF', contentHash: 'abc',
      contentType: 'application/pdf', byteLength: 4096, templateVersion: 'payslip-v1',
      scanStatus: 'Clean', createdAtUtc: '2026-10-02T12:00:00Z', isReplay: false
    });
    fixture.detectChanges();

    expect(page.payslipArtifact()?.employeeResultId).toBe(91);
    expect(page.payslipMessage()).toBe('Payslip artifact generated and stored privately.');
    http.verify();
  });

  it('does not request Payslip generation when permission or approved-run state is missing', async () => {
    const { page, http } = await create(['payroll.payslip.view', 'payroll.salary.view']);
    page.runId = 8;
    page.payslipEmployeeId = 15;
    page.run.set({
      id: 8, status: 'Draft', version: 1, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });
    page.generatePayslip();

    http.expectNone(`${environment.apiUrl}/payroll/v1/runs/8/payslips/15`);
    expect(page.payslipError()).toContain('approved run');
    http.verify();
  });

  it('exposes download, print, and email actions only through their dedicated permissions', async () => {
    const { page } = await create([
      'payroll.payslip.view', 'payroll.salary.view', 'payroll.payslip.download'
    ]);

    expect(page.canUsePayslips()).toBe(true);
    expect(page.canDownloadPayslip()).toBe(true);
    expect(page.canPrintPayslip()).toBe(false);
    expect(page.canEmailPayslip()).toBe(false);
  });

  it('does not request a private download without download permission', async () => {
    const { page, http } = await create([
      'payroll.payslip.view', 'payroll.salary.view', 'payroll.payslip.print'
    ]);
    page.runId = 8;
    page.payslipEmployeeId = 15;
    page.run.set({
      id: 8, status: 'Approved', version: 2, approvalInstanceId: 12,
      submittedByUserId: 10, submittedAtUtc: '2026-10-01T00:00:00Z',
      decidedByUserId: 11, decidedAtUtc: '2026-10-02T00:00:00Z', decisionReason: null
    });
    page.payslipArtifact.set({
      id: 'artifact-1', runId: 8, runRevision: 1, employeeResultId: 91,
      versionNo: 1, artifactType: 'Payslip', format: 'PDF', contentHash: 'abc',
      contentType: 'application/pdf', byteLength: 128, templateVersion: 'payslip-v1',
      scanStatus: 'Clean', createdAtUtc: '2026-10-02T12:00:00Z', isReplay: false
    });

    page.downloadPayslip();

    http.expectNone(`${environment.apiUrl}/payroll/v1/runs/8/payslips/15/download`);
    http.verify();
  });

  it('requires export and confidential salary permissions', async () => {
    const exportOnly = await create(['payroll.export']);
    expect(exportOnly.page.canExport()).toBe(false);
    expect(exportOnly.fixture.nativeElement.textContent).not.toContain('Export CSV');
    exportOnly.http.verify();

    TestBed.resetTestingModule();
    const salaryOnly = await create(['payroll.salary.view']);
    expect(salaryOnly.page.canExport()).toBe(false);
    expect(salaryOnly.fixture.nativeElement.textContent).not.toContain('Export CSV');
    salaryOnly.http.verify();
  });

  it('downloads approved-run exports as blobs using safe run-based filenames', async () => {
    const { fixture, page, http } = await create([
      'payroll.run.view', 'payroll.export', 'payroll.salary.view'
    ]);
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:payroll-export');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    let filename = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      filename = this.download;
    });

    page.runId = 8;
    page.run.set({
      id: 8, status: 'Approved', version: 5, approvalInstanceId: 22,
      submittedByUserId: 10, submittedAtUtc: '2026-10-01T00:00:00Z',
      decidedByUserId: 11, decidedAtUtc: '2026-10-02T00:00:00Z', decisionReason: null
    });
    page.result.set({
      payrollRunId: 8, payrollPeriodId: 4, runStatus: 'Approved',
      calculationVersion: 1, isCalculated: true, canSubmit: false,
      blockingErrorCount: 0, warningCount: 0, page: 1, pageSize: 50,
      totalFindings: 0, findings: []
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Export CSV');

    vi.useFakeTimers();
    const exports = [
      { trigger: () => page.downloadCsv(), path: 'csv', file: 'payroll-run-8-register.csv' },
      { trigger: () => page.downloadExcel(), path: 'excel', file: 'payroll-run-8-register.xlsx' },
      { trigger: () => page.downloadPdfSummary(), path: 'pdf-summary', file: 'payroll-run-8-summary.pdf' }
    ];
    for (const item of exports) {
      item.trigger();
      const request = http.expectOne(`${environment.apiUrl}/payroll/v1/runs/8/exports/${item.path}`);
      expect(request.request.method).toBe('GET');
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['approved Payroll export']));
      expect(filename).toBe(item.file);
      expect(page.exportMessage()).toBe('Payroll export downloaded securely.');
      vi.advanceTimersByTime(60_000);
    }
    expect(createUrl).toHaveBeenCalledTimes(3);
    expect(revokeUrl).toHaveBeenCalledTimes(3);
    vi.useRealTimers();
    http.verify();
  });

  it('does not request exports without protected-run state', async () => {
    const { page, http } = await create([
      'payroll.run.view', 'payroll.export', 'payroll.salary.view'
    ]);
    page.runId = 8;
    page.run.set({
      id: 8, status: 'Draft', version: 1, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null
    });

    page.downloadCsv();

    http.expectNone(`${environment.apiUrl}/payroll/v1/runs/8/exports/csv`);
    expect(page.exportError()).toContain('approved run');
    http.verify();
  });

  describe('shared UI kit presentation', () => {
    const runResponse = (status: string, extra: Record<string, unknown> = {}) => ({
      id: 8, status, version: 3, approvalInstanceId: null,
      submittedByUserId: null, submittedAtUtc: null,
      decidedByUserId: null, decidedAtUtc: null, decisionReason: null,
      ...extra
    });
    const finding = (severity: string, category: string, employeeId: number | null, message: string) => ({
      code: `${category}-${severity}`, category, severity, employeeId, relatedRunId: null, message
    });
    const validation = (extra: Record<string, unknown> = {}) => ({
      payrollRunId: 8, payrollPeriodId: 4, runStatus: 'Draft',
      calculationVersion: 1, isCalculated: true, canSubmit: false,
      blockingErrorCount: 1, warningCount: 1, page: 1, pageSize: 50, totalFindings: 2,
      findings: [
        finding('Warning', 'Leave', 20, 'Leave pending approval.'),
        finding('BlockingError', 'PaymentDetails', 15, 'No payment destination.')
      ],
      ...extra
    });

    async function loaded(
      permissions: string[],
      run: Record<string, unknown> = runResponse('Draft'),
      result: Record<string, unknown> = validation()
    ) {
      const ctx = await create(permissions);
      ctx.page.periodId = 4;
      ctx.page.runId = 8;
      ctx.page.load();
      ctx.http.expectOne(`${base}/4/runs/8`).flush(run);
      ctx.http.expectOne(req => req.url === `${base}/4/runs/8/validation`).flush(result);
      ctx.fixture.detectChanges();
      return ctx;
    }

    const root = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
    const buttonNames = (element: HTMLElement) =>
      Array.from(element.querySelectorAll('button')).map(button => button.textContent?.trim());
    const buttonByText = (element: HTMLElement, text: string) =>
      Array.from(element.querySelectorAll('button')).find(button => button.textContent?.trim() === text);
    const bodyRows = (element: HTMLElement) => Array.from(element.querySelectorAll('tbody tr'));
    const alertTexts = (element: HTMLElement) =>
      Array.from(element.querySelectorAll('app-ui-alert[role="alert"]')).map(item => item.textContent ?? '');

    it('renders one h1 page header, a main landmark and a breadcrumb link', async () => {
      const { fixture } = await create(['payroll.run.view', 'payroll.salary.view']);
      const element = root(fixture);

      expect(element.querySelector('main')).not.toBeNull();
      const headings = element.querySelectorAll('h1');
      expect(headings.length).toBe(1);
      expect(headings[0].textContent).toContain('Payroll Validation Workbench');
      expect(element.textContent).toContain('Review blocking errors and warnings before Payroll submission.');
      const crumb = element.querySelector('nav[aria-label="Breadcrumb"] a') as HTMLAnchorElement;
      expect(crumb.textContent?.trim()).toBe('Dashboard');
      expect(crumb.getAttribute('href')).toBe('/dashboard');
      expect(element.querySelector('nav[aria-label="Breadcrumb"] [aria-current="page"]')?.textContent)
        .toContain('Validation workbench');
    });

    it('ties the run selection labels to required number inputs and keeps ngModel bindings', async () => {
      const { fixture, page, http } = await create(['payroll.run.view', 'payroll.salary.view']);
      const element = root(fixture);

      for (const [label, name] of [['Payroll period ID', 'periodId'], ['Payroll run ID', 'runId']]) {
        const labelElement = Array.from(element.querySelectorAll('label'))
          .find(item => item.textContent?.includes(label)) as HTMLLabelElement;
        const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        expect(labelElement.htmlFor).toBe(input.id);
        expect(input.type).toBe('number');
        expect(input.required).toBe(true);
        expect(input.getAttribute('aria-required')).toBe('true');
      }

      const period = element.querySelector('input[name="periodId"]') as HTMLInputElement;
      const run = element.querySelector('input[name="runId"]') as HTMLInputElement;
      period.value = '4';
      period.dispatchEvent(new Event('input'));
      run.value = '8';
      run.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(page.periodId).toBe(4);
      expect(page.runId).toBe(8);

      element.querySelector('form')!.dispatchEvent(new Event('submit'));
      http.expectOne(`${base}/4/runs/8`);
    });

    it('shows the permission alert instead of the run form without both view permissions', async () => {
      const { fixture } = await create(['payroll.salary.view']);
      const element = root(fixture);

      const alert = element.querySelector('app-ui-alert[role="alert"]');
      expect(alert?.textContent).toContain('Payroll run and salary view permissions are required');
      expect(element.querySelector('form')).toBeNull();
      expect(element.querySelector('input[name="periodId"]')).toBeNull();
    });

    it('shows a polite loading state while validating, then a danger alert on failure', async () => {
      const { fixture, page, http } = await create(['payroll.run.view', 'payroll.salary.view']);
      page.periodId = 4;
      page.runId = 8;
      page.load();
      fixture.detectChanges();
      const element = root(fixture);

      const loading = element.querySelector('app-ui-state[role="status"]');
      expect(loading?.textContent).toContain('Loading validation findings...');
      expect((buttonByText(element, 'Validate run') as HTMLButtonElement).disabled).toBe(true);

      http.expectOne(`${base}/4/runs/8`).flush({ message: 'nope' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(element.querySelector('app-ui-state')).toBeNull();
      const alert = element.querySelector('app-ui-alert[role="alert"]');
      expect(alert?.textContent).toContain('Unable to load Payroll run status.');
      expect((buttonByText(element, 'Validate run') as HTMLButtonElement).disabled).toBe(false);
    });

    it('renders findings in a named table region with status badges and employee text', async () => {
      const { fixture } = await loaded(['payroll.run.view', 'payroll.salary.view']);
      const element = root(fixture);

      const region = element.querySelector('[role="region"][aria-label="Validation findings"]');
      expect(region).not.toBeNull();
      expect(element.querySelector('app-ui-data-table input[type="search"]')).toBeNull();
      expect(element.querySelector('nav[aria-label="Pagination"]')).toBeNull();
      const rows = bodyRows(element);
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('Warning');
      expect(rows[0].textContent).toContain('Employee #20');
      expect(rows[1].querySelector('app-ui-badge')?.textContent?.trim()).toBe('BlockingError');
      expect(rows[1].textContent).toContain('No payment destination.');
      expect(element.querySelector('[role="status"]')?.textContent).toContain('1 blocking errors');
      expect(element.textContent).toContain('1 warnings');
      expect(Array.from(element.querySelectorAll('h2')).map(item => item.textContent)).toEqual(
        expect.arrayContaining([expect.stringContaining('Run #8')]));
    });

    it('sorts findings through accessible header buttons with aria-sort', async () => {
      const { fixture } = await loaded(['payroll.run.view', 'payroll.salary.view']);
      const element = root(fixture);
      const header = () => Array.from(element.querySelectorAll('th'))
        .find(cell => cell.textContent?.includes('Severity')) as HTMLTableCellElement;

      expect(header().getAttribute('aria-sort')).toBe('none');
      (header().querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(header().getAttribute('aria-sort')).toBe('ascending');
      expect(bodyRows(element)[0].textContent).toContain('BlockingError');

      (header().querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(header().getAttribute('aria-sort')).toBe('descending');
      expect(bodyRows(element)[0].textContent).toContain('Warning');
      const messageHeader = Array.from(element.querySelectorAll('th'))
        .find(cell => cell.textContent?.includes('Message')) as HTMLTableCellElement;
      expect(messageHeader.hasAttribute('aria-sort')).toBe(false);
    });

    it('shows an empty-findings state without a table and a ready alert when submission is clear', async () => {
      const { fixture } = await loaded(
        ['payroll.run.view', 'payroll.salary.view'],
        runResponse('Draft'),
        validation({ canSubmit: true, blockingErrorCount: 0, warningCount: 0, totalFindings: 0, findings: [] })
      );
      const element = root(fixture);

      expect(element.querySelector('app-ui-data-table')).toBeNull();
      expect(element.textContent).toContain('No validation findings were returned.');
      expect(element.querySelector('app-ui-alert[role="status"]')?.textContent)
        .toContain('Validation is clear for submission.');
      expect(element.textContent).toContain('0 blocking errors');
    });

    it('shows run status as a badge with approval and decision details', async () => {
      const { fixture } = await loaded(
        ['payroll.run.view', 'payroll.salary.view'],
        runResponse('Rejected', {
          approvalInstanceId: 22, submittedAtUtc: '2026-10-01T00:00:00Z',
          decidedAtUtc: '2026-10-02T00:00:00Z', decisionReason: 'Totals look wrong'
        })
      );
      const element = root(fixture);
      const badges = Array.from(element.querySelectorAll('app-ui-badge')).map(item => item.textContent?.trim());
      const text = element.textContent ?? '';

      expect(badges).toContain('Rejected');
      expect(text).toContain('Run status: Rejected');
      expect(text).toContain('Approval #22');
      expect(text).toContain('Submitted');
      expect(text).toContain('Decided');
      expect(text).toContain('Totals look wrong');
    });

    it('pages findings with Previous/Next buttons that keep the existing request contract', async () => {
      const { fixture, page, http } = await loaded(
        ['payroll.run.view', 'payroll.salary.view'],
        runResponse('Draft'),
        validation({ totalFindings: 120 })
      );
      const element = root(fixture);
      const nav = element.querySelector('nav[aria-label="Findings pagination"]') as HTMLElement;

      expect(nav.textContent).toContain('Page 1 · 120 findings');
      expect((buttonByText(nav, 'Previous') as HTMLButtonElement).disabled).toBe(true);
      expect((buttonByText(nav, 'Next') as HTMLButtonElement).disabled).toBe(false);

      buttonByText(nav, 'Next')!.click();
      expect(page.page()).toBe(2);
      http.expectOne(`${base}/4/runs/8`).flush(runResponse('Draft'));
      const request = http.expectOne(req =>
        req.url === `${base}/4/runs/8/validation` && req.params.get('page') === '2');
      request.flush(validation({ page: 2, totalFindings: 120 }));
      fixture.detectChanges();
      expect(nav.textContent).toContain('Page 2 · 120 findings');
      expect((buttonByText(nav, 'Previous') as HTMLButtonElement).disabled).toBe(false);
    });

    it('shows the Submit action only for a clear Draft run with submit permission', async () => {
      const ready = { canSubmit: true, blockingErrorCount: 0, warningCount: 0, totalFindings: 0, findings: [] };
      const allowed = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.run.submit'], runResponse('Draft'), validation(ready));
      expect(buttonNames(root(allowed.fixture))).toContain('Submit for approval');
      expect(root(allowed.fixture).querySelector('[role="group"][aria-label="Run actions"]')).not.toBeNull();

      TestBed.resetTestingModule();
      const noPermission = await loaded(
        ['payroll.run.view', 'payroll.salary.view'], runResponse('Draft'), validation(ready));
      expect(buttonNames(root(noPermission.fixture))).not.toContain('Submit for approval');
      expect(root(noPermission.fixture).querySelector('[role="group"][aria-label="Run actions"]')).toBeNull();

      TestBed.resetTestingModule();
      const blocked = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.run.submit'], runResponse('Draft'), validation());
      expect(buttonNames(root(blocked.fixture))).not.toContain('Submit for approval');
      expect(root(blocked.fixture).querySelector('app-ui-alert[role="alert"]')?.textContent)
        .toContain('This run is not ready for submission.');
    });

    it('shows Approve and Reject only to approvers of a pending run and posts the decision', async () => {
      const pending = runResponse('PendingApproval', { approvalInstanceId: 22 });
      const approver = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.run.approve'], pending);
      const element = root(approver.fixture);
      expect(buttonNames(element)).toEqual(expect.arrayContaining(['Approve', 'Reject']));

      buttonByText(element, 'Approve')!.click();
      const decision = approver.http.expectOne(`${environment.apiUrl}/payroll/v1/approvals/runs/22/approve`);
      expect(decision.request.method).toBe('POST');
      expect(approver.page.busy()).toBe(true);
      approver.fixture.detectChanges();
      expect((buttonByText(element, 'Reject') as HTMLButtonElement).disabled).toBe(true);
      expect((buttonByText(element, 'Approve') as HTMLButtonElement).disabled).toBe(true);

      TestBed.resetTestingModule();
      const viewer = await loaded(['payroll.run.view', 'payroll.salary.view'], pending);
      expect(buttonNames(root(viewer.fixture))).not.toContain('Approve');
      expect(buttonNames(root(viewer.fixture))).not.toContain('Reject');
    });

    it('keeps the confidential sections hidden for runs that are not approved', async () => {
      const { fixture } = await loaded([
        'payroll.run.view', 'payroll.salary.view', 'payroll.payslip.view',
        'payroll.export', 'payroll.accounting.post'
      ]);
      const text = root(fixture).textContent ?? '';

      expect(text).not.toContain('Confidential Payslip');
      expect(text).not.toContain('Payroll Exports');
      expect(text).not.toContain('Accounting posting');
    });

    it('exposes Payslip actions by their own permissions and enables them once an artifact exists', async () => {
      const { fixture, page } = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.payslip.view', 'payroll.payslip.download'],
        runResponse('Approved'), validation({ canSubmit: false })
      );
      const element = root(fixture);
      const employee = element.querySelector('input[name="payslipEmployeeId"]') as HTMLInputElement;
      const label = Array.from(element.querySelectorAll('label'))
        .find(item => item.textContent?.includes('Employee ID')) as HTMLLabelElement;

      expect(element.textContent).toContain('Confidential Payslip');
      expect(label.htmlFor).toBe(employee.id);
      expect(buttonNames(element)).toEqual(expect.arrayContaining(['Generate / reuse PDF', 'Download PDF']));
      expect(buttonNames(element)).not.toContain('Print');
      expect(buttonNames(element)).not.toContain('Email securely');
      expect((buttonByText(element, 'Download PDF') as HTMLButtonElement).disabled).toBe(true);

      page.payslipArtifact.set({
        id: 'artifact-1', runId: 8, runRevision: 1, employeeResultId: 91,
        versionNo: 3, artifactType: 'Payslip', format: 'PDF', contentHash: 'abc',
        contentType: 'application/pdf', byteLength: 128, templateVersion: 'payslip-v1',
        scanStatus: 'Clean', createdAtUtc: '2026-10-02T12:00:00Z', isReplay: false
      });
      page.payslipError.set('Payslip download failed. Verify access and try again.');
      fixture.detectChanges();

      expect((buttonByText(element, 'Download PDF') as HTMLButtonElement).disabled).toBe(false);
      expect(element.textContent).toContain('Version 3');
      expect(element.textContent).toContain('Clean');
      expect(alertTexts(element).join(' ')).toContain('Payslip download failed.');
    });

    it('shows export buttons as busy and disabled while one export is prepared', async () => {
      const { fixture, page } = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.export'], runResponse('Approved'));
      const element = root(fixture);

      expect(buttonNames(element)).toEqual(expect.arrayContaining(['Export CSV', 'Export Excel', 'Export PDF Summary']));
      page.exportBusy.set('csv');
      fixture.detectChanges();

      expect(buttonNames(element)).toContain('Preparing CSV...');
      expect(element.textContent).toContain('Preparing confidential Payroll export...');
      for (const name of ['Preparing CSV...', 'Export Excel', 'Export PDF Summary']) {
        expect((buttonByText(element, name) as HTMLButtonElement).disabled).toBe(true);
      }

      page.exportBusy.set(null);
      page.exportMessage.set('Payroll export downloaded securely.');
      page.exportError.set('Payroll export failed. Verify access and try again.');
      fixture.detectChanges();
      expect(element.querySelector('app-ui-alert[role="status"]')?.textContent)
        .toContain('Payroll export downloaded securely.');
      expect(alertTexts(element).join(' ')).toContain('Payroll export failed.');
    });

    it('shows the Accounting posting status as a badge and blocks a second posting', async () => {
      const { fixture, page } = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'payroll.accounting.post'], runResponse('Approved'));
      const element = root(fixture);

      expect(element.textContent).toContain('Accounting posting');
      const post = buttonByText(element, 'Post Payroll expense') as HTMLButtonElement;
      expect(post.disabled).toBe(false);

      page.accountingPosting.set({
        id: 1, businessId: 1, payrollPeriodId: 4, payrollRunId: 8, runRevision: 1,
        postingType: 'PayrollExpense', postingReference: 'ref', idempotencyKey: 'key',
        requestPayloadHash: null, conflictPayloadHash: null, accountingJournalId: 9,
        accountingReference: 'JV-9', status: 'Posted', errorMessage: 'Earlier attempt failed',
        attemptCount: 2, version: 3, postedAtUtc: '2026-10-02T00:00:00Z'
      });
      fixture.detectChanges();

      expect((buttonByText(element, 'Post Payroll expense') as HTMLButtonElement).disabled).toBe(true);
      expect(element.querySelector('p[role="status"] app-ui-badge')?.textContent?.trim()).toBe('Posted');
      expect(element.textContent).toContain('Journal JV-9');
      expect(alertTexts(element).join(' ')).toContain('Earlier attempt failed');
    });

    it('lists Accounting mappings in a table and keeps the mapping form bindings', async () => {
      const { fixture, page, http } = await loaded(
        ['payroll.run.view', 'payroll.salary.view', 'accounting.configuration.edit'], runResponse('Draft'));
      http.expectOne(`${environment.apiUrl}/payroll/v1/accounting-mappings`).flush([]);
      fixture.detectChanges();
      const element = root(fixture);

      expect(element.textContent).toContain('No Payroll Accounting mappings');
      page.accountingMappings.set([{
        componentType: 'Earning', componentCode: 'BASIC', accountId: 5,
        accountCode: '5100', accountName: 'Salaries', isActive: true, version: 1
      }]);
      fixture.detectChanges();
      const region = element.querySelector('[role="region"][aria-label="Payroll Accounting mappings"]');
      expect(region?.textContent).toContain('BASIC');
      expect(region?.textContent).toContain('Salaries');

      const code = element.querySelector('input[name="mappingCode"]') as HTMLInputElement;
      const label = Array.from(element.querySelectorAll('label'))
        .find(item => item.textContent?.includes('Component code')) as HTMLLabelElement;
      expect(label.htmlFor).toBe(code.id);
      code.value = 'OT';
      code.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(page.mappingCode).toBe('OT');
      expect((element.querySelector('select[name="mappingType"]') as HTMLSelectElement).options.length).toBe(3);
    });
  });
});
