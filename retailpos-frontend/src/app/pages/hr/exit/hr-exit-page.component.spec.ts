import { CompanyDateService } from '../../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrExitCase, PayrollFinalSettlement } from '../../../services/hr-exit.service';
import { HrExitPageComponent } from './hr-exit-page.component';

describe('HR exit and clearance workbench', () => {
  const base = `${environment.apiUrl}/hr/v1/exits`;
  const permissions = new Set(['hr.exit.view', 'hr.exit.initiate', 'hr.exit.clearance']);

  afterEach(() => {
    TestBed.resetTestingModule();
    permissions.delete('payroll.final_settlement.view');
    permissions.delete('payroll.final_settlement.calculate');
    permissions.delete('payroll.salary.view');
  });

  async function create(hrEnabled = true, payrollEnabled = false) {
    await TestBed.configureTestingModule({
      imports: [HrExitPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.has(permission) } }
      ]
    }).compileComponents();
    TestBed.inject(HrPayrollCapabilityStore).setVerified({
      businessId: 1, hrEnabled, attendanceEnabled: false,
      payrollEnabled, deviceIntegrationEnabled: false
    });
    const fixture = TestBed.createComponent(HrExitPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('blocks backend access while HR capability is disabled', async () => {
    const { fixture, http } = await create(false);
    http.expectNone(request => request.url.startsWith(base));
    expect(fixture.nativeElement.textContent).toContain('HR capability is disabled');
    http.verify();
  });

  it('loads scoped cases/templates and creates an exit case with the authorized API', async () => {
    const { fixture, page, http } = await create();
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);

    Object.assign(page.newExit, {
      employeeId: 42,
      exitType: 'Resignation',
      initiatedDate: '2026-10-09',
      proposedLastWorkingDate: '2026-10-30',
      reason: 'Voluntary resignation'
    });
    page.initiate();
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(page.newExit);
    request.flush({
      id: 8, employeeId: 42, employeeCode: 'E42', employeeName: 'Employee 42',
      exitType: 'Resignation', initiatedDate: '2026-10-09', proposedLastWorkingDate: '2026-10-30',
      actualLastWorkingDate: null, reason: 'Voluntary resignation', status: 'Draft',
      approvalInstanceId: null, primaryLocationProjectionId: 11, locationType: 'Outlet',
      clearanceTemplateId: 2, clearanceTemplateVersionNo: 1, version: 1,
      clearanceItems: [], events: []
    });
    fixture.detectChanges();
    expect(page.cases()[0].id).toBe(8);
    expect(fixture.nativeElement.textContent).toContain('Employee 42');
    http.verify();
  });

  it('creates immutable template versions with checklist data', async () => {
    const { page, http } = await create();
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);
    page.template.code = 'EXIT-BASE';
    page.template.name = 'Standard exit';
    page.template.items[0].code = 'ASSET';
    page.template.items[0].name = 'Return assets';
    page.createTemplate();
    const request = http.expectOne(`${base}/templates`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.items[0]).toEqual({
      sequence: 1, code: 'ASSET', name: 'Return assets',
      responsibleRole: null, responsibleReference: null, isMandatory: true
    });
    request.flush({
      id: 4, code: 'EXIT-BASE', name: 'Standard exit', scopeType: 'Business',
      scopeReference: null, versionNo: 1, effectiveFrom: page.template.effectiveFrom,
      effectiveTo: null, isActive: true, items: []
    });
    http.expectOne(`${base}/templates`).flush([]);
    http.verify();
  });

  it('keeps final settlement hidden while the Payroll capability is disabled', async () => {
    permissions.add('payroll.final_settlement.view');
    permissions.add('payroll.final_settlement.calculate');
    permissions.add('payroll.salary.view');
    const { fixture, http } = await create(true, false);
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Payroll final settlement');
    http.expectNone(request => request.url.includes('/final-settlement'));
    http.verify();
  });

  it('calculates and displays a settlement only with Payroll capability and permissions', async () => {
    permissions.add('payroll.final_settlement.view');
    permissions.add('payroll.salary.view');
    permissions.add('payroll.final_settlement.calculate');
    const { fixture, page, http } = await create(true, true);
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);
    const exitCase = {
      id: 22, employeeId: 42, employeeCode: 'E42', employeeName: 'Employee 42',
      exitType: 'Resignation', initiatedDate: '2026-10-09',
      proposedLastWorkingDate: '2026-10-30', actualLastWorkingDate: null,
      reason: 'Voluntary resignation', status: 'Approved', approvalInstanceId: 9,
      primaryLocationProjectionId: 11, locationType: 'Outlet',
      clearanceTemplateId: 2, clearanceTemplateVersionNo: 1, version: 3,
      clearanceItems: [], events: []
    } satisfies HrExitCase;
    page.cases.set([exitCase]);
    fixture.detectChanges();

    page.calculateFinalSettlement(exitCase);
    const request = http.expectOne(`${base}/${exitCase.id}/final-settlement/calculate`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.expectedExitVersion).toBe(exitCase.version);
    expect(request.request.body.idempotencyKey).toBeTruthy();
    request.flush({
      id: 7, exitCaseId: exitCase.id, employeeId: exitCase.employeeId, revisionNo: 1,
      settlementDate: '2026-10-30', currencyCode: 'BDT', decimalPrecision: 2,
      roundingMode: 'AwayFromZero', grossDue: 1000, totalDeductions: 100,
      netSettlement: 900, calculationInputHash: 'a'.repeat(64), snapshotHash: 'b'.repeat(64),
      snapshotJson: '{}', calculatedAtUtc: '2026-10-30T12:00:00Z', calculatedByUserId: 5,
      isReplay: false,
      lines: [{
        lineNo: 1, componentCode: 'PAYROLL_NET_DUE',
        componentName: 'Outstanding approved Payroll net pay', lineType: 'Earning',
        amount: 1000, isApplicable: true, notApplicableReason: null,
        sourceType: 'PayrollEmployeeResult', sourceId: '31',
        evidenceSnapshotJson: '{}'
      }]
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.toLowerCase()).toContain('revision 1');
    expect(fixture.nativeElement.textContent).toContain('Net 900');
    expect(fixture.nativeElement.textContent).toContain('Snapshot hash');
    http.verify();
  });

  it('uses server completion preview before completing an approved exit', async () => {
    permissions.add('payroll.final_settlement.view');
    permissions.add('payroll.salary.view');
    const { page, http } = await create(true, true);
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);
    const exitCase = {
      id: 41, employeeId: 42, employeeCode: 'E42', employeeName: 'Employee 42',
      exitType: 'Resignation', initiatedDate: '2026-10-09',
      proposedLastWorkingDate: '2026-10-30', actualLastWorkingDate: null,
      reason: 'Voluntary resignation', status: 'Approved', approvalInstanceId: 9,
      primaryLocationProjectionId: 11, locationType: 'Outlet',
      clearanceTemplateId: 2, clearanceTemplateVersionNo: 1, version: 3,
      clearanceItems: [], events: []
    } satisfies HrExitCase;
    page.cases.set([exitCase]);
    page.actualDate[exitCase.id] = '2026-10-30';
    page.completeReason[exitCase.id] = 'All obligations closed';

    page.complete(exitCase);
    const preview = http.expectOne(`${base}/${exitCase.id}/completion-preview`);
    expect(preview.request.method).toBe('GET');
    preview.flush({
      exitCaseId: exitCase.id,
      exitVersion: exitCase.version,
      payrollEnabled: true,
      canComplete: true,
      pendingMandatoryClearanceCount: 0,
      reasonlessNotApplicableCount: 0,
      blockers: [],
      settlement: {
        settlementId: 7,
        revisionNo: 1,
        status: 'Approved',
        completionMode: 'PositiveSettlement',
        netSettlement: 900,
        currencyCode: 'BDT',
        submittedEvidenceHash: 'c'.repeat(64),
        paymentRequired: true,
        activePaymentCount: 1,
        outstandingAmount: 0,
        paymentsSatisfied: true,
        reconciliationRequired: true,
        reconciliationSatisfied: true,
        accountingRequired: true,
        requiredPostingType: 'Expense',
        accountingSatisfied: true,
        requiredPaymentAccountingCount: 1,
        postedPaymentAccountingCount: 1
      }
    });
    const complete = http.expectOne(`${base}/${exitCase.id}/complete`);
    expect(complete.request.method).toBe('POST');
    expect(complete.request.body.actualLastWorkingDate).toBe('2026-10-30');
    expect(complete.request.body.reason).toBe('All obligations closed');
    complete.flush({ ...exitCase, actualLastWorkingDate: '2026-10-30', status: 'Completed', version: 4 });
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.verify();
  });

  it('surfaces server completion blockers and does not post complete when preview is blocked', async () => {
    permissions.add('payroll.final_settlement.view');
    permissions.add('payroll.salary.view');
    const { fixture, page, http } = await create(true, true);
    http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
    http.expectOne(`${base}/templates`).flush([]);
    const exitCase = {
      id: 51, employeeId: 42, employeeCode: 'E42', employeeName: 'Employee 42',
      exitType: 'Resignation', initiatedDate: '2026-10-09',
      proposedLastWorkingDate: '2026-10-30', actualLastWorkingDate: null,
      reason: 'Voluntary resignation', status: 'Approved', approvalInstanceId: 9,
      primaryLocationProjectionId: 11, locationType: 'Outlet',
      clearanceTemplateId: 2, clearanceTemplateVersionNo: 1, version: 3,
      clearanceItems: [], events: []
    } satisfies HrExitCase;
    page.cases.set([exitCase]);
    page.actualDate[exitCase.id] = '2026-10-30';
    page.completeReason[exitCase.id] = 'All obligations closed';

    page.complete(exitCase);
    http.expectOne(`${base}/${exitCase.id}/completion-preview`).flush({
      exitCaseId: exitCase.id,
      exitVersion: exitCase.version,
      payrollEnabled: true,
      canComplete: false,
      pendingMandatoryClearanceCount: 0,
      reasonlessNotApplicableCount: 0,
      blockers: ['Verified final settlement payments must be fully reconciled before exit completion.'],
      settlement: null
    });

    fixture.detectChanges();
    http.expectNone(`${base}/${exitCase.id}/complete`);
    expect(page.error()).toContain('fully reconciled');
    expect(fixture.nativeElement.textContent).toContain('Blocked');
    http.verify();
  });

  describe('shared UI kit presentation', () => {
    const basePermissions = ['hr.exit.view', 'hr.exit.initiate', 'hr.exit.clearance'];

    afterEach(() => {
      permissions.clear();
      basePermissions.forEach(permission => permissions.add(permission));
    });

    const exitCase = (overrides: Partial<HrExitCase> = {}): HrExitCase => ({
      id: 31, employeeId: 42, employeeCode: 'E42', employeeName: 'Employee 42',
      exitType: 'Resignation', initiatedDate: '2026-10-09',
      proposedLastWorkingDate: '2026-10-30', actualLastWorkingDate: null,
      reason: 'Voluntary resignation', status: 'Draft', approvalInstanceId: null,
      primaryLocationProjectionId: 11, locationType: 'Outlet',
      clearanceTemplateId: 2, clearanceTemplateVersionNo: 1, version: 3,
      clearanceItems: [], events: [],
      ...overrides
    });

    const clearanceItem = (overrides: Partial<HrExitCase['clearanceItems'][number]> = {}) => ({
      id: 501, sequence: 1, code: 'ASSET', name: 'Return assets', responsibleRole: null,
      responsibleReference: null, isMandatory: true, status: 'Pending', clearedByUserId: null,
      clearedAtUtc: null, comment: null, evidenceReference: null, version: 2,
      ...overrides
    });

    const settlement = (overrides: Record<string, unknown> = {}) => ({
      id: 7, exitCaseId: 31, employeeId: 42, revisionNo: 1, settlementDate: '2026-10-30',
      currencyCode: 'BDT', decimalPrecision: 2, roundingMode: 'AwayFromZero',
      grossDue: 1000, totalDeductions: 100, netSettlement: 900,
      calculationInputHash: 'a'.repeat(64), snapshotHash: 'b'.repeat(64), snapshotJson: '{}',
      calculatedAtUtc: '2026-10-30T12:00:00Z', calculatedByUserId: 5, isReplay: false,
      lines: [
        { lineNo: 1, componentCode: 'NET', componentName: 'Net pay due', lineType: 'Earning', amount: 1000,
          isApplicable: true, notApplicableReason: null, sourceType: 'x', sourceId: null, evidenceSnapshotJson: '{}' },
        { lineNo: 2, componentCode: 'LOAN', componentName: 'Loan recovery', lineType: 'NotApplicable', amount: 0,
          isApplicable: false, notApplicableReason: 'No loans', sourceType: 'x', sourceId: null, evidenceSnapshotJson: '{}' }
      ],
      status: 'Calculated', approvalInstanceId: null, requestedByUserId: null, requestedAtUtc: null,
      approvalPolicyCode: null, approvalPolicyVersion: null, submittedEvidenceHash: null, approvalHistory: null,
      ...overrides
    }) as unknown as PayrollFinalSettlement;

    async function ready(options: { hr?: boolean; payroll?: boolean; cases?: HrExitCase[]; templates?: unknown[] } = {}) {
      const result = await create(options.hr ?? true, options.payroll ?? false);
      if (options.hr ?? true) {
        if (permissions.has('hr.exit.view')) {
          result.http.expectOne(request => request.url === base && request.method === 'GET').flush(options.cases ?? []);
        }
        if (permissions.has('hr.exit.clearance')) result.http.expectOne(`${base}/templates`).flush(options.templates ?? []);
      }
      await settle(result.fixture);
      return result;
    }

    async function settle(fixture: ComponentFixture<unknown>) {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    function type(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) {
      element.value = element.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (element.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
      element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input'));
    }

    function fieldIn(container: ParentNode, label: string, nth = 0) {
      const labels = Array.from(container.querySelectorAll('label'))
        .filter(candidate => candidate.textContent?.trim().startsWith(label));
      const target = labels[nth];
      expect(target, `label "${label}" #${nth}`).toBeTruthy();
      const control = container.querySelector(`#${target.htmlFor}`);
      expect(control, `control for "${label}"`).toBeTruthy();
      return control as HTMLInputElement & HTMLSelectElement & HTMLTextAreaElement;
    }

    function cardTitled(root: HTMLElement, title: string) {
      const card = Array.from(root.querySelectorAll('app-ui-card'))
        .find(candidate => candidate.querySelector('h2')?.textContent?.trim() === title);
      return card as HTMLElement | undefined;
    }

    function buttons(root: ParentNode, text: string) {
      return Array.from(root.querySelectorAll('button'))
        .filter(candidate => candidate.textContent?.replace(/\s+/g, ' ').trim() === text) as HTMLButtonElement[];
    }

    function casesRegion(root: HTMLElement) {
      return root.querySelector('[role="region"][aria-label="Exit cases"]') as HTMLElement;
    }

    it('renders a single h1 page header inside one main landmark with description and breadcrumbs', async () => {
      const { fixture } = await ready();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelectorAll('main').length).toBe(1);
      expect(root.querySelectorAll('h1').length).toBe(1);
      expect(root.querySelector('h1')!.textContent).toContain('Employee Exit and Clearance');
      expect(root.textContent).toContain('Approval freezes the calculation; Accounting and payment execution remain separate auditable actions.');
      const crumbs = root.querySelector('nav[aria-label="Breadcrumb"]')!;
      expect(crumbs.textContent).toContain('HR');
      expect(crumbs.querySelector('[aria-current="page"]')!.textContent).toContain('Exit and clearance');
    });

    it('lays the sections out as kit cards and a named exit cases region', async () => {
      const { fixture } = await ready();
      const root = fixture.nativeElement as HTMLElement;
      expect(cardTitled(root, 'Clearance templates')).toBeTruthy();
      expect(cardTitled(root, 'Initiate non-financial exit')).toBeTruthy();
      expect(root.querySelector('section[aria-labelledby="exit-cases-heading"] h2')!.textContent).toContain('Exit cases');
      expect(casesRegion(root)).toBeTruthy();
      expect(root.querySelector('[role="region"][aria-label="Clearance templates"]')).toBeTruthy();
    });

    it('shows a polite loading status only while the exit cases are loading', async () => {
      const { fixture, http } = await create();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('[role="status"]')!.textContent).toContain('Loading exit cases...');
      http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
      http.expectOne(`${base}/templates`).flush([]);
      await settle(fixture);
      expect(root.textContent).not.toContain('Loading exit cases...');
    });

    it('announces the disabled HR capability as an alert and requests nothing', async () => {
      const { fixture, http } = await create(false);
      await settle(fixture);
      const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')!;
      expect(alert.textContent).toContain('HR capability is disabled.');
      http.expectNone(request => request.url.startsWith(base));
    });

    it('shows API failures as an alert and success messages as a status', async () => {
      const { fixture, page, http } = await ready();
      const root = fixture.nativeElement as HTMLElement;
      page.search();
      http.expectOne(request => request.url === base && request.method === 'GET')
        .flush({ detail: 'Scope not permitted.' }, { status: 403, statusText: 'Forbidden' });
      await settle(fixture);
      expect(root.querySelector('[role="alert"]')!.textContent).toContain('Scope not permitted.');

      page.search();
      http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
      page.notice.set('Exit case submitted.');
      await settle(fixture);
      expect(root.querySelector('[role="alert"]')).toBeNull();
      expect(Array.from(root.querySelectorAll('[role="status"]')).some(element => element.textContent?.includes('Exit case submitted.'))).toBe(true);
    });

    it('hides the template and initiation cards without the matching permissions', async () => {
      permissions.delete('hr.exit.initiate');
      permissions.delete('hr.exit.clearance');
      const { fixture } = await ready();
      const root = fixture.nativeElement as HTMLElement;
      expect(cardTitled(root, 'Clearance templates')).toBeUndefined();
      expect(cardTitled(root, 'Initiate non-financial exit')).toBeUndefined();
      expect(casesRegion(root)).toBeTruthy();
    });

    describe('initiate form', () => {
      it('ties every label to its control, marks required fields and keeps the ngModel bindings', async () => {
        const { fixture, page } = await ready();
        const card = cardTitled(fixture.nativeElement, 'Initiate non-financial exit')!;
        const employee = fieldIn(card, 'Employee ID');
        expect(employee.name).toBe('employeeId');
        expect(employee.type).toBe('number');
        expect(employee.required).toBe(true);
        expect(employee.classList).toContain('ui-control');
        const reason = fieldIn(card, 'Reason');
        expect(reason.tagName).toBe('TEXTAREA');
        expect(reason.maxLength).toBe(1000);
        expect(card.querySelector(`label[for="${reason.id}"]`)!.textContent).toContain('(required)');

        type(employee, '77');
        type(fieldIn(card, 'Exit type'), 'Retirement');
        type(fieldIn(card, 'Initiated date'), '2026-11-01');
        type(fieldIn(card, 'Proposed last working date'), '2026-11-30');
        type(reason, 'Reached retirement age');
        expect(page.newExit).toEqual({
          employeeId: 77, exitType: 'Retirement', initiatedDate: '2026-11-01',
          proposedLastWorkingDate: '2026-11-30', reason: 'Reached retirement age'
        });
      });

      it('submits the form through the kit submit button and disables it while busy', async () => {
        const { fixture, page, http } = await ready();
        const card = cardTitled(fixture.nativeElement, 'Initiate non-financial exit')!;
        const submit = buttons(card, 'Create exit case')[0];
        expect(submit.type).toBe('submit');
        expect(submit.disabled).toBe(false);
        type(fieldIn(card, 'Employee ID'), '42');
        type(fieldIn(card, 'Reason'), 'Voluntary resignation');
        card.querySelector('form')!.dispatchEvent(new Event('submit'));
        const request = http.expectOne(base);
        expect(request.request.method).toBe('POST');
        expect(request.request.body).toEqual(page.newExit);
        expect(request.request.body.employeeId).toBe(42);
        await settle(fixture);
        expect(buttons(card, 'Create exit case')[0].disabled).toBe(true);
        request.flush(exitCase({ id: 8 }));
        await settle(fixture);
        expect(buttons(card, 'Create exit case')[0].disabled).toBe(false);
        expect(fixture.nativeElement.textContent).toContain('Exit case 8 created.');
        http.verify();
      });
    });

    describe('clearance template form and list', () => {
      it('shows the scope reference field only for non-business scopes', async () => {
        const { fixture } = await ready();
        const card = cardTitled(fixture.nativeElement, 'Clearance templates')!;
        expect(Array.from(card.querySelectorAll('label')).some(label => label.textContent?.startsWith('Scope reference'))).toBe(false);
        type(fieldIn(card, 'Scope'), 'Location');
        await settle(fixture);
        const reference = fieldIn(card, 'Scope reference');
        expect(reference.required).toBe(true);
        expect(reference.name).toBe('templateScopeReference');
      });

      it('adds and removes checklist items, only offering Remove item when several exist', async () => {
        const { fixture, page } = await ready();
        const card = cardTitled(fixture.nativeElement, 'Clearance templates')!;
        expect(buttons(card, 'Remove item').length).toBe(0);
        buttons(card, 'Add checklist item')[0].click();
        await settle(fixture);
        expect(page.template.items.length).toBe(2);
        expect(page.template.items[1].sequence).toBe(2);
        expect(buttons(card, 'Remove item').length).toBe(2);
        buttons(card, 'Remove item')[0].click();
        await settle(fixture);
        expect(page.template.items.length).toBe(1);
        expect(page.template.items[0].sequence).toBe(1);
        expect(buttons(card, 'Remove item').length).toBe(0);
      });

      it('creates a template from the typed values', async () => {
        const { fixture, http } = await ready();
        const card = cardTitled(fixture.nativeElement, 'Clearance templates')!;
        type(fieldIn(card, 'Code', 0), 'EXIT-BASE');
        type(fieldIn(card, 'Name', 0), 'Standard exit');
        type(fieldIn(card, 'Code', 1), 'ASSET');
        type(fieldIn(card, 'Name', 1), 'Return assets');
        const submit = buttons(card, 'Create immutable template version')[0];
        expect(submit.type).toBe('submit');
        card.querySelector('form')!.dispatchEvent(new Event('submit'));
        const request = http.expectOne(`${base}/templates`);
        expect(request.request.method).toBe('POST');
        expect(request.request.body).toEqual(expect.objectContaining({
          code: 'EXIT-BASE', name: 'Standard exit', scopeType: 'Business', versionNo: 1, isActive: true
        }));
        expect(request.request.body.items[0]).toEqual(expect.objectContaining({ code: 'ASSET', name: 'Return assets' }));
        request.flush({});
        http.expectOne(`${base}/templates`).flush([]);
        await settle(fixture);
        expect(fixture.nativeElement.textContent).toContain('Clearance template version created.');
        http.verify();
      });

      it('shows the empty state when no template is configured', async () => {
        const { fixture } = await ready();
        const region = (fixture.nativeElement as HTMLElement).querySelector('[role="region"][aria-label="Clearance templates"]')!;
        expect(region.closest('app-ui-data-table')!.textContent).toContain('No clearance templates are configured.');
      });

      it('lists templates with version, scope reference and open-ended dates, and sorts by code', async () => {
        const { fixture } = await ready({
          templates: [
            { id: 2, code: 'ZED', name: 'Zed', scopeType: 'Location', scopeReference: 'DHK-1', versionNo: 2,
              effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true, items: [] },
            { id: 1, code: 'ALPHA', name: 'Alpha', scopeType: 'Business', scopeReference: null, versionNo: 1,
              effectiveFrom: '2025-01-01', effectiveTo: '2025-12-31', isActive: false, items: [] }
          ]
        });
        const root = fixture.nativeElement as HTMLElement;
        const table = root.querySelector('[role="region"][aria-label="Clearance templates"]')!.closest('app-ui-data-table')!;
        const rows = () => Array.from(table.querySelectorAll('tbody tr')).map(row => row.textContent!.replace(/\s+/g, ' ').trim());
        expect(rows()[0]).toContain('ZED v2');
        expect(rows()[0]).toContain('Location (DHK-1)');
        expect(rows()[0]).toContain('01/01/2026–open');
        expect(rows()[1]).toContain('ALPHA v1');
        expect(rows()[1]).toContain('01/01/2025–31/12/2025');
        const header = Array.from(table.querySelectorAll('th')).find(th => th.textContent?.includes('Code / version'))!;
        expect(header.getAttribute('aria-sort')).toBe('none');
        (header.querySelector('button') as HTMLButtonElement).click();
        await settle(fixture);
        expect(header.getAttribute('aria-sort')).toBe('ascending');
        expect(rows()[0]).toContain('ALPHA v1');
      });
    });

    describe('exit cases table', () => {
      it('renders every column of an exit case with visible status text', async () => {
        const row = exitCase({
          status: 'PendingApproval', approvalInstanceId: 9,
          clearanceItems: [clearanceItem({ comment: 'Laptop missing', status: 'Exception' })],
          events: [{ sequence: 1, eventType: 'Submitted', previousStatus: 'Draft', newStatus: 'PendingApproval',
            actualLastWorkingDate: null, reason: null, actorUserId: 1, occurredAtUtc: '2026-10-10T08:00:00Z' }]
        });
        const { fixture } = await ready({ cases: [row] });
        const region = casesRegion(fixture.nativeElement);
        const headers = Array.from(region.querySelectorAll('th')).map(th => th.textContent!.trim());
        expect(headers).toEqual(['Employee / type', 'Dates / location', 'Status / version', 'Clearance', 'Actions / history']);
        const text = region.querySelector('tbody')!.textContent!.replace(/\s+/g, ' ');
        expect(text).toContain('E42 — Employee 42 (42)');
        expect(text).toContain('Resignation · Voluntary resignation');
        expect(text).toContain("Initiated 09/10/2026 · proposed 30/10/2026");
        expect(text).toContain('actual — · location 11 / Outlet');
        expect(text).toContain('PendingApproval');
        expect(text).toContain('v3');
        expect(text).toContain('Template 2 v1');
        expect(text).toContain('approval 9');
        expect(text).toContain('1. Return assets');
        expect(text).toContain('Exception');
        expect(text).toContain('(mandatory)');
        expect(text).toContain('Reason: Laptop missing');
        expect(text).toContain(`Submitted · PendingApproval · ${TestBed.inject(CompanyDateService).format('2026-10-10T08:00:00Z', true)}`);
        expect(region.querySelector('app-ui-badge')!.textContent).toContain('PendingApproval');
      });

      it('sorts by employee name with aria-sort and keeps the loaded order until sorted', async () => {
        const { fixture } = await ready({
          cases: [exitCase({ id: 1, employeeName: 'Beta' }), exitCase({ id: 2, employeeName: 'Alpha' })]
        });
        const region = casesRegion(fixture.nativeElement);
        const names = () => Array.from(region.querySelectorAll('tbody tr')).map(row => /Alpha|Beta/.exec(row.textContent!)![0]);
        expect(names()).toEqual(['Beta', 'Alpha']);
        const header = Array.from(region.querySelectorAll('th')).find(th => th.textContent?.includes('Employee / type'))!;
        expect(header.getAttribute('aria-sort')).toBe('none');
        const sort = header.querySelector('button') as HTMLButtonElement;
        sort.click();
        await settle(fixture);
        expect(header.getAttribute('aria-sort')).toBe('ascending');
        expect(names()).toEqual(['Alpha', 'Beta']);
        sort.click();
        await settle(fixture);
        expect(header.getAttribute('aria-sort')).toBe('descending');
        expect(names()).toEqual(['Beta', 'Alpha']);
        const clearance = Array.from(region.querySelectorAll('th')).find(th => th.textContent?.includes('Clearance'))!;
        expect(clearance.hasAttribute('aria-sort')).toBe(false);
      });

      it('paginates the loaded cases client-side without issuing new requests', async () => {
        const many = Array.from({ length: 12 }, (_, index) => exitCase({ id: index + 1, employeeName: `Person ${index + 1}` }));
        const { fixture, http } = await ready({ cases: many });
        const root = fixture.nativeElement as HTMLElement;
        const region = casesRegion(root);
        expect(region.querySelectorAll('tbody tr').length).toBe(10);
        const pager = region.closest('app-ui-data-table')!.querySelector('nav[aria-label="Pagination"]')!;
        (pager.querySelector('button[aria-label="Next page"]') as HTMLButtonElement).click();
        await settle(fixture);
        expect(region.querySelectorAll('tbody tr').length).toBe(2);
        expect(region.textContent).toContain('Person 11');
        http.expectNone(request => request.url.startsWith(base));
      });

      it('explains why the list is empty before and after the first search result', async () => {
        const before = await create();
        expect(before.fixture.nativeElement.textContent).toContain('No exit cases to show yet.');
        before.http.expectOne(request => request.url === base && request.method === 'GET').flush([]);
        before.http.expectOne(`${base}/templates`).flush([]);
        await settle(before.fixture);
        expect(before.fixture.nativeElement.textContent).toContain('No exit cases match these filters.');
        expect(before.fixture.nativeElement.textContent).not.toContain('No exit cases to show yet.');
      });

      it('searches with the typed filters through the labelled filter controls', async () => {
        const { fixture, http } = await ready();
        const root = fixture.nativeElement as HTMLElement;
        const filters = root.querySelector('form.ex-filters')!;
        type(fieldIn(filters, 'Initiated from'), '2026-10-01');
        type(fieldIn(filters, 'To'), '2026-10-31');
        type(fieldIn(filters, 'Employee ID'), '42');
        type(fieldIn(filters, 'Status'), 'Approved');
        const search = buttons(filters, 'Search')[0];
        expect(search.type).toBe('submit');
        filters.dispatchEvent(new Event('submit'));
        const request = http.expectOne(candidate => candidate.url === base && candidate.method === 'GET');
        expect(request.request.params.get('from')).toBe('2026-10-01');
        expect(request.request.params.get('to')).toBe('2026-10-31');
        expect(request.request.params.get('employeeId')).toBe('42');
        expect(request.request.params.get('status')).toBe('Approved');
        await settle(fixture);
        expect(buttons(filters, 'Search')[0].disabled).toBe(true);
        request.flush([]);
        await settle(fixture);
        expect(buttons(filters, 'Search')[0].disabled).toBe(false);
        expect(root.textContent).toContain('No exit cases match these filters.');
        const options = Array.from(fieldIn(filters, 'Status').querySelectorAll('option')).map(option => option.textContent!.trim());
        expect(options).toEqual(['All statuses', 'Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled', 'Completed']);
      });
    });

    describe('row actions and permissions', () => {
      const actionsOf = (fixture: ComponentFixture<unknown>) =>
        casesRegion(fixture.nativeElement).querySelector('tbody tr')!;

      it('offers Submit and Cancel for a Draft case and posts the expected version', async () => {
        const { fixture, http } = await ready({ cases: [exitCase({ status: 'Draft' })] });
        const row = actionsOf(fixture);
        expect(buttons(row, 'Submit').length).toBe(1);
        expect(buttons(row, 'Cancel').length).toBe(1);
        expect(buttons(row, 'Complete exit').length).toBe(0);
        buttons(row, 'Submit')[0].click();
        const request = http.expectOne(`${base}/31/submit`);
        expect(request.request.body).toEqual({ expectedVersion: 3 });
        await settle(fixture);
        expect(buttons(actionsOf(fixture), 'Submit')[0].disabled).toBe(true);
        request.flush({});
        http.expectOne(candidate => candidate.url === base && candidate.method === 'GET').flush([]);
        await settle(fixture);
        http.verify();
      });

      it('confirms before cancelling and does nothing when the confirmation is declined', async () => {
        const { fixture, http } = await ready({ cases: [exitCase({ status: 'PendingApproval' })] });
        const row = actionsOf(fixture);
        expect(buttons(row, 'Submit').length).toBe(0);
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        buttons(row, 'Cancel')[0].click();
        expect(confirm).toHaveBeenCalledWith('Cancel exit case 31?');
        http.expectNone(`${base}/31/cancel`);
        confirm.mockReturnValue(true);
        buttons(row, 'Cancel')[0].click();
        const request = http.expectOne(`${base}/31/cancel`);
        expect(request.request.body.reason).toBe('Cancelled from the exit workbench.');
        request.flush({});
        http.expectOne(candidate => candidate.url === base && candidate.method === 'GET').flush([]);
        confirm.mockRestore();
      });

      it('hides Submit and Cancel without the initiate permission and for completed cases', async () => {
        permissions.delete('hr.exit.initiate');
        const first = await ready({ cases: [exitCase({ status: 'Draft' })] });
        expect(buttons(actionsOf(first.fixture), 'Submit').length).toBe(0);
        expect(buttons(actionsOf(first.fixture), 'Cancel').length).toBe(0);
        TestBed.resetTestingModule();
        permissions.add('hr.exit.initiate');

        const { fixture } = await ready({ cases: [exitCase({ status: 'Completed' })] });
        expect(buttons(actionsOf(fixture), 'Submit').length).toBe(0);
        expect(buttons(actionsOf(fixture), 'Cancel').length).toBe(0);
        expect(casesRegion(fixture.nativeElement).querySelector('app-ui-badge')!.textContent).toContain('Completed');
      });

      it('shows the clearance editor only for Draft/Approved cases with the clearance permission and saves it', async () => {
        const items = [clearanceItem()];
        const { fixture, http } = await ready({ cases: [exitCase({ status: 'Draft', clearanceItems: items })] });
        const row = actionsOf(fixture);
        const status = row.querySelector('select[aria-label="Status for Return assets"]') as HTMLSelectElement;
        expect(status).toBeTruthy();
        expect(Array.from(status.options).map(option => option.textContent!.trim()))
          .toEqual(['Pending', 'Cleared', 'Exception', 'NotApplicable']);
        type(status, 'Cleared');
        type(row.querySelector('input[aria-label="Reason / comment for Return assets"]') as HTMLInputElement, 'Returned');
        type(row.querySelector('input[aria-label="Evidence reference for Return assets"]') as HTMLInputElement, 'REC-1');
        (row.querySelector('button[aria-label="Save Return assets"]') as HTMLButtonElement).click();
        const request = http.expectOne(`${base}/clearance-items`);
        expect(request.request.method).toBe('PUT');
        expect(request.request.body).toEqual({
          itemId: 501, expectedVersion: 2, status: 'Cleared', comment: 'Returned', evidenceReference: 'REC-1'
        });
        request.flush(exitCase());
        http.expectOne(candidate => candidate.url === base && candidate.method === 'GET').flush([]);
      });

      it('has no clearance editor for other statuses or without the clearance permission', async () => {
        const rejected = await ready({ cases: [exitCase({ status: 'Rejected', clearanceItems: [clearanceItem()] })] });
        expect(rejected.fixture.nativeElement.querySelector('select[aria-label="Status for Return assets"]')).toBeNull();
        expect(rejected.fixture.nativeElement.textContent).toContain('Return assets');
        TestBed.resetTestingModule();

        permissions.delete('hr.exit.clearance');
        const { fixture } = await ready({ cases: [exitCase({ status: 'Draft', clearanceItems: [clearanceItem()] })] });
        expect(fixture.nativeElement.querySelector('select[aria-label="Status for Return assets"]')).toBeNull();
        expect(fixture.nativeElement.textContent).toContain('Return assets');
      });

      it('shows labelled completion fields for an Approved case and requires both values', async () => {
        const { fixture, page } = await ready({ cases: [exitCase({ status: 'Approved' })] });
        const row = actionsOf(fixture);
        const date = fieldIn(row, 'Actual last working date');
        const reason = fieldIn(row, 'Completion reason');
        expect(date.type).toBe("text");
        type(date, '2026-10-30');
        expect(page.actualDate[31]).toBe('2026-10-30');
        type(reason, '  Done ');
        expect(page.completeReason[31]).toBe('  Done ');
        expect(buttons(row, 'Complete exit').length).toBe(1);
        expect(buttons(row, 'Preview completion readiness').length).toBe(1);
        page.completeReason[31] = '';
        buttons(row, 'Complete exit')[0].click();
        await settle(fixture);
        const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')!;
        expect(alert.textContent).toContain('Actual last working date and completion reason are required.');
      });

      it('shows the completion readiness result with visible Ready text and counts', async () => {
        permissions.add('payroll.final_settlement.view');
        permissions.add('payroll.salary.view');
        const { fixture, page, http } = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        buttons(actionsOf(fixture), 'Preview completion readiness')[0].click();
        http.expectOne(`${base}/31/completion-preview`).flush({
          exitCaseId: 31, exitVersion: 3, payrollEnabled: true, canComplete: false,
          pendingMandatoryClearanceCount: 2, reasonlessNotApplicableCount: 1,
          blockers: ['Two mandatory items are pending.'], settlement: null
        });
        await settle(fixture);
        const row = actionsOf(fixture);
        const text = row.textContent!.replace(/\s+/g, ' ');
        expect(text).toContain('Completion readiness: Blocked');
        expect(text).toContain('payroll-backed');
        expect(text).toContain('Pending mandatory items 2 · reasonless NotApplicable items 1');
        expect(text).toContain('Two mandatory items are pending.');
        expect(buttons(row, 'Refresh completion readiness').length).toBe(1);
        expect(page.completionPreviews()[31].canComplete).toBe(false);
        expect(page.notice()).toBe('Exit completion blockers were refreshed.');
      });
    });

    describe('final settlement', () => {
      const grant = (...keys: string[]) => keys.forEach(key => permissions.add(`payroll.final_settlement.${key}`));

      async function withSettlement(status: string) {
        permissions.add('payroll.salary.view');
        const result = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        result.page.settlements.set({ 31: settlement({ status }) });
        await settle(result.fixture);
        return { ...result, row: casesRegion(result.fixture.nativeElement).querySelector('tbody tr')! };
      }

      it('hides the whole settlement section without the view permissions', async () => {
        const { fixture } = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        expect(fixture.nativeElement.textContent).not.toContain('Payroll final settlement');
      });

      it('lets a viewer load but not calculate, and offers calculate only with its permission', async () => {
        grant('view');
        permissions.add('payroll.salary.view');
        const first = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        const row = casesRegion(first.fixture.nativeElement).querySelector('tbody tr')!;
        expect(row.querySelector('h3')!.textContent).toContain('Payroll final settlement');
        expect(buttons(row, 'Load existing calculation').length).toBe(1);
        expect(buttons(row, 'Calculate settlement').length).toBe(0);
        TestBed.resetTestingModule();

        grant('calculate');
        const { fixture } = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        const calculable = casesRegion(fixture.nativeElement).querySelector('tbody tr')!;
        expect(buttons(calculable, 'Calculate settlement').length).toBe(1);
      });

      it('keeps the displayed calculation text, rounding precision and not-applicable reasons', async () => {
        grant('view');
        const { row } = await withSettlement('Calculated');
        const text = row.textContent!.replace(/\s+/g, ' ');
        expect(text).toContain("Revision 1 · 30/10/2026 · BDT");
        expect(text).toContain('Gross due 1,000 · Deductions 100 · Net 900');
        expect(text).toContain('Net pay due · Earning 1,000');
        expect(text).toContain('Loan recovery · NotApplicable Not applicable: No loans');
        expect(text).toContain(`Snapshot hash: ${'b'.repeat(64)}`);
        expect(text).toContain('Approval status: Calculated');
        expect(buttons(row, 'Load existing calculation').length).toBe(0);
        expect(buttons(row, 'Create new calculation revision').length).toBe(0);
      });

      it('shows Submit for approval only to submitters of a Calculated settlement', async () => {
        grant('view');
        const viewer = await withSettlement('Calculated');
        expect(buttons(viewer.row, 'Submit for approval').length).toBe(0);
        TestBed.resetTestingModule();

        grant('submit');
        const { row } = await withSettlement('Calculated');
        expect(buttons(row, 'Submit for approval').length).toBe(1);
        expect(buttons(row, 'Approve').length).toBe(0);
      });

      it('shows each decision button only with its own permission for a Pending settlement', async () => {
        grant('view', 'approve');
        const approver = await withSettlement('Pending');
        expect(buttons(approver.row, 'Approve').length).toBe(1);
        expect(buttons(approver.row, 'Reject').length).toBe(0);
        expect(buttons(approver.row, 'Cancel approval').length).toBe(0);
        expect(buttons(approver.row, 'Submit for approval').length).toBe(0);
        const reason = fieldIn(approver.row, 'Decision reason');
        expect(reason.maxLength).toBe(1000);
        type(reason, 'Looks right');
        expect(approver.page.settlementDecisionReason[7]).toBe('Looks right');
        TestBed.resetTestingModule();

        grant('reject', 'cancel');
        const { row } = await withSettlement('Pending');
        expect(buttons(row, 'Approve').length).toBe(1);
        expect(buttons(row, 'Reject').length).toBe(1);
        expect(buttons(row, 'Cancel approval').length).toBe(1);
        expect(Array.from(row.querySelectorAll('app-ui-badge')).some(badge => badge.textContent?.trim() === 'Pending')).toBe(true);
      });

      it('has no decision reason field or decision buttons once the settlement is Approved', async () => {
        grant('view', 'approve', 'reject', 'cancel', 'submit');
        const { row } = await withSettlement('Approved');
        expect(Array.from(row.querySelectorAll('label')).some(label => label.textContent?.startsWith('Decision reason'))).toBe(false);
        ['Approve', 'Reject', 'Cancel approval', 'Submit for approval'].forEach(label =>
          expect(buttons(row, label).length, label).toBe(0));
      });

      it('shows approval history entries', async () => {
        grant('view');
        permissions.add('payroll.salary.view');
        const { fixture, page } = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        page.settlements.set({
          31: settlement({
            status: 'Rejected', approvalPolicyCode: 'FS', approvalPolicyVersion: 2, requestedAtUtc: '2026-10-31T09:00:00Z',
            approvalHistory: [{ decision: 'Rejected', actorUserId: 5, occurredAtUtc: '2026-11-01T10:00:00Z', reason: 'Wrong amount', instanceStatusAfterDecision: 'Rejected' }]
          })
        });
        await settle(fixture);
        const text = casesRegion(fixture.nativeElement).textContent!.replace(/\s+/g, ' ');
        expect(text).toContain('Approval history');
        expect(text).toContain("Rejected · user 5 · 01/11/2026 10:00:00 · Wrong amount");
        expect(text).toContain('FS v2');
        expect(text).toContain('submitted 31/10/2026 09:00:00');
      });

      it('keeps financial execution confidential without the financial view permission', async () => {
        grant('view', 'payment.create', 'accounting.post');
        const { row } = await withSettlement('Approved');
        expect(row.textContent).not.toContain('Financial execution');
        expect(buttons(row, 'Load execution status').length).toBe(0);
      });

      it('shows financial execution, payments and postings only to financial viewers and gates payment/accounting actions', async () => {
        grant('view', 'financial.view');
        const viewer = await withSettlement('Approved');
        expect(viewer.row.querySelector('h4')!.textContent).toContain('Financial execution');
        expect(buttons(viewer.row, 'Load execution status').length).toBe(1);
        const execution = {
          finalSettlementId: 7, revisionNo: 1, settlementStatus: 'Approved', approvalInstanceId: 9,
          payments: [{
            id: 3, payableAmount: 900, paidAmount: 400, outstandingAmount: 500, currencyCode: 'BDT',
            method: 'Bank', status: 'Pending', attemptNo: 1,
            evidence: [{ id: 11, type: 'Settlement', amount: 400, evidenceDate: '2026-11-02', evidenceReference: 'TRX-1' }]
          }],
          accountingPostings: [{
            id: 1, revisionNo: 1, postingType: 'Expense', postingReference: 'POST-1', status: 'Posted',
            accountingJournalId: 5, accountingReference: 'JV-9', errorMessage: null, attemptCount: 1, version: 1, postedAtUtc: null
          }]
        };
        viewer.page.executions.set({ 7: execution });
        await settle(viewer.fixture);
        const viewerText = viewer.row.textContent!.replace(/\s+/g, ' ');
        expect(viewerText).toContain('Settlement Approved · revision 1');
        expect(viewerText).toContain('Payment 3 · Bank · Pending · payable 900 · paid 400 · outstanding 500');
        expect(viewerText).toContain('Settlement · 400 · TRX-1 · 02/11/2026');
        expect(viewerText).toContain('Expense · Posted · POST-1 · JV-9');
        expect(buttons(viewer.row, 'Refresh execution status').length).toBe(1);
        expect(buttons(viewer.row, 'Create payment attempt').length).toBe(0);
        expect(buttons(viewer.row, 'Post approved settlement to Accounting').length).toBe(0);
        expect(buttons(viewer.row, 'Post reconciled payment evidence').length).toBe(0);
        TestBed.resetTestingModule();

        grant('payment.create', 'accounting.post');
        const { page, fixture } = await withSettlement('Approved');
        page.executions.set({ 7: execution });
        await settle(fixture);
        const operator = casesRegion(fixture.nativeElement).querySelector('tbody tr')!;
        expect(buttons(operator, 'Create payment attempt').length).toBe(1);
        expect(buttons(operator, 'Post approved settlement to Accounting').length).toBe(1);
        expect(buttons(operator, 'Post reconciled payment evidence').length).toBe(1);
        const amount = fieldIn(operator, 'Payment amount');
        expect(amount.max).toBe('900');
        expect(amount.min).toBe('0.01');
        const method = fieldIn(operator, 'Payment method');
        expect(Array.from(method.options).map(option => option.value)).toEqual(['Cash', 'Bank', 'Mobile']);
        type(amount, '250');
        expect(page.paymentAmount[7]).toBe(250);
      });

      it('hides the payment form when nothing is payable', async () => {
        grant('view', 'financial.view', 'payment.create');
        permissions.add('payroll.salary.view');
        const { fixture, page } = await ready({ payroll: true, cases: [exitCase({ status: 'Approved' })] });
        page.settlements.set({ 31: settlement({ status: 'Approved', netSettlement: -50 }) });
        page.executions.set({ 7: {
          finalSettlementId: 7, revisionNo: 1, settlementStatus: 'Approved', approvalInstanceId: null,
          payments: [], accountingPostings: []
        } });
        await settle(fixture);
        const text = casesRegion(fixture.nativeElement).textContent!.replace(/\s+/g, ' ');
        expect(text).toContain('No payment attempts recorded.');
        expect(text).toContain('No Accounting posting recorded.');
        expect(text).not.toContain('Payment amount');
        expect(buttons(casesRegion(fixture.nativeElement), 'Create payment attempt').length).toBe(0);
      });
    });
  });
});
