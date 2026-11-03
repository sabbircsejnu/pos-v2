import { CompanyDateService } from '../../services/company-date.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';
import { PayrollPayment, PayrollPaymentBatch } from '../../services/payroll-payment.service';
import { PayrollPaymentsPageComponent } from './payroll-payments-page.component';

describe('Payroll payments page', () => {
  const base = `${environment.apiUrl}/payroll/v1/payments`;

  afterEach(() => TestBed.resetTestingModule());

  function payment(status: string, version: number): PayrollPayment {
    return {
      id: 33,
      businessId: 1,
      payrollPeriodId: 5,
      payrollRunId: 9,
      payrollEmployeeResultId: 12,
      employeeId: 2,
      locationIdSnapshot: 10,
      payableAmount: 100,
      paidAmount: 0,
      outstandingAmount: 100,
      currencyCode: 'BDT',
      method: 'Cash',
      status,
      version,
      attemptNo: 1,
      originalPaymentId: null,
      idempotencyKey: 'payment-key',
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      notes: null,
      allocations: [{
        id: 44,
        allocationNo: 1,
        method: 'Cash',
        amount: 100,
        settledAmount: 0,
        state: 'Pending',
        externalReference: null,
        paymentDestinationId: null,
        paymentDestinationVersionNo: null,
        bankName: null,
        accountName: null,
        accountNumber: null,
        mobileProvider: null,
        mobileAccountName: null,
        mobileAccountNumber: null
      }],
      paidAtUtc: null,
      externalReference: null,
      failureReturnReason: null,
      heldByUserId: null,
      heldAtUtc: null,
      holdReason: null,
      releasedByUserId: null,
      releasedAtUtc: null,
      releaseReason: null,
      resolutionStatus: 'None',
      resolutionEvidenceReference: null,
      evidence: [],
      paymentBatchId: null,
      isReplay: false
    };
  }

  async function create(permissions: string[]) {
    await TestBed.configureTestingModule({
      imports: [PayrollPaymentsPageComponent],
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
    const fixture = TestBed.createComponent(PayrollPaymentsPageComponent);
    fixture.detectChanges();
    return {
      fixture,
      page: fixture.componentInstance,
      http: TestBed.inject(HttpTestingController)
    };
  }

  it('requires the payment and salary permissions before showing creation', async () => {
    const { fixture, page, http } = await create(['payroll.payment.process']);

    expect(page.canProcess()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Payroll payment and salary view permissions');
    page.resultId = 10;
    page.runVersion = 4;
    page.cashAmount = 100;
    page.create();

    http.expectNone(base);
    expect(page.error()).toContain('permissions are required');
    http.verify();
  });

  it('creates mixed pending allocations and hides sensitive destination fields', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.process',
      'payroll.payment.view',
      'payroll.salary.view',
      'payroll.payment.view_sensitive'
    ]);
    page.resultId = 12;
    page.runVersion = 7;
    page.cashAmount = 500;
    page.bankAmount = 250;
    page.externalReference = 'attempt-7';
    page.create();

    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      payrollEmployeeResultId: 12,
      expectedRunVersion: 7,
      allocations: [
        { method: 'Cash', amount: 500, externalReference: 'attempt-7' },
        { method: 'Bank', amount: 250, externalReference: 'attempt-7' }
      ]
    });
    expect(request.request.body.idempotencyKey).toBeTruthy();
    request.flush({
      id: 33,
      businessId: 1,
      payrollPeriodId: 5,
      payrollRunId: 9,
      payrollEmployeeResultId: 12,
      employeeId: 2,
      locationIdSnapshot: 10,
      payableAmount: 1000,
      paidAmount: 0,
      outstandingAmount: 1000,
      currencyCode: 'BDT',
      method: 'Mixed',
      status: 'Pending',
      version: 1,
      attemptNo: 1,
      idempotencyKey: 'key',
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      notes: null,
      isReplay: false,
      allocations: [{
        id: 44,
        allocationNo: 2,
        method: 'Bank',
        amount: 250,
        settledAmount: 0,
        state: 'Pending',
        externalReference: 'attempt-7',
        paymentDestinationId: 11,
        paymentDestinationVersionNo: 1,
        bankName: 'Bank A',
        accountName: 'Payroll',
        accountNumber: '001122',
        mobileProvider: null,
        mobileAccountName: null,
        mobileAccountNumber: null
      }]
    });
    fixture.detectChanges();

    expect(page.payment()?.status).toBe('Pending');
    expect(page.payment()?.paidAmount).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('Outstanding: 1000');
    expect(fixture.nativeElement.textContent).toContain('001122');
    http.verify();
  });

  it('preserves the idempotency key after conflict and reads payment details with permissions', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.process',
      'payroll.payment.view',
      'payroll.salary.view'
    ]);
    page.resultId = 12;
    page.runVersion = 7;
    page.cashAmount = 500;
    page.create();
    const first = http.expectOne(base);
    const key = first.request.body.idempotencyKey as string;
    first.flush({ title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
    expect(page.error()).toContain('conflicts');

    page.create();
    const retry = http.expectOne(base);
    expect(retry.request.body.idempotencyKey).toBe(key);
    retry.flush({
      id: 33,
      businessId: 1,
      payrollPeriodId: 5,
      payrollRunId: 9,
      payrollEmployeeResultId: 12,
      employeeId: 2,
      locationIdSnapshot: 10,
      payableAmount: 1000,
      paidAmount: 0,
      outstandingAmount: 1000,
      currencyCode: 'BDT',
      method: 'Cash',
      status: 'Pending',
      version: 1,
      attemptNo: 1,
      idempotencyKey: key,
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      notes: null,
      isReplay: true,
      allocations: []
    });
    page.load();
    const get = http.expectOne(`${base}/33`);
    expect(get.request.method).toBe('GET');
    get.flush({
      id: 33,
      businessId: 1,
      payrollPeriodId: 5,
      payrollRunId: 9,
      payrollEmployeeResultId: 12,
      employeeId: 2,
      locationIdSnapshot: 10,
      payableAmount: 1000,
      paidAmount: 0,
      outstandingAmount: 1000,
      currencyCode: 'BDT',
      method: 'Cash',
      status: 'Pending',
      version: 1,
      attemptNo: 1,
      idempotencyKey: key,
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      notes: null,
      isReplay: false,
      allocations: []
    });
    http.expectOne(`${environment.apiUrl}/payroll/v1/payment-reconciliation?paymentId=33`)
      .flush([]);
    fixture.detectChanges();
    expect(page.payment()?.id).toBe(33);
    http.verify();
  });

  it('requires sensitive destination permission for non-cash allocations', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.process',
      'payroll.salary.view'
    ]);
    page.resultId = 12;
    page.runVersion = 7;
    page.bankAmount = 250;
    page.create();

    http.expectNone(base);
    expect(page.error()).toContain('sensitive-view permission');
    expect(fixture.nativeElement.textContent).toContain(
      'Bank and mobile payment attempts require payment destination sensitive-view permission');
    http.verify();
  });

  it('does not render sensitive fields in payment details without the sensitive-view permission', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.view',
      'payroll.salary.view'
    ]);
    page.paymentId = 33;
    page.load();
    http.expectOne(`${base}/33`).flush({
      id: 33,
      businessId: 1,
      payrollPeriodId: 5,
      payrollRunId: 9,
      payrollEmployeeResultId: 12,
      employeeId: 2,
      locationIdSnapshot: 10,
      payableAmount: 1000,
      paidAmount: 0,
      outstandingAmount: 1000,
      currencyCode: 'BDT',
      method: 'Bank',
      status: 'Pending',
      version: 1,
      attemptNo: 1,
      idempotencyKey: 'key',
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      notes: null,
      isReplay: false,
      allocations: [{
        id: 44,
        allocationNo: 1,
        method: 'Bank',
        amount: 1000,
        settledAmount: 0,
        state: 'Pending',
        externalReference: null,
        paymentDestinationId: 11,
        paymentDestinationVersionNo: 1,
        bankName: 'Bank A',
        accountName: 'Payroll',
        accountNumber: '001122',
        mobileProvider: null,
        mobileAccountName: null,
        mobileAccountNumber: null
      }]
    });
    http.expectOne(`${environment.apiUrl}/payroll/v1/payment-reconciliation?paymentId=33`)
      .flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Bank A');
    expect(fixture.nativeElement.textContent).not.toContain('001122');
    http.verify();
  });

  it('sends versioned hold and settlement-evidence actions for authorized users', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.hold',
      'payroll.payment.process',
      'payroll.payment.view',
      'payroll.salary.view'
    ]);
    page.payment.set(payment('Pending', 1));
    page.actionReason = 'Risk review';
    page.hold();
    const hold = http.expectOne(`${base}/33/hold`);
    expect(hold.request.body).toEqual({ expectedVersion: 1, reason: 'Risk review' });
    hold.flush(payment('Held', 2));

    page.payment.set(payment('Processing', 2));
    page.evidenceAllocationNo = 1;
    page.evidenceAmount = 100;
    page.evidenceDate = '2026-10-11';
    page.evidenceReference = 'cash-receipt-100';
    page.settle();
    const settlement = http.expectOne(`${base}/33/settlements`);
    expect(settlement.request.body).toMatchObject({
      expectedVersion: 2,
      evidenceDate: '2026-10-11',
      evidenceReference: 'cash-receipt-100',
      allocations: [{ allocationNo: 1, amount: 100 }]
    });
    expect(settlement.request.body.idempotencyKey).toBeTruthy();
    settlement.flush(payment('Paid', 3));
    fixture.detectChanges();
    expect(page.payment()?.status).toBe('Paid');
    http.verify();
  });

  it('posts versioned reconciliation evidence only with reconciliation permission', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.reconcile',
      'payroll.payment.view',
      'payroll.salary.view'
    ]);
    const current = payment('Paid', 4);
    current.paidAmount = 100;
    current.outstandingAmount = 0;
    current.allocations[0].settledAmount = 100;
    current.allocations[0].state = 'Settled';
    page.payment.set(current);
    page.paymentId = current.id;
    page.reconciliationAllocationNo = 1;
    page.reconciliationSettledAmount = 100;
    page.reconciliationDate = '2026-10-11';
    page.reconciliationReference = 'cash-confirmed-33';
    page.reconcilePayment();

    const request = http.expectOne(`${base}/33/reconcile`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      expectedPaymentVersion: 4,
      expectedReconciliationVersion: 0,
      allocationNo: 1,
      settledAmount: 100,
      settlementDate: '2026-10-11',
      evidenceSource: 'Cash',
      externalReference: 'cash-confirmed-33'
    });
    request.flush({ items: [], isReplay: false });
    expect(page.error()).toBe('');
    expect(page.canReconcile()).toBe(true);
    http.expectOne(`${base}/33`).flush(current);
    http.expectOne(`${environment.apiUrl}/payroll/v1/payment-reconciliation?paymentId=33`)
      .flush([]);
    fixture.detectChanges();
    expect(page.payment()?.version).toBe(4);
    http.verify();
  });

  it('does not expose lifecycle actions without their exact permissions', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.view',
      'payroll.salary.view',
      'payroll.payment.process'
    ]);
    page.payment.set(payment('Pending', 1));
    fixture.detectChanges();

    expect(page.canHold()).toBe(false);
    expect(page.canRelease()).toBe(false);
    expect(page.canRetry()).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain('Hold payment');
    expect(fixture.nativeElement.textContent).not.toContain('Release payment');
    http.verify();
  });

  it('records a cash-batch submission with version and idempotency evidence', async () => {
    const { fixture, page, http } = await create([
      'payroll.payment.process',
      'payroll.payment.view',
      'payroll.salary.view'
    ]);
    const batch: PayrollPaymentBatch = {
      id: 81,
      businessId: 1,
      payrollRunId: 9,
      payrollPeriodId: 5,
      batchReference: 'cash-81',
      method: 'Cash',
      currencyCode: 'BDT',
      expectedAmount: 100,
      status: 'Ready',
      version: 3,
      createdByUserId: 101,
      createdAtUtc: '2026-10-01T00:00:00Z',
      payments: [{
        paymentId: 33,
        employeeId: 2,
        amount: 100,
        status: 'Ready',
        version: 2,
        method: 'Cash'
      }],
      bankFileVersions: [],
      isReplay: false,
      submittedAtUtc: null,
      submittedByUserId: null
    };
    page.paymentBatch.set(batch);
    page.submissionExternalReference = 'cash-batch-81';
    page.submissionReason = 'Sent to cashier';
    expect(page.canSubmitBatch()).toBe(true);
    page.submitBatch();

    const request = http.expectOne(
      `${environment.apiUrl}/payroll/v1/payment-batches/81/submit`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      expectedVersion: 3,
      externalReference: 'cash-batch-81',
      reason: 'Sent to cashier'
    });
    expect(request.request.body.idempotencyKey).toBeTruthy();
    request.flush({ ...batch, status: 'Processing', version: 4 });
    expect(page.paymentBatch()?.status).toBe('Processing');
    expect(page.submissionExternalReference).toBe('');
    http.verify();
    fixture.destroy();
  });

  describe('shared UI kit presentation', () => {
    const root = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;
    const buttonNames = (element: HTMLElement) =>
      Array.from(element.querySelectorAll('button')).map(button => button.textContent?.trim());
    const buttonByText = (element: HTMLElement, text: string) =>
      Array.from(element.querySelectorAll('button')).find(button => button.textContent?.trim() === text);
    const labelFor = (element: HTMLElement, text: string) =>
      Array.from(element.querySelectorAll('label')).find(item => item.textContent?.includes(text)) as HTMLLabelElement;
    const headings = (element: HTMLElement) =>
      Array.from(element.querySelectorAll('h2, h3, h4')).map(item => item.textContent?.trim());
    const region = (element: HTMLElement, name: string) =>
      element.querySelector(`[role="region"][aria-label="${name}"]`) as HTMLElement | null;

    const view = ['payroll.payment.view', 'payroll.salary.view'];

    function batchOf(overrides: Partial<PayrollPaymentBatch> = {}): PayrollPaymentBatch {
      return {
        id: 81, businessId: 1, payrollRunId: 9, payrollPeriodId: 5, batchReference: 'cash-81',
        method: 'Cash', currencyCode: 'BDT', expectedAmount: 100, status: 'Ready', version: 3,
        createdByUserId: 101, createdAtUtc: '2026-10-01T00:00:00Z',
        payments: [{ paymentId: 33, employeeId: 2, amount: 100, status: 'Ready', version: 2, method: 'Cash' }],
        bankFileVersions: [], isReplay: false, submittedAtUtc: null, submittedByUserId: null,
        ...overrides
      };
    }

    it('renders a single h1 page header inside main with a breadcrumb link', async () => {
      const { fixture } = await create(view);
      const element = root(fixture);

      expect(element.querySelector('main')).not.toBeNull();
      expect(element.querySelectorAll('h1').length).toBe(1);
      expect(element.querySelector('h1')?.textContent).toContain('Payroll Payment Attempts');
      expect(element.textContent).toContain('Amounts remain unsettled until separate settlement evidence is recorded.');
      const crumb = element.querySelector('nav[aria-label="Breadcrumb"] a') as HTMLAnchorElement;
      expect(crumb.getAttribute('href')).toBe('/dashboard');
      expect(element.querySelector('nav[aria-label="Breadcrumb"] [aria-current="page"]')?.textContent)
        .toContain('Payment attempts');
    });

    it('shows a warning alert and no forms without the read permissions', async () => {
      const { fixture } = await create([]);
      const element = root(fixture);

      expect(element.querySelector('app-ui-alert[role="alert"]')?.textContent)
        .toContain('Payroll payment and salary view permissions are required to read payment details.');
      expect(element.querySelector('input[name="paymentId"]')).toBeNull();
      expect(element.querySelector('input[name="resultId"]')).toBeNull();
      expect(buttonNames(element)).not.toContain('Load payment details');
    });

    it('shows loading and error states through the kit and disables submit buttons while busy', async () => {
      const { fixture, page } = await create([...view, 'payroll.payment.process']);
      const element = root(fixture);

      page.busy.set(true);
      fixture.detectChanges();
      expect(element.querySelector('app-ui-state[role="status"]')?.textContent)
        .toContain('Loading / saving Payroll payment...');
      for (const name of ['Create pending attempt', 'Load payment details', 'Load batches', 'Load batch details']) {
        expect((buttonByText(element, name) as HTMLButtonElement).disabled).toBe(true);
      }

      page.busy.set(false);
      page.error.set('Payment creation failed.');
      fixture.detectChanges();
      expect(element.querySelector('app-ui-state')).toBeNull();
      expect(element.querySelector('app-ui-alert[role="alert"]')?.textContent).toContain('Payment creation failed.');
    });

    it('ties creation labels to controls, flags required fields and keeps ngModel bindings through submit', async () => {
      const { fixture, page, http } = await create([...view, 'payroll.payment.process']);
      const element = root(fixture);

      for (const [label, name, required] of [
        ['Payroll employee result ID', 'resultId', true],
        ['Approved run version', 'runVersion', true],
        ['Cash amount', 'cashAmount', false],
        ['External reference', 'externalReference', false]
      ] as const) {
        const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        expect(labelFor(element, label).htmlFor).toBe(input.id);
        expect(input.required).toBe(required);
      }
      expect(element.querySelector('input[name="bankAmount"]')).toBeNull();
      expect(element.querySelector('input[name="mobileAmount"]')).toBeNull();
      expect(element.textContent).toContain('Bank and mobile payment attempts require payment destination');

      const type = (name: string, value: string) => {
        const input = element.querySelector(`input[name="${name}"]`) as HTMLInputElement;
        input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(value, (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : value;
        input.dispatchEvent(new Event('input'));
      };
      type('resultId', '12');
      type('runVersion', '7');
      type('cashAmount', '500');
      type('externalReference', 'attempt-7');
      await fixture.whenStable();
      expect(page.resultId).toBe(12);
      expect(page.runVersion).toBe(7);
      expect(page.cashAmount).toBe(500);
      expect(page.externalReference).toBe('attempt-7');

      element.querySelectorAll('form')[0].dispatchEvent(new Event('submit'));
      const request = http.expectOne(base);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toMatchObject({
        payrollEmployeeResultId: 12,
        expectedRunVersion: 7,
        allocations: [{ method: 'Cash', amount: 500, externalReference: 'attempt-7' }]
      });
    });

    it('shows bank and mobile amount fields only with the sensitive-view permission', async () => {
      const { fixture } = await create([...view, 'payroll.payment.process', 'payroll.payment.view_sensitive']);
      const element = root(fixture);

      for (const [label, name] of [['Bank amount', 'bankAmount'], ['Mobile amount', 'mobileAmount']]) {
        expect(labelFor(element, label).htmlFor).toBe((element.querySelector(`input[name="${name}"]`) as HTMLInputElement).id);
      }
      expect(element.textContent).not.toContain('Bank and mobile payment attempts require');
    });

    it('looks up a payment from the labelled Payment ID field', async () => {
      const { fixture, page, http } = await create(view);
      const element = root(fixture);
      const input = element.querySelector('input[name="paymentId"]') as HTMLInputElement;

      expect(labelFor(element, 'Payment ID').htmlFor).toBe(input.id);
      input.value = input.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('33', (input.getAttribute('placeholder') ?? '').includes('HH:mm')) : '33';
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(page.paymentId).toBe(33);

      input.form!.dispatchEvent(new Event('submit'));
      http.expectOne(`${base}/33`).flush(payment('Pending', 1));
      http.expectOne(`${environment.apiUrl}/payroll/v1/payment-reconciliation?paymentId=33`).flush([]);
      fixture.detectChanges();

      expect(headings(element)).toContain('Payment #33');
      expect(element.querySelector('app-ui-card app-ui-badge')?.textContent?.trim()).toBe('Pending');
    });

    it('presents amounts verbatim and the allocations table in a named region', async () => {
      const { fixture, page } = await create(view);
      const current = payment('Pending', 1);
      current.payableAmount = 1234.5;
      current.outstandingAmount = 1234.5;
      current.allocations[0].externalReference = 'ref-1';
      page.payment.set(current);
      fixture.detectChanges();
      const element = root(fixture);

      expect(element.textContent).toContain('Payable: 1234.5 BDT');
      expect(element.textContent).toContain('Settled: 0');
      expect(element.textContent).toContain('Outstanding: 1234.5');
      const allocations = region(element, 'Payment allocations')!;
      expect(allocations).not.toBeNull();
      expect(allocations.querySelector('input[type="search"]')).toBeNull();
      expect(allocations.querySelectorAll('tbody tr').length).toBe(1);
      expect(allocations.querySelector('tbody tr')?.textContent).toContain('Cash');
      expect(allocations.querySelector('tbody tr app-ui-badge')?.textContent?.trim()).toBe('Pending');
      expect(allocations.querySelector('tbody tr')?.textContent).toContain('ref-1');
      expect(Array.from(allocations.querySelectorAll('th')).map(item => item.textContent?.trim()))
        .not.toContain('Destination');
    });

    it('shows destination details in a column only with the sensitive-view permission', async () => {
      const { fixture, page } = await create([...view, 'payroll.payment.view_sensitive']);
      const current = payment('Pending', 1);
      current.allocations[0].method = 'Bank';
      current.allocations[0].bankName = 'Bank A';
      current.allocations[0].accountNumber = '001122';
      page.payment.set(current);
      fixture.detectChanges();
      const allocations = region(root(fixture), 'Payment allocations')!;

      expect(Array.from(allocations.querySelectorAll('th')).map(item => item.textContent?.trim()))
        .toContain('Destination');
      expect(allocations.textContent).toContain('Bank A');
      expect(allocations.textContent).toContain('001122');
    });

    it('sorts allocations with aria-sort on sortable headers', async () => {
      const { fixture, page } = await create(view);
      const current = payment('Pending', 1);
      current.allocations = [
        { ...current.allocations[0], id: 1, allocationNo: 1, method: 'Mobile', amount: 10 },
        { ...current.allocations[0], id: 2, allocationNo: 2, method: 'Bank', amount: 90 }
      ];
      page.payment.set(current);
      fixture.detectChanges();
      const allocations = region(root(fixture), 'Payment allocations')!;
      const header = () => Array.from(allocations.querySelectorAll('th'))
        .find(cell => cell.textContent?.includes('Method')) as HTMLTableCellElement;

      expect(header().getAttribute('aria-sort')).toBe('none');
      (header().querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(header().getAttribute('aria-sort')).toBe('ascending');
      expect(allocations.querySelector('tbody tr')?.textContent).toContain('Bank');
      (header().querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(allocations.querySelector('tbody tr')?.textContent).toContain('Mobile');
    });

    it('lists evidence with an empty state, and offers settlement posting only with Accounting permission', async () => {
      const noPosting = await create(view);
      noPosting.page.payment.set(payment('Paid', 3));
      noPosting.fixture.detectChanges();
      const emptyElement = root(noPosting.fixture);
      expect(emptyElement.textContent).toContain('No settlement or return evidence recorded');
      expect(region(emptyElement, 'Settlement and return evidence')?.querySelector('tbody tr')).toBeNull();
      expect(emptyElement.textContent).toContain('Payroll Accounting posting permission and salary view are required');

      TestBed.resetTestingModule();
      const posting = await create([...view, 'payroll.accounting.post']);
      const current = payment('Paid', 3);
      current.paidAmount = 100;
      current.evidence = [{
        id: 7, type: 'Settlement', amount: 100, evidenceDate: '2026-10-11', evidenceReference: 'receipt-1',
        verifiedByUserId: 5, verifiedAtUtc: '2026-10-11T00:00:00Z',
        allocations: [{ allocationNo: 1, method: 'Cash', amount: 100 }]
      }, {
        id: 8, type: 'Return', amount: 10, evidenceDate: '2026-10-12', evidenceReference: 'return-1',
        verifiedByUserId: 5, verifiedAtUtc: '2026-10-12T00:00:00Z', allocations: []
      }];
      posting.page.payment.set(current);
      posting.fixture.detectChanges();
      const element = root(posting.fixture);
      const evidence = region(element, 'Settlement and return evidence')!;

      expect(evidence.querySelectorAll('tbody tr').length).toBe(2);
      expect(evidence.textContent).toContain('receipt-1');
      expect(evidence.querySelectorAll('button').length).toBe(1);
      expect(buttonNames(evidence)).toEqual(['Post settlement to Accounting']);
      expect(element.textContent).not.toContain('Payroll Accounting posting permission and salary view are required');

      posting.page.busy.set(true);
      posting.fixture.detectChanges();
      expect((evidence.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    });

    it('shows replay, hold and failure reasons as alerts', async () => {
      const { fixture, page } = await create(view);
      const current = payment('Held', 2);
      current.isReplay = true;
      current.holdReason = 'Risk review';
      current.failureReturnReason = 'Account closed';
      page.payment.set(current);
      fixture.detectChanges();
      const element = root(fixture);
      const alerts = Array.from(element.querySelectorAll('app-ui-alert')).map(item => item.textContent ?? '');

      expect(alerts.join('|')).toContain('This is a replay of the existing idempotent request.');
      expect(alerts.join('|')).toContain('Hold reason: Risk review');
      expect(element.querySelector('app-ui-alert[role="alert"]')?.textContent).toContain('Hold reason: Risk review');
      expect(alerts.join('|')).toContain('Failure/return reason: Account closed');
    });

    it('shows lifecycle action cards only for the matching status and permission', async () => {
      const hold = await create([...view, 'payroll.payment.hold']);
      hold.page.payment.set(payment('Ready', 1));
      hold.fixture.detectChanges();
      const holdElement = root(hold.fixture);
      expect(headings(holdElement)).toContain('Hold payment');
      expect(labelFor(holdElement, 'Hold reason').htmlFor)
        .toBe((holdElement.querySelector('input[name="holdReason"]') as HTMLInputElement).id);
      expect(buttonNames(holdElement)).toContain('Hold payment');
      hold.page.payment.set(payment('Paid', 1));
      hold.fixture.detectChanges();
      expect(buttonNames(holdElement)).not.toContain('Hold payment');

      TestBed.resetTestingModule();
      const release = await create([...view, 'payroll.payment.release']);
      release.page.payment.set(payment('Held', 1));
      release.fixture.detectChanges();
      expect(buttonNames(root(release.fixture))).toContain('Release payment');

      TestBed.resetTestingModule();
      const processing = await create([...view, 'payroll.payment.process']);
      processing.page.payment.set(payment('Processing', 1));
      processing.fixture.detectChanges();
      const processingElement = root(processing.fixture);
      expect(buttonNames(processingElement)).toEqual(
        expect.arrayContaining(['Mark failed', 'Record verified settlement']));
      expect(buttonNames(processingElement)).not.toContain('Record verified return');
      expect(processingElement.querySelector('input[name="settlementAmount"]')).not.toBeNull();
    });

    it('offers the verified return form only once money was paid and keeps its bindings', async () => {
      const { fixture, page } = await create([...view, 'payroll.payment.process']);
      const current = payment('Paid', 3);
      current.paidAmount = 100;
      current.outstandingAmount = 0;
      page.payment.set(current);
      fixture.detectChanges();
      await fixture.whenStable();
      const element = root(fixture);

      expect(buttonNames(element)).toContain('Record verified return');
      const reason = element.querySelector('input[name="returnReason"]') as HTMLInputElement;
      expect(labelFor(element, 'Return reason').htmlFor).toBe(reason.id);
      expect(reason.required).toBe(true);
      reason.value = reason.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('Bank rejected', (reason.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'Bank rejected';
      reason.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(page.actionReason).toBe('Bank rejected');
    });

    it('offers retry only for failed or returned payments with outstanding amounts and disables destination methods without sensitive access', async () => {
      const { fixture, page } = await create([...view, 'payroll.payment.retry']);
      page.payment.set(payment('Failed', 2));
      fixture.detectChanges();
      const element = root(fixture);

      expect(buttonNames(element)).toContain('Retry payment');
      const options = Array.from((element.querySelector('select[name="retryMethod"]') as HTMLSelectElement).options);
      expect(options.map(option => [option.value, option.disabled])).toEqual([
        ['Cash', false], ['Bank', true], ['Mobile', true]
      ]);

      const settled = payment('Failed', 2);
      settled.outstandingAmount = 0;
      page.payment.set(settled);
      fixture.detectChanges();
      expect(buttonNames(element)).not.toContain('Retry payment');
    });

    it('shows the reconciliation card and status table only with the reconcile permission', async () => {
      const denied = await create(view);
      denied.page.payment.set(payment('Paid', 3));
      denied.fixture.detectChanges();
      expect(headings(root(denied.fixture))).not.toContain('Payment reconciliation');

      TestBed.resetTestingModule();
      const { fixture, page } = await create([...view, 'payroll.payment.reconcile']);
      page.payment.set(payment('Paid', 3));
      page.paymentReconciliations.set([{
        id: 1, businessId: 1, payrollPaymentId: 33, paymentBatchId: null, employeeId: 2, payrollPeriodId: 5,
        allocationNo: 1, expectedAmount: 100, settledAmount: 90, difference: 10, status: 'PartiallyReconciled',
        settlementDate: null, evidenceSource: 'Cash', externalReference: 'cash-1', notes: null, version: 1,
        paymentVersion: 3, reconciledByUserId: 5, reconciledAtUtc: '2026-10-11T00:00:00Z', isReplay: false
      }]);
      fixture.detectChanges();
      const element = root(fixture);

      expect(headings(element)).toContain('Payment reconciliation');
      const select = element.querySelector('select[name="reconciliationAllocationNo"]') as HTMLSelectElement;
      expect(labelFor(element, 'Allocation').htmlFor).toBe(select.id);
      expect(select.options.length).toBe(1);
      const table = region(element, 'Allocation reconciliation status')!;
      expect(table.querySelector('app-ui-badge')?.textContent?.trim()).toBe('PartiallyReconciled');
      expect(table.textContent).toContain('cash-1');
      expect(table.textContent).toContain('90');
      expect(labelFor(element, 'Record this as an explicit reconciliation exception').querySelector('input[type="checkbox"]'))
        .not.toBeNull();
    });

    it('lists batches in a table and opens one through an accessibly named View button', async () => {
      const { fixture, page } = await create(view);
      vi.spyOn(page, 'loadBatchReconciliationDetails').mockImplementation(() => undefined);
      page.batches.set([batchOf()]);
      fixture.detectChanges();
      const element = root(fixture);
      const table = region(element, 'Payment batches')!;

      expect(table.querySelector('tbody tr')?.textContent).toContain('Batch #81');
      expect(table.querySelector('tbody tr')?.textContent).toContain('cash-81');
      expect(table.querySelector('tbody tr')?.textContent).toContain('100 BDT');
      expect(table.querySelector('tbody tr app-ui-badge')?.textContent?.trim()).toBe('Ready');
      const open = table.querySelector('button[aria-label="View batch #81"]') as HTMLButtonElement;
      expect(open.textContent?.trim()).toBe('View');
      open.click();
      fixture.detectChanges();

      expect(page.paymentBatch()?.id).toBe(81);
      expect(page.batchId).toBe(81);
      expect(headings(element)).toContain('Batch #81');
      expect(region(element, 'Batch payments')?.textContent).toContain('#33');
    });

    it('loads batches and batch details through their labelled filter fields', async () => {
      const { fixture, page, http } = await create(view);
      const element = root(fixture);
      const runFilter = element.querySelector('input[name="batchListRunId"]') as HTMLInputElement;
      const batchId = element.querySelector('input[name="batchId"]') as HTMLInputElement;

      expect(labelFor(element, 'Filter batches by Payroll run ID (optional)').htmlFor).toBe(runFilter.id);
      expect(labelFor(element, 'Payment batch ID').htmlFor).toBe(batchId.id);
      runFilter.value = runFilter.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('9', (runFilter.getAttribute('placeholder') ?? '').includes('HH:mm')) : '9';
      runFilter.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      runFilter.form!.dispatchEvent(new Event('submit'));
      const list = http.expectOne(`${environment.apiUrl}/payroll/v1/payment-batches?payrollRunId=9`);
      expect(list.request.method).toBe('GET');
      list.flush([batchOf()]);
      fixture.detectChanges();
      expect(region(element, 'Payment batches')?.querySelectorAll('tbody tr').length).toBe(1);
    });

    it('shows bank files, reconcile and submission cards by permission for a Ready bank batch', async () => {
      const bank = batchOf({
        method: 'Bank', payments: [],
        bankFileVersions: [{
          versionNo: 2, contentHash: 'abc123', rowCount: 4, expectedAmount: 400,
          fileName: 'f.csv', generatedByUserId: 1, generatedAtUtc: '2026-10-01T00:00:00Z', generationReason: 'initial'
        }]
      });
      const allowed = await create([
        ...view, 'payroll.payment.process', 'payroll.payment.view_sensitive', 'payroll.bank_file.generate'
      ]);
      allowed.page.paymentBatch.set(bank);
      allowed.fixture.detectChanges();
      const element = root(allowed.fixture);

      expect(region(element, 'Bank file versions')?.textContent).toContain('Bank file v2');
      expect(region(element, 'Bank file versions')?.textContent).toContain('SHA-256 abc123');
      expect(buttonNames(element)).toEqual(expect.arrayContaining([
        'Generate and download bank file', 'Record batch submission'
      ]));
      expect(labelFor(element, 'Generation / regeneration reason').htmlFor)
        .toBe((element.querySelector('input[name="bankFileReason"]') as HTMLInputElement).id);
      expect(element.textContent).toContain('Payment reconciliation requires the payment reconcile permission');
      expect(element.textContent).not.toContain('Generate a bank file before submitting this batch.');

      TestBed.resetTestingModule();
      const denied = await create(view);
      denied.page.paymentBatch.set(batchOf({ method: 'Bank', payments: [] }));
      denied.fixture.detectChanges();
      const deniedElement = root(denied.fixture);
      expect(buttonNames(deniedElement)).not.toContain('Generate and download bank file');
      expect(buttonNames(deniedElement)).not.toContain('Record batch submission');
      expect(deniedElement.textContent).toContain('Generate a bank file before submitting this batch.');
      expect(deniedElement.textContent).toContain('Bank-file generation requires bank-file generation');
    });

    it('renders batch reconciliation inputs per allocation and keeps draft bindings', async () => {
      const { fixture, page } = await create([...view, 'payroll.payment.reconcile']);
      page.paymentBatch.set(batchOf({ payments: [] }));
      const detail = payment('Paid', 3);
      page.batchPaymentDetails.set([detail]);
      page.batchReconciliationDrafts['33:1'] = {
        settledAmount: 0, settlementDate: '', externalReference: '', notes: '',
        isException: false, expectedReconciliationVersion: 0
      };
      fixture.detectChanges();
      const element = root(fixture);

      expect(headings(element)).toContain('Batch settlement reconciliation');
      const amountLabel = labelFor(element, 'Reconciled settled amount');
      const amount = element.querySelector(`#${amountLabel.htmlFor}`) as HTMLInputElement;
      const dateLabel = labelFor(element, 'Settlement date');
      const date = element.querySelector(`#${dateLabel.htmlFor}`) as HTMLInputElement;
      expect(amount.type).toBe('number');
      expect(date.type).toBe("text");
      expect(date.required).toBe(false);
      await fixture.whenStable();
      amount.value = amount.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('50', (amount.getAttribute('placeholder') ?? '').includes('HH:mm')) : '50';
      amount.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(page.batchReconciliationDrafts['33:1'].settledAmount).toBe(50);
      expect(date.required).toBe(true);
      expect(buttonNames(element)).toContain('Record batch reconciliation');
    });
  });
});
