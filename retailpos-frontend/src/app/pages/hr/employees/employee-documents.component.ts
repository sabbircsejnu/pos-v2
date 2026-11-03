import { HttpClient } from '@angular/common/http';
import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../../environments/environment';
import { UI_KIT, UiBadgeVariant } from '../../../components/shared/ui';
import { AuthService } from '../../../services/auth.service';

interface EmployeeDocumentType {
  id: number;
  code: string;
  name: string;
  classification: string;
  requiresExpiryDate: boolean;
  isActive: boolean;
}

interface EmployeeDocumentVersion {
  id: string;
  versionNo: number;
  originalFileName: string;
  scanStatus: string;
  uploadedAtUtc: string;
  revokedAtUtc: string | null;
}

interface EmployeeDocument {
  id: number;
  documentTypeId: number;
  documentTypeCode: string;
  documentTypeName: string;
  classification: string;
  documentNumber: string | null;
  issueDate: string | null;
  expiryDate: string | null;
  status: string;
  isLegalHold: boolean;
  version: number;
  currentVersionId: string | null;
  versions: EmployeeDocumentVersion[];
}

@Component({
  selector: 'app-employee-documents',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  template: `
    <app-ui-card title="Employee documents" [headingLevel]="3">
      <div class="doc-stack">
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (auth.hasPermission('hr.employee_document.upload')) {
          <details class="doc-upload">
            <summary>Upload a document</summary>
            <form class="doc-grid" (ngSubmit)="upload()">
              <app-ui-form-field label="Document type" [required]="true">
                <select appUiControl name="documentTypeId" required [(ngModel)]="documentTypeId">
                  <option [ngValue]="null">Select type</option>
                  @for (type of types(); track type.id) {
                    <option [ngValue]="type.id">{{ type.name }} · {{ type.classification }}</option>
                  }
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="File" [required]="true">
                <input appUiControl type="file" name="uploadFile" required (change)="selectFile($event)">
              </app-ui-form-field>
              <app-ui-form-field label="Document number">
                <input appUiControl name="documentNumber" maxlength="100" [(ngModel)]="documentNumber">
              </app-ui-form-field>
              <app-ui-date-picker label="Issue date" name="issueDate" [(ngModel)]="issueDate" />
              <app-ui-date-picker label="Expiry date" name="expiryDate" [(ngModel)]="expiryDate" />
              <app-ui-form-field label="Notes">
                <textarea appUiControl name="notes" maxlength="1000" [(ngModel)]="notes"></textarea>
              </app-ui-form-field>
              <div class="doc-actions doc-span-2">
                <button appUiButton type="submit">Upload document</button>
              </div>
            </form>
          </details>
        }
        @if (documents().length === 0) {
          <app-ui-state title="No employee documents." [compact]="true" />
        }
        @for (document of documents(); track document.id) {
          <app-ui-card>
            <div class="doc-stack">
              <div class="doc-head">
                <div class="doc-meta">
                  <h4 class="doc-title">
                    <span>{{ document.documentTypeName }}</span>
                    <app-ui-badge [variant]="statusVariant(document.status)">{{ document.status }}</app-ui-badge>
                    @if (document.isLegalHold) {
                      <app-ui-badge variant="warning">Legal hold</app-ui-badge>
                    }
                  </h4>
                  <p>{{ document.documentTypeCode }} · {{ document.classification }} ·
                    {{ document.documentNumber || 'No document number' }}</p>
                  <p>Issue: {{ (document.issueDate | appDate) || '—' }} · Expiry: {{ (document.expiryDate | appDate) || '—' }}</p>
                </div>
                <div class="doc-actions">
                  @if (auth.hasPermission('hr.employee_document.download') && canDownload(document)) {
                    <button appUiButton variant="outline" size="sm" type="button"
                      [attr.aria-label]="'Download ' + document.documentTypeName" (click)="download(document)">Download</button>
                  }
                  @if (auth.hasPermission('hr.employee_document.delete') && document.status !== 'Archived') {
                    <button appUiButton variant="outline" size="sm" type="button"
                      [attr.aria-label]="'Archive ' + document.documentTypeName" (click)="archive(document)">Archive</button>
                  }
                </div>
              </div>
              <details class="doc-versions">
                <summary>Version history</summary>
                <ol>
                  @for (version of document.versions; track version.id) {
                    <li>
                      v{{ version.versionNo }} · {{ version.originalFileName }} · {{ version.scanStatus }} ·
                      {{ version.uploadedAtUtc | appDate:'medium' }}
                    </li>
                  }
                </ol>
              </details>
              @if (auth.hasPermission('hr.employee_document.replace') && document.status === 'Active') {
                <app-ui-form-field label="Replace with new version">
                  <input appUiControl type="file" (change)="replace(document, $event)">
                </app-ui-form-field>
              }
            </div>
          </app-ui-card>
        }
      </div>
    </app-ui-card>
  `,
  styles: [`
    :host { display: block; }
    .doc-stack { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .doc-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: var(--rp-form-gap); }
    .doc-upload {
      padding: var(--rp-space-3);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
    }
    .doc-upload summary {
      cursor: pointer;
      color: var(--rp-color-text-link);
      font-weight: var(--rp-font-weight-medium);
    }
    .doc-upload[open] summary { margin-bottom: var(--rp-space-3); }
    .doc-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .doc-head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--rp-space-3);
    }
    .doc-meta { min-width: 0; }
    .doc-meta p { margin: 0; color: var(--rp-color-text-secondary); overflow-wrap: anywhere; }
    .doc-title {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
      margin: 0 0 var(--rp-space-1);
      font-size: var(--rp-text-section-size);
      font-weight: var(--rp-font-weight-semibold);
    }
    .doc-versions summary { cursor: pointer; color: var(--rp-color-text-link); font-weight: var(--rp-font-weight-medium); }
    .doc-versions ol { margin: var(--rp-space-2) 0 0; padding-left: var(--rp-space-6); overflow-wrap: anywhere; }
    @media (min-width: 768px) {
      .doc-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .doc-span-2 { grid-column: 1 / -1; }
    }
  `]
})
export class EmployeeDocumentsComponent implements OnChanges {
  @Input({ required: true }) employeeId!: number;

  readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/hr/v1`;
  readonly types = signal<EmployeeDocumentType[]>([]);
  readonly documents = signal<EmployeeDocument[]>([]);
  readonly error = signal('');
  documentTypeId: number | null = null;
  documentNumber = '';
  issueDate = '';
  expiryDate = '';
  notes = '';
  private selectedFile: File | null = null;

  ngOnChanges(): void {
    if (this.employeeId > 0) {
      this.load();
    }
  }

  selectFile(event: Event): void {
    this.selectedFile = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  upload(): void {
    if (!this.documentTypeId || !this.selectedFile) {
      this.error.set('Select a document type and file.');
      return;
    }
    const body = new FormData();
    body.set('DocumentTypeId', String(this.documentTypeId));
    body.set('DocumentNumber', this.documentNumber);
    body.set('IssueDate', this.issueDate);
    body.set('ExpiryDate', this.expiryDate);
    body.set('Notes', this.notes);
    body.set('IsLegalHold', 'false');
    body.set('File', this.selectedFile);
    this.http.post(`${this.apiUrl}/employees/${this.employeeId}/documents`, body).subscribe({
      next: () => {
        this.error.set('');
        this.selectedFile = null;
        this.documentNumber = '';
        this.issueDate = '';
        this.expiryDate = '';
        this.notes = '';
        this.load();
      },
      error: () => this.error.set('Document upload failed. Check your access and storage configuration.')
    });
  }

  replace(document: EmployeeDocument, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const body = new FormData();
    body.set('ExpectedDocumentVersion', String(this.getDocumentVersion(document)));
    body.set('File', file);
    this.http.post(`${this.apiUrl}/employee-documents/${document.id}/versions`, body).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Document replacement failed. Check your access and storage configuration.')
    });
  }

  download(document: EmployeeDocument): void {
    const currentVersion = document.versions.find(version => version.id === document.currentVersionId);
    if (!this.canDownload(document) || !currentVersion) {
      this.error.set('No approved current document version is available.');
      return;
    }
    this.http.get(`${this.apiUrl}/employee-documents/${document.id}/download`, { responseType: 'blob' })
      .subscribe({
        next: blob => {
          const url = URL.createObjectURL(blob);
          const anchor = window.document.createElement('a');
          anchor.href = url;
          anchor.download = currentVersion.originalFileName;
          anchor.click();
          URL.revokeObjectURL(url);
        },
        error: () => this.error.set('Document download failed or access was denied.')
      });
  }

  canDownload(document: EmployeeDocument): boolean {
    const version = document.versions.find(item => item.id === document.currentVersionId);
    return document.status === 'Active' && version?.scanStatus === 'Clean' && !version.revokedAtUtc;
  }

  statusVariant(status: string): UiBadgeVariant {
    return status === 'Active' ? 'success' : status === 'Expired' ? 'danger' : 'neutral';
  }

  archive(document: EmployeeDocument): void {
    const reason = window.prompt('Reason for archiving this document:');
    if (!reason?.trim()) return;
    this.http.post(`${this.apiUrl}/employee-documents/${document.id}/archive`, {
      expectedVersion: this.getDocumentVersion(document),
      reason
    }).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Document archive failed. Check your access and document status.')
    });
  }

  private load(): void {
    this.error.set('');
    this.http.get<EmployeeDocumentType[]>(`${this.apiUrl}/employee-document-types`).subscribe({
      next: values => this.types.set(values.filter(type => type.isActive)),
      error: () => this.error.set('Document types could not be loaded.')
    });
    this.http.get<EmployeeDocument[]>(`${this.apiUrl}/employees/${this.employeeId}/documents`).subscribe({
      next: values => this.documents.set(values),
      error: () => this.error.set('Employee documents could not be loaded.')
    });
  }

  private getDocumentVersion(document: EmployeeDocument): number {
    return document.version;
  }
}
