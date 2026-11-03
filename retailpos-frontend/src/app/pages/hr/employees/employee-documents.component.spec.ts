import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EmployeeDocumentsComponent } from './employee-documents.component';
import { AuthService } from '../../../services/auth.service';
import { environment } from '../../../../environments/environment';

describe('Employee documents HTTP boundary', () => {
  const baseUrl = `${environment.apiUrl}/hr/v1`;
  const document = {
    id: 9, documentTypeId: 2, documentTypeCode: 'ID', documentTypeName: 'Identity',
    classification: 'Confidential', documentNumber: null, issueDate: null, expiryDate: null,
    status: 'Active', isLegalHold: false, version: 3, currentVersionId: 'v1', versions: [{
      id: 'v1', versionNo: 1, originalFileName: 'identity.pdf', scanStatus: 'Clean',
      uploadedAtUtc: '2026-10-01T00:00:00Z', revokedAtUtc: null
    }]
  };
  let component: EmployeeDocumentsComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { hasPermission: () => true } }
    ] });
    component = TestBed.createComponent(EmployeeDocumentsComponent).componentInstance;
    component.employeeId = 7;
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function flushList(): void {
    http.expectOne(`${baseUrl}/employee-document-types`).flush([]);
    http.expectOne(`${baseUrl}/employees/7/documents`).flush([document]);
  }

  function fileChange(file: File): Event {
    const event = new Event('change');
    Object.defineProperty(event, 'target', { value: { files: [file] } });
    return event;
  }

  it('loads types and metadata without a duplicate api prefix', () => {
    component.ngOnChanges();
    flushList();
    expect(component.documents()).toEqual([document]);
  });

  it('uploads only through the employee document API', () => {
    component.documentTypeId = 2;
    const file = new File(['content'], 'identity.pdf', { type: 'application/pdf' });
    component.selectFile(fileChange(file));
    component.upload();
    const request = http.expectOne(`${baseUrl}/employees/7/documents`);
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('File')).toBe(file);
    request.flush(document);
    flushList();
  });

  it('replaces a version with its optimistic concurrency token', () => {
    const file = new File(['content'], 'replacement.pdf', { type: 'application/pdf' });
    component.replace(document, fileChange(file));
    const request = http.expectOne(`${baseUrl}/employee-documents/9/versions`);
    expect((request.request.body as FormData).get('ExpectedDocumentVersion')).toBe('3');
    request.flush(document);
    flushList();
  });

  it('requests a blob from the logical authenticated route and surfaces denial', () => {
    component.download(document);
    const request = http.expectOne(`${baseUrl}/employee-documents/9/download`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(), { status: 403, statusText: 'Forbidden' });
    expect(component.error()).toContain('denied');
  });

  it('does not offer or request unapproved, revoked or archived content', () => {
    for (const value of [
      { ...document, currentVersionId: null },
      { ...document, status: 'Archived' },
      { ...document, versions: [{ ...document.versions[0], scanStatus: 'Rejected' }] },
      { ...document, versions: [{ ...document.versions[0], revokedAtUtc: '2026-10-01T00:00:00Z' }] }
    ]) {
      expect(component.canDownload(value)).toBe(false);
      component.download(value);
    }
    http.expectNone(`${baseUrl}/employee-documents/9/download`);
  });

  it('uses the published version rather than a newer rejected replacement filename', () => {
    const value = { ...document, versions: [
      { ...document.versions[0], id: 'v2', versionNo: 2,
        originalFileName: 'rejected.pdf', scanStatus: 'Rejected' },
      ...document.versions
    ] };
    const clickedNames: string[] = [];
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clickedNames.push(this.download);
    });
    expect(component.canDownload(value)).toBe(true);
    component.download(value);
    http.expectOne(`${baseUrl}/employee-documents/9/download`).flush(new Blob(['clean']));
    expect(clickedNames).toEqual(['identity.pdf']);
    vi.restoreAllMocks();
  });

  it('archives through the logical route without sending a storage key', () => {
    vi.spyOn(window, 'prompt').mockReturnValue('Retention review');
    component.archive(document);
    const request = http.expectOne(`${baseUrl}/employee-documents/9/archive`);
    expect(request.request.body).toEqual({ expectedVersion: 3, reason: 'Retention review' });
    request.flush(document);
    flushList();
    vi.restoreAllMocks();
  });
});
