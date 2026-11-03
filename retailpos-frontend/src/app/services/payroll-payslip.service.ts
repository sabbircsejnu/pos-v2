import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollPayslipArtifact {
  id: string;
  runId: number;
  runRevision: number;
  employeeResultId: number;
  versionNo: number;
  artifactType: string;
  format: string;
  contentHash: string;
  contentType: string;
  byteLength: number;
  templateVersion: string;
  scanStatus: string;
  createdAtUtc: string;
  isReplay: boolean;
}

export interface PayrollPayslipDelivery {
  id: number;
  artifactVersionId: string;
  recipientMasked: string;
  status: 'Pending' | 'Sent' | 'Failed';
  failureCode: string | null;
  requestedAtUtc: string;
  sentAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class PayrollPayslipService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/runs`;

  generate(runId: number, employeeId: number) {
    return this.http.post<PayrollPayslipArtifact>(
      `${this.base}/${runId}/payslips/${employeeId}`, {}
    );
  }

  download(runId: number, employeeId: number) {
    return this.http.get(
      `${this.base}/${runId}/payslips/${employeeId}/download`, { responseType: 'blob' }
    );
  }

  print(runId: number, employeeId: number) {
    return this.http.get(
      `${this.base}/${runId}/payslips/${employeeId}/print`, { responseType: 'blob' }
    );
  }

  email(runId: number, employeeId: number, idempotencyKey: string) {
    return this.http.post<PayrollPayslipDelivery>(
      `${this.base}/${runId}/payslips/${employeeId}/email`, {},
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) }
    );
  }
}
