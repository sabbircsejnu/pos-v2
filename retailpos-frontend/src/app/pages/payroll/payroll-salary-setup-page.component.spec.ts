import { CompanyDateService } from '../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PayrollSalarySetupPageComponent } from './payroll-salary-setup-page.component';
import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';

describe('Payroll salary setup page', () => {
  const base = `${environment.apiUrl}/payroll/v1`;
  afterEach(() => TestBed.resetTestingModule());

  async function create(permissions: string[]) {
    await TestBed.configureTestingModule({
      imports: [PayrollSalarySetupPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { hasPermission: (permission: string) => permissions.includes(permission) } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(PayrollSalarySetupPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('loads Business-scoped components and templates for view-authorized users', async () => {
    const { fixture, http } = await create(['payroll.salary.view']);
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No salary components have been configured.');
    expect(fixture.nativeElement.textContent).toContain('No salary structure templates have been configured.');
    expect(fixture.nativeElement.textContent).not.toContain('Create component');
    http.verify();
  });

  it('creates component configuration and refreshes both lists', async () => {
    const { page, http } = await create(['payroll.salary.view', 'payroll.salary.manage']);
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    page.componentData = { ...page.componentData, code: 'BASE', name: 'Base salary' };
    page.saveComponent();
    const request = http.expectOne(`${base}/salary-components`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.code).toBe('BASE');
    request.flush({
      id: 1, businessId: 1, data: page.componentData, isActive: true, version: 1
    });
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    http.verify();
  });

  it('sends expected versions when updating a component', async () => {
    const { page, http } = await create(['payroll.salary.view', 'payroll.salary.manage']);
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    const row = {
      id: 7,
      businessId: 1,
      data: { ...page.componentData, code: 'BASE', name: 'Base salary' },
      isActive: true,
      version: 4
    };
    page.editComponent(row);
    page.componentData.name = 'Updated base salary';
    page.saveComponent();
    const request = http.expectOne(`${base}/salary-components/7`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.expectedVersion).toBe(4);
    request.flush({ ...row, version: 5, data: page.componentData });
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    http.verify();
  });

  it('creates a frozen initial salary proposal and reloads its request history', async () => {
    const permissions = [
      'payroll.salary.view',
      'payroll.salary.manage',
      'payroll.salary_setup.create'
    ];
    const { page, http } = await create(permissions);
    http.expectOne(`${base}/salary-components`).flush([{
      id: 3,
      businessId: 1,
      data: {
        code: 'BASE', name: 'Base salary', componentType: 'Earning', valueType: 'Fixed',
        taxTreatmentCode: null, isRecurring: true, calculationOrder: 1, formulaExpression: null
      },
      isActive: true,
      version: 1
    }]);
    http.expectOne(`${base}/salary-structures`).flush([{
      id: 8,
      businessId: 1,
      data: {
        code: 'STANDARD', name: 'Standard',
        lines: [{ salaryComponentId: 3, defaultValue: 900, formulaOverride: null, calculationOrder: 1 }]
      },
      isActive: true,
      version: 2
    }]);
    page.setupEmployeeId = 12;
    page.setupStructureId = 8;
    page.selectSetupStructure();
    page.setupComponentValues[3] = 1250;
    page.effectiveFrom = '2026-10-10';
    page.setupReason = 'Initial hire';
    page.createInitialSalarySetup();

    const createRequest = http.expectOne(`${base}/salary-setup-requests`);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toMatchObject({
      employeeId: 12,
      salaryStructureId: 8,
      effectiveFrom: '2026-10-10',
      components: [{ salaryComponentId: 3, value: 1250 }],
      reason: 'Initial hire'
    });
    expect(createRequest.request.body.idempotencyKey).toBeTruthy();
    createRequest.flush({ employeeId: 12 });

    http.expectOne(`${base}/employees/12/salary-setup-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-revision-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-history`).flush([]);
    expect(page.salaryHistoryLoaded()).toBe(true);
    expect(page.currentSalary()).toBeNull();
    http.verify();
  });

  it('submits a draft request only through the permission-gated Approval Kernel route', async () => {
    const { page, http } = await create(['payroll.salary.view', 'payroll.salary_setup.submit']);
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    const request = {
      id: 15,
      businessId: 1,
      employeeId: 12,
      salaryStructureId: 8,
      salaryStructureVersion: 1,
      effectiveFrom: '2026-10-10',
      operationalLocationProjectionId: 10,
      structureCode: 'STANDARD',
      structureName: 'Standard',
      components: [],
      reason: 'Initial hire',
      status: 'Draft',
      approvalInstanceId: null,
      createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z',
      submittedByUserId: null,
      submittedAtUtc: null,
      decidedByUserId: null,
      decidedAtUtc: null,
      decisionReason: null,
      version: 1,
      approvalSteps: [],
      approvalHistory: [],
      isReplay: false
    };
    page.submitRequest(request);
    const submit = http.expectOne(`${base}/salary-setup-requests/15/submit`);
    expect(submit.request.method).toBe('POST');
    expect(submit.request.body.expectedVersion).toBe(1);
    submit.flush({ instanceId: 25 });
    http.expectOne(`${base}/employees/12/salary-setup-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-revision-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-history`).flush([]);
    http.verify();
  });

  it('loads, creates, and displays scoped employee adjustments only with their permissions', async () => {
    const { fixture, page, http } = await create([
      'payroll.salary.view',
      'payroll.allowance_deduction.view',
      'payroll.allowance_deduction.manage'
    ]);
    http.expectOne(`${base}/salary-components`).flush([
      {
        id: 3,
        businessId: 1,
        data: {
          code: 'TRANSPORT', name: 'Transport', componentType: 'Earning', valueType: 'Fixed',
          taxTreatmentCode: null, isRecurring: true, calculationOrder: 1, formulaExpression: null
        },
        isActive: true,
        version: 1
      },
      {
        id: 4,
        businessId: 1,
        data: {
          code: 'BONUS', name: 'Bonus', componentType: 'Earning', valueType: 'Fixed',
          taxTreatmentCode: null, isRecurring: false, calculationOrder: 2, formulaExpression: null
        },
        isActive: true,
        version: 1
      }
    ]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    expect(page.eligibleAdjustmentComponents().map(component => component.id)).toEqual([3]);
    page.adjustmentEmployeeId = 12;
    page.adjustmentAsOf = '2026-10-10';
    page.loadAdjustments();
    http.expectOne(request =>
      request.url === `${base}/employees/12/adjustments` && request.params.get('asOf') === '2026-10-10'
    ).flush([]);
    expect(page.adjustmentsLoaded()).toBe(true);

    page.adjustmentComponentId = 3;
    page.adjustmentAmount = 250;
    page.adjustmentEffectiveFrom = '2026-10-10';
    page.adjustmentReason = 'Transport allowance';
    page.saveAdjustment();
    const createRequest = http.expectOne(`${base}/adjustments`);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toMatchObject({
      employeeId: 12,
      salaryComponentId: 3,
      adjustmentKind: 'RecurringAllowance',
      classification: 'Manual',
      amount: 250,
      effectiveFrom: '2026-10-10',
      reason: 'Transport allowance'
    });
    expect(createRequest.request.body.idempotencyKey).toBeTruthy();
    createRequest.flush({ adjustmentIdentity: '3e2ac590-24f5-44a7-a600-66bad2e46180' });
    http.expectOne(request =>
      request.url === `${base}/employees/12/adjustments` && request.params.get('asOf') === '2026-10-10'
    ).flush([{
      id: 1,
      businessId: 1,
      employeeId: 12,
      adjustmentIdentity: '3e2ac590-24f5-44a7-a600-66bad2e46180',
      version: 1,
      predecessorVersionId: null,
      salaryComponentId: 3,
      componentCode: 'TRANSPORT',
      componentName: 'Transport',
      componentType: 'Earning',
      valueType: 'Fixed',
      adjustmentKind: 'RecurringAllowance',
      classification: 'Manual',
      amount: 250,
      effectiveFrom: '2026-10-10',
      effectiveTo: null,
      operationalLocationProjectionId: 10,
      reason: 'Transport allowance',
      reference: null,
      status: 'Active',
      createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z',
      isReplay: false
    }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('TRANSPORT');
    expect(fixture.nativeElement.textContent).toContain('250');
    page.loadAdjustmentHistory(page.adjustments()[0]);
    http.expectOne(`${base}/adjustments/3e2ac590-24f5-44a7-a600-66bad2e46180/history`).flush([]);
    http.verify();
  });

  it('creates a salary revision from the frozen current salary snapshot', async () => {
    const { page, http } = await create([
      'payroll.salary.view',
      'payroll.salary.manage',
      'payroll.salary_revision.create'
    ]);
    http.expectOne(`${base}/salary-components`).flush([]);
    http.expectOne(`${base}/salary-structures`).flush([]);
    page.historyEmployeeId = 12;
    page.currentSalary.set({
      id: 91,
      businessId: 1,
      employeeId: 12,
      salarySetupRequestId: 34,
      salaryStructureId: 8,
      salaryStructureVersion: 2,
      effectiveFrom: '2026-10-10',
      operationalLocationProjectionId: 10,
      structureCode: 'STANDARD',
      structureName: 'Standard',
      components: [{
        salaryComponentId: 3,
        code: 'BASE',
        name: 'Base salary',
        componentType: 'Earning',
        valueType: 'Fixed',
        value: 1250
      }],
      approvalInstanceId: 25,
      approvedByUserId: 202,
      approvedAtUtc: '2026-10-10T00:00:00Z'
    });
    page.revisionEffectiveFrom = '2026-10-20';
    page.revisionReason = 'Annual adjustment';
    page.revisionComponentValues[3] = 1500;
    page.createSalaryRevision();
    const createRequest = http.expectOne(`${base}/salary-revision-requests`);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toMatchObject({
      employeeId: 12,
      sourceSalaryVersionId: 91,
      effectiveFrom: '2026-10-20',
      components: [{ salaryComponentId: 3, value: 1500 }],
      reason: 'Annual adjustment'
    });
    createRequest.flush({ employeeId: 12 });
    http.expectOne(`${base}/employees/12/salary-setup-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-revision-requests`).flush([]);
    http.expectOne(`${base}/employees/12/salary-history`).flush([]);
    expect(page.salaryHistoryLoaded()).toBe(true);
    http.verify();
  });

  it('does not load or send writes without the required permission', async () => {
    const { fixture, page, http } = await create([]);
    http.expectNone(`${base}/salary-components`);
    http.expectNone(`${base}/salary-structures`);
    expect(fixture.nativeElement.textContent).toContain('salary setup view access');
    page.saveComponent();
    expect(page.error()).toContain('Manage access is required');
    expect(fixture.nativeElement.textContent).not.toContain('Employee allowances and deductions');
    http.verify();
  });

  describe('shared UI kit rendering', () => {
    const componentRow = (id: number, code: string, overrides: Record<string, unknown> = {}, isActive = true) => ({
      id,
      businessId: 1,
      data: {
        code, name: `${code} name`, componentType: 'Earning', valueType: 'Fixed',
        taxTreatmentCode: null, isRecurring: true, calculationOrder: id, formulaExpression: null,
        ...overrides
      },
      isActive,
      version: id + 1
    });

    const structureRow = (id: number, code: string, isActive = true) => ({
      id,
      businessId: 1,
      data: {
        code, name: `${code} template`,
        lines: [{ salaryComponentId: 3, defaultValue: 900, formulaOverride: null, calculationOrder: 1 }]
      },
      isActive,
      version: 2
    });

    const salaryRequest = (status: string, id = 15) => ({
      id,
      businessId: 1,
      employeeId: 12,
      salaryStructureId: 8,
      salaryStructureVersion: 1,
      effectiveFrom: '2026-10-10',
      operationalLocationProjectionId: 10,
      structureCode: 'STANDARD',
      structureName: 'Standard',
      components: [],
      reason: 'Initial hire',
      status,
      approvalInstanceId: null,
      createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z',
      submittedByUserId: null,
      submittedAtUtc: null,
      decidedByUserId: null,
      decidedAtUtc: null,
      decisionReason: null,
      version: 1,
      approvalSteps: [],
      approvalHistory: [],
      isReplay: false
    });

    const adjustment = (id: number, status = 'Active', kind = 'RecurringAllowance') => ({
      id,
      businessId: 1,
      employeeId: 12,
      adjustmentIdentity: `identity-${id}`,
      version: 1,
      predecessorVersionId: null,
      salaryComponentId: 3,
      componentCode: 'TRANSPORT',
      componentName: 'Transport',
      componentType: 'Earning',
      valueType: 'Fixed',
      adjustmentKind: kind,
      classification: 'Manual',
      amount: 250.5,
      effectiveFrom: '2026-10-10',
      effectiveTo: null,
      operationalLocationProjectionId: 10,
      reason: 'Transport allowance',
      reference: 'REF-9',
      status,
      createdByUserId: 101,
      createdAtUtc: '2026-10-10T00:00:00Z',
      isReplay: false
    });

    async function createLoaded(permissions: string[], components: unknown[] = [], structures: unknown[] = []) {
      const created = await create(permissions);
      created.http.expectOne(`${base}/salary-components`).flush(components);
      created.http.expectOne(`${base}/salary-structures`).flush(structures);
      created.fixture.detectChanges();
      return created;
    }

    function element(fixture: { nativeElement: HTMLElement }) {
      return fixture.nativeElement;
    }

    function buttonByText(root: HTMLElement, text: string): HTMLButtonElement | undefined {
      return Array.from(root.querySelectorAll('button')).find(button => button.textContent?.trim() === text);
    }

    function buttonsByText(root: HTMLElement, text: string): HTMLButtonElement[] {
      return Array.from(root.querySelectorAll('button')).filter(button => button.textContent?.trim() === text);
    }

    it('renders a single page-header h1 inside the main landmark with kit cards and section headings', async () => {
      const { fixture, http } = await createLoaded(['payroll.salary.view', 'payroll.allowance_deduction.view']);
      const root = element(fixture);
      expect(root.querySelectorAll('h1').length).toBe(1);
      expect(root.querySelector('main app-ui-page-header h1')?.textContent).toContain('Payroll Salary Setup');
      expect(root.querySelector('app-ui-page-header')?.textContent)
        .toContain('Manage reusable Business-scoped salary components and structure templates.');
      const headings = Array.from(root.querySelectorAll('app-ui-card > .ui-card__header h2'))
        .map(heading => heading.textContent?.trim());
      expect(headings).toEqual([
        'Initial employee salary setup',
        'Employee salary requests and approved salary',
        'Employee allowances and deductions',
        'Salary components',
        'Salary structure templates'
      ]);
      http.verify();
    });

    it('ties every visible form label to its control and keeps the ngModel bindings', async () => {
      const { fixture, page, http } = await createLoaded([
        'payroll.salary.view', 'payroll.salary.manage', 'payroll.salary_setup.create'
      ]);
      const root = element(fixture);
      const employee = root.querySelector('input[name="setupEmployeeId"]') as HTMLInputElement;
      const reason = root.querySelector('input[name="setupReason"]') as HTMLInputElement;
      const labelFor = (control: HTMLElement) => root.querySelector(`label[for="${control.id}"]`);
      expect(employee.id).toBeTruthy();
      expect(labelFor(employee)?.textContent).toContain('Employee ID');
      expect(labelFor(reason)?.textContent).toContain('Reason');
      expect(employee.getAttribute('aria-required')).toBe('true');

      reason.value = reason.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Typed reason', (reason.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Typed reason';
      reason.dispatchEvent(new Event('input'));
      expect(page.setupReason).toBe('Typed reason');
      page.setupEmployeeId = 44;
      fixture.detectChanges();
      await fixture.whenStable();
      expect(employee.value).toBe('44');
      http.verify();
    });

    it('lists salary components in a named table region with status text and sortable headers', async () => {
      const { fixture, http } = await createLoaded(
        ['payroll.salary.view'],
        [componentRow(2, 'BONUS', { valueType: 'Formula', formulaExpression: 'BASE * 0.1' }), componentRow(1, 'BASE', {}, false)]
      );
      const root = element(fixture);
      const region = root.querySelector('[role="region"][aria-label="Salary components"]') as HTMLElement;
      expect(region).toBeTruthy();
      const rows = Array.from(region.querySelectorAll('tbody tr')).map(row => row.textContent?.replace(/\s+/g, ' ').trim());
      expect(rows[0]).toContain('BONUS');
      expect(rows[0]).toContain('Formula: BASE * 0.1');
      expect(rows[0]).toContain('Active');
      expect(rows[1]).toContain('BASE');
      expect(rows[1]).toContain('Inactive');
      const codeHeader = region.querySelector('th') as HTMLElement;
      expect(codeHeader.getAttribute('aria-sort')).toBe('none');
      (codeHeader.querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(codeHeader.getAttribute('aria-sort')).toBe('ascending');
      const sorted = Array.from(region.querySelectorAll('tbody tr td:first-child')).map(cell => cell.textContent?.trim());
      expect(sorted).toEqual(['BASE', 'BONUS']);
      http.verify();
    });

    it('hides the actions column and management forms without salary manage permission', async () => {
      const { fixture, http } = await createLoaded(['payroll.salary.view'], [componentRow(1, 'BASE')], [structureRow(8, 'STANDARD')]);
      const root = element(fixture);
      expect(root.textContent).toContain('BASE');
      expect(root.textContent).toContain('STANDARD');
      expect(buttonByText(root, 'Edit')).toBeUndefined();
      expect(buttonByText(root, 'Deactivate')).toBeUndefined();
      expect(Array.from(root.querySelectorAll('th')).some(header => header.textContent?.trim() === 'Actions')).toBe(false);
      expect(root.textContent).not.toContain('Create structure');
      expect(root.querySelector('input[name="componentCode"]')).toBeNull();
      http.verify();
    });

    it('shows Edit and Deactivate only for active rows when the user can manage, and edits through the form', async () => {
      const { fixture, page, http } = await createLoaded(
        ['payroll.salary.view', 'payroll.salary.manage'],
        [componentRow(1, 'BASE'), componentRow(2, 'OLD', {}, false)],
        [structureRow(8, 'STANDARD')]
      );
      const root = element(fixture);
      const componentTable = root.querySelector('[aria-label="Salary components"]') as HTMLElement;
      expect(componentTable.querySelectorAll('tbody tr').length).toBe(2);
      expect(componentTable.querySelectorAll('button[aria-label="Edit component BASE"]').length).toBe(1);
      expect(componentTable.querySelectorAll('button[aria-label="Deactivate component BASE"]').length).toBe(1);
      expect(componentTable.querySelector('button[aria-label="Edit component OLD"]')).toBeNull();
      expect(root.textContent).toContain('Create component');

      (componentTable.querySelector('button[aria-label="Edit component BASE"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(page.componentEdit?.id).toBe(1);
      expect(root.textContent).toContain('Edit component');
      const code = root.querySelector('input[name="componentCode"]') as HTMLInputElement;
      expect(code.readOnly).toBe(true);
      expect(buttonByText(root, 'Cancel edit')).toBeTruthy();

      const structureTable = root.querySelector('[aria-label="Salary structure templates"]') as HTMLElement;
      expect(structureTable.querySelector('button[aria-label="Edit structure STANDARD"]')).toBeTruthy();
      http.verify();
    });

    it('sends the deactivate request from the row action', async () => {
      const { fixture, http } = await createLoaded(['payroll.salary.view', 'payroll.salary.manage'], [componentRow(1, 'BASE')]);
      (element(fixture).querySelector('button[aria-label="Deactivate component BASE"]') as HTMLButtonElement).click();
      const request = http.expectOne(req => req.url.startsWith(`${base}/salary-components/1`));
      expect(request.request.method).not.toBe('GET');
      request.flush({});
      http.match(() => true).forEach(pending => pending.flush([]));
    });

    it('searches the loaded components in the browser without extra requests', async () => {
      const { fixture, http } = await createLoaded(
        ['payroll.salary.view'], [componentRow(1, 'BASE'), componentRow(2, 'TRANSPORT'), componentRow(3, 'BONUS')]
      );
      const root = element(fixture);
      const search = root.querySelector('input[aria-label="Search salary components"]') as HTMLInputElement;
      search.value = search.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('trans', (search.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'trans';
      search.dispatchEvent(new Event('input'));
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      fixture.detectChanges();
      const table = root.querySelector('[aria-label="Salary components"]') as HTMLElement;
      expect(Array.from(table.querySelectorAll('tbody tr')).map(row => row.textContent)).toEqual([
        expect.stringContaining('TRANSPORT')
      ]);
      search.value = search.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('zzz', (search.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'zzz';
      search.dispatchEvent(new Event('input'));
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      fixture.detectChanges();
      expect(root.textContent).toContain('No matching results');
      http.verify();
    });

    it('paginates long component lists on the client', async () => {
      const rows = Array.from({ length: 12 }, (_, index) => componentRow(index + 1, `C${String(index + 1).padStart(2, '0')}`));
      const { fixture, http } = await createLoaded(['payroll.salary.view'], rows);
      const root = element(fixture);
      const table = root.querySelector('[aria-label="Salary components"]') as HTMLElement;
      expect(table.querySelectorAll('tbody tr').length).toBe(10);
      expect(root.textContent).toContain('Page 1 of 2');
      (root.querySelector('button[aria-label="Next page"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(table.querySelectorAll('tbody tr').length).toBe(2);
      expect(root.textContent).toContain('Page 2 of 2');
      http.verify();
    });

    it('shows the empty messages for components and structures', async () => {
      const { fixture, http } = await createLoaded(['payroll.salary.view']);
      const root = element(fixture);
      expect(root.querySelectorAll('app-ui-state').length).toBeGreaterThanOrEqual(2);
      expect(root.textContent).toContain('No salary components have been configured.');
      expect(root.textContent).toContain('No salary structure templates have been configured.');
      http.verify();
    });

    it('shows a loading notice while the lists load and a role=alert error when loading fails', async () => {
      const { fixture, page, http } = await create(['payroll.salary.view']);
      const root = element(fixture);
      expect(page.busy()).toBe(true);
      expect(root.querySelector('app-ui-alert[role="status"]')?.textContent).toContain('Loading / saving...');
      expect(root.textContent).not.toContain('No salary components have been configured.');
      http.expectOne(`${base}/salary-components`).flush({ detail: 'Payroll service unavailable' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      const alert = root.querySelector('app-ui-alert[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Payroll service unavailable');
      expect(root.textContent).not.toContain('Loading / saving...');
    });

    it('renders the no-access warning as an alert without any data cards', async () => {
      const { fixture, http } = await create([]);
      const root = element(fixture);
      expect(root.querySelector('app-ui-alert[role="alert"]')?.textContent).toContain('You do not have salary setup view access.');
      expect(root.querySelectorAll('app-ui-card').length).toBe(0);
      expect(root.querySelectorAll('h1').length).toBe(1);
      http.verify();
    });

    it('marks calculated setup values as read-only with a visible hint and keeps validation on submit', async () => {
      const { fixture, page, http } = await createLoaded(
        ['payroll.salary.view', 'payroll.salary.manage', 'payroll.salary_setup.create'],
        [componentRow(3, 'BASE'), componentRow(4, 'TAX', { valueType: 'Calculated' })],
        [{
          ...structureRow(8, 'STANDARD'),
          data: {
            code: 'STANDARD', name: 'Standard',
            lines: [
              { salaryComponentId: 3, defaultValue: 900, formulaOverride: null, calculationOrder: 1 },
              { salaryComponentId: 4, defaultValue: null, formulaOverride: null, calculationOrder: 2 }
            ]
          }
        }]
      );
      const root = element(fixture);
      (buttonByText(root, 'Create salary setup request') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(page.error()).toBe('Employee, active salary structure, effective date, and reason are required.');
      expect(root.querySelector('app-ui-alert[role="alert"]')?.textContent).toContain(page.error());

      const select = root.querySelector('select[name="setupStructureId"]') as HTMLSelectElement;
      select.selectedIndex = 1;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(page.setupStructureId).toBe(8);
      const [editable, calculated] = Array.from(root.querySelectorAll('fieldset input[type="number"]')) as HTMLInputElement[];
      expect(editable.disabled).toBe(false);
      expect(calculated.disabled).toBe(true);
      expect(root.textContent).toContain('Calculated by the salary structure');
      expect(calculated.getAttribute('aria-describedby')).toBeTruthy();
      http.verify();
    });

    it('disables setup creation and explains why when the setup-create permission is missing', async () => {
      const { fixture, http } = await createLoaded(['payroll.salary.view', 'payroll.salary.manage']);
      const root = element(fixture);
      expect((buttonByText(root, 'Create salary setup request') as HTMLButtonElement).disabled).toBe(true);
      expect(root.textContent).toContain('Salary setup creation requires salary manage and setup-create permissions.');
      http.verify();
    });

    it('shows approval buttons for setup requests only with the matching permissions', async () => {
      const full = await createLoaded([
        'payroll.salary.view', 'payroll.salary_setup.submit', 'payroll.salary_setup.approve'
      ]);
      full.page.salaryRequests.set([salaryRequest('Draft', 1), salaryRequest('PendingApproval', 2)]);
      full.fixture.detectChanges();
      const root = element(full.fixture);
      expect(buttonsByText(root, 'Submit for approval').length).toBe(1);
      expect(buttonsByText(root, 'Approve').length).toBe(1);
      expect(buttonsByText(root, 'Reject').length).toBe(1);
      expect(buttonsByText(root, 'Cancel request').length).toBe(1);
      expect(root.textContent).toContain('Request #1');
      expect(root.textContent).toContain('PendingApproval');
      expect(root.querySelector('input[name="decisionReason"]')).toBeTruthy();

      (buttonByText(root, 'Reject') as HTMLButtonElement).click();
      full.fixture.detectChanges();
      expect(root.querySelector('app-ui-alert[role="alert"]')?.textContent)
        .toContain('A reason is required to reject this salary setup.');
      full.http.verify();
    });

    it('hides every approval action when the user only has view access', async () => {
      const { fixture, page, http } = await createLoaded(['payroll.salary.view']);
      page.salaryRequests.set([salaryRequest('Draft', 1), salaryRequest('PendingApproval', 2)]);
      fixture.detectChanges();
      const root = element(fixture);
      expect(root.textContent).toContain('Request #2');
      for (const label of ['Submit for approval', 'Approve', 'Reject', 'Cancel request']) {
        expect(buttonByText(root, label)).toBeUndefined();
      }
      http.verify();
    });

    it('shows revision approval actions by revision permission and the proposed amounts', async () => {
      const { fixture, page, http } = await createLoaded([
        'payroll.salary.view', 'payroll.salary_revision.approve'
      ]);
      page.salaryRevisionRequests.set([{
        id: 7, businessId: 1, employeeId: 12, sourceSalaryVersionId: 91, effectiveFrom: '2026-10-20',
        operationalLocationProjectionId: 10, currentComponents: [],
        proposedComponents: [{
          salaryComponentId: 3, code: 'BASE', name: 'Base', componentType: 'Earning', valueType: 'Fixed', value: 1500.25
        }],
        reason: 'Annual adjustment', status: 'PendingApproval', approvalInstanceId: null, createdByUserId: 1,
        createdAtUtc: '2026-10-10T00:00:00Z', submittedByUserId: null, submittedAtUtc: null,
        decidedByUserId: null, decidedAtUtc: null, decisionReason: null, version: 1,
        approvalSteps: [], approvalHistory: []
      } as never]);
      fixture.detectChanges();
      const root = element(fixture);
      const table = root.querySelector('[aria-label="Proposed components of revision #7"]') as HTMLElement;
      expect(table.textContent).toContain('BASE');
      expect(table.textContent).toContain('1500.25');
      expect(root.textContent).toContain('Revision #7');
      expect(buttonsByText(root, 'Approve').length).toBe(1);
      expect(buttonsByText(root, 'Reject').length).toBe(1);
      expect(buttonByText(root, 'Cancel request')).toBeUndefined();
      expect(buttonByText(root, 'Submit for approval')).toBeUndefined();
      http.verify();
    });

    it('shows the approved salary amounts and gates the revision form on its permissions', async () => {
      const salary = {
        id: 91, businessId: 1, employeeId: 12, salarySetupRequestId: 34, salaryStructureId: 8,
        salaryStructureVersion: 2, effectiveFrom: '2026-10-10', operationalLocationProjectionId: 10,
        structureCode: 'STANDARD', structureName: 'Standard',
        components: [
          { salaryComponentId: 3, code: 'BASE', name: 'Base', componentType: 'Earning', valueType: 'Fixed', value: 1250 },
          { salaryComponentId: 4, code: 'TAX', name: 'Tax', componentType: 'Deduction', valueType: 'Calculated', value: 80.5 }
        ],
        approvalInstanceId: 25, approvedByUserId: 202, approvedAtUtc: '2026-10-10T00:00:00Z'
      };
      const viewOnly = await createLoaded(['payroll.salary.view']);
      viewOnly.page.currentSalary.set(salary);
      viewOnly.fixture.detectChanges();
      let root = element(viewOnly.fixture);
      const table = root.querySelector('[aria-label="Approved salary components"]') as HTMLElement;
      expect(table.textContent).toContain('1250');
      expect(table.textContent).toContain('80.5');
      expect(root.textContent).toContain("STANDARD — effective 10/10/2026");
      expect(root.textContent).toContain('Salary manage and revision-create permissions are required to propose a revision.');
      expect(root.querySelector('input[name="revisionReason"]')).toBeNull();
      viewOnly.http.verify();
      TestBed.resetTestingModule();

      const manager = await createLoaded(['payroll.salary.view', 'payroll.salary.manage', 'payroll.salary_revision.create']);
      manager.page.currentSalary.set(salary);
      manager.fixture.detectChanges();
      await manager.fixture.whenStable();
      manager.fixture.detectChanges();
      root = element(manager.fixture);
      expect(root.querySelector('input[name="revisionReason"]')).toBeTruthy();
      const revisionForm = (root.querySelector('input[name="revisionReason"]') as HTMLElement).closest('form') as HTMLFormElement;
      const revisionInputs = Array.from(revisionForm.querySelectorAll('fieldset input[type="number"]')) as HTMLInputElement[];
      expect(revisionInputs.length).toBe(2);
      expect(revisionInputs[1].disabled).toBe(true);
      expect(revisionInputs[0].disabled).toBe(false);
      expect(root.textContent).toContain('Retained from the frozen salary snapshot');
      expect(buttonByText(root, 'Create revision request')).toBeTruthy();
      manager.http.verify();
    });

    it('keeps adjustments and their amounts hidden without the adjustment view permission', async () => {
      const { fixture, page, http } = await createLoaded(['payroll.salary.view']);
      page.adjustments.set([adjustment(1) as never]);
      fixture.detectChanges();
      const root = element(fixture);
      expect(root.textContent).not.toContain('Employee allowances and deductions');
      expect(root.textContent).not.toContain('250.5');
      expect(root.textContent).not.toContain('TRANSPORT');
      http.verify();
    });

    it('lists adjustments in a table with unchanged amounts and permission-based row actions', async () => {
      const viewer = await createLoaded(['payroll.salary.view', 'payroll.allowance_deduction.view']);
      viewer.page.adjustments.set([adjustment(1) as never, adjustment(2, 'Ended') as never, adjustment(3, 'Active', 'OneTimeEarning') as never]);
      viewer.page.adjustmentsLoaded.set(true);
      viewer.fixture.detectChanges();
      let root = element(viewer.fixture);
      const table = root.querySelector('[aria-label="Employee adjustments"]') as HTMLElement;
      expect(table.querySelectorAll('tbody tr').length).toBe(3);
      expect(table.textContent).toContain('250.5');
      expect(table.textContent).toContain('REF-9');
      expect(table.textContent).toContain('Ended');
      expect(buttonsByText(root, 'Version history').length).toBe(3);
      expect(buttonByText(root, 'Create successor')).toBeUndefined();
      expect(buttonByText(root, 'End adjustment')).toBeUndefined();
      expect(root.textContent).toContain('Adjustment management permission is required to create, version, or end adjustments.');
      expect(root.querySelector('input[name="adjustmentAmount"]')).toBeNull();
      viewer.http.verify();
      TestBed.resetTestingModule();

      const manager = await createLoaded(['payroll.salary.view', 'payroll.allowance_deduction.view', 'payroll.allowance_deduction.manage']);
      manager.page.adjustments.set([adjustment(1) as never, adjustment(2, 'Ended') as never, adjustment(3, 'Active', 'OneTimeEarning') as never]);
      manager.fixture.detectChanges();
      root = element(manager.fixture);
      expect(buttonsByText(root, 'Create successor').length).toBe(1);
      expect(buttonsByText(root, 'End adjustment').length).toBe(1);
      expect(root.querySelector('input[name="adjustmentAmount"]')).toBeTruthy();
      manager.http.verify();
    });

    it('starts a successor version from the row action and cancels it again', async () => {
      const { fixture, page, http } = await createLoaded([
        'payroll.salary.view', 'payroll.allowance_deduction.view', 'payroll.allowance_deduction.manage'
      ]);
      page.adjustments.set([adjustment(1) as never]);
      fixture.detectChanges();
      const root = element(fixture);
      expect(buttonByText(root, 'Cancel version')).toBeUndefined();
      (buttonByText(root, 'Create successor') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(page.adjustmentToVersion?.id).toBe(1);
      expect(root.textContent).toContain('Create recurring adjustment successor');
      expect(buttonByText(root, 'Create successor version')).toBeTruthy();
      (buttonByText(root, 'Cancel version') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(page.adjustmentToVersion).toBeNull();
      expect(root.textContent).toContain('Create employee adjustment');
      http.verify();
    });

    it('loads the adjustment version history from the row action into its own table', async () => {
      const { fixture, page, http } = await createLoaded(['payroll.salary.view', 'payroll.allowance_deduction.view']);
      page.adjustments.set([adjustment(1) as never]);
      fixture.detectChanges();
      const root = element(fixture);
      (buttonByText(root, 'Version history') as HTMLButtonElement).click();
      http.expectOne(`${base}/adjustments/identity-1/history`).flush([
        { ...adjustment(1), version: 2, predecessorVersionId: 1, amount: 300 }
      ]);
      fixture.detectChanges();
      const history = root.querySelector('[aria-label="Adjustment version history"]') as HTMLElement;
      expect(history.textContent).toContain('300');
      expect(history.textContent).toContain('Successor of version record');
      expect(history.querySelectorAll('tbody tr').length).toBe(1);
      http.verify();
    });

    it('shows the adjustment empty state after an empty load and validation errors as alerts', async () => {
      const { fixture, page, http } = await createLoaded(['payroll.salary.view', 'payroll.allowance_deduction.view']);
      const root = element(fixture);
      (buttonByText(root, 'Load adjustments') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(root.querySelector('app-ui-alert[role="alert"]')?.textContent)
        .toContain('Adjustment view permission, employee ID, and effective date are required.');

      page.adjustmentEmployeeId = 12;
      page.adjustmentAsOf = '2026-10-10';
      page.loadAdjustments();
      http.expectOne(req => req.url === `${base}/employees/12/adjustments`).flush([]);
      fixture.detectChanges();
      expect(root.textContent).toContain('No effective adjustments were found.');
      http.verify();
    });

    it('shows the empty salary history message once an employee has no data', async () => {
      const { fixture, page, http } = await createLoaded(['payroll.salary.view']);
      page.historyEmployeeId = 12;
      page.loadEmployeeSalary();
      http.expectOne(`${base}/employees/12/salary-setup-requests`).flush([]);
      http.expectOne(`${base}/employees/12/salary-revision-requests`).flush([]);
      http.expectOne(`${base}/employees/12/salary-history`).flush([]);
      fixture.detectChanges();
      expect(element(fixture).textContent).toContain('No salary setup requests or approved salary were found.');
      http.verify();
    });
  });
});
