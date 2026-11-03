import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { OpeningEmployeeImportComponent } from './opening-employee-import.component';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

describe('OpeningEmployeeImportComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

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
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController) };
  }

  it('gates upload and execute for view-only callers', async () => {
    const { fixture } = await create(['hr.import.view']);
    expect(fixture.nativeElement.textContent).toContain('CSV template v1');
    expect(fixture.nativeElement.textContent).not.toContain('Upload private import');
  });

  it('uses Payroll permissions and endpoint for opening payroll imports', async () => {
    const { fixture, http } = await create(
      ['payroll.import.view', 'payroll.import.execute', 'payroll.salary.manage'],
      '/payroll/opening-imports');
    expect(fixture.nativeElement.textContent).toContain('Opening Payroll Import');
    expect(fixture.nativeElement.textContent).toContain('Initial salary setup');
    fixture.componentInstance.downloadTemplate();
    const request = http.expectOne(value => value.url.endsWith('/template'));
    expect(request.request.url).toBe(`${environment.apiUrl}/payroll/v1/opening-imports/template`);
    expect(request.request.params.get('importType')).toBe('Salary');
    request.flush(new Blob(['header'], { type: 'text/csv' }));
    http.verify();
  });

  it('requires successful dry-run before execution', async () => {
    const { fixture, http } = await create(['hr.import.view', 'hr.import.execute']);
    fixture.componentInstance.batchId = 5;
    fixture.componentInstance.load();
    http.expectOne(request => request.url.endsWith('/5')).flush({
      id: 5, importType: 'Employee', templateVersion: '1', status: 'Uploaded',
      totalRows: 0, validRows: 0, invalidRows: 0, rows: []
    });
    fixture.componentInstance.process('execute');
    http.expectNone(request => request.url.endsWith('/execute'));
    fixture.componentInstance.process('dry-run');
    http.expectOne(request => request.url.endsWith('/5/dry-run')).flush({
      id: 5, importType: 'Employee', templateVersion: '1', status: 'Ready',
      totalRows: 1, validRows: 1, invalidRows: 0, rows: []
    });
    fixture.componentInstance.process('execute');
    http.expectOne(request => request.url.endsWith('/5/execute')).flush({
      id: 5, importType: 'Employee', templateVersion: '1', status: 'Completed',
      totalRows: 1, validRows: 1, invalidRows: 0,
      rows: [{ rowNo: 1, externalKey: 'row-1', status: 'Completed', errors: [], createdEntityId: 20, createdEntityType: 'HrEmployee' }]
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Import completed');
    expect(fixture.nativeElement.textContent).toContain('HrEmployee #20');
    http.verify();
  });

  it('shows row-level errors and explicit composition failures', async () => {
    const { fixture, http } = await create(['hr.import.view', 'hr.import.execute']);
    fixture.componentInstance.batchId = 6;
    fixture.componentInstance.load();
    http.expectOne(request => request.url.endsWith('/6')).flush({
      id: 6, status: 'ValidationFailed', totalRows: 1, invalidRows: 1, validRows: 0,
      rows: [{ rowNo: 1, externalKey: 'row-1', status: 'Invalid', errors: ['Invalid location projection.'] }]
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Invalid location projection.');
    fixture.componentInstance.process('dry-run');
    http.expectOne(request => request.url.endsWith('/dry-run')).flush(
      { message: 'Private storage is not composed.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Private storage is not composed.');
    http.verify();
  });

  it('declares the CSV contract independently of OS file MIME labels', async () => {
    const { fixture, http } = await create(['hr.import.view', 'hr.import.execute']);
    const input = document.createElement('input');
    const file = new File(['csv content'], 'opening.csv', { type: 'application/vnd.ms-excel' });
    Object.defineProperty(input, 'files', { value: [file] });
    input.addEventListener('change', event => fixture.componentInstance.selectFile(event));
    input.dispatchEvent(new Event('change'));
    fixture.componentInstance.idempotencyKey = 'stable-key';
    fixture.componentInstance.upload();
    const request = http.expectOne(value => value.method === 'POST');
    expect(request.request.url).toBe(`${environment.apiUrl}/hr/v1/opening-imports`);
    expect(request.request.body.get('File').type).toBe('text/csv');
    expect(request.request.body.get('File').name).toBe('opening.csv');
    expect(request.request.body.get('IdempotencyKey')).toBe('stable-key');
    expect(request.request.body.get('TemplateVersion')).toBe('1');
    request.flush({ id: 7, status: 'Uploaded', rows: [] });
    http.verify();
  });

  it('refreshes failed execution evidence without hiding the conflict', async () => {
    const { fixture, http } = await create(['hr.import.view', 'hr.import.execute']);
    fixture.componentInstance.batchId = 8;
    fixture.componentInstance.load();
    http.expectOne(request => request.url.endsWith('/8')).flush({
      id: 8, status: 'Ready', totalRows: 1, invalidRows: 0, validRows: 1, rows: []
    });
    fixture.componentInstance.process('execute');
    http.expectOne(request => request.url.endsWith('/execute')).flush(
      { message: 'Entire batch rolled back.' }, { status: 409, statusText: 'Conflict' });
    http.expectOne(request => request.url.endsWith('/8')).flush({
      id: 8, status: 'Failed', totalRows: 1, invalidRows: 1, validRows: 0,
      rows: [{ rowNo: 1, externalKey: 'row-1', status: 'Invalid', errors: ['All rows rolled back.'] }]
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Entire batch rolled back.');
    expect(fixture.nativeElement.textContent).toContain('Batch 8: Failed');
    http.verify();
  });
});
