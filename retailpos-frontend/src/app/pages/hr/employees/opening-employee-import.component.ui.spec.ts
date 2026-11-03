import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { OpeningEmployeeImportComponent } from './opening-employee-import.component';
import { AuthService } from '../../../services/auth.service';

async function create(permissions: string[], url = '/hr/opening-imports') {
  await TestBed.configureTestingModule({
    imports: [OpeningEmployeeImportComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: Router, useValue: { url } },
      { provide: AuthService, useValue: { hasPermission: (value: string) => permissions.includes(value) } }
    ]
  }).compileComponents();
  const fixture = TestBed.createComponent(OpeningEmployeeImportComponent);
  await settle(fixture);
  return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function button(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return Array.from(root.querySelectorAll('button')).find(item => item.textContent?.trim() === text);
}

function control(root: ParentNode, label: string): HTMLInputElement {
  const labelElement = Array.from(root.querySelectorAll('label'))
    .find(item => item.textContent?.trim().startsWith(label));
  expect(labelElement, `label "${label}"`).toBeTruthy();
  return globalThis.document.getElementById(labelElement!.getAttribute('for') ?? '') as HTMLInputElement;
}

const batch = (overrides: Record<string, unknown> = {}) => ({
  id: 5, importType: 'Employee', templateVersion: '1', status: 'Ready', isDryRun: false,
  totalRows: 2, validRows: 1, invalidRows: 1,
  rows: [
    { rowNo: 1, externalKey: 'row-1', status: 'Invalid', errors: ['Bad location.', 'Bad position.'] },
    { rowNo: 2, externalKey: 'row-2', status: 'Completed', errors: [], createdEntityType: 'HrEmployee', createdEntityId: 20 }
  ],
  ...overrides
});

async function loadBatch(fixture: ComponentFixture<OpeningEmployeeImportComponent>, http: HttpTestingController, value: object) {
  fixture.componentInstance.batchId = 5;
  fixture.componentInstance.load();
  http.expectOne(request => request.url.endsWith('/5')).flush(value);
  await settle(fixture);
}

describe('OpeningEmployeeImportComponent kit presentation', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders a single page heading with breadcrumbs, description and the no-batch state', async () => {
    const { el } = await create(['hr.import.view']);
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('Opening Employee Import');
    expect(el.querySelector('nav[aria-label="Breadcrumb"]')?.textContent).toContain('HR');
    expect(el.textContent).toContain('CSV template v1. Dry-run is required.');
    expect(el.textContent).toContain('Position and org unit IDs must reference existing records');
    expect(el.textContent).toContain('No batch loaded.');
    expect(el.textContent).toContain('Upload a CSV or enter a batch ID');
    expect(el.querySelector('app-ui-state')?.textContent).toContain('No batch loaded.');
  });

  it('uses the payroll title, breadcrumb, note and import types on the payroll route', async () => {
    const { el } = await create(['payroll.import.view'], '/payroll/opening-imports');
    expect(el.querySelector('h1')?.textContent).toContain('Opening Payroll Import');
    expect(el.querySelector('nav[aria-label="Breadcrumb"]')?.textContent).toContain('Payroll');
    expect(el.textContent).toContain('Salary and opening-loan imports create approval drafts');
    expect(el.textContent).not.toContain('Position and org unit IDs');
    const type = control(el, 'Import type') as unknown as HTMLSelectElement;
    expect(Array.from(type.options).map(option => option.value)).toEqual(['Salary', 'LoanOpening']);
  });

  it('offers the employee import types and a labelled template download for view-only callers', async () => {
    const { el } = await create(['hr.import.view']);
    const type = control(el, 'Import type') as unknown as HTMLSelectElement;
    expect(Array.from(type.options).map(option => option.value)).toEqual(['Employee', 'Assignment']);
    expect(button(el, 'Download selected CSV v1 template')).toBeTruthy();
  });

  it('shows the upload controls only for callers who can execute, with labelled fields', async () => {
    const view = await create(['hr.import.view']);
    expect(view.el.textContent).not.toContain('Upload private import');
    expect(view.el.querySelector('input[type="file"]')).toBeNull();
    TestBed.resetTestingModule();

    const { el } = await create(['hr.import.view', 'hr.import.execute']);
    const key = control(el, 'Stable retry key');
    expect(key.hasAttribute('required')).toBe(true);
    expect(key.getAttribute('aria-required')).toBe('true');
    expect(key.getAttribute('maxlength')).toBe('100');
    const file = control(el, 'CSV file');
    expect(file.type).toBe('file');
    expect(file.getAttribute('accept')).toBe('.csv,text/csv');
    expect(button(el, 'Upload private import')?.getAttribute('type')).toBe('submit');
  });

  it('keeps the existing client-side upload rule and announces its error', async () => {
    const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Select a CSV file and provide a stable retry key.');
    http.expectNone(request => request.method === 'POST');
  });

  it('uploads with the typed retry key through the labelled controls', async () => {
    const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
    const key = control(el, 'Stable retry key');
    key.value = 'key-1';
    key.dispatchEvent(new Event('input'));
    const file = new File(['a,b'], 'opening.csv', { type: 'text/csv' });
    const input = control(el, 'CSV file');
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne(value => value.method === 'POST');
    expect(request.request.body.get('IdempotencyKey')).toBe('key-1');
    expect(request.request.body.get('ImportType')).toBe('Employee');
    request.flush(batch());
    await settle(fixture);
    expect(el.textContent).toContain('Batch 5: Ready');
  });

  it('resumes a batch by id from the labelled field', async () => {
    const { fixture, http, el } = await create(['hr.import.view']);
    const id = control(el, 'Resume batch ID');
    expect(id.type).toBe('number');
    expect(id.hasAttribute('required')).toBe(true);
    id.value = '5';
    id.dispatchEvent(new Event('input'));
    await settle(fixture);
    el.querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    http.expectOne(request => request.url.endsWith('/5')).flush(batch());
    await settle(fixture);
    expect(el.textContent).toContain('Batch 5: Ready');
    expect(el.textContent).not.toContain('No batch loaded.');
  });

  it('shows processing and disables the actions while a request is in flight', async () => {
    const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
    await loadBatch(fixture, http, batch());
    fixture.componentInstance.process('dry-run');
    await settle(fixture);
    expect(Array.from(el.querySelectorAll('[role="status"]')).map(item => item.textContent))
      .toEqual(expect.arrayContaining([expect.stringContaining('Processing import request...')]));
    expect(button(el, 'Upload private import')?.disabled).toBe(true);
    expect(button(el, 'Load batch')?.disabled).toBe(true);
    expect(button(el, 'Download selected CSV v1 template')?.disabled).toBe(true);
    http.expectOne(request => request.url.endsWith('/5/dry-run')).flush(batch());
    await settle(fixture);
    expect(el.textContent).not.toContain('Processing import request...');
  });

  describe('batch result', () => {
    it('renders the title, summary and a named row table with per-row errors and badges', async () => {
      const { fixture, http, el } = await create(['hr.import.view']);
      await loadBatch(fixture, http, batch());
      const heading = Array.from(el.querySelectorAll('app-ui-card h2')).map(item => item.textContent);
      expect(heading).toContain('Batch 5: Ready');
      expect(el.textContent).toContain('2 rows / 1 valid / 1 invalid');
      const region = el.querySelector('[role="region"][aria-label="Import rows"]')!;
      const headers = Array.from(region.querySelectorAll('th')).map(item => item.textContent?.trim());
      expect(headers).toEqual(['Row', 'External key', 'Status', 'Errors / result']);
      expect(region.textContent).toContain('Bad location.');
      expect(region.textContent).toContain('Bad position.');
      expect(region.textContent).toContain('HrEmployee #20');
      const badges = Array.from(region.querySelectorAll('app-ui-badge')).map(item => item.textContent?.trim());
      expect(badges).toEqual(expect.arrayContaining(['Invalid', 'Completed']));
    });

    it('searches the import rows by external key', async () => {
      const { fixture, http, el } = await create(['hr.import.view']);
      await loadBatch(fixture, http, batch());
      const search = el.querySelector('input[aria-label="Search import rows"]') as HTMLInputElement;
      search.value = 'row-2';
      search.dispatchEvent(new Event('input'));
      search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      await settle(fixture);
      const body = el.querySelector('tbody')!.textContent;
      expect(body).toContain('row-2');
      expect(body).not.toContain('row-1');
    });

    it('shows the empty rows message when a batch has no rows', async () => {
      const { fixture, http, el } = await create(['hr.import.view']);
      await loadBatch(fixture, http, batch({ rows: [] }));
      expect(el.textContent).toContain('No rows in this batch.');
    });

    it('announces completion as a status and keeps repeat execution blocked', async () => {
      const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
      await loadBatch(fixture, http, batch({ status: 'Completed' }));
      const status = Array.from(el.querySelectorAll('app-ui-alert')).find(item => item.textContent?.includes('Import completed'))!;
      expect(status.getAttribute('role')).toBe('status');
      expect(button(el, 'Dry-run validation')?.disabled).toBe(true);
      expect(button(el, 'Execute validated batch')?.disabled).toBe(true);
    });

    it('enables execute only for a Ready batch and runs the dry-run action', async () => {
      const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
      await loadBatch(fixture, http, batch({ status: 'Uploaded' }));
      expect(button(el, 'Execute validated batch')?.disabled).toBe(true);
      expect(button(el, 'Dry-run validation')?.disabled).toBe(false);
      button(el, 'Dry-run validation')!.click();
      http.expectOne(request => request.url.endsWith('/5/dry-run')).flush(batch({ status: 'Ready' }));
      await settle(fixture);
      expect(button(el, 'Execute validated batch')?.disabled).toBe(false);
    });

    it('hides the dry-run and execute actions for view-only callers', async () => {
      const { fixture, http, el } = await create(['hr.import.view']);
      await loadBatch(fixture, http, batch());
      expect(button(el, 'Dry-run validation')).toBeUndefined();
      expect(button(el, 'Execute validated batch')).toBeUndefined();
    });

    it('uses the payroll salary and loan permissions for the actions', async () => {
      const salaryOnly = await create(['payroll.import.view', 'payroll.import.execute', 'payroll.salary.manage'],
        '/payroll/opening-imports');
      await loadBatch(salaryOnly.fixture, salaryOnly.http, batch({ importType: 'Salary' }));
      expect(button(salaryOnly.el, 'Dry-run validation')).toBeTruthy();
      TestBed.resetTestingModule();

      const loan = await create(['payroll.import.view', 'payroll.import.execute', 'payroll.salary.manage'],
        '/payroll/opening-imports');
      await loadBatch(loan.fixture, loan.http, batch({ importType: 'LoanOpening' }));
      expect(button(loan.el, 'Dry-run validation')).toBeUndefined();
    });
  });

  it('announces service errors in a single danger alert', async () => {
    const { fixture, http, el } = await create(['hr.import.view', 'hr.import.execute']);
    await loadBatch(fixture, http, batch());
    fixture.componentInstance.process('dry-run');
    http.expectOne(request => request.url.endsWith('/dry-run')).flush(
      { message: 'Private storage is not composed.' }, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);
    const alerts = el.querySelectorAll('[role="alert"]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('Private storage is not composed.');
  });
});
