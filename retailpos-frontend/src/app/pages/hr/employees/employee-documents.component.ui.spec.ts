import { CompanyDateService } from '../../../services/company-date.service';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { EmployeeDocumentsComponent } from './employee-documents.component';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

const baseUrl = `${environment.apiUrl}/hr/v1`;

const document = {
  id: 9, documentTypeId: 2, documentTypeCode: 'ID', documentTypeName: 'Identity',
  classification: 'Confidential', documentNumber: 'A-1', issueDate: '2026-01-01', expiryDate: null,
  status: 'Active', isLegalHold: true, version: 3, currentVersionId: 'v1',
  versions: [{
    id: 'v1', versionNo: 1, originalFileName: 'identity.pdf', scanStatus: 'Clean',
    uploadedAtUtc: '2026-10-01T00:00:00Z', revokedAtUtc: null
  }]
};

const documentType = {
  id: 2, code: 'ID', name: 'Identity', classification: 'Confidential', requiresExpiryDate: false, isActive: true
};

@Component({
  standalone: true,
  imports: [EmployeeDocumentsComponent],
  template: '<app-employee-documents [employeeId]="7" />'
})
class HostComponent {}

async function setup(permissions: string[], documents: unknown[] = [document]) {
  await TestBed.configureTestingModule({
    imports: [HostComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { hasPermission: (value: string) => permissions.includes(value) } }
    ]
  }).compileComponents();
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne(`${baseUrl}/employee-document-types`).flush([documentType, { ...documentType, id: 3, isActive: false }]);
  http.expectOne(`${baseUrl}/employees/7/documents`).flush(documents);
  await settle(fixture);
  return { fixture, http, el: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  await Promise.resolve();
  fixture.detectChanges();
}

async function openUploadForm(fixture: ComponentFixture<unknown>, el: HTMLElement): Promise<void> {
  const details = el.querySelector<HTMLElement>('.doc-upload')!;
  (details.querySelector('summary') as HTMLElement).click();
  await settle(fixture);
}

function button(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return Array.from(root.querySelectorAll('button')).find(item => item.textContent?.trim() === text);
}

function control(root: ParentNode, label: string): HTMLInputElement {
  const labelElement = Array.from(root.querySelectorAll('label'))
    .find(item => item.textContent?.trim().startsWith(label));
  expect(labelElement, `label "${label}"`).toBeTruthy();
  return document_.getElementById(labelElement!.getAttribute('for') ?? '') as HTMLInputElement;
}

const document_ = globalThis.document;

describe('EmployeeDocumentsComponent kit presentation', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders a titled card with a document card, status text and metadata', async () => {
    const { el } = await setup([]);
    expect(el.querySelector('app-ui-card h3')?.textContent).toContain('Employee documents');
    expect(el.querySelectorAll('h1').length).toBe(0);
    const heading = el.querySelector('h4')!;
    expect(heading.textContent).toContain('Identity');
    expect(heading.querySelector('app-ui-badge')?.textContent).toContain('Active');
    expect(el.textContent).toContain('Legal hold');
    expect(el.textContent).toContain('ID · Confidential · A-1');
    expect(el.textContent).toContain("Issue: 01/01/2026 · Expiry: —");
    expect(el.textContent).toContain('v1 · identity.pdf · Clean ·');
  });

  it('shows the empty state when there are no documents', async () => {
    const { el } = await setup([], []);
    expect(el.textContent).toContain('No employee documents.');
    expect(el.querySelector('h4')).toBeNull();
  });

  it('shows no document number placeholder text and no legal hold badge when not applicable', async () => {
    const { el } = await setup([], [{ ...document, documentNumber: null, isLegalHold: false, status: 'Archived' }]);
    expect(el.textContent).toContain('No document number');
    expect(el.textContent).not.toContain('Legal hold');
    expect(el.querySelector('h4 app-ui-badge')?.textContent).toContain('Archived');
  });

  it('keeps the version history disclosure reachable', async () => {
    const { el } = await setup([]);
    const details = Array.from(el.querySelectorAll('details'))
      .find(item => item.querySelector('summary')?.textContent?.includes('Version history'))!;
    expect(details.querySelector('summary')?.textContent).toContain('Version history');
    expect(details.querySelectorAll('li').length).toBe(1);
  });

  describe('permissions', () => {
    it('offers no upload, download, archive or replace controls without permissions', async () => {
      const { el } = await setup([]);
      expect(el.querySelector('form')).toBeNull();
      expect(button(el, 'Download')).toBeUndefined();
      expect(button(el, 'Archive')).toBeUndefined();
      expect(el.querySelector('input[type="file"]')).toBeNull();
    });

    it('shows download and archive with names that include the document type', async () => {
      const { el } = await setup(['hr.employee_document.download', 'hr.employee_document.delete']);
      expect(button(el, 'Download')?.getAttribute('aria-label')).toBe('Download Identity');
      expect(button(el, 'Archive')?.getAttribute('aria-label')).toBe('Archive Identity');
    });

    it('hides download for unapproved content and archive for archived documents', async () => {
      const { el } = await setup(['hr.employee_document.download', 'hr.employee_document.delete'], [
        { ...document, versions: [{ ...document.versions[0], scanStatus: 'Rejected' }] },
        { ...document, id: 10, status: 'Archived' }
      ]);
      expect(button(el, 'Download')).toBeUndefined();
      expect(el.querySelectorAll('button').length).toBe(1);
      expect(button(el, 'Archive')?.getAttribute('aria-label')).toBe('Archive Identity');
    });

    it('offers replacement only for active documents', async () => {
      const { el } = await setup(['hr.employee_document.replace'], [document, { ...document, id: 10, status: 'Archived' }]);
      const inputs = el.querySelectorAll('input[type="file"]');
      expect(inputs.length).toBe(1);
      expect(control(el, 'Replace with new version')).toBe(inputs[0]);
    });
  });

  describe('upload form', () => {
    it('ties labels to controls, marks required fields and lists only active types', async () => {
      const { fixture, el } = await setup(['hr.employee_document.upload']);
      expect(el.querySelector('.doc-upload')?.hasAttribute('open')).toBe(false);
      await openUploadForm(fixture, el);
      const type = control(el, 'Document type') as unknown as HTMLSelectElement;
      expect(type.hasAttribute('required')).toBe(true);
      expect(type.getAttribute('aria-required')).toBe('true');
      expect(Array.from(type.options).map(option => option.textContent?.trim()))
        .toEqual(['Select type', 'Identity · Confidential']);
      expect(control(el, 'File').type).toBe('file');
      expect(control(el, 'File').hasAttribute('required')).toBe(true);
      expect(control(el, 'Notes').getAttribute('maxlength')).toBe('1000');
      expect(control(el, 'Document number').getAttribute('maxlength')).toBe('100');
      expect(control(el, 'Issue date').placeholder).toBe('dd/MM/yyyy');
      expect(control(el, 'Expiry date').placeholder).toBe('dd/MM/yyyy');
      expect(button(el, 'Upload document')?.getAttribute('type')).toBe('submit');
    });

    it('announces the validation error from the existing upload rule', async () => {
      const { fixture, http, el } = await setup(['hr.employee_document.upload']);
      await openUploadForm(fixture, el);
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await settle(fixture);
      const alert = el.querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('Select a document type and file.');
      http.expectNone(`${baseUrl}/employees/7/documents`);
    });

    it('uploads the typed values through the employee document API and reloads', async () => {
      const { fixture, http, el } = await setup(['hr.employee_document.upload']);
      await openUploadForm(fixture, el);
      const type = control(el, 'Document type') as unknown as HTMLSelectElement;
      type.value = type.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText(type.options[1].value, (type.getAttribute('placeholder') ?? '').includes('HH:mm')) : type.options[1].value;
      type.dispatchEvent(new Event('change'));
      const number = control(el, 'Document number');
      number.value = number.hasAttribute('appCompanyDate') ? TestBed.inject(CompanyDateService).inputText('N-77', (number.getAttribute('placeholder') ?? '').includes('HH:mm')) : 'N-77';
      number.dispatchEvent(new Event('input'));
      const issueDate = control(el, 'Issue date');
      issueDate.value = '14/02/2024';
      issueDate.dispatchEvent(new Event('input'));
      const expiryDate = control(el, 'Expiry date');
      expiryDate.value = '14/02/2028';
      expiryDate.dispatchEvent(new Event('input'));
      const file = new File(['content'], 'id.pdf', { type: 'application/pdf' });
      const fileInput = control(el, 'File');
      Object.defineProperty(fileInput, 'files', { value: [file] });
      fileInput.dispatchEvent(new Event('change'));
      await settle(fixture);
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      const request = http.expectOne(`${baseUrl}/employees/7/documents`);
      const body = request.request.body as FormData;
      expect(body.get('DocumentTypeId')).toBe('2');
      expect(body.get('DocumentNumber')).toBe('N-77');
      expect(body.get('IssueDate')).toBe('2024-02-14');
      expect(body.get('ExpiryDate')).toBe('2028-02-14');
      expect(body.get('File')).toBe(file);
      request.flush(document);
      http.expectOne(`${baseUrl}/employee-document-types`).flush([documentType]);
      http.expectOne(`${baseUrl}/employees/7/documents`).flush([document]);
      await settle(fixture);
      expect(el.querySelector('[role="alert"]')).toBeNull();
      expect(number.value).toBe('');
    });
  });

  describe('errors', () => {
    it('announces a failed download through one danger alert', async () => {
      const { fixture, http, el } = await setup(['hr.employee_document.download']);
      button(el, 'Download')!.click();
      http.expectOne(`${baseUrl}/employee-documents/9/download`)
        .flush(new Blob(), { status: 403, statusText: 'Forbidden' });
      await settle(fixture);
      const alerts = el.querySelectorAll('[role="alert"]');
      expect(alerts.length).toBe(1);
      expect(alerts[0].textContent).toContain('Document download failed or access was denied.');
    });

    it('announces a failed documents load', async () => {
      await TestBed.configureTestingModule({
        imports: [HostComponent],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: AuthService, useValue: { hasPermission: () => false } }
        ]
      }).compileComponents();
      const fixture = TestBed.createComponent(HostComponent);
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      http.expectOne(`${baseUrl}/employee-document-types`).flush([]);
      http.expectOne(`${baseUrl}/employees/7/documents`).flush('x', { status: 500, statusText: 'Server Error' });
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent)
        .toContain('Employee documents could not be loaded.');
    });
  });

  it('archives through the existing prompt flow from the card button', async () => {
    const { fixture, http, el } = await setup(['hr.employee_document.delete']);
    vi.spyOn(window, 'prompt').mockReturnValue('Retention review');
    button(el, 'Archive')!.click();
    const request = http.expectOne(`${baseUrl}/employee-documents/9/archive`);
    expect(request.request.body).toEqual({ expectedVersion: 3, reason: 'Retention review' });
    request.flush(document);
    http.expectOne(`${baseUrl}/employee-document-types`).flush([]);
    http.expectOne(`${baseUrl}/employees/7/documents`).flush([document]);
    await settle(fixture);
    vi.restoreAllMocks();
  });
});
